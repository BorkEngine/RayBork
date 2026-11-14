using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Deterministic CPU generation of the engine's built-in primitive meshes.
/// </summary>
/// <remarks>
/// <para>
/// Every method returns a fresh <see cref="PrimitiveMeshData"/> with new vertex and index arrays, so
/// the factory is pure CPU and needs no GPU, asset catalog, or app instance. The output is fully
/// deterministic: no randomness, clocks, or environment state is involved.
/// </para>
/// <para>
/// The meshes use the engine conventions: right-handed, Y-up, counter-clockwise front faces, the
/// 88-byte <see cref="Vertex"/> layout, and opaque white vertex colors that the primitive shader
/// multiplies by the per-instance tint. Solid shapes have flat or smooth outward normals; line meshes
/// keep the constructor's default normal because lines are unlit.
/// </para>
/// </remarks>
internal static class PrimitiveMeshFactory {
    /// <summary>The number of longitudinal subdivisions of the unit sphere.</summary>
    internal const int SPHERE_SLICES = 32;

    /// <summary>The number of latitudinal subdivisions of the unit sphere.</summary>
    internal const int SPHERE_RINGS = 16;

    /// <summary>The number of segments in each wire-sphere great circle.</summary>
    internal const int WIRE_SPHERE_SEGMENTS = 64;

    /// <summary>The number of great circles in the wire sphere.</summary>
    internal const int WIRE_SPHERE_CIRCLES = 3;

    private const float HALF_EXTENT = 0.5f;

    private static readonly CubeFace[] _cubeFaces = [
        new(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f)),
        new(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
        new(new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
    ];

    /// <summary>
    /// Creates the unit cube: 1x1x1 centered at the origin, 24 vertices with flat outward normals, and
    /// 36 indices.
    /// </summary>
    /// <remarks>
    /// Each face is four independent vertices, so the shared cube corners keep per-face normals and
    /// UVs. Every triangle winds counter-clockwise when viewed from outside the cube.
    /// </remarks>
    /// <returns>The cube geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateCube() {
        List<Vertex> vertices = new(24);
        List<uint> indices = new(36);
        foreach (CubeFace face in _cubeFaces) {
            Vector3 center = face.Normal * HALF_EXTENT;
            Vector3 right = face.Right * HALF_EXTENT;
            Vector3 up = face.Up * HALF_EXTENT;
            uint start = (uint)vertices.Count;
            vertices.Add(new Vertex(
                center + right - up,
                face.Normal,
                uv: new Vector2(1f, 1f),
                color: Color.White));
            vertices.Add(new Vertex(
                center + right + up,
                face.Normal,
                uv: new Vector2(1f, 0f),
                color: Color.White));
            vertices.Add(new Vertex(
                center - right + up,
                face.Normal,
                uv: new Vector2(0f, 0f),
                color: Color.White));
            vertices.Add(new Vertex(
                center - right - up,
                face.Normal,
                uv: new Vector2(0f, 1f),
                color: Color.White));
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);
        }

