using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Builds one immutable procedural <see cref="Mesh"/> from mutable vertex and index lists.
/// </summary>
/// <remarks>
/// <para>
/// Create the builder with <see cref="EngineContext.CreateMeshBuilder"/>. Fill <see cref="Vertices"/>
/// and <see cref="Indices"/> (or call <see cref="SetVertices"/> and <see cref="SetIndices"/>),
/// optionally compute normals, tangents, and bounds, then call <see cref="BuildAsync"/> once. The
/// built mesh is one-shot: changing the builder afterwards does not change the mesh, and the builder
/// can be reused to build another mesh.
/// </para>
/// <para>
/// A builder instance is not thread-safe; use one per thread. The factory, the CPU helpers, and
/// <see cref="BuildAsync"/> do not touch tick-affine engine state, so they may be called from any
/// thread, including a worker thread. Awaiting a build on the tick thread suspends that tick's
/// continuation exactly like an asset load. After the app stops, every method throws
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// <para>
/// <see cref="ComputeNormals"/> and <see cref="ComputeTangents"/> support only the triangle
/// topologies. A line or point mesh is still valid for <see cref="BuildAsync"/>; it simply has no
/// generated normals or tangents. The fixed vertex layout is documented on <see cref="Vertex"/>.
/// </para>
/// <para>
/// The <c>Generate*</c> methods replace the current geometry with one named procedural primitive.
/// They fill <see cref="Vertices"/>, <see cref="Indices"/>, outward normals, and the first UV set,
/// and set <see cref="Topology"/> to <see cref="PrimitiveTopology.TriangleList"/>; an explicit
/// <see cref="ComputeBounds"/> result is discarded. They are pure CPU and need no GPU, tick thread,
/// or async path, so <see cref="BuildAsync"/> uploads the generated geometry like any other builder
/// content. Every generated triangle winds counter-clockwise around its outward normal. The
/// generators do not compute tangents; call <see cref="ComputeTangents"/> after generation when a
/// tangent basis is needed.
/// </para>
/// </remarks>
public sealed class MeshBuilder {
    private const int MIN_SUBDIVISIONS = 1;
    private const int MIN_TESSELLATION = 3;

