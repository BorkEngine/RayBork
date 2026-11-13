using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Assets;

/// <summary>
/// Tests the baked ray-mesh collision payload codec: its documented header, the round trip, the
/// traversal equivalence with the brute-force query, and the controlled failure surface for corrupt
/// and truncated payloads.
/// </summary>
public sealed class MeshCollisionCodecTests {
    private const string MESH_PATH = "meshes/test";
    private const int RAY_COUNT = 400;
    private const int FUZZ_ITERATIONS = 500;
    private const int FUZZ_SEED = 20261113;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HeaderCarriesTheDocumentedLayout() {
        MeshCollisionData data = BuildCollision("cube");
        byte[] payload = MeshCollisionCodec.Encode(data);

        Assert.Equal(
            MeshCollisionCodec.HEADER_LENGTH
                + (data.Nodes.Length * MeshCollisionCodec.NODE_LENGTH)
                + (data.TriangleIds.Length * MeshCollisionCodec.TRIANGLE_ID_LENGTH),
            payload.Length);
        Assert.True(payload.AsSpan(0, MeshCollisionCodec.MAGIC_LENGTH)
            .SequenceEqual(MeshCollisionCodec.MagicBytes));
        Assert.Equal(
            MeshCollisionCodec.CURRENT_VERSION,
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(MeshCollisionCodec.VERSION_OFFSET)));
        Assert.Equal(
            (uint)data.Topology,
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(MeshCollisionCodec.TOPOLOGY_OFFSET)));
        Assert.Equal(
            (uint)data.Nodes.Length,
            BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(MeshCollisionCodec.NODE_COUNT_OFFSET)));
        Assert.Equal(
            (uint)data.TriangleIds.Length,
            BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(MeshCollisionCodec.TRIANGLE_ID_COUNT_OFFSET)));
        Assert.Equal(
            (uint)data.TriangleCount,
            BinaryPrimitives.ReadUInt32LittleEndian(
                payload.AsSpan(MeshCollisionCodec.TRIANGLE_COUNT_OFFSET)));
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RoundTripPreservesEveryNodeAndTriangleId(string shape) {
        PrimitiveMeshData mesh = CreatePrimitive(shape);
        MeshCollisionData original = BuildCollision(mesh);
        byte[] payload = MeshCollisionCodec.Encode(original);

        MeshCollisionData decoded = MeshCollisionCodec.Decode(
            payload,
            mesh.Topology,
            mesh.Indices.Length,
            MESH_PATH);

        Assert.Equal(original.Topology, decoded.Topology);
        Assert.Equal(original.TriangleCount, decoded.TriangleCount);
        Assert.Equal(original.Bounds, decoded.Bounds);
        Assert.Equal(original.TriangleIds.ToArray(), decoded.TriangleIds.ToArray());
        Assert.Equal(original.Nodes.Length, decoded.Nodes.Length);
        for (int index = 0; index < original.Nodes.Length; index++) {
            MeshCollisionNode expected = original.Nodes[index];
            MeshCollisionNode actual = decoded.Nodes[index];
            Assert.Equal(expected.Bounds, actual.Bounds);
            Assert.Equal(expected.LeftOrFirst, actual.LeftOrFirst);
            Assert.Equal(expected.Count, actual.Count);
        }
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DecodedTraversalMatchesTheBruteForceQuery(string shape) {
        PrimitiveMeshData mesh = CreatePrimitive(shape);
        MeshCollisionData decoded = MeshCollisionCodec.Decode(
            MeshCollisionCodec.Encode(BuildCollision(mesh)),
            mesh.Topology,
            mesh.Indices.Length,
            MESH_PATH);
        uint state = 0x9E3779B9u ^ (uint)shape.Length;
        int hits = 0;
        for (int index = 0; index < RAY_COUNT; index++) {
            Ray ray = CreateRay(ref state, index);
            bool expectedHit = TryFindNearestBruteForce(
                mesh,
                ray,
                out int expectedTriangle,
                out float expectedDistance);
            bool actualHit = decoded.TryFindNearest(
                ray,
                mesh.Vertices,
                mesh.Indices,
                out int actualTriangle,
                out float actualDistance);

            Assert.Equal(expectedHit, actualHit);
            if (!expectedHit) {
                continue;
            }

            hits++;
            Assert.Equal(expectedTriangle, actualTriangle);
            Assert.Equal(expectedDistance, actualDistance, 4);
        }

        Assert.InRange(hits, 1, RAY_COUNT - 1);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CorruptMagicFailsTheDecode() {
        byte[] payload = CubePayload();
        payload[0] = (byte)'X';

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "RBVC magic");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedVersionFailsTheDecode() {
        byte[] payload = CubePayload();
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(MeshCollisionCodec.VERSION_OFFSET),
            MeshCollisionCodec.CURRENT_VERSION + 1);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "format version");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TruncatedHeaderFailsTheDecode() {
        byte[] payload = CubePayload();

        AssertDecodeFails(
            payload[..(MeshCollisionCodec.HEADER_LENGTH - 1)],
            CubeTopology,
            CubeIndexCount,
            "header is truncated");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OversizedNodeCountFailsBeforeAllocating() {
        byte[] payload = CubePayload();
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(MeshCollisionCodec.NODE_COUNT_OFFSET),
            uint.MaxValue);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "the header requires exactly");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OutOfRangeChildIndexFailsTheDecode() {
        byte[] payload = CubePayload();
        MeshCollisionData data = DecodeCube(payload);
        int internalNode = FindNode(data.Nodes, internalNode: true);
        WriteNodeChild(payload, internalNode, data.Nodes.Length);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "collision structure is invalid");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OutOfRangeLeafRangeFailsTheDecode() {
        byte[] payload = CubePayload();
        MeshCollisionData data = DecodeCube(payload);
        int leafNode = FindNode(data.Nodes, internalNode: false);
        WriteNodeChild(payload, leafNode, data.TriangleIds.Length);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "collision structure is invalid");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OutOfRangeTriangleIdFailsTheDecode() {
        byte[] payload = CubePayload();
        MeshCollisionData data = DecodeCube(payload);
        int offset = MeshCollisionCodec.HEADER_LENGTH
            + (data.Nodes.Length * MeshCollisionCodec.NODE_LENGTH);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), data.TriangleCount);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "collision structure is invalid");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateTriangleIdFailsTheDecode() {
        byte[] payload = CubePayload();
        MeshCollisionData data = DecodeCube(payload);
        int offset = MeshCollisionCodec.HEADER_LENGTH
            + (data.Nodes.Length * MeshCollisionCodec.NODE_LENGTH);
        int first = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + sizeof(int)), first);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "collision structure is invalid");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TopologyMismatchFailsTheDecode() {
        byte[] payload = CubePayload();

        AssertDecodeFails(payload, PrimitiveTopology.TriangleStrip, CubeIndexCount, "topology");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonTriangleMeshTopologyFailsTheDecode() {
        byte[] payload = CubePayload();

        AssertDecodeFails(payload, PrimitiveTopology.LineList, CubeIndexCount, "not a triangle topology");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrongCanonicalTriangleCountFailsTheDecode() {
        byte[] payload = CubePayload();
        uint triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(
            payload.AsSpan(MeshCollisionCodec.TRIANGLE_COUNT_OFFSET));
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(MeshCollisionCodec.TRIANGLE_COUNT_OFFSET),
            triangleCount + 1);

        AssertDecodeFails(payload, CubeTopology, CubeIndexCount, "canonical triangle");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SeededMutationsAndTruncationsNeverEscapeTheDocumentedFailureSurface() {
        byte[] original = CubePayload();
        var random = new Random(FUZZ_SEED);
        for (int iteration = 0; iteration < FUZZ_ITERATIONS; iteration++) {
            ExerciseDecode(Mutate(original, random, iteration), iteration);
        }

        for (int length = 0; length < original.Length; length++) {
            ExerciseDecode(original[..length], length);
        }
    }

    private static PrimitiveTopology CubeTopology => PrimitiveTopology.TriangleList;

    private static int CubeIndexCount => PrimitiveMeshFactory.CreateCube().Indices.Length;

    private static byte[] CubePayload() => MeshCollisionCodec.Encode(BuildCollision("cube"));

    private static MeshCollisionData DecodeCube(byte[] payload) => MeshCollisionCodec.Decode(
        payload,
        CubeTopology,
        CubeIndexCount,
        MESH_PATH);

    private static PrimitiveMeshData CreatePrimitive(string shape) => shape switch {
        "cube" => PrimitiveMeshFactory.CreateCube(),
        "sphere" => PrimitiveMeshFactory.CreateSphere(),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown test shape."),
    };

    private static MeshCollisionData BuildCollision(string shape) {
        PrimitiveMeshData mesh = CreatePrimitive(shape);
        return BuildCollision(mesh);
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

    private static void AssertDecodeFails(
        byte[] payload,
        PrimitiveTopology topology,
        int indexCount,
        string expectedReason) {
        AssetLoadException exception = Assert.Throws<AssetLoadException>(
            () => MeshCollisionCodec.Decode(payload, topology, indexCount, MESH_PATH));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, exception.DiagnosticCode);
        Assert.Contains("/meshes/test", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedReason, exception.Message, StringComparison.Ordinal);
    }

    private static int FindNode(ReadOnlySpan<MeshCollisionNode> nodes, bool internalNode) {
        for (int index = 0; index < nodes.Length; index++) {
            if ((nodes[index].Count == 0) == internalNode) {
                return index;
            }
        }

        throw new InvalidOperationException(
            $"The tree has no {(internalNode ? "internal" : "leaf")} node.");
    }

    private static void WriteNodeChild(byte[] payload, int nodeIndex, int value) {
        int offset = MeshCollisionCodec.HEADER_LENGTH
            + (nodeIndex * MeshCollisionCodec.NODE_LENGTH)
            + (3 * sizeof(float) * 2);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), value);
    }

    private static bool TryFindNearestBruteForce(
        PrimitiveMeshData mesh,
        Ray ray,
        out int triangleId,
        out float distance) {
        triangleId = -1;
        distance = 0f;
        float bestDistance = float.PositiveInfinity;
        int bestTriangle = int.MaxValue;
        int triangleCount = MeshCollisionData.GetTriangleCount(mesh.Topology, mesh.Indices.Length);
        for (int candidate = 0; candidate < triangleCount; candidate++) {
            if (!MeshCollisionData.TryGetTriangleIndices(
                    mesh.Topology,
                    mesh.Indices,
                    candidate,
                    out uint index0,
                    out uint index1,
                    out uint index2)) {
                continue;
            }

            if (!Collision.TryGetTriangleIntersection(
                    ray,
                    mesh.Vertices[(int)index0].Position,
                    mesh.Vertices[(int)index1].Position,
                    mesh.Vertices[(int)index2].Position,
                    out float hitDistance,
                    out _)) {
                continue;
            }

            if (hitDistance < bestDistance
                || (hitDistance == bestDistance && candidate < bestTriangle)) {
                bestDistance = hitDistance;
                bestTriangle = candidate;
                triangleId = candidate;
                distance = hitDistance;
            }
        }

        return bestTriangle != int.MaxValue;
    }

    private static byte[] Mutate(byte[] original, Random random, int iteration) {
        byte[] mutated = (byte[])original.Clone();
        switch (iteration % 6) {
            case 0:
                for (int index = 0; index < 1 + (iteration % 4); index++) {
                    mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
                }

                break;
            case 1:
                return mutated[..random.Next(0, mutated.Length)];
            case 2: {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Fill(0xFF);
                break;
            }
            case 3: {
                int start = random.Next(mutated.Length);
                int length = Math.Min(mutated.Length - start, random.Next(1, 16));
                mutated.AsSpan(start, length).Clear();
                break;
            }
            case 4: {
                int[] headerOffsets = [
                    MeshCollisionCodec.VERSION_OFFSET,
                    MeshCollisionCodec.TOPOLOGY_OFFSET,
                    MeshCollisionCodec.NODE_COUNT_OFFSET,
                    MeshCollisionCodec.TRIANGLE_ID_COUNT_OFFSET,
                    MeshCollisionCodec.TRIANGLE_COUNT_OFFSET,
                ];
                int offset = headerOffsets[random.Next(headerOffsets.Length)];
                BinaryPrimitives.WriteUInt32LittleEndian(
                    mutated.AsSpan(offset),
                    (uint)random.NextInt64(0, 1L << 32));
                break;
            }
            default: {
                int offset = random.Next(
                    MeshCollisionCodec.HEADER_LENGTH,
                    mutated.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    mutated.AsSpan(offset),
                    (uint)random.NextInt64(0, 1L << 32));
                break;
            }
        }

        return mutated;
    }

    private static void ExerciseDecode(byte[] payload, int iteration) {
        try {
            MeshCollisionCodec.Decode(payload, CubeTopology, CubeIndexCount, MESH_PATH);
        } catch (AssetLoadException) {
        } catch (Exception exception) {
            Assert.Fail(
                $"Mutation {iteration} leaked {exception.GetType().FullName}: {exception.Message}");
        }
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
}