        return CreateResult(vertices, indices, PrimitiveTopology.TriangleList);
    }

    /// <summary>
    /// Creates the unit-radius UV sphere centered at the origin: 32 slices by 16 rings with outward
    /// normals, counter-clockwise winding, and spherical UVs.
    /// </summary>
    /// <remarks>
    /// The poles are single vertices and the seam duplicates the first meridian at <c>u = 1</c>, so
    /// every triangle has a non-zero area. UV <c>v</c> is zero at the top pole and one at the bottom
    /// pole, matching the engine's top-left UV convention.
    /// </remarks>
    /// <returns>The sphere geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateSphere() {
        int rowLength = SPHERE_SLICES + 1;
        List<Vertex> vertices = new(2 + ((SPHERE_RINGS - 1) * rowLength));
        List<uint> indices = new(SPHERE_RINGS * SPHERE_SLICES * 6);

        int top = AddSphereVertex(vertices, new Vector3(0f, 1f, 0f), new Vector2(0.5f, 0f));
        int firstRing = vertices.Count;
        for (int ring = 1; ring < SPHERE_RINGS; ring++) {
            float v = (float)ring / SPHERE_RINGS;
            float phi = v * MathF.PI;
            float y = MathF.Cos(phi);
            float radius = MathF.Sin(phi);
            for (int slice = 0; slice <= SPHERE_SLICES; slice++) {
                float u = (float)slice / SPHERE_SLICES;
                // The seam vertex repeats the first meridian's exact position so the two vertices
                // coincide, while its U coordinate closes the texture span.
                float theta = (slice % SPHERE_SLICES) * MathF.Tau / SPHERE_SLICES;
                Vector3 normal = new(radius * MathF.Sin(theta), y, radius * MathF.Cos(theta));
                AddSphereVertex(vertices, normal, new Vector2(u, v));
            }
        }

        int bottom = AddSphereVertex(vertices, new Vector3(0f, -1f, 0f), new Vector2(0.5f, 1f));

        for (int slice = 0; slice < SPHERE_SLICES; slice++) {
            AddTriangle(indices, top, firstRing + slice, firstRing + slice + 1);
        }

        for (int ring = 0; ring < SPHERE_RINGS - 2; ring++) {
            int upper = firstRing + (ring * rowLength);
            int lower = upper + rowLength;
            for (int slice = 0; slice < SPHERE_SLICES; slice++) {
                AddTriangle(indices, upper + slice, lower + slice, upper + slice + 1);
                AddTriangle(indices, upper + slice + 1, lower + slice, lower + slice + 1);
            }
        }

        int lastRing = firstRing + ((SPHERE_RINGS - 2) * rowLength);
        for (int slice = 0; slice < SPHERE_SLICES; slice++) {
            AddTriangle(indices, lastRing + slice, bottom, lastRing + slice + 1);
        }

        return CreateResult(vertices, indices, PrimitiveTopology.TriangleList);
    }

    /// <summary>
    /// Creates the unit plane: one 1x1 quad in the XZ plane centered at the origin with its front face
    /// toward +Y.
    /// </summary>
    /// <remarks>
    /// The four corners wind counter-clockwise when viewed from +Y. UVs run 0..1 over the quad, with
    /// the origin at the -X/-Z corner and V increasing toward +Z.
    /// </remarks>
    /// <returns>The plane geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreatePlane() {
        Vertex[] vertices = [
            new(
                new Vector3(-HALF_EXTENT, 0f, -HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(0f, 0f),
                color: Color.White),
            new(
                new Vector3(-HALF_EXTENT, 0f, HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(0f, 1f),
                color: Color.White),
            new(
                new Vector3(HALF_EXTENT, 0f, HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(1f, 1f),
                color: Color.White),
            new(
                new Vector3(HALF_EXTENT, 0f, -HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(1f, 0f),
                color: Color.White),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        return new PrimitiveMeshData(vertices, indices, PrimitiveTopology.TriangleList, ComputeBounds(vertices));
    }

    /// <summary>
    /// Creates the unit quad: one 1x1 quad in the XY plane centered at the origin with its front face
    /// toward +Z.
    /// </summary>
    /// <remarks>
    /// The four corners wind counter-clockwise when viewed from +Z. UV (0, 0) is the top-left corner
    /// (<c>-X/+Y</c>), matching the engine's top-left UV convention, and V increases downward.
    /// </remarks>
    /// <returns>The quad geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateQuad() {
        Vertex[] vertices = [
            new(
                new Vector3(-HALF_EXTENT, HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 0f),
                color: Color.White),
            new(
                new Vector3(-HALF_EXTENT, -HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 1f),
                color: Color.White),
            new(
                new Vector3(HALF_EXTENT, -HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 1f),
                color: Color.White),
            new(
                new Vector3(HALF_EXTENT, HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 0f),
                color: Color.White),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        return new PrimitiveMeshData(vertices, indices, PrimitiveTopology.TriangleList, ComputeBounds(vertices));
    }

    /// <summary>
    /// Creates the unit right triangle (0,0,0), (1,0,0), (0,1,0) in the XY plane with its front face
    /// toward +Z.
    /// </summary>
    /// <remarks>
    /// The vertices wind counter-clockwise when viewed from +Z. UV (0, 0) is the top-left vertex
    /// (0,1,0), matching the engine's top-left UV convention.
    /// </remarks>
    /// <returns>The triangle geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateTriangle() {
        Vertex[] vertices = [
            new(
                new Vector3(0f, 0f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 1f),
                color: Color.White),
            new(
                new Vector3(1f, 0f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 1f),
                color: Color.White),
            new(
                new Vector3(0f, 1f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 0f),
                color: Color.White),
        ];
        uint[] indices = [0u, 1u, 2u];
        return new PrimitiveMeshData(vertices, indices, PrimitiveTopology.TriangleList, ComputeBounds(vertices));
    }

    /// <summary>
    /// Creates one unit line segment from (0,0,0) to (1,0,0) with line-list topology.
    /// </summary>
    /// <returns>The line geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateLine() {
        Vertex[] vertices = [
            new(new Vector3(0f, 0f, 0f), color: Color.White),
            new(new Vector3(1f, 0f, 0f), color: Color.White),
        ];
        uint[] indices = [0u, 1u];
        return new PrimitiveMeshData(vertices, indices, PrimitiveTopology.LineList, ComputeBounds(vertices));
    }

    /// <summary>
    /// Creates the unit wire cube: the 12 edges of a 1x1x1 cube centered at the origin with line-list
    /// topology.
    /// </summary>
    /// <remarks>
    /// The eight corners are shared vertices and the index list has two endpoints per edge, so the
    /// mesh has 8 vertices and 24 indices.
    /// </remarks>
    /// <returns>The wire-cube geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateWireCube() {
        Vertex[] vertices = [
            new(new Vector3(-HALF_EXTENT, -HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, -HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, -HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, -HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, HALF_EXTENT, HALF_EXTENT), color: Color.White),
        ];
        uint[] indices = [
            0u, 1u,
            1u, 5u,
            5u, 4u,
            4u, 0u,
            3u, 2u,
            2u, 6u,
            6u, 7u,
            7u, 3u,
            0u, 3u,
            1u, 2u,
            5u, 6u,
            4u, 7u,
        ];
        return new PrimitiveMeshData(vertices, indices, PrimitiveTopology.LineList, ComputeBounds(vertices));
    }

    /// <summary>
    /// Creates the unit wire sphere: three great circles (XY, XZ, and YZ) of radius 1 in one
    /// line-list mesh, with 64 segments per circle.
    /// </summary>
    /// <remarks>
    /// Each circle reuses its 64 vertices and wraps the last edge back to the first, so the mesh has
    /// 192 vertices and 384 indices.
    /// </remarks>
    /// <returns>The wire-sphere geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateWireSphere() {
        List<Vertex> vertices = new(WIRE_SPHERE_CIRCLES * WIRE_SPHERE_SEGMENTS);
        List<uint> indices = new(WIRE_SPHERE_CIRCLES * WIRE_SPHERE_SEGMENTS * 2);
        for (int circle = 0; circle < WIRE_SPHERE_CIRCLES; circle++) {
            uint start = (uint)vertices.Count;
            for (int segment = 0; segment < WIRE_SPHERE_SEGMENTS; segment++) {
                float angle = MathF.Tau * segment / WIRE_SPHERE_SEGMENTS;
                float cosine = MathF.Cos(angle);
                float sine = MathF.Sin(angle);
                Vector3 position = circle switch {
                    0 => new Vector3(cosine, sine, 0f),
                    1 => new Vector3(cosine, 0f, sine),
                    _ => new Vector3(0f, cosine, sine),
                };
                vertices.Add(new Vertex(position, color: Color.White));
            }

            for (uint segment = 0; segment < WIRE_SPHERE_SEGMENTS; segment++) {
                indices.Add(start + segment);
                indices.Add(start + ((segment + 1) % WIRE_SPHERE_SEGMENTS));
            }
        }

        return CreateResult(vertices, indices, PrimitiveTopology.LineList);
    }

    private static int AddSphereVertex(List<Vertex> vertices, Vector3 normal, Vector2 uv) {
        int index = vertices.Count;
        vertices.Add(new Vertex(normal, normal, uv: uv, color: Color.White));
        return index;
    }

    private static void AddTriangle(List<uint> indices, int index0, int index1, int index2) {
        indices.Add((uint)index0);
        indices.Add((uint)index1);
        indices.Add((uint)index2);
    }

    private static PrimitiveMeshData CreateResult(
        List<Vertex> vertices,
        List<uint> indices,
        PrimitiveTopology topology) {
        Vertex[] vertexArray = [.. vertices];
        uint[] indexArray = [.. indices];
        return new PrimitiveMeshData(vertexArray, indexArray, topology, ComputeBounds(vertexArray));
    }

    private static BoundingBox ComputeBounds(IReadOnlyList<Vertex> vertices) {
        Vector3 minimum = vertices[0].Position;
        Vector3 maximum = minimum;
        for (int index = 1; index < vertices.Count; index++) {
            Vector3 position = vertices[index].Position;
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return new BoundingBox(minimum, maximum);
    }

    private readonly record struct CubeFace(Vector3 Normal, Vector3 Right, Vector3 Up);
}