    private static readonly (Vector3 Normal, Vector3 Right, Vector3 Up)[] _cubeFaces = [
        (new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)),
        (new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)),
        (new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f)),
        (new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
        (new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
        (new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
    ];

    private readonly ProceduralAssetService _service;
    private readonly List<Vertex> _vertices = [];
    private readonly List<uint> _indices = [];
    private PrimitiveTopology _topology;
    private BoundingBox? _bounds;
    private bool _generateCollisions;

    /// <summary>
    /// Initializes a builder. Game code uses <see cref="EngineContext.CreateMeshBuilder"/> so the
    /// builder always has the app's GPU upload service.
    /// </summary>
    /// <param name="service">The app's procedural asset service.</param>
    /// <param name="topology">The primitive topology of the mesh.</param>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="topology"/> is not a defined value.</exception>
    internal MeshBuilder(ProceduralAssetService service, PrimitiveTopology topology) {
        ArgumentNullException.ThrowIfNull(service);

        ValidateTopology(topology);
        _service = service;
        _topology = topology;
    }

    /// <summary>
    /// Gets or sets the primitive topology of the mesh. Only triangle topologies support normal and
    /// tangent generation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined topology.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public PrimitiveTopology Topology {
        get {
            _service.ThrowIfStopped();
            return _topology;
        }

        set {
            _service.ThrowIfStopped();
            ValidateTopology(value);
            _topology = value;
        }
    }

    /// <summary>
    /// Gets the mutable vertex list. Add vertices directly or replace the list contents with
    /// <see cref="SetVertices"/>.
    /// </summary>
    public List<Vertex> Vertices {
        get {
            _service.ThrowIfStopped();
            return _vertices;
        }
    }

    /// <summary>
    /// Gets the mutable 32-bit index list. Add indices directly or replace the list contents with
    /// <see cref="SetIndices"/>.
    /// </summary>
    public List<uint> Indices {
        get {
            _service.ThrowIfStopped();
            return _indices;
        }
    }

    /// <summary>
    /// Replaces every vertex with the supplied span. The builder keeps a copy.
    /// </summary>
    /// <param name="vertices">The new vertices.</param>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public void SetVertices(ReadOnlySpan<Vertex> vertices) {
        _service.ThrowIfStopped();
        _vertices.Clear();
        _vertices.AddRange(vertices);
        _bounds = null;
    }

    /// <summary>
    /// Replaces every index with the supplied span. The builder keeps a copy.
    /// </summary>
    /// <param name="indices">The new 32-bit indices.</param>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public void SetIndices(ReadOnlySpan<uint> indices) {
        _service.ThrowIfStopped();
        _indices.Clear();
        _indices.AddRange(indices);
        _bounds = null;
    }

    /// <summary>
    /// Computes the vertex normals from the triangle geometry.
    /// </summary>
    /// <param name="mode">
    /// <see cref="MeshNormalMode.Smooth"/> for shared area-weighted normals, or
    /// <see cref="MeshNormalMode.Flat"/> to split each triangle and give it its face normal.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    /// <exception cref="NotSupportedException">The topology is not a triangle topology.</exception>
    /// <exception cref="InvalidOperationException">
    /// The mesh has no vertices or indices, or the index count does not describe whole triangles, or
    /// an index is outside the vertex list.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// Smooth normals accumulate the unnormalized face normal (the cross product, so its length is
    /// twice the triangle area) per shared position and normalize the sum. A vertex that only belongs
    /// to degenerate triangles keeps a zero normal. Flat normals rebuild
    /// <see cref="Vertices"/> and <see cref="Indices"/>: every triangle gets three vertices with the
    /// normalized face normal, and a triangle strip keeps its topology by inserting one degenerate
    /// connecting index when the next triangle would start at an odd position.
    /// </remarks>
    public void ComputeNormals(MeshNormalMode mode = MeshNormalMode.Smooth) {
        _service.ThrowIfStopped();
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The normal mode is not a defined value.");
        }

        EnsureTriangleGeometry();
        if (mode == MeshNormalMode.Flat) {
            ComputeFlatNormals();
        } else {
            ComputeSmoothNormals();
        }
    }

    /// <summary>
    /// Computes the per-vertex tangents from the normals and the first UV set.
    /// </summary>
    /// <exception cref="NotSupportedException">The topology is not a triangle topology.</exception>
    /// <exception cref="InvalidOperationException">
    /// The mesh has no vertices or indices, the index count does not describe whole triangles, an
    /// index is outside the vertex list, a vertex normal is zero, or an attribute is not finite.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// Each triangle contributes its UV-space tangent and bitangent to the three vertices; the result
    /// is orthonormalized against the vertex normal and the bitangent handedness is stored in
    /// <see cref="Vertex.Tangent"/>.<c>W</c>. A vertex that only belongs to degenerate triangles
    /// (zero-area geometry or a collapsed UV triangle) keeps a zero tangent, and the <c>W</c> of a
    /// zero tangent is zero. Call <see cref="ComputeNormals"/> first.
    /// </remarks>
    public void ComputeTangents() {
        _service.ThrowIfStopped();
        EnsureTriangleGeometry();
        EnsureNormalsAndUvs();

        Vector3[] tangents = new Vector3[_vertices.Count];
        Vector3[] bitangents = new Vector3[_vertices.Count];
        for (int triangle = 0; triangle < TriangleCount; triangle++) {
            GetTriangle(triangle, out int index0, out int index1, out int index2);
            Vertex vertex0 = _vertices[index0];
            Vertex vertex1 = _vertices[index1];
            Vertex vertex2 = _vertices[index2];
            Vector3 edge1 = vertex1.Position - vertex0.Position;
            Vector3 edge2 = vertex2.Position - vertex0.Position;
            Vector2 deltaUv1 = vertex1.Uv - vertex0.Uv;
            Vector2 deltaUv2 = vertex2.Uv - vertex0.Uv;
            float determinant = (deltaUv1.X * deltaUv2.Y) - (deltaUv1.Y * deltaUv2.X);
            if (MathF.Abs(determinant) < 1e-12f) {
                continue;
            }

            float reciprocal = 1f / determinant;
            Vector3 tangent = ((edge1 * deltaUv2.Y) - (edge2 * deltaUv1.Y)) * reciprocal;
            Vector3 bitangent = ((edge2 * deltaUv1.X) - (edge1 * deltaUv2.X)) * reciprocal;
            tangents[index0] += tangent;
            tangents[index1] += tangent;
            tangents[index2] += tangent;
            bitangents[index0] += bitangent;
            bitangents[index1] += bitangent;
            bitangents[index2] += bitangent;
        }

        for (int index = 0; index < _vertices.Count; index++) {
            Vertex vertex = _vertices[index];
            Vector3 normal = Vector3.Normalize(vertex.Normal);
            Vector3 tangent = tangents[index];
            if (tangent.LengthSquared() <= 1e-12f) {
                vertex.Tangent = Vector4.Zero;
                _vertices[index] = vertex;
                continue;
            }

            Vector3 orthogonal = tangent - (normal * Vector3.Dot(normal, tangent));
            if (orthogonal.LengthSquared() <= 1e-12f) {
                vertex.Tangent = Vector4.Zero;
                _vertices[index] = vertex;
                continue;
            }

            orthogonal = Vector3.Normalize(orthogonal);
            Vector3 orthonormalBitangent = Vector3.Cross(normal, orthogonal);
            float handedness = Vector3.Dot(orthonormalBitangent, bitangents[index]) < 0f ? -1f : 1f;
            vertex.Tangent = new Vector4(orthogonal, handedness);
            _vertices[index] = vertex;
        }
    }

    /// <summary>
    /// Selects that <see cref="BuildAsync"/> builds a collision acceleration structure from the same
    /// geometry snapshot it validates and uploads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is an imperative builder step like <see cref="ComputeNormals"/> and
    /// <see cref="ComputeTangents"/>: it does not change the builder geometry. The structure is built
    /// by the next <see cref="BuildAsync"/> from that build's snapshot and is exposed on the returned
    /// mesh as <see cref="Mesh.HasCollisionAcceleration"/>. The selection persists for every later
    /// build from this builder, and calling this method more than once has no additional effect.
    /// </para>
    /// <para>
    /// <see cref="Collision.GetRayMesh"/> traverses the structure when the mesh has one and tests every
    /// triangle otherwise; both paths return the same nearest hit. A snapshot with no non-degenerate
    /// triangle builds without a structure. A failed or canceled build creates no asset, leaves the
    /// builder reusable, and does not let a later geometry change affect the structure.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The topology is not <see cref="PrimitiveTopology.TriangleList"/> or
    /// <see cref="PrimitiveTopology.TriangleStrip"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public void GenerateCollisions() {
        _service.ThrowIfStopped();
        if (_topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw new NotSupportedException(
                $"Collision generation supports only triangle topologies; the builder topology is {_topology}.");
        }

        _generateCollisions = true;
    }

    /// <summary>
    /// Replaces the builder geometry with a regular polygon disc in the XZ plane, the raylib
    /// <c>GenMeshPoly</c> shape.
    /// </summary>
    /// <param name="sides">The number of polygon sides. The minimum is 3.</param>
    /// <param name="radius">The distance from the center to every corner. It must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sides"/> is less than 3, or <paramref name="radius"/> is not positive and finite.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The polygon is a triangle fan with one center vertex and one perimeter vertex per side. It has
    /// <c>sides + 1</c> vertices and <c>sides * 3</c> indices, its front face is toward +Y, and every
    /// normal is <c>(0, 1, 0)</c>. UVs map the disc into the unit square: the center is
    /// <c>(0.5, 0.5)</c> and the first corner, on +Z, is <c>(0.5, 0)</c>, so the perimeter follows the
    /// unit circle around the center.
    /// </remarks>
    public void GeneratePoly(int sides, float radius) {
        _service.ThrowIfStopped();
        ValidateTessellation(sides, nameof(sides));
        ValidatePositiveFinite(radius, nameof(radius));

        BeginTriangleGeometry();
        int center = AddVertex(Vector3.Zero, Vector3.UnitY, new Vector2(0.5f, 0.5f));
        int first = _vertices.Count;
        for (int side = 0; side < sides; side++) {
            float angle = MathF.Tau * side / sides;
            float sine = MathF.Sin(angle);
            float cosine = MathF.Cos(angle);
            AddVertex(
                new Vector3(sine * radius, 0f, cosine * radius),
                Vector3.UnitY,
                new Vector2(0.5f + (0.5f * sine), 0.5f - (0.5f * cosine)));
        }

        for (int side = 0; side < sides; side++) {
            AddTriangle(center, first + side, first + ((side + 1) % sides));
        }
    }

    /// <summary>
    /// Replaces the builder geometry with a subdivided plane in the XZ plane, the raylib
    /// <c>GenMeshPlane</c> shape.
    /// </summary>
    /// <param name="width">The extent along X. It must be positive and finite.</param>
    /// <param name="length">The extent along Z. It must be positive and finite.</param>
    /// <param name="resX">The number of quad subdivisions along X. The minimum is 1.</param>
    /// <param name="resZ">The number of quad subdivisions along Z. The minimum is 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="length"/> is not positive and finite, or
    /// <paramref name="resX"/> or <paramref name="resZ"/> is less than 1.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The plane covers <c>[-width / 2, width / 2]</c> by <c>[-length / 2, length / 2]</c> at
    /// <c>y = 0</c>, has <c>(resX + 1) * (resZ + 1)</c> vertices and <c>resX * resZ * 6</c> indices,
    /// and its front face is toward +Y. Every normal is <c>(0, 1, 0)</c>. UV <c>u</c> runs from 0 at
    /// the -X edge to 1 at the +X edge; UV <c>v</c> runs from 0 at the -Z edge to 1 at the +Z edge.
    /// </remarks>
    public void GeneratePlane(float width, float length, int resX, int resZ) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(width, nameof(width));
        ValidatePositiveFinite(length, nameof(length));
        ValidateSubdivisions(resX, nameof(resX));
        ValidateSubdivisions(resZ, nameof(resZ));

        BeginTriangleGeometry();
        int rowLength = resX + 1;
        for (int row = 0; row <= resZ; row++) {
            float v = (float)row / resZ;
            float z = ((v - 0.5f) * length);
            for (int column = 0; column <= resX; column++) {
                float u = (float)column / resX;
                float x = ((u - 0.5f) * width);
                AddVertex(new Vector3(x, 0f, z), Vector3.UnitY, new Vector2(u, v));
            }
        }

        for (int row = 0; row < resZ; row++) {
            for (int column = 0; column < resX; column++) {
                int lowerLeft = (row * rowLength) + column;
                int lowerRight = lowerLeft + 1;
                int upperLeft = lowerLeft + rowLength;
                int upperRight = upperLeft + 1;
                AddTriangle(lowerLeft, upperLeft, lowerRight);
                AddTriangle(lowerRight, upperLeft, upperRight);
            }
        }
    }

    /// <summary>
    /// Replaces the builder geometry with a centered box, the raylib <c>GenMeshCube</c> shape.
    /// </summary>
    /// <param name="width">The extent along X. It must be positive and finite.</param>
    /// <param name="height">The extent along Y. It must be positive and finite.</param>
    /// <param name="length">The extent along Z. It must be positive and finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive and finite.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The box is centered at the origin and extends <c>width / 2</c>, <c>height / 2</c>, and
    /// <c>length / 2</c> along each axis. It has 24 vertices, one four-vertex quad per face, and 36
    /// indices; every face keeps its outward face normal instead of a smoothed corner normal. Each
    /// face's UVs cover 0..1 with the origin at the face's top-left corner and V increasing
    /// downward, matching the engine's top-left UV convention.
    /// </remarks>
    public void GenerateCube(float width, float height, float length) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(width, nameof(width));
        ValidatePositiveFinite(height, nameof(height));
        ValidatePositiveFinite(length, nameof(length));

        BeginTriangleGeometry();
        Vector3 half = new(width * 0.5f, height * 0.5f, length * 0.5f);
        foreach ((Vector3 normal, Vector3 rightAxis, Vector3 upAxis) in _cubeFaces) {
            Vector3 center = normal * Extent(normal, half);
            Vector3 right = rightAxis * Extent(rightAxis, half);
            Vector3 up = upAxis * Extent(upAxis, half);
            int first = _vertices.Count;
            AddVertex(center + right - up, normal, new Vector2(1f, 1f));
            AddVertex(center + right + up, normal, new Vector2(1f, 0f));
            AddVertex(center - right + up, normal, new Vector2(0f, 0f));
            AddVertex(center - right - up, normal, new Vector2(0f, 1f));
            AddTriangle(first, first + 1, first + 2);
            AddTriangle(first, first + 2, first + 3);
        }
    }

    /// <summary>
    /// Replaces the builder geometry with a UV sphere, the raylib <c>GenMeshSphere</c> shape.
    /// </summary>
    /// <param name="radius">The sphere radius. It must be positive and finite.</param>
    /// <param name="rings">The number of latitude bands from pole to pole. The minimum is 3.</param>
    /// <param name="slices">The number of longitude segments. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not positive and finite, or <paramref name="rings"/> or
    /// <paramref name="slices"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The sphere is centered at the origin with its poles on +Y and -Y. Each pole is one vertex and
    /// the seam duplicates the first meridian at <c>u = 1</c>, so the mesh has
    /// <c>(rings - 1) * (slices + 1) + 2</c> vertices and <c>slices * (rings - 1) * 6</c> indices.
    /// Smooth normals point outward and equal the position divided by <paramref name="radius"/>. UV
    /// <c>u</c> runs 0..1 around the +Y axis starting at +Z and turning toward +X; UV <c>v</c> is 0 at
    /// the +Y pole and 1 at the -Y pole.
    /// </remarks>
    public void GenerateSphere(float radius, int rings, int slices) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidateTessellation(rings, nameof(rings));
        ValidateTessellation(slices, nameof(slices));

        BeginTriangleGeometry();
        int top = AddVertex(new Vector3(0f, radius, 0f), Vector3.UnitY, new Vector2(0.5f, 0f));
        int firstRing = _vertices.Count;
        int rowLength = slices + 1;
        for (int ring = 1; ring < rings; ring++) {
            float v = (float)ring / rings;
            float phi = v * MathF.PI;
            float y = radius * MathF.Cos(phi);
            float ringRadius = radius * MathF.Sin(phi);
            for (int slice = 0; slice <= slices; slice++) {
                float u = (float)slice / slices;
                float theta = MathF.Tau * (slice % slices) / slices;
                float sine = MathF.Sin(theta);
                float cosine = MathF.Cos(theta);
                Vector3 normal = new(sine * MathF.Sin(phi), MathF.Cos(phi), cosine * MathF.Sin(phi));
                AddVertex(
                    new Vector3(ringRadius * sine, y, ringRadius * cosine),
                    normal,
                    new Vector2(u, v));
            }
        }

        int bottom = AddVertex(new Vector3(0f, -radius, 0f), -Vector3.UnitY, new Vector2(0.5f, 1f));
        for (int slice = 0; slice < slices; slice++) {
            AddTriangle(top, firstRing + slice, firstRing + slice + 1);
        }

        for (int ring = 0; ring < rings - 2; ring++) {
            int upper = firstRing + (ring * rowLength);
            int lower = upper + rowLength;
            for (int slice = 0; slice < slices; slice++) {
                AddTriangle(upper + slice, lower + slice, upper + slice + 1);
                AddTriangle(upper + slice + 1, lower + slice, lower + slice + 1);
            }
        }

        int lastRing = firstRing + ((rings - 2) * rowLength);
        for (int slice = 0; slice < slices; slice++) {
            AddTriangle(lastRing + slice, bottom, lastRing + slice + 1);
        }
    }

    /// <summary>
    /// Replaces the builder geometry with the upper half of a UV sphere, the raylib
    /// <c>GenMeshHemiSphere</c> shape. The flat equator is open and has no bottom cap.
    /// </summary>
    /// <param name="radius">The sphere radius. It must be positive and finite.</param>
    /// <param name="rings">The number of latitude bands from the pole to the equator. The minimum is 3.</param>
    /// <param name="slices">The number of longitude segments. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not positive and finite, or <paramref name="rings"/> or
    /// <paramref name="slices"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The hemisphere covers the +Y half of the sphere from the pole at <c>(0, radius, 0)</c> down to
    /// the equator at <c>y = 0</c> and is centered on the Y axis. It has
    /// <c>rings * (slices + 1) + 1</c> vertices and <c>slices * (2 * rings - 1) * 3</c> indices; the
    /// equator ring is the open boundary, so there is no cap and the underside is visible from below.
    /// Smooth normals point outward and equal the position divided by <paramref name="radius"/>. UVs
    /// match the full sphere: <c>u</c> runs 0..1 around +Y starting at +Z, and <c>v</c> is 0 at the
    /// pole and 1 at the equator.
    /// </remarks>
    public void GenerateHemiSphere(float radius, int rings, int slices) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidateTessellation(rings, nameof(rings));
        ValidateTessellation(slices, nameof(slices));

        BeginTriangleGeometry();
        int top = AddVertex(new Vector3(0f, radius, 0f), Vector3.UnitY, new Vector2(0.5f, 0f));
        int firstRing = _vertices.Count;
        int rowLength = slices + 1;
        for (int ring = 1; ring <= rings; ring++) {
            float v = (float)ring / rings;
            float phi = v * MathF.PI * 0.5f;
            bool equator = ring == rings;
            float y = equator ? 0f : radius * MathF.Cos(phi);
            float ringRadius = radius * MathF.Sin(phi);
            for (int slice = 0; slice <= slices; slice++) {
                float u = (float)slice / slices;
                float theta = MathF.Tau * (slice % slices) / slices;
                float sine = MathF.Sin(theta);
                float cosine = MathF.Cos(theta);
                Vector3 normal = equator
                    ? new Vector3(sine, 0f, cosine)
                    : new Vector3(sine * MathF.Sin(phi), MathF.Cos(phi), cosine * MathF.Sin(phi));
                AddVertex(
                    new Vector3(ringRadius * sine, y, ringRadius * cosine),
                    normal,
                    new Vector2(u, v));
            }
        }

        for (int slice = 0; slice < slices; slice++) {
            AddTriangle(top, firstRing + slice, firstRing + slice + 1);
        }

        for (int ring = 0; ring < rings - 1; ring++) {
            int upper = firstRing + (ring * rowLength);
            int lower = upper + rowLength;
            for (int slice = 0; slice < slices; slice++) {
                AddTriangle(upper + slice, lower + slice, upper + slice + 1);
                AddTriangle(upper + slice + 1, lower + slice, lower + slice + 1);
            }
        }
    }

    /// <summary>
    /// Replaces the builder geometry with a capped cylinder, the raylib <c>GenMeshCylinder</c> shape.
    /// </summary>
    /// <param name="radius">The base and top radius. It must be positive and finite.</param>
    /// <param name="height">The height along +Y. It must be positive and finite.</param>
    /// <param name="slices">The number of segments around the axis. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="height"/> is not positive and finite, or
    /// <paramref name="slices"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The cylinder stands on the XZ plane: its base is at <c>y = 0</c>, its top is at
    /// <c>y = height</c>, and its axis is +Y. It has side quads plus one cap at each end, for
    /// <c>4 * slices + 4</c> vertices and <c>12 * slices</c> indices. Side normals are horizontal and
    /// point away from the axis; the top cap normal is +Y and the bottom cap normal is -Y. Side UV
    /// <c>u</c> runs 0..1 around the axis starting at +Z and turning toward +X; side UV <c>v</c> is 0
    /// at the top and 1 at the base. Each cap uses the planar disc mapping
    /// <c>u = 0.5 + 0.5 * sin(angle)</c>, <c>v = 0.5 - 0.5 * cos(angle)</c> centered at
    /// <c>(0.5, 0.5)</c>.
    /// </remarks>
    public void GenerateCylinder(float radius, float height, int slices) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidatePositiveFinite(height, nameof(height));
        ValidateTessellation(slices, nameof(slices));

        BeginTriangleGeometry();
        int sideStart = _vertices.Count;
        for (int slice = 0; slice <= slices; slice++) {
            float u = (float)slice / slices;
            float theta = MathF.Tau * (slice % slices) / slices;
            float sine = MathF.Sin(theta);
            float cosine = MathF.Cos(theta);
            Vector3 normal = new(sine, 0f, cosine);
            Vector3 top = new(sine * radius, height, cosine * radius);
            Vector3 bottom = new(sine * radius, 0f, cosine * radius);
            AddVertex(top, normal, new Vector2(u, 0f));
            AddVertex(bottom, normal, new Vector2(u, 1f));
        }

        for (int slice = 0; slice < slices; slice++) {
            int top = sideStart + (slice * 2);
            int bottom = top + 1;
            int nextTop = top + 2;
            int nextBottom = nextTop + 1;
            AddTriangle(bottom, nextBottom, top);
            AddTriangle(top, nextBottom, nextTop);
        }

        AddCap(height, radius, slices, isTop: true);
        AddCap(0f, radius, slices, isTop: false);
    }

    /// <summary>
    /// Replaces the builder geometry with a capped cone, the raylib <c>GenMeshCone</c> shape.
    /// </summary>
    /// <param name="radius">The base radius. It must be positive and finite.</param>
    /// <param name="height">The height along +Y. It must be positive and finite.</param>
    /// <param name="slices">The number of segments around the axis. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="height"/> is not positive and finite, or
    /// <paramref name="slices"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The cone stands on the XZ plane: its base circle is at <c>y = 0</c>, its apex is at
    /// <c>(0, height, 0)</c>, and its axis is +Y. It has a smooth side and one base cap, for
    /// <c>3 * slices + 3</c> vertices and <c>6 * slices</c> indices. Side normals slant outward and
    /// up; the base cap normal is -Y. Side UV <c>u</c> runs 0..1 around the axis starting at +Z and
    /// turning toward +X; side UV <c>v</c> is 0 at the apex and 1 at the base. The base cap uses the
    /// planar disc mapping <c>u = 0.5 + 0.5 * sin(angle)</c>,
    /// <c>v = 0.5 - 0.5 * cos(angle)</c> centered at <c>(0.5, 0.5)</c>.
    /// </remarks>
    public void GenerateCone(float radius, float height, int slices) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidatePositiveFinite(height, nameof(height));
        ValidateTessellation(slices, nameof(slices));

        BeginTriangleGeometry();
        int sideStart = _vertices.Count;
        float normalScale = 1f / MathF.Sqrt((height * height) + (radius * radius));
        for (int slice = 0; slice <= slices; slice++) {
            float u = (float)slice / slices;
            float theta = MathF.Tau * (slice % slices) / slices;
            float sine = MathF.Sin(theta);
            float cosine = MathF.Cos(theta);
            Vector3 normal = new(height * sine * normalScale, radius * normalScale, height * cosine * normalScale);
            AddVertex(new Vector3(0f, height, 0f), normal, new Vector2(u, 0f));
            AddVertex(
                new Vector3(sine * radius, 0f, cosine * radius),
                normal,
                new Vector2(u, 1f));
        }

        for (int slice = 0; slice < slices; slice++) {
            int apex = sideStart + (slice * 2);
            int baseVertex = apex + 1;
            int nextBase = baseVertex + 2;
            AddTriangle(apex, baseVertex, nextBase);
        }

        AddCap(0f, radius, slices, isTop: false);
    }

    /// <summary>
    /// Replaces the builder geometry with a torus, the raylib <c>GenMeshTorus</c> shape.
    /// </summary>
    /// <param name="radius">The major radius from the center to the tube center. It must be positive and finite.</param>
    /// <param name="size">The tube (minor) radius. It must be positive and finite.</param>
    /// <param name="radSeg">The number of segments around the major ring. The minimum is 3.</param>
    /// <param name="sides">The number of segments around the tube. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="size"/> is not positive and finite, or
    /// <paramref name="radSeg"/> or <paramref name="sides"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The torus is centered at the origin and lies in the XZ plane, so its hole axis is +Y.
    /// <paramref name="radius"/> is the distance from the center to the tube center and
    /// <paramref name="size"/> is the tube radius, which makes the outer radius
    /// <c>radius + size</c>. The mesh has <c>(radSeg + 1) * (sides + 1)</c> vertices and
    /// <c>radSeg * sides * 6</c> indices, with both seams duplicated so the UVs are continuous. UV
    /// <c>u</c> runs 0..1 around the major ring starting at +Z and turning toward +X; UV <c>v</c> runs
    /// 0..1 around the tube, starting at the outer equator and turning toward +Y. Normals point
    /// outward from the tube center line.
    /// </remarks>
    public void GenerateTorus(float radius, float size, int radSeg, int sides) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidatePositiveFinite(size, nameof(size));
        ValidateTessellation(radSeg, nameof(radSeg));
        ValidateTessellation(sides, nameof(sides));

        BeginTriangleGeometry();
        int rowLength = sides + 1;
        for (int segment = 0; segment <= radSeg; segment++) {
            float u = (float)segment / radSeg;
            float theta = MathF.Tau * (segment % radSeg) / radSeg;
            float sine = MathF.Sin(theta);
            float cosine = MathF.Cos(theta);
            for (int side = 0; side <= sides; side++) {
                float v = (float)side / sides;
                float phi = MathF.Tau * (side % sides) / sides;
                float cosPhi = MathF.Cos(phi);
                float sinPhi = MathF.Sin(phi);
                Vector3 normal = new(sine * cosPhi, sinPhi, cosine * cosPhi);
                float ringRadius = radius + (size * cosPhi);
                AddVertex(
                    new Vector3(ringRadius * sine, size * sinPhi, ringRadius * cosine),
                    normal,
                    new Vector2(u, v));
            }
        }

        for (int segment = 0; segment < radSeg; segment++) {
            for (int side = 0; side < sides; side++) {
                int a = (segment * rowLength) + side;
                int b = a + rowLength;
                int c = a + 1;
                int d = b + 1;
                AddTriangle(a, b, c);
                AddTriangle(c, b, d);
            }
        }
    }

    /// <summary>
    /// Replaces the builder geometry with a trefoil-knot tube, the raylib <c>GenMeshKnot</c> shape.
    /// </summary>
    /// <param name="radius">The winding radius of the knot center line. It must be positive and finite.</param>
    /// <param name="size">The tube radius. It must be positive and finite.</param>
    /// <param name="radSeg">The number of segments along the knot. The minimum is 3.</param>
    /// <param name="sides">The number of segments around the tube. The minimum is 3.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="size"/> is not positive and finite, or
    /// <paramref name="radSeg"/> or <paramref name="sides"/> is less than 3.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// The knot is a closed trefoil tube centered at the origin. Its loop lies in the XZ plane and
    /// its three lobes rise along +Y and -Y; every vertex is within
    /// <c>radius + size</c> of the origin. <paramref name="radius"/> is the winding radius (the
    /// center line's maximum distance from the origin) and <paramref name="size"/> is the tube
    /// radius. The mesh has <c>(radSeg + 1) * (sides + 1)</c> vertices and
    /// <c>radSeg * sides * 6</c> indices, with both seams duplicated so the UVs are continuous. UV
    /// <c>u</c> runs 0..1 along the closed curve; UV <c>v</c> runs 0..1 around the tube. Normals
    /// point outward from the tube center line.
    /// </remarks>
    public void GenerateKnot(float radius, float size, int radSeg, int sides) {
        _service.ThrowIfStopped();
        ValidatePositiveFinite(radius, nameof(radius));
        ValidatePositiveFinite(size, nameof(size));
        ValidateTessellation(radSeg, nameof(radSeg));
        ValidateTessellation(sides, nameof(sides));

        BeginTriangleGeometry();
        // The par_shapes trefoil center line has a maximum radius of a + b; scale it so the requested
        // winding radius is the actual maximum.
        const float curveRadius = 0.8f;
        float scale = radius / curveRadius;
        int rowLength = sides + 1;
        for (int segment = 0; segment <= radSeg; segment++) {
            float u = (float)segment / radSeg;
            float angle = MathF.Tau * 2f * (segment % radSeg) / radSeg;
            float wavelet = 1.5f * angle;
            float ringRadius = 0.5f + (0.3f * MathF.Cos(wavelet));
            float ringRadiusDerivative = -0.45f * MathF.Sin(wavelet);
            float sine = MathF.Sin(angle);
            float cosine = MathF.Cos(angle);
            Vector3 center = new(
                ringRadius * cosine * scale,
                0.5f * MathF.Sin(wavelet) * scale,
                ringRadius * sine * scale);
            Vector3 tangent = new(
                (ringRadiusDerivative * cosine) - (ringRadius * sine),
                0.75f * MathF.Cos(wavelet),
                (ringRadiusDerivative * sine) + (ringRadius * cosine));
            tangent = Vector3.Normalize(tangent);
            Vector3 across = new(tangent.Z, 0f, -tangent.X);
            across = across.LengthSquared() <= 1e-12f
                ? Vector3.UnitX
                : Vector3.Normalize(across);
            Vector3 vertical = Vector3.Cross(tangent, across);
            for (int side = 0; side <= sides; side++) {
                float v = (float)side / sides;
                float phi = MathF.Tau * (side % sides) / sides;
                float cosPhi = MathF.Cos(phi);
                float sinPhi = MathF.Sin(phi);
                Vector3 normal = Vector3.Normalize((across * cosPhi) + (vertical * sinPhi));
                AddVertex(center + (normal * size), normal, new Vector2(u, v));
            }
        }

        for (int segment = 0; segment < radSeg; segment++) {
            for (int side = 0; side < sides; side++) {
                int a = (segment * rowLength) + side;
                int b = a + rowLength;
                int c = a + 1;
                int d = b + 1;
                AddTriangle(a, c, b);
                AddTriangle(c, d, b);
            }
        }
    }

    /// <summary>
    /// Computes the mesh-space bounds of the current vertices and stores them for the next build.
    /// </summary>
    /// <returns>The bounds of every vertex position.</returns>
    /// <exception cref="InvalidOperationException">The builder has no vertices.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// <see cref="BuildAsync"/> computes the bounds when this method was not called. The stored bounds
    /// are not recomputed when the exposed <see cref="Vertices"/> list is mutated afterwards; call
    /// this method again after changing positions.
    /// </remarks>
    public BoundingBox ComputeBounds() {
        _service.ThrowIfStopped();
        BoundingBox bounds = ComputeBoundsCore(_vertices);
        _bounds = bounds;
        return bounds;
    }

    /// <summary>
    /// Uploads the mesh on the render thread and returns the immutable asset after the GPU resource
    /// is ready.
    /// </summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The created mesh. The caller owns one reference and must dispose it exactly once.</returns>
    /// <exception cref="InvalidOperationException">
    /// The mesh has no vertices or indices, the index count does not match the topology, an index is
    /// outside the vertex list, or an attribute is not finite.
    /// </exception>
    /// <exception cref="OperationCanceledException">The token was canceled before the upload completed.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <exception cref="AssetLoadException">The GPU upload failed.</exception>
    /// <remarks>
    /// The builder snapshots its vertices and indices before the upload, so it is reusable whether the
    /// build succeeds, fails, or is canceled. A failed or canceled build destroys any GPU resource it
    /// created. The returned mesh has <see cref="Mesh.IsProcedural"/> set, a reserved synthetic
    /// <see cref="Mesh.Path"/>, and its CPU vertices and indices exposed on the asset. When
    /// <see cref="GenerateCollisions"/> was called, the build also creates the collision acceleration
    /// structure from the same snapshot and exposes it through <see cref="Mesh.HasCollisionAcceleration"/>.
    /// </remarks>
    public async Task<Mesh> BuildAsync(CancellationToken cancellationToken = default) {
        _service.ThrowIfStopped();
        ValidateForBuild();

        Vertex[] vertices = [.. _vertices];
        uint[] indices = [.. _indices];
        BoundingBox bounds = _bounds ?? ComputeBoundsCore(vertices);
        PrimitiveTopology topology = _topology;
        MeshCollisionData? collision = null;
        if (_generateCollisions) {
            if (topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
                throw new NotSupportedException(
                    $"Collision generation supports only triangle topologies; the builder topology is {topology}.");
            }

            Vector3[] positions = new Vector3[vertices.Length];
            for (int index = 0; index < vertices.Length; index++) {
                positions[index] = vertices[index].Position;
            }

            collision = MeshCollisionBuilder.Build(positions, indices, topology);
        }

        int id = ProceduralAssetIds.NextMeshId();
        string canonicalPath = ProceduralAssetIds.MeshPath(id);
        MeshAssetDescriptor descriptor = new() {
            Payload = canonicalPath,
            VertexCount = checked((uint)vertices.Length),
            IndexCount = checked((uint)indices.Length),
            Bounds = [
                bounds.Min.X,
                bounds.Min.Y,
                bounds.Min.Z,
                bounds.Max.X,
                bounds.Max.Y,
                bounds.Max.Z,
            ],
            Topology = topology,
        };
        MeshGpuUpload upload = new(canonicalPath, descriptor, MeshPayloadCodec.Encode(vertices, indices));
        GpuAssetResource resource = await _service.UploadAsync(upload, cancellationToken).ConfigureAwait(false);
        try {
            EngineAsset lifetime = new(canonicalPath, resource, _service.QueueDestruction, static () => { });
            lifetime.AddOwnerReference();
            return new Mesh(lifetime, descriptor, vertices, indices, isProcedural: true, collision);
        } catch {
            _service.QueueDestruction(resource);
            throw;
        }
    }

    private void BeginTriangleGeometry() {
        _vertices.Clear();
        _indices.Clear();
        _topology = PrimitiveTopology.TriangleList;
        _bounds = null;
    }

    private int AddVertex(Vector3 position, Vector3 normal, Vector2 uv) {
        int index = _vertices.Count;
        _vertices.Add(new Vertex(position, normal, uv: uv));
        return index;
    }

    private void AddTriangle(int index0, int index1, int index2) {
        _indices.Add((uint)index0);
        _indices.Add((uint)index1);
        _indices.Add((uint)index2);
    }

    private void AddCap(float y, float radius, int slices, bool isTop) {
        Vector3 normal = isTop ? Vector3.UnitY : -Vector3.UnitY;
        int center = AddVertex(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
        int first = _vertices.Count;
        for (int slice = 0; slice < slices; slice++) {
            float theta = MathF.Tau * slice / slices;
            float sine = MathF.Sin(theta);
            float cosine = MathF.Cos(theta);
            AddVertex(
                new Vector3(sine * radius, y, cosine * radius),
                normal,
                new Vector2(0.5f + (0.5f * sine), 0.5f - (0.5f * cosine)));
        }

        for (int slice = 0; slice < slices; slice++) {
            int current = first + slice;
            int next = first + ((slice + 1) % slices);
            if (isTop) {
                AddTriangle(center, current, next);
            } else {
                AddTriangle(center, next, current);
            }
        }
    }

    private static float Extent(Vector3 axis, Vector3 half) =>
        (MathF.Abs(axis.X) * half.X) + (MathF.Abs(axis.Y) * half.Y) + (MathF.Abs(axis.Z) * half.Z);

    private static void ValidateTessellation(int value, string parameterName) {
        if (value < MIN_TESSELLATION) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The tessellation must be at least {MIN_TESSELLATION}.");
        }
    }

    private static void ValidateSubdivisions(int value, string parameterName) {
        if (value < MIN_SUBDIVISIONS) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The subdivision count must be at least {MIN_SUBDIVISIONS}.");
        }
    }

    private static void ValidatePositiveFinite(float value, string parameterName) {
        if (!float.IsFinite(value) || value <= 0f) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be positive and finite.");
        }
    }

    private static void ValidateTopology(PrimitiveTopology topology) {
        if (!Enum.IsDefined(topology)) {
            throw new ArgumentOutOfRangeException(
                nameof(topology),
                topology,
                "The primitive topology is not a defined value.");
        }
    }

    private void EnsureTriangleGeometry() {
        if (_topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw new NotSupportedException(
                $"Normal and tangent generation supports only triangle topologies; the builder topology is {_topology}.");
        }

        if (_vertices.Count == 0) {
            throw new InvalidOperationException("The mesh has no vertices.");
        }

        if (_topology == PrimitiveTopology.TriangleList) {
            if (_indices.Count < 3 || _indices.Count % 3 != 0) {
                throw new InvalidOperationException(
                    $"A triangle list requires at least three indices in whole triangles; the builder has {_indices.Count}.");
            }
        } else if (_indices.Count < 3) {
            throw new InvalidOperationException(
                $"A triangle strip requires at least three indices; the builder has {_indices.Count}.");
        }

        for (int index = 0; index < _indices.Count; index++) {
            if (_indices[index] >= (uint)_vertices.Count) {
                throw new InvalidOperationException(
                    $"The index {_indices[index]} at position {index} is outside the {_vertices.Count} vertices.");
            }
        }
    }

    private void EnsureNormalsAndUvs() {
        for (int index = 0; index < _vertices.Count; index++) {
            Vertex vertex = _vertices[index];
            if (!IsFinite(vertex.Normal) || !IsFinite(vertex.Uv)) {
                throw new InvalidOperationException(
                    $"The normal or first UV of vertex {index} is not finite.");
            }

            if (vertex.Normal.LengthSquared() <= 0f) {
                throw new InvalidOperationException(
                    $"The normal of vertex {index} is zero; compute the normals before the tangents.");
            }
        }
    }

    private void ComputeSmoothNormals() {
        Dictionary<Vector3, Vector3> accumulated = [];
        for (int triangle = 0; triangle < TriangleCount; triangle++) {
            GetTriangle(triangle, out int index0, out int index1, out int index2);
            Vector3 position0 = _vertices[index0].Position;
            Vector3 position1 = _vertices[index1].Position;
            Vector3 position2 = _vertices[index2].Position;
            Vector3 face = Vector3.Cross(position1 - position0, position2 - position0);
            if (face.LengthSquared() <= 0f) {
                continue;
            }

            Accumulate(accumulated, position0, face);
            Accumulate(accumulated, position1, face);
            Accumulate(accumulated, position2, face);
        }

        for (int index = 0; index < _vertices.Count; index++) {
            Vertex vertex = _vertices[index];
            vertex.Normal = accumulated.TryGetValue(vertex.Position, out Vector3 sum)
                && sum.LengthSquared() > 0f
                    ? Vector3.Normalize(sum)
                    : Vector3.Zero;
            _vertices[index] = vertex;
        }
    }

    private void ComputeFlatNormals() {
        List<Vertex> source = [.. _vertices];
        List<Vertex> vertices = new(TriangleCount * 3);
        List<uint> indices = new(TriangleCount * 3);
        for (int triangle = 0; triangle < TriangleCount; triangle++) {
            GetTriangle(triangle, out int index0, out int index1, out int index2);
            Vertex vertex0 = source[index0];
            Vertex vertex1 = source[index1];
            Vertex vertex2 = source[index2];
            Vector3 face = Vector3.Cross(
                vertex1.Position - vertex0.Position,
                vertex2.Position - vertex0.Position);
            Vector3 normal = face.LengthSquared() > 0f ? Vector3.Normalize(face) : Vector3.Zero;

            if (_topology == PrimitiveTopology.TriangleStrip && (indices.Count & 1) != 0) {
                // A strip flips the winding of a triangle that starts at an odd index. Duplicating the
                // last index shifts the next triangle to an even position and inserts a degenerate
                // triangle that the rasterizer discards.
                indices.Add(indices[^1]);
            }

            vertex0.Normal = normal;
            vertex1.Normal = normal;
            vertex2.Normal = normal;
            indices.Add((uint)vertices.Count);
            vertices.Add(vertex0);
            indices.Add((uint)vertices.Count);
            vertices.Add(vertex1);
            indices.Add((uint)vertices.Count);
            vertices.Add(vertex2);
        }

        _vertices.Clear();
        _vertices.AddRange(vertices);
        _indices.Clear();
        _indices.AddRange(indices);
    }

    private void ValidateForBuild() {
        if (_vertices.Count == 0) {
            throw new InvalidOperationException("The mesh must contain at least one vertex.");
        }

        if (_indices.Count == 0) {
            throw new InvalidOperationException("The mesh must contain at least one index.");
        }

        ValidateIndexCount(_topology, _indices.Count);
        for (int index = 0; index < _indices.Count; index++) {
            if (_indices[index] >= (uint)_vertices.Count) {
                throw new InvalidOperationException(
                    $"The index {_indices[index]} at position {index} is outside the {_vertices.Count} vertices.");
            }
        }

        for (int index = 0; index < _vertices.Count; index++) {
            if (!IsFinite(_vertices[index])) {
                throw new InvalidOperationException(
                    $"The vertex at index {index} has a non-finite attribute.");
            }
        }
    }

    private static void ValidateIndexCount(PrimitiveTopology topology, int indexCount) {
        bool valid = topology switch {
            PrimitiveTopology.TriangleList => indexCount >= 3 && indexCount % 3 == 0,
            PrimitiveTopology.TriangleStrip => indexCount >= 3,
            PrimitiveTopology.LineList => indexCount >= 2 && indexCount % 2 == 0,
            PrimitiveTopology.LineStrip => indexCount >= 2,
            PrimitiveTopology.PointList => indexCount >= 1,
            _ => false,
        };
        if (!valid) {
            throw new InvalidOperationException(
                $"A {topology} mesh cannot have {indexCount} indices; the count does not describe whole primitives.");
        }
    }

    private static BoundingBox ComputeBoundsCore(IReadOnlyList<Vertex> vertices) {
        if (vertices.Count == 0) {
            throw new InvalidOperationException("The mesh must contain at least one vertex.");
        }

        Vector3 minimum = vertices[0].Position;
        Vector3 maximum = minimum;
        for (int index = 1; index < vertices.Count; index++) {
            Vector3 position = vertices[index].Position;
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return new BoundingBox(minimum, maximum);
    }

    private int TriangleCount => _topology == PrimitiveTopology.TriangleList
        ? _indices.Count / 3
        : _indices.Count - 2;

    private void GetTriangle(int triangle, out int index0, out int index1, out int index2) {
        if (_topology == PrimitiveTopology.TriangleList) {
            int offset = triangle * 3;
            index0 = checked((int)_indices[offset]);
            index1 = checked((int)_indices[offset + 1]);
            index2 = checked((int)_indices[offset + 2]);
            return;
        }

        index0 = checked((int)_indices[triangle]);
        index1 = checked((int)_indices[triangle + 1]);
        index2 = checked((int)_indices[triangle + 2]);
        if ((triangle & 1) != 0) {
            (index0, index1) = (index1, index0);
        }
    }

    private static void Accumulate(Dictionary<Vector3, Vector3> accumulated, Vector3 position, Vector3 face) {
        accumulated[position] = accumulated.TryGetValue(position, out Vector3 sum) ? sum + face : face;
    }

    private static bool IsFinite(Vertex vertex) =>
        IsFinite(vertex.Position)
        && IsFinite(vertex.Normal)
        && IsFinite(vertex.Uv)
        && IsFinite(vertex.Uv1)
        && IsFinite(vertex.Uv2)
        && IsFinite(vertex.Uv3)
        && float.IsFinite(vertex.Color.R)
        && float.IsFinite(vertex.Color.G)
        && float.IsFinite(vertex.Color.B)
        && float.IsFinite(vertex.Color.A)
        && IsFinite(vertex.Tangent);

    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
