using System.Numerics;

namespace RayBork.Internal;

/// <summary>Deterministic CPU generation of the engine's built-in primitive meshes.</summary>
/// <remarks>Every call returns fresh arrays; meshes use right-handed, Y-up, counter-clockwise-facing
/// engine conventions with opaque white vertex colors.</remarks>
internal static class PrimitiveMeshFactory
{
    /// <summary>The number of longitudinal subdivisions of the unit sphere.</summary>
    internal const int SPHERE_SLICES = 32;

    /// <summary>The number of latitudinal subdivisions of the unit sphere.</summary>
    internal const int SPHERE_RINGS = 16;

    /// <summary>The number of segments in each wire-sphere great circle.</summary>
    internal const int WIRE_SPHERE_SEGMENTS = 64;

    /// <summary>The number of great circles in the wire sphere.</summary>
    internal const int WIRE_SPHERE_CIRCLES = 3;

    /// <summary>The number of radial segments in the unit disc.</summary>
    internal const int DISC_SEGMENTS = 36;

    private const float HALF_EXTENT = 0.5f;

    private static readonly CubeFace[] _cubeFaces =
    [
        new(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f)),
        new(new Vector3(0f, -1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f)),
        new(new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
        new(new Vector3(0f, 0f, -1f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 1f, 0f)),
    ];

    /// <summary>Creates the 1x1x1 cube with flat outward normals, 24 vertices, and 36 indices.</summary>
    /// <returns>The cube geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateCube()
    {
        List<Vertex> vertices = new(24);
        List<uint> indices = new(36);
        foreach (CubeFace face in _cubeFaces)
        {
            Vector3 center = face.Normal * HALF_EXTENT;
            Vector3 right = face.Right * HALF_EXTENT;
            Vector3 up = face.Up * HALF_EXTENT;
            uint start = (uint)vertices.Count;
            vertices.Add(
                new Vertex(
                    center + right - up,
                    face.Normal,
                    uv: new Vector2(1f, 1f),
                    color: Color.White
                )
            );
            vertices.Add(
                new Vertex(
                    center + right + up,
                    face.Normal,
                    uv: new Vector2(1f, 0f),
                    color: Color.White
                )
            );
            vertices.Add(
                new Vertex(
                    center - right + up,
                    face.Normal,
                    uv: new Vector2(0f, 0f),
                    color: Color.White
                )
            );
            vertices.Add(
                new Vertex(
                    center - right - up,
                    face.Normal,
                    uv: new Vector2(0f, 1f),
                    color: Color.White
                )
            );
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);
        }

