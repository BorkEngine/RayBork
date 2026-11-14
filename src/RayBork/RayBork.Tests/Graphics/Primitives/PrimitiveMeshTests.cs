using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Graphics.Primitives;

/// <summary>
/// Tests the deterministic CPU geometry of <see cref="PrimitiveMeshFactory"/>: counts, topology,
/// bounds, winding, normals, UVs, and array independence. The factory needs no GPU or asset catalog.
/// </summary>
public sealed class PrimitiveMeshTests {
    private const float TOLERANCE = 1e-5f;
    private const float CENTROID_TOLERANCE = 1e-4f;
    private const float CENTROID_GRID_SCALE = 10000f;
    private const float MINIMUM_AREA_SQUARED = 1e-12f;
    private const float MINIMUM_SEGMENT_LENGTH = 1e-5f;

    private static readonly PrimitiveMeshCase[] _cases = [
        new(
            "cube",
            PrimitiveMeshFactory.CreateCube,
            VertexCount: 24,
            IndexCount: 36,
            PrimitiveTopology.TriangleList,
            new Vector3(1f),
            new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)),
            IsCentered: true,
            IsClosed: true),
        new(
            "sphere",
            PrimitiveMeshFactory.CreateSphere,
            VertexCount: 497,
            IndexCount: 2880,
            PrimitiveTopology.TriangleList,
            new Vector3(2f),
            new BoundingBox(new Vector3(-1f), new Vector3(1f)),
            IsCentered: true,
            IsClosed: true),
        new(
            "plane",
            PrimitiveMeshFactory.CreatePlane,
            VertexCount: 4,
            IndexCount: 6,
            PrimitiveTopology.TriangleList,
            new Vector3(1f, 0f, 1f),
            new BoundingBox(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f)),
            IsCentered: true,
            IsClosed: false),
        new(
            "quad",
            PrimitiveMeshFactory.CreateQuad,
            VertexCount: 4,
            IndexCount: 6,
            PrimitiveTopology.TriangleList,
            new Vector3(1f, 1f, 0f),
            new BoundingBox(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)),
            IsCentered: true,
            IsClosed: false),
        new(
            "triangle",
            PrimitiveMeshFactory.CreateTriangle,
            VertexCount: 3,
            IndexCount: 3,
            PrimitiveTopology.TriangleList,
            new Vector3(1f, 1f, 0f),
            new BoundingBox(Vector3.Zero, new Vector3(1f, 1f, 0f)),
            IsCentered: false,
            IsClosed: false),
        new(
            "line",
            PrimitiveMeshFactory.CreateLine,
            VertexCount: 2,
            IndexCount: 2,
            PrimitiveTopology.LineList,
            new Vector3(1f, 0f, 0f),
            new BoundingBox(Vector3.Zero, new Vector3(1f, 0f, 0f)),
            IsCentered: false,
            IsClosed: false),
        new(
            "wirecube",
            PrimitiveMeshFactory.CreateWireCube,
            VertexCount: 8,
            IndexCount: 24,
            PrimitiveTopology.LineList,
            new Vector3(1f),
            new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f)),
            IsCentered: true,
            IsClosed: false),
        new(
            "wiresphere",
            PrimitiveMeshFactory.CreateWireSphere,
            VertexCount: 192,
            IndexCount: 384,
            PrimitiveTopology.LineList,
            new Vector3(2f),
            new BoundingBox(new Vector3(-1f), new Vector3(1f)),
            IsCentered: true,
            IsClosed: false),
    ];

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [InlineData("plane")]
    [InlineData("quad")]
    [InlineData("triangle")]
    [InlineData("line")]
    [InlineData("wirecube")]
    [InlineData("wiresphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryMeshMatchesItsDocumentedDefinition(string name) {
        PrimitiveMeshCase testCase = FindCase(name);
        PrimitiveMeshData mesh = testCase.Create();

        Assert.Equal(testCase.VertexCount, mesh.Vertices.Length);
        Assert.Equal(testCase.IndexCount, mesh.Indices.Length);
        Assert.Equal(testCase.Topology, mesh.Topology);
        AssertVectorClose(testCase.Bounds.Min, mesh.Bounds.Min, $"{name} minimum bounds");
        AssertVectorClose(testCase.Bounds.Max, mesh.Bounds.Max, $"{name} maximum bounds");
        AssertVectorClose(testCase.Size, mesh.Bounds.Max - mesh.Bounds.Min, $"{name} size");
        Assert.True(IsFinite(mesh.Bounds.Min) && IsFinite(mesh.Bounds.Max), $"{name} bounds are finite.");
        Assert.True(
            mesh.Bounds.Min.X <= mesh.Bounds.Max.X
                && mesh.Bounds.Min.Y <= mesh.Bounds.Max.Y
                && mesh.Bounds.Min.Z <= mesh.Bounds.Max.Z,
            $"{name} bounds are ordered.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryMeshHasFiniteAttributesAndWhiteVertexColors() {
        foreach (PrimitiveMeshCase testCase in _cases) {
            PrimitiveMeshData mesh = testCase.Create();
            for (int index = 0; index < mesh.Vertices.Length; index++) {
                Vertex vertex = mesh.Vertices[index];
                string context = $"{testCase.Name} vertex {index}";
                AssertFinite(vertex.Position, context + " position");
                AssertFinite(vertex.Normal, context + " normal");
                AssertFinite(vertex.Uv, context + " uv");
                AssertFinite(vertex.Uv1, context + " uv1");
                AssertFinite(vertex.Uv2, context + " uv2");
                AssertFinite(vertex.Uv3, context + " uv3");
                AssertFinite(vertex.Color.R, context + " color.r");
                AssertFinite(vertex.Color.G, context + " color.g");
                AssertFinite(vertex.Color.B, context + " color.b");
                AssertFinite(vertex.Color.A, context + " color.a");
                AssertFinite(vertex.Tangent, context + " tangent");
                Assert.Equal(Color.White, vertex.Color);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryMeshHasInRangeIndicesAndNoDegeneratePrimitives() {
        foreach (PrimitiveMeshCase testCase in _cases) {
            PrimitiveMeshData mesh = testCase.Create();
            foreach (uint index in mesh.Indices) {
                Assert.True(
                    index < (uint)mesh.Vertices.Length,
                    $"{testCase.Name} index {index} is outside the {mesh.Vertices.Length} vertices.");
            }

            if (mesh.Topology == PrimitiveTopology.TriangleList) {
                for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3) {
                    Vector3 face = Vector3.Cross(
                        mesh.Vertices[(int)mesh.Indices[triangle + 1]].Position
                            - mesh.Vertices[(int)mesh.Indices[triangle]].Position,
                        mesh.Vertices[(int)mesh.Indices[triangle + 2]].Position
                            - mesh.Vertices[(int)mesh.Indices[triangle]].Position);
                    Assert.True(
                        face.LengthSquared() > MINIMUM_AREA_SQUARED,
                        $"{testCase.Name} triangle {triangle / 3} has zero area.");
                }
            } else {
                for (int segment = 0; segment < mesh.Indices.Length; segment += 2) {
                    float length = Vector3.Distance(
                        mesh.Vertices[(int)mesh.Indices[segment]].Position,
                        mesh.Vertices[(int)mesh.Indices[segment + 1]].Position);
                    Assert.True(
                        length > MINIMUM_SEGMENT_LENGTH,
                        $"{testCase.Name} line segment {segment / 2} has zero length.");
                }
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ClosedShapesUseOutwardNormals() {
        foreach (PrimitiveMeshCase testCase in _cases) {
            if (!testCase.IsClosed) {
                continue;
            }

            PrimitiveMeshData mesh = testCase.Create();
            for (int index = 0; index < mesh.Vertices.Length; index++) {
                Vertex vertex = mesh.Vertices[index];
                Assert.Equal(1f, vertex.Normal.Length(), 5);
                Assert.True(
                    Vector3.Dot(vertex.Normal, vertex.Position) > 0f,
                    $"{testCase.Name} vertex {index} normal {vertex.Normal} does not face outward from "
                        + $"position {vertex.Position}.");
            }

            for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3) {
                Vertex vertex0 = mesh.Vertices[(int)mesh.Indices[triangle]];
                Vertex vertex1 = mesh.Vertices[(int)mesh.Indices[triangle + 1]];
                Vertex vertex2 = mesh.Vertices[(int)mesh.Indices[triangle + 2]];
                Vector3 face = Vector3.Normalize(Vector3.Cross(
                    vertex1.Position - vertex0.Position,
                    vertex2.Position - vertex0.Position));
                Vector3 normal = Vector3.Normalize(vertex0.Normal + vertex1.Normal + vertex2.Normal);
                Assert.True(
                    Vector3.Dot(face, normal) > 0f,
                    $"{testCase.Name} triangle {triangle / 3} winds inward: face {face}, normal {normal}.");
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CenteredShapesHaveTheirCentroidAtTheOrigin() {
        foreach (PrimitiveMeshCase testCase in _cases) {
            if (!testCase.IsCentered) {
                continue;
            }

            // Collapse positions that the seam or two great circles describe twice. Float trig leaves
            // tiny differences between the copies, so compare on a 1e-4 grid instead of by exact value.
            PrimitiveMeshData mesh = testCase.Create();
            HashSet<(int X, int Y, int Z)> keys = [];
            Vector3 sum = Vector3.Zero;
            foreach (Vertex vertex in mesh.Vertices) {
                (int X, int Y, int Z) key = (
                    (int)MathF.Round(vertex.Position.X * CENTROID_GRID_SCALE),
                    (int)MathF.Round(vertex.Position.Y * CENTROID_GRID_SCALE),
                    (int)MathF.Round(vertex.Position.Z * CENTROID_GRID_SCALE));
                if (keys.Add(key)) {
                    sum += vertex.Position;
                }
            }

            Vector3 centroid = sum / keys.Count;
            Assert.True(
                centroid.Length() <= CENTROID_TOLERANCE,
                $"{testCase.Name} centroid is {centroid} instead of the origin.");
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryBuildReturnsIndependentArrays() {
        foreach (PrimitiveMeshCase testCase in _cases) {
            PrimitiveMeshData first = testCase.Create();
            PrimitiveMeshData second = testCase.Create();

            Assert.NotSame(first.Vertices, second.Vertices);
            Assert.NotSame(first.Indices, second.Indices);
            Assert.Equal(first.Vertices.Length, second.Vertices.Length);
            Assert.Equal(first.Indices, second.Indices);
            for (int index = 0; index < first.Vertices.Length; index++) {
                Assert.Equal(first.Vertices[index].Position, second.Vertices[index].Position);
                Assert.Equal(first.Vertices[index].Normal, second.Vertices[index].Normal);
                Assert.Equal(first.Vertices[index].Uv, second.Vertices[index].Uv);
                Assert.Equal(first.Vertices[index].Color, second.Vertices[index].Color);
            }

            first.Vertices[0].Position = new Vector3(123f, 456f, 789f);
            first.Indices[0] = 999u;
            Assert.NotEqual(first.Vertices[0].Position, second.Vertices[0].Position);
            Assert.NotEqual(first.Indices[0], second.Indices[0]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CubeHasSixFlatOutwardFaces() {
        PrimitiveMeshData cube = PrimitiveMeshFactory.CreateCube();
        Dictionary<Vector3, int> verticesPerNormal = [];

        foreach (Vertex vertex in cube.Vertices) {
            Assert.Equal(1f, vertex.Normal.Length(), 5);
            Assert.True(
                MathF.Abs(Vector3.Dot(vertex.Normal, vertex.Position) - 0.5f) <= TOLERANCE,
                $"Cube vertex at {vertex.Position} is not on the face for normal {vertex.Normal}.");
            verticesPerNormal[vertex.Normal] = verticesPerNormal.GetValueOrDefault(vertex.Normal) + 1;
        }

        Assert.Equal(6, verticesPerNormal.Count);
        foreach (int count in verticesPerNormal.Values) {
            Assert.Equal(4, count);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SphereUsesSphericalUvsWithTheSeamAndPoles() {
        PrimitiveMeshData sphere = PrimitiveMeshFactory.CreateSphere();
        int rowLength = PrimitiveMeshFactory.SPHERE_SLICES + 1;

        Assert.Equal(
            2 + ((PrimitiveMeshFactory.SPHERE_RINGS - 1) * rowLength),
            sphere.Vertices.Length);
        Assert.Equal(new Vector2(0.5f, 0f), sphere.Vertices[0].Uv);
        Assert.Equal(new Vector2(0.5f, 1f), sphere.Vertices[^1].Uv);
        AssertVectorClose(new Vector3(0f, 1f, 0f), sphere.Vertices[0].Position, "sphere top pole");
        AssertVectorClose(new Vector3(0f, -1f, 0f), sphere.Vertices[^1].Position, "sphere bottom pole");

        for (int ring = 0; ring < PrimitiveMeshFactory.SPHERE_RINGS - 1; ring++) {
            int first = 1 + (ring * rowLength);
            int last = first + PrimitiveMeshFactory.SPHERE_SLICES;
            AssertVectorClose(
                sphere.Vertices[first].Position,
                sphere.Vertices[last].Position,
                $"sphere seam at ring {ring + 1}");
        }

        foreach (Vertex vertex in sphere.Vertices) {
            Assert.True(
                vertex.Uv.X is >= 0f and <= 1f && vertex.Uv.Y is >= 0f and <= 1f,
                $"Sphere UV {vertex.Uv} is outside 0..1.");
            Assert.Equal(1f, vertex.Position.Length(), 5);
            AssertVectorClose(vertex.Position, vertex.Normal, "sphere outward normal");
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlaneIsCounterClockwiseFacingUpWithUvs() {
        PrimitiveMeshData plane = PrimitiveMeshFactory.CreatePlane();

        foreach (Vertex vertex in plane.Vertices) {
            Assert.Equal(Vector3.UnitY, vertex.Normal);
        }

        AssertFaceNormals(plane, Vector3.UnitY, "plane");
        AssertVectorClose(new Vector3(-0.5f, 0f, -0.5f), plane.Vertices[0].Position, "plane origin corner");
        Assert.Equal(new Vector2(0f, 0f), plane.Vertices[0].Uv);
        Assert.Equal(new Vector2(0f, 1f), plane.Vertices[1].Uv);
        Assert.Equal(new Vector2(1f, 1f), plane.Vertices[2].Uv);
        Assert.Equal(new Vector2(1f, 0f), plane.Vertices[3].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QuadIsCounterClockwiseFacingForwardWithTopLeftUvOrigin() {
        PrimitiveMeshData quad = PrimitiveMeshFactory.CreateQuad();

        foreach (Vertex vertex in quad.Vertices) {
            Assert.Equal(Vector3.UnitZ, vertex.Normal);
        }

        AssertFaceNormals(quad, Vector3.UnitZ, "quad");
        AssertVectorClose(new Vector3(-0.5f, 0.5f, 0f), quad.Vertices[0].Position, "quad top-left corner");
        Assert.Equal(new Vector2(0f, 0f), quad.Vertices[0].Uv);
        Assert.Equal(new Vector2(0f, 1f), quad.Vertices[1].Uv);
        Assert.Equal(new Vector2(1f, 1f), quad.Vertices[2].Uv);
        Assert.Equal(new Vector2(1f, 0f), quad.Vertices[3].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TriangleIsTheDocumentedUnitRightTriangle() {
        PrimitiveMeshData triangle = PrimitiveMeshFactory.CreateTriangle();

        AssertVectorClose(Vector3.Zero, triangle.Vertices[0].Position, "triangle vertex 0");
        AssertVectorClose(Vector3.UnitX, triangle.Vertices[1].Position, "triangle vertex 1");
        AssertVectorClose(Vector3.UnitY, triangle.Vertices[2].Position, "triangle vertex 2");
        Assert.Equal([0u, 1u, 2u], triangle.Indices);
        Assert.Equal(new Vector2(0f, 0f), triangle.Vertices[2].Uv);
        AssertFaceNormals(triangle, Vector3.UnitZ, "triangle");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WireCubeIsTwelveUnitEdges() {
        PrimitiveMeshData wireCube = PrimitiveMeshFactory.CreateWireCube();
        HashSet<(uint Start, uint End)> edges = [];
        for (int index = 0; index < wireCube.Indices.Length; index += 2) {
            uint start = wireCube.Indices[index];
            uint end = wireCube.Indices[index + 1];
            Vector3 difference = wireCube.Vertices[(int)end].Position - wireCube.Vertices[(int)start].Position;
            Assert.Equal(1f, difference.Length(), 5);
            Assert.Equal(1, CountNonZeroComponents(difference));
            edges.Add(start < end ? (start, end) : (end, start));
        }

        Assert.Equal(12, edges.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WireSphereIsThreeUnitGreatCircles() {
        PrimitiveMeshData wireSphere = PrimitiveMeshFactory.CreateWireSphere();
        int segments = PrimitiveMeshFactory.WIRE_SPHERE_SEGMENTS;

        Assert.Equal(PrimitiveMeshFactory.WIRE_SPHERE_CIRCLES * segments, wireSphere.Vertices.Length);
        Assert.Equal(PrimitiveMeshFactory.WIRE_SPHERE_CIRCLES * segments * 2, wireSphere.Indices.Length);
        for (int circle = 0; circle < PrimitiveMeshFactory.WIRE_SPHERE_CIRCLES; circle++) {
            for (int segment = 0; segment < segments; segment++) {
                Vector3 position = wireSphere.Vertices[(circle * segments) + segment].Position;
                Assert.Equal(1f, position.Length(), 5);
                if (circle == 0) {
                    Assert.Equal(0f, position.Z);
                } else if (circle == 1) {
                    Assert.Equal(0f, position.Y);
                } else {
                    Assert.Equal(0f, position.X);
                }
            }

            for (int segment = 0; segment < segments; segment++) {
                uint start = wireSphere.Indices[(circle * segments * 2) + (segment * 2)];
                uint end = wireSphere.Indices[(circle * segments * 2) + (segment * 2) + 1];
                Assert.Equal((uint)(circle * segments) + (uint)segment, start);
                Assert.Equal((uint)(circle * segments) + (uint)((segment + 1) % segments), end);
            }
        }
    }

    private static PrimitiveMeshCase FindCase(string name)
        => _cases.Single(testCase => string.Equals(testCase.Name, name, StringComparison.Ordinal));

    private static void AssertFaceNormals(PrimitiveMeshData mesh, Vector3 expected, string context) {
        for (int triangle = 0; triangle < mesh.Indices.Length; triangle += 3) {
            Vector3 vertex0 = mesh.Vertices[(int)mesh.Indices[triangle]].Position;
            Vector3 vertex1 = mesh.Vertices[(int)mesh.Indices[triangle + 1]].Position;
            Vector3 vertex2 = mesh.Vertices[(int)mesh.Indices[triangle + 2]].Position;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(vertex1 - vertex0, vertex2 - vertex0));
            AssertVectorClose(expected, normal, $"{context} triangle {triangle / 3} winding");
        }
    }

    private static void AssertVectorClose(Vector3 expected, Vector3 actual, string context) {
        Assert.True(
            Vector3.Distance(expected, actual) <= TOLERANCE,
            $"{context}: expected {expected}, actual {actual}.");
    }

    private static void AssertFinite(Vector2 value, string context) {
        AssertFinite(value.X, context + ".x");
        AssertFinite(value.Y, context + ".y");
    }

    private static void AssertFinite(Vector3 value, string context) {
        AssertFinite(value.X, context + ".x");
        AssertFinite(value.Y, context + ".y");
        AssertFinite(value.Z, context + ".z");
    }

    private static void AssertFinite(Vector4 value, string context) {
        AssertFinite(value.X, context + ".x");
        AssertFinite(value.Y, context + ".y");
        AssertFinite(value.Z, context + ".z");
        AssertFinite(value.W, context + ".w");
    }

    private static void AssertFinite(float value, string context)
        => Assert.True(float.IsFinite(value), $"{context} is not finite.");

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static int CountNonZeroComponents(Vector3 value) {
        int count = 0;
        if (MathF.Abs(value.X) > 0f) {
            count++;
        }

        if (MathF.Abs(value.Y) > 0f) {
            count++;
        }

        if (MathF.Abs(value.Z) > 0f) {
            count++;
        }

        return count;
    }

    private sealed record PrimitiveMeshCase(
        string Name,
        Func<PrimitiveMeshData> Create,
        int VertexCount,
        int IndexCount,
        PrimitiveTopology Topology,
        Vector3 Size,
        BoundingBox Bounds,
        bool IsCentered,
        bool IsClosed);
}
