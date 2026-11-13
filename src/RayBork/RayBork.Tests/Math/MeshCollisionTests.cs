using System.Numerics;
using System.Runtime.CompilerServices;
using RayBork.Internal;
using RayBork.Tests.Assets.Procedural;

namespace RayBork.Tests;

/// <summary>
/// Tests the mesh collision BVH: builder invariants, data validation, traversal equivalence with the
/// brute-force query, and the deterministic triangle-test counter.
/// </summary>
public sealed class MeshCollisionTests {
    private const int RAY_COUNT = 1000;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CollisionNodeKeepsTheDocumentedThirtyTwoByteLayout() {
        Assert.Equal(32, Unsafe.SizeOf<MeshCollisionNode>());
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderCreatesAValidTree(string shape) {
        PrimitiveMeshData mesh = CreatePrimitive(shape);
        MeshCollisionData data = BuildCollision(mesh);

        Assert.Equal(mesh.Topology, data.Topology);
        Assert.Equal(
            MeshCollisionData.GetTriangleCount(mesh.Topology, mesh.Indices.Length),
            data.TriangleCount);
        Assert.True(data.Nodes.Length > 0);

        bool[] expectedTriangles = NonDegenerateTriangles(mesh);
        bool[] observedTriangles = new bool[data.TriangleCount];
        int leafCount = 0;
        int internalCount = 0;
        int triangleIds = 0;
        for (int index = 0; index < data.Nodes.Length; index++) {
            MeshCollisionNode node = data.Nodes[index];
            AssertFiniteOrdered(node.Bounds);
            if (node.Count == 0) {
                internalCount++;
                Assert.InRange(node.LeftOrFirst, 0, data.Nodes.Length - 2);
                AssertContains(node.Bounds, data.Nodes[node.LeftOrFirst].Bounds);
                AssertContains(node.Bounds, data.Nodes[node.LeftOrFirst + 1].Bounds);
            } else {
                leafCount++;
                Assert.InRange(node.Count, 1, 4);
                Assert.InRange(node.LeftOrFirst, 0, data.TriangleIds.Length - 1);
                Assert.True(node.LeftOrFirst + node.Count <= data.TriangleIds.Length);
                for (int slot = node.LeftOrFirst; slot < node.LeftOrFirst + node.Count; slot++) {
                    int triangleId = data.TriangleIds[slot];
                    Assert.InRange(triangleId, 0, data.TriangleCount - 1);
                    Assert.True(
                        MeshCollisionData.TryGetTriangleIndices(
                            mesh.Topology,
                            mesh.Indices,
                            triangleId,
                            out uint index0,
                            out uint index1,
                            out uint index2),
                        "Every stored triangle id describes a whole triangle.");
                    AssertContains(node.Bounds, TriangleBounds(mesh.Vertices, index0, index1, index2));
                    Assert.False(observedTriangles[triangleId], "A triangle id appears in two leaves.");
                    observedTriangles[triangleId] = true;
                    triangleIds++;
                }
            }
        }

        Assert.Equal((2 * leafCount) - 1, data.Nodes.Length);
        Assert.Equal(leafCount - 1, internalCount);
        Assert.Equal(triangleIds, data.TriangleIds.Length);
        Assert.InRange(MaxDepth(data.Nodes), 0, MeshCollisionData.MAX_DEPTH);
        for (int triangleId = 0; triangleId < data.TriangleCount; triangleId++) {
            Assert.Equal(expectedTriangles[triangleId], observedTriangles[triangleId]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderIsDeterministicForTheSameGeometry() {
        PrimitiveMeshData mesh = CreatePrimitive("sphere");
        MeshCollisionData first = BuildCollision(mesh);
        MeshCollisionData second = BuildCollision(mesh);

        Assert.Equal(first.Topology, second.Topology);
        Assert.Equal(first.TriangleCount, second.TriangleCount);
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(first.TriangleIds.ToArray(), second.TriangleIds.ToArray());
        Assert.Equal(first.Nodes.Length, second.Nodes.Length);
        for (int index = 0; index < first.Nodes.Length; index++) {
            Assert.Equal(first.Nodes[index].Bounds, second.Nodes[index].Bounds);
            Assert.Equal(first.Nodes[index].LeftOrFirst, second.Nodes[index].LeftOrFirst);
            Assert.Equal(first.Nodes[index].Count, second.Nodes[index].Count);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderReturnsNullForEmptyAndFullyDegenerateGeometry() {
        Assert.Null(MeshCollisionBuilder.Build([], [], PrimitiveTopology.TriangleList));
        Assert.Null(MeshCollisionBuilder.Build([], [], PrimitiveTopology.TriangleStrip));
        Assert.Null(MeshCollisionBuilder.Build(
            [Vector3.Zero, Vector3.UnitX, new Vector3(2f, 0f, 0f)],
            [0u, 1u, 2u],
            PrimitiveTopology.TriangleList));
        Assert.Null(MeshCollisionBuilder.Build(
            [Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero],
            [0u, 0u, 0u],
            PrimitiveTopology.TriangleList));
        Assert.Null(MeshCollisionBuilder.Build(
            [Vector3.Zero, Vector3.UnitX, new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f)],
            [0u, 1u, 2u, 3u],
            PrimitiveTopology.TriangleStrip));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderSkipsDegenerateTrianglesAndKeepsTheCanonicalCount() {
        MeshCollisionData? data = MeshCollisionBuilder.Build(
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(2f, 0f, 0f)],
            [0u, 1u, 2u, 0u, 1u, 3u],
            PrimitiveTopology.TriangleList);

        Assert.NotNull(data);
        Assert.Equal(2, data.TriangleCount);
        Assert.Equal(new[] { 0 }, data.TriangleIds.ToArray());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataConstructorRejectsAnOutOfRangeChildIndex() {
        (MeshCollisionNode[] nodes, int[] triangleIds, int triangleCount) = CreateValidArrays();
        int internalNode = FindNode(nodes, internalNode: true);
        nodes[internalNode] = new MeshCollisionNode(nodes[internalNode].Bounds, nodes.Length, 0);

        Assert.Throws<ArgumentException>(
            () => new MeshCollisionData(nodes, triangleIds, PrimitiveTopology.TriangleList, triangleCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataConstructorRejectsAnOutOfRangeLeafRange() {
        (MeshCollisionNode[] nodes, int[] triangleIds, int triangleCount) = CreateValidArrays();
        int leafNode = FindNode(nodes, internalNode: false);
        MeshCollisionNode leaf = nodes[leafNode];
        nodes[leafNode] = new MeshCollisionNode(leaf.Bounds, triangleIds.Length, leaf.Count);

        Assert.Throws<ArgumentException>(
            () => new MeshCollisionData(nodes, triangleIds, PrimitiveTopology.TriangleList, triangleCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataConstructorRejectsAnOutOfRangeTriangleId() {
        (MeshCollisionNode[] nodes, int[] triangleIds, int triangleCount) = CreateValidArrays();
        triangleIds[0] = triangleCount;

        Assert.Throws<ArgumentException>(
            () => new MeshCollisionData(nodes, triangleIds, PrimitiveTopology.TriangleList, triangleCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataConstructorRejectsNonFiniteBounds() {
        (MeshCollisionNode[] nodes, int[] triangleIds, int triangleCount) = CreateValidArrays();
        MeshCollisionNode root = nodes[0];
        nodes[0] = new MeshCollisionNode(
            new BoundingBox(new Vector3(float.NaN), root.Bounds.Max),
            root.LeftOrFirst,
            root.Count);

        Assert.Throws<ArgumentException>(
            () => new MeshCollisionData(nodes, triangleIds, PrimitiveTopology.TriangleList, triangleCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DataConstructorRejectsANonTriangleTopology() {
        (MeshCollisionNode[] nodes, int[] triangleIds, int triangleCount) = CreateValidArrays();

        Assert.Throws<ArgumentException>(
            () => new MeshCollisionData(nodes, triangleIds, PrimitiveTopology.LineList, triangleCount));
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AcceleratedQueryMatchesBruteForceForDeterministicRays(string shape) {
        PrimitiveMeshData geometry = CreatePrimitive(shape);
        using ProceduralTestHarness harness = new();
        Mesh bruteForce = await BuildMeshAsync(harness, geometry, collisions: false);
        Mesh accelerated = await BuildMeshAsync(harness, geometry, collisions: true);
        try {
            Assert.False(bruteForce.HasCollisionAcceleration);
            Assert.True(accelerated.HasCollisionAcceleration);
            uint state = 0x9E3779B9u ^ (uint)shape.Length;
            int hits = 0;
            for (int index = 0; index < RAY_COUNT; index++) {
                Ray ray = CreateRay(ref state, index);
                RayCollision expected = Collision.GetRayMesh(ray, bruteForce, Matrix4x4.Identity);
                RayCollision actual = Collision.GetRayMesh(ray, accelerated, Matrix4x4.Identity);
                AssertSameHit(expected, actual);
                if (expected.Hit) {
                    hits++;
                }
            }

            Assert.InRange(hits, 1, RAY_COUNT - 1);
        } finally {
            bruteForce.Dispose();
            accelerated.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AcceleratedTriangleStripQueryMatchesBruteForce() {
        PrimitiveMeshData geometry = CreateStrip();
        using ProceduralTestHarness harness = new();
        Mesh bruteForce = await BuildMeshAsync(harness, geometry, collisions: false);
        Mesh accelerated = await BuildMeshAsync(harness, geometry, collisions: true);
        try {
            Assert.False(bruteForce.HasCollisionAcceleration);
            Assert.True(accelerated.HasCollisionAcceleration);
            uint state = 0xA5A5A5A5u;
            int hits = 0;
            for (int index = 0; index < RAY_COUNT; index++) {
                // Half the rays are aimed straight at the ribbon so the sweep covers real hits; the
                // other half keeps the random inside, outside, and miss coverage.
                Ray ray = (index & 1) == 0 ? CreateStripHitRay(ref state) : CreateRay(ref state, index);
                RayCollision expected = Collision.GetRayMesh(ray, bruteForce, Matrix4x4.Identity);
                RayCollision actual = Collision.GetRayMesh(ray, accelerated, Matrix4x4.Identity);
                AssertSameHit(expected, actual);
                if (expected.Hit) {
                    hits++;
                }
            }

            Assert.InRange(hits, 400, RAY_COUNT);
        } finally {
            bruteForce.Dispose();
            accelerated.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TraversalTestsFarFewerTrianglesThanTheBruteForceScan() {
        PrimitiveMeshData mesh = CreatePrimitive("sphere");
        MeshCollisionData data = BuildCollision(mesh);
        Assert.Equal(960, data.TriangleCount);

        bool hit = data.TryFindNearest(
            new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ),
            mesh.Vertices,
            mesh.Indices,
            out int triangleId,
            out float distance,
            out int triangleTests);

        Assert.True(hit);
        Assert.InRange(triangleId, 0, data.TriangleCount - 1);
        Assert.Equal(4f, distance, 4);
        Assert.InRange(triangleTests, 1, 99);
        Assert.True(
            triangleTests < data.TriangleIds.Length,
            $"The accelerated query tested {triangleTests} of {data.TriangleIds.Length} triangles.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TraversalTieKeepsTheLowestTriangleId() {
        MeshCollisionData? data = MeshCollisionBuilder.Build(
            [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.Zero, Vector3.UnitY, Vector3.UnitX],
            [0u, 1u, 2u, 3u, 4u, 5u],
            PrimitiveTopology.TriangleList);

        Assert.NotNull(data);
        bool hit = data!.TryFindNearest(
            new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY),
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitY),
                new Vertex(Vector3.UnitX),
            ],
            [0u, 1u, 2u, 3u, 4u, 5u],
            out int triangleId,
            out float distance);

        Assert.True(hit);
        Assert.Equal(0, triangleId);
        Assert.Equal(1f, distance, 4);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TraversalMissReportsNoTriangle() {
        PrimitiveMeshData mesh = CreatePrimitive("cube");
        MeshCollisionData data = BuildCollision(mesh);

        bool hit = data.TryFindNearest(
            new Ray(new Vector3(3f, 3f, 3f), Vector3.UnitX),
            mesh.Vertices,
            mesh.Indices,
            out int triangleId,
            out float distance,
            out int triangleTests);

        Assert.False(hit);
        Assert.Equal(-1, triangleId);
        Assert.Equal(0f, distance);
        Assert.Equal(0, triangleTests);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TraversalMissesForANonFiniteRay() {
        PrimitiveMeshData mesh = CreatePrimitive("cube");
        MeshCollisionData data = BuildCollision(mesh);

        bool hit = data.TryFindNearest(
            new Ray(new Vector3(float.NaN, 0f, 0f), -Vector3.UnitZ),
            mesh.Vertices,
            mesh.Indices,
            out int triangleId,
            out _);

        Assert.False(hit);
        Assert.Equal(-1, triangleId);
    }

    private static PrimitiveMeshData CreatePrimitive(string shape) => shape switch {
        "cube" => PrimitiveMeshFactory.CreateCube(),
        "sphere" => PrimitiveMeshFactory.CreateSphere(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown test shape."),
    };

    private static PrimitiveMeshData CreateStrip() {
        List<Vertex> vertices = new(16);
        for (int index = 0; index < 16; index++) {
            float x = (index * 0.4f) - 3f;
            float y = (index & 1) == 0 ? -1f : 1f;
            vertices.Add(new Vertex(new Vector3(x, y, 0f)));
        }

        Vertex[] vertexArray = [.. vertices];
        uint[] indices = new uint[16];
        for (uint index = 0; index < indices.Length; index++) {
            indices[index] = index;
        }

        return new PrimitiveMeshData(
            vertexArray,
            indices,
            PrimitiveTopology.TriangleStrip,
            new BoundingBox(new Vector3(-3f, -1f, 0f), new Vector3(3f, 1f, 0f)));
    }

    private static MeshCollisionData BuildCollision(PrimitiveMeshData mesh) {
        Vector3[] positions = new Vector3[mesh.Vertices.Length];
        for (int index = 0; index < mesh.Vertices.Length; index++) {
            positions[index] = mesh.Vertices[index].Position;
        }

        MeshCollisionData? data = MeshCollisionBuilder.Build(positions, mesh.Indices, mesh.Topology);
        Assert.NotNull(data);
        return data!;
    }

    private static async Task<Mesh> BuildMeshAsync(
        ProceduralTestHarness harness,
        PrimitiveMeshData geometry,
        bool collisions) {
        MeshBuilder builder = harness.Service.CreateMeshBuilder(geometry.Topology);
        builder.SetVertices(geometry.Vertices);
        builder.SetIndices(geometry.Indices);
        if (collisions) {
            builder.GenerateCollisions();
        }

        return await builder.BuildAsync(TestContext.Current.CancellationToken);
    }

    private static (MeshCollisionNode[] Nodes, int[] TriangleIds, int TriangleCount) CreateValidArrays() {
        MeshCollisionData data = BuildCollision(CreatePrimitive("cube"));
        return (data.Nodes.ToArray(), data.TriangleIds.ToArray(), data.TriangleCount);
    }

    private static int FindNode(ReadOnlySpan<MeshCollisionNode> nodes, bool internalNode) {
        for (int index = 0; index < nodes.Length; index++) {
            if ((nodes[index].Count == 0) == internalNode) {
                return index;
            }
        }

        throw new InvalidOperationException($"The tree has no {(internalNode ? "internal" : "leaf")} node.");
    }

    private static bool[] NonDegenerateTriangles(PrimitiveMeshData mesh) {
        bool[] result = new bool[MeshCollisionData.GetTriangleCount(mesh.Topology, mesh.Indices.Length)];
        for (int triangleId = 0; triangleId < result.Length; triangleId++) {
            if (!MeshCollisionData.TryGetTriangleIndices(
                    mesh.Topology,
                    mesh.Indices,
                    triangleId,
                    out uint index0,
                    out uint index1,
                    out uint index2)) {
                continue;
            }

            Vector3 a = mesh.Vertices[(int)index0].Position;
            Vector3 b = mesh.Vertices[(int)index1].Position;
            Vector3 c = mesh.Vertices[(int)index2].Position;
            result[triangleId] = Vector3.Cross(b - a, c - a).LengthSquared() > 0f;
        }

        return result;
    }

    private static BoundingBox TriangleBounds(
        ReadOnlySpan<Vertex> vertices,
        uint index0,
        uint index1,
        uint index2) {
        Vector3 a = vertices[(int)index0].Position;
        Vector3 b = vertices[(int)index1].Position;
        Vector3 c = vertices[(int)index2].Position;
        return new BoundingBox(
            Vector3.Min(a, Vector3.Min(b, c)),
            Vector3.Max(a, Vector3.Max(b, c)));
    }

    private static int MaxDepth(ReadOnlySpan<MeshCollisionNode> nodes) {
        Stack<(int Index, int Depth)> pending = new();
        pending.Push((0, 0));
        int maximum = 0;
        while (pending.Count > 0) {
            (int index, int depth) = pending.Pop();
            maximum = Math.Max(maximum, depth);
            MeshCollisionNode node = nodes[index];
            if (node.Count == 0) {
                pending.Push((node.LeftOrFirst, depth + 1));
                pending.Push((node.LeftOrFirst + 1, depth + 1));
            }
        }

        return maximum;
    }

    private static void AssertContains(BoundingBox outer, BoundingBox inner) {
        Assert.True(outer.Min.X <= inner.Min.X, "The node bound does not contain the child minimum X.");
        Assert.True(outer.Min.Y <= inner.Min.Y, "The node bound does not contain the child minimum Y.");
        Assert.True(outer.Min.Z <= inner.Min.Z, "The node bound does not contain the child minimum Z.");
        Assert.True(outer.Max.X >= inner.Max.X, "The node bound does not contain the child maximum X.");
        Assert.True(outer.Max.Y >= inner.Max.Y, "The node bound does not contain the child maximum Y.");
        Assert.True(outer.Max.Z >= inner.Max.Z, "The node bound does not contain the child maximum Z.");
    }

    private static void AssertFiniteOrdered(BoundingBox bounds) {
        AssertFinite(bounds.Min);
        AssertFinite(bounds.Max);
        Assert.True(bounds.Min.X <= bounds.Max.X, "The node bound is inverted on X.");
        Assert.True(bounds.Min.Y <= bounds.Max.Y, "The node bound is inverted on Y.");
        Assert.True(bounds.Min.Z <= bounds.Max.Z, "The node bound is inverted on Z.");
    }

    private static void AssertFinite(Vector3 value) {
        Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z));
    }

    private static Ray CreateRay(ref uint state, int index) {
        return (index % 4) switch {
            0 => CreateDirectedRay(ref state),
            1 => new Ray(RandomPointInBox(ref state, 0.85f), RandomDirection(ref state)),
            2 => new Ray(RandomDirection(ref state) * 2.5f, RandomDirection(ref state)),
            _ => new Ray(RandomPointInBox(ref state, 2f), RandomDirection(ref state)),
        };
    }

    private static Ray CreateDirectedRay(ref uint state) {
        Vector3 origin = RandomDirection(ref state) * (1.5f + (NextFloat(ref state) * 2f));
        Vector3 target = RandomPointInBox(ref state, 0.75f);
        return new Ray(origin, Normalize(target - origin));
    }

    private static Ray CreateStripHitRay(ref uint state) {
        Vector3 origin = new(
            (NextFloat(ref state) * 6f) - 3f,
            (NextFloat(ref state) * 2f) - 1f,
            2.5f);
        return new Ray(origin, -Vector3.UnitZ);
    }

    private static Vector3 RandomPointInBox(ref uint state, float extent) => new(
        (NextFloat(ref state) * 2f * extent) - extent,
        (NextFloat(ref state) * 2f * extent) - extent,
        (NextFloat(ref state) * 2f * extent) - extent);

    private static Vector3 RandomDirection(ref uint state) => Normalize(new Vector3(
        (NextFloat(ref state) * 2f) - 1f,
        (NextFloat(ref state) * 2f) - 1f,
        (NextFloat(ref state) * 2f) - 1f));

    private static Vector3 Normalize(Vector3 value) {
        float lengthSquared = value.LengthSquared();
        return lengthSquared > 0f ? value / MathF.Sqrt(lengthSquared) : Vector3.UnitZ;
    }

    private static float NextFloat(ref uint state) {
        state = (state * 1664525u) + 1013904223u;
        return (state >> 8) * (1f / 16777216f);
    }

    private static void AssertSameHit(RayCollision expected, RayCollision actual) {
        Assert.Equal(expected.Hit, actual.Hit);
        if (!expected.Hit) {
            return;
        }

        Assert.Equal(expected.Distance, actual.Distance, 4);
        Assert.Equal(expected.Point.X, actual.Point.X, 4);
        Assert.Equal(expected.Point.Y, actual.Point.Y, 4);
        Assert.Equal(expected.Point.Z, actual.Point.Z, 4);
        Assert.Equal(expected.Normal.X, actual.Normal.X, 4);
        Assert.Equal(expected.Normal.Y, actual.Normal.Y, 4);
        Assert.Equal(expected.Normal.Z, actual.Normal.Z, 4);
    }
}