        return CreateResult(vertices, indices, PrimitiveTopology.TriangleList);
    }

    /// <summary>Creates the 32-slice, 16-ring unit sphere with outward normals and spherical UVs.</summary>
    /// <returns>The sphere geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateSphere()
    {
        int rowLength = SPHERE_SLICES + 1;
        List<Vertex> vertices = new(2 + ((SPHERE_RINGS - 1) * rowLength));
        List<uint> indices = new(SPHERE_RINGS * SPHERE_SLICES * 6);

        int top = AddSphereVertex(vertices, new Vector3(0f, 1f, 0f), new Vector2(0.5f, 0f));
        int firstRing = vertices.Count;
        for (int ring = 1; ring < SPHERE_RINGS; ring++)
        {
            float v = (float)ring / SPHERE_RINGS;
            float phi = v * MathF.PI;
            float y = MathF.Cos(phi);
            float radius = MathF.Sin(phi);
            for (int slice = 0; slice <= SPHERE_SLICES; slice++)
            {
                float u = (float)slice / SPHERE_SLICES;
                // The seam vertex repeats the first meridian's exact position so the two vertices
                // coincide, while its U coordinate closes the texture span.
                float theta = (slice % SPHERE_SLICES) * MathF.Tau / SPHERE_SLICES;
                Vector3 normal = new(radius * MathF.Sin(theta), y, radius * MathF.Cos(theta));
                AddSphereVertex(vertices, normal, new Vector2(u, v));
            }
        }

        int bottom = AddSphereVertex(vertices, new Vector3(0f, -1f, 0f), new Vector2(0.5f, 1f));

        for (int slice = 0; slice < SPHERE_SLICES; slice++)
        {
            AddTriangle(indices, top, firstRing + slice, firstRing + slice + 1);
        }

        for (int ring = 0; ring < SPHERE_RINGS - 2; ring++)
        {
            int upper = firstRing + (ring * rowLength);
            int lower = upper + rowLength;
            for (int slice = 0; slice < SPHERE_SLICES; slice++)
            {
                AddTriangle(indices, upper + slice, lower + slice, upper + slice + 1);
                AddTriangle(indices, upper + slice + 1, lower + slice, lower + slice + 1);
            }
        }

        int lastRing = firstRing + ((SPHERE_RINGS - 2) * rowLength);
        for (int slice = 0; slice < SPHERE_SLICES; slice++)
        {
            AddTriangle(indices, lastRing + slice, bottom, lastRing + slice + 1);
        }

        return CreateResult(vertices, indices, PrimitiveTopology.TriangleList);
    }

    /// <summary>Creates the 1x1 XZ plane centered at the origin with its front face toward +Y.</summary>
    /// <returns>The plane geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreatePlane()
    {
        Vertex[] vertices =
        [
            new(
                new Vector3(-HALF_EXTENT, 0f, -HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(0f, 0f),
                color: Color.White
            ),
            new(
                new Vector3(-HALF_EXTENT, 0f, HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(0f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(HALF_EXTENT, 0f, HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(1f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(HALF_EXTENT, 0f, -HALF_EXTENT),
                Vector3.UnitY,
                uv: new Vector2(1f, 0f),
                color: Color.White
            ),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        return new PrimitiveMeshData(
            vertices,
            indices,
            PrimitiveTopology.TriangleList,
            ComputeBounds(vertices)
        );
    }

    /// <summary>Creates the 1x1 XY quad centered at the origin with its front face toward +Z.</summary>
    /// <returns>The quad geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateQuad()
    {
        Vertex[] vertices =
        [
            new(
                new Vector3(-HALF_EXTENT, HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 0f),
                color: Color.White
            ),
            new(
                new Vector3(-HALF_EXTENT, -HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(HALF_EXTENT, -HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(HALF_EXTENT, HALF_EXTENT, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 0f),
                color: Color.White
            ),
        ];
        uint[] indices = [0u, 1u, 2u, 0u, 2u, 3u];
        return new PrimitiveMeshData(
            vertices,
            indices,
            PrimitiveTopology.TriangleList,
            ComputeBounds(vertices)
        );
    }

    /// <summary>Creates the 36-segment unit disc in the XY plane facing +Z.</summary>
    /// <returns>The disc geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateDisc()
    {
        List<Vertex> vertices = new(DISC_SEGMENTS + 1);
        List<uint> indices = new(DISC_SEGMENTS * 3);
        vertices.Add(
            new Vertex(Vector3.Zero, Vector3.UnitZ, uv: new Vector2(0.5f, 0.5f), color: Color.White)
        );
        for (int segment = 0; segment < DISC_SEGMENTS; segment++)
        {
            float angle = MathF.Tau * segment / DISC_SEGMENTS;
            float cosine = MathF.Cos(angle);
            float sine = MathF.Sin(angle);
            vertices.Add(
                new Vertex(
                    new Vector3(cosine, sine, 0f),
                    Vector3.UnitZ,
                    uv: new Vector2(0.5f + (cosine * 0.5f), 0.5f - (sine * 0.5f)),
                    color: Color.White
                )
            );
        }

        for (int segment = 0; segment < DISC_SEGMENTS; segment++)
        {
            indices.Add(0u);
            indices.Add((uint)(segment + 1));
            indices.Add((uint)(((segment + 1) % DISC_SEGMENTS) + 1));
        }

        return CreateResult(vertices, indices, PrimitiveTopology.TriangleList);
    }

    /// <summary>Creates the unit right triangle in the XY plane with its front face toward +Z.</summary>
    /// <returns>The triangle geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateTriangle()
    {
        Vertex[] vertices =
        [
            new(
                new Vector3(0f, 0f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(1f, 0f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(1f, 1f),
                color: Color.White
            ),
            new(
                new Vector3(0f, 1f, 0f),
                Vector3.UnitZ,
                uv: new Vector2(0f, 0f),
                color: Color.White
            ),
        ];
        uint[] indices = [0u, 1u, 2u];
        return new PrimitiveMeshData(
            vertices,
            indices,
            PrimitiveTopology.TriangleList,
            ComputeBounds(vertices)
        );
    }

    /// <summary>Creates the unit line segment along +X with line-list topology.</summary>
    /// <returns>The line geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateLine()
    {
        Vertex[] vertices =
        [
            new(new Vector3(0f, 0f, 0f), color: Color.White),
            new(new Vector3(1f, 0f, 0f), color: Color.White),
        ];
        uint[] indices = [0u, 1u];
        return new PrimitiveMeshData(
            vertices,
            indices,
            PrimitiveTopology.LineList,
            ComputeBounds(vertices)
        );
    }

    /// <summary>Creates the 12-edge unit wire cube with line-list topology.</summary>
    /// <returns>The wire-cube geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateWireCube()
    {
        Vertex[] vertices =
        [
            new(new Vector3(-HALF_EXTENT, -HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, -HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, HALF_EXTENT, -HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, -HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, -HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(HALF_EXTENT, HALF_EXTENT, HALF_EXTENT), color: Color.White),
            new(new Vector3(-HALF_EXTENT, HALF_EXTENT, HALF_EXTENT), color: Color.White),
        ];
        uint[] indices =
        [
            0u,
            1u,
            1u,
            5u,
            5u,
            4u,
            4u,
            0u,
            3u,
            2u,
            2u,
            6u,
            6u,
            7u,
            7u,
            3u,
            0u,
            3u,
            1u,
            2u,
            5u,
            6u,
            4u,
            7u,
        ];
        return new PrimitiveMeshData(
            vertices,
            indices,
            PrimitiveTopology.LineList,
            ComputeBounds(vertices)
        );
    }

    /// <summary>Creates the three-great-circle unit wire sphere with 64 segments per circle.</summary>
    /// <returns>The wire-sphere geometry and its bounds.</returns>
    internal static PrimitiveMeshData CreateWireSphere()
    {
        List<Vertex> vertices = new(WIRE_SPHERE_CIRCLES * WIRE_SPHERE_SEGMENTS);
        List<uint> indices = new(WIRE_SPHERE_CIRCLES * WIRE_SPHERE_SEGMENTS * 2);
        for (int circle = 0; circle < WIRE_SPHERE_CIRCLES; circle++)
        {
            uint start = (uint)vertices.Count;
            for (int segment = 0; segment < WIRE_SPHERE_SEGMENTS; segment++)
            {
                float angle = MathF.Tau * segment / WIRE_SPHERE_SEGMENTS;
                float cosine = MathF.Cos(angle);
                float sine = MathF.Sin(angle);
                Vector3 position = circle switch
                {
                    0 => new Vector3(cosine, sine, 0f),
                    1 => new Vector3(cosine, 0f, sine),
                    _ => new Vector3(0f, cosine, sine),
                };
                vertices.Add(new Vertex(position, color: Color.White));
            }

            for (uint segment = 0; segment < WIRE_SPHERE_SEGMENTS; segment++)
            {
                indices.Add(start + segment);
                indices.Add(start + ((segment + 1) % WIRE_SPHERE_SEGMENTS));
            }
        }

        return CreateResult(vertices, indices, PrimitiveTopology.LineList);
    }

    private static int AddSphereVertex(List<Vertex> vertices, Vector3 normal, Vector2 uv)
    {
        int index = vertices.Count;
        vertices.Add(new Vertex(normal, normal, uv: uv, color: Color.White));
        return index;
    }

    private static void AddTriangle(List<uint> indices, int index0, int index1, int index2)
    {
        indices.Add((uint)index0);
        indices.Add((uint)index1);
        indices.Add((uint)index2);
    }

    private static PrimitiveMeshData CreateResult(
        List<Vertex> vertices,
        List<uint> indices,
        PrimitiveTopology topology
    )
    {
        Vertex[] vertexArray = [.. vertices];
        uint[] indexArray = [.. indices];
        return new PrimitiveMeshData(vertexArray, indexArray, topology, ComputeBounds(vertexArray));
    }

    private static BoundingBox ComputeBounds(IReadOnlyList<Vertex> vertices)
    {
        Vector3 minimum = vertices[0].Position;
        Vector3 maximum = minimum;
        for (int index = 1; index < vertices.Count; index++)
        {
            Vector3 position = vertices[index].Position;
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return new BoundingBox(minimum, maximum);
    }

    private readonly record struct CubeFace(Vector3 Normal, Vector3 Right, Vector3 Up);
}
