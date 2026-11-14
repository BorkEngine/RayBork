using System.Numerics;
using PolyType;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the runtime mesh loader's baked ray-mesh collision payload path: a descriptor that names a
/// payload loads an accelerated mesh, a descriptor without one loads the brute-force mesh, and a
/// corrupt or missing payload fails the load with the documented asset error.
/// </summary>
public sealed class MeshCollisionLoadTests {
    private const string ACCELERATED_MESH_PATH = "meshes/triangle";
    private const string BRUTE_MESH_PATH = "meshes/triangle-brute";
    private const string MESH_PAYLOAD_PATH = "meshes/triangle-mesh";
    private const string COLLISION_PAYLOAD_PATH = "meshes/triangle-collision";
    private const uint VERTEX_COUNT = 3;
    private const uint INDEX_COUNT = 3;
    private const int RAY_COUNT = 200;

    private static readonly Vertex[] _vertices = [
        new(new Vector3(0f, 0f, 0f)),
        new(new Vector3(1f, 0f, 0f)),
        new(new Vector3(0f, 1f, 0f)),
    ];

    private static readonly uint[] _indices = [0u, 1u, 2u];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APackWithACollisionPayloadLoadsAnAcceleratedMesh() {
        byte[] pack = BuildAcceleratedPack(corruptCollision: false, topology: PrimitiveTopology.TriangleList);
        using CatalogTestHarness harness = new(userPacks: [pack]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Mesh accelerated = await harness.Catalog.Load<Mesh>(
            "/" + ACCELERATED_MESH_PATH,
            cancellationToken);
        Mesh brute = await harness.Catalog.Load<Mesh>("/" + BRUTE_MESH_PATH, cancellationToken);
        try {
            Assert.True(accelerated.HasCollisionAcceleration);
            Assert.False(brute.HasCollisionAcceleration);
            Assert.Equal(brute.Topology, accelerated.Topology);
            Assert.Equal(brute.IndexCount, accelerated.IndexCount);

            uint state = 0x5EED1234u;
            int hits = 0;
            for (int index = 0; index < RAY_COUNT; index++) {
                Ray ray = CreateRay(ref state, index);
                RayCollision expected = Collision.GetRayMesh(ray, brute, Matrix4x4.Identity);
                RayCollision actual = Collision.GetRayMesh(ray, accelerated, Matrix4x4.Identity);
                AssertSameHit(expected, actual);
                if (expected.Hit) {
                    hits++;
                }
            }

            Assert.InRange(hits, RAY_COUNT / 2, RAY_COUNT);
        } finally {
            accelerated.Dispose();
            brute.Dispose();
        }

        using PackArchive archive = PackArchive.Open(pack.AsMemory(), "mesh-collision.pck");
        Assert.True(archive.TryGetEntry(COLLISION_PAYLOAD_PATH, out PackEntry? entry));
        Assert.NotNull(entry);
        Assert.Equal(AssetEntryType.Raw, entry.Type);
        Assert.Equal(16u, entry.Alignment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APackWithoutACollisionPayloadLoadsABruteForceMesh() {
        using CatalogTestHarness harness = new(userPacks: [BuildAcceleratedPack(
            corruptCollision: false,
            topology: PrimitiveTopology.TriangleList,
            withCollisionPayload: false,
            referenceCollisionEntry: false)]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Mesh mesh = await harness.Catalog.Load<Mesh>("/" + ACCELERATED_MESH_PATH, cancellationToken);
        try {
            Assert.False(mesh.HasCollisionAcceleration);
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);
            Assert.True(hit.Hit);
            Assert.Equal(1f, hit.Distance, 4);
            Assert.Equal(new Vector3(0.25f, 0.25f, 0f), hit.Point);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LegacyDescriptorWithoutTheCollisionMemberLoadsWithoutAcceleration() {
        PackBuilder builder = new("mesh-collision.pck", PackCompressionPolicy.Release);
        builder.Add(
            ACCELERATED_MESH_PATH,
            AssetEntryType.Mesh,
            AssetSerialization.Serialize(new LegacyMeshAssetDescriptor {
                Payload = MESH_PAYLOAD_PATH,
                VertexCount = VERTEX_COUNT,
                IndexCount = INDEX_COUNT,
                Bounds = [0f, 0f, 0f, 1f, 1f, 0f],
                Topology = PrimitiveTopology.TriangleList,
            }),
            1);
        builder.Add(MESH_PAYLOAD_PATH, AssetEntryType.Raw, CreateMeshPayload(), 16);
        using CatalogTestHarness harness = new(userPacks: [builder.Build()]);

        Mesh mesh = await harness.Catalog.Load<Mesh>(
            "/" + ACCELERATED_MESH_PATH,
            TestContext.Current.CancellationToken);
        try {
            Assert.False(mesh.HasCollisionAcceleration);
            Assert.True(
                Collision.GetRayMesh(
                    new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
                    mesh,
                    Matrix4x4.Identity).Hit);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CorruptCollisionPayloadFailsTheLoadWithoutAnUpload() {
        using CatalogTestHarness harness = new(userPacks: [
            BuildAcceleratedPack(corruptCollision: true, topology: PrimitiveTopology.TriangleList),
        ]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>(
                "/" + ACCELERATED_MESH_PATH,
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("/" + ACCELERATED_MESH_PATH, thrown.Message, StringComparison.Ordinal);
        Assert.Contains("header is truncated", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MissingCollisionPayloadEntryFailsTheLoadWithoutAnUpload() {
        using CatalogTestHarness harness = new(userPacks: [
            BuildAcceleratedPack(
                corruptCollision: false,
                topology: PrimitiveTopology.TriangleList,
                withCollisionPayload: false,
                referenceCollisionEntry: true),
        ]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>(
                "/" + ACCELERATED_MESH_PATH,
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("not in the visible pack stack", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CollisionTopologyMismatchFailsTheLoadWithoutAnUpload() {
        using CatalogTestHarness harness = new(userPacks: [
            BuildAcceleratedPack(corruptCollision: false, topology: PrimitiveTopology.TriangleStrip),
        ]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>(
                "/" + ACCELERATED_MESH_PATH,
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("topology", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CollisionCanonicalCountMismatchFailsTheLoadWithoutAnUpload() {
        byte[] collisionPayload = CreateCollisionPayload();
        uint triangleCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            collisionPayload.AsSpan(MeshCollisionCodec.TRIANGLE_COUNT_OFFSET));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            collisionPayload.AsSpan(MeshCollisionCodec.TRIANGLE_COUNT_OFFSET),
            triangleCount + 1);
        using CatalogTestHarness harness = new(userPacks: [
            BuildAcceleratedPack(
                corruptCollision: false,
                topology: PrimitiveTopology.TriangleList,
                collisionPayload: collisionPayload),
        ]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>(
                "/" + ACCELERATED_MESH_PATH,
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("canonical triangle", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    private static byte[] BuildAcceleratedPack(
        bool corruptCollision,
        PrimitiveTopology topology,
        bool withCollisionPayload = true,
        bool referenceCollisionEntry = true,
        byte[]? collisionPayload = null) {
        PackBuilder builder = new("mesh-collision.pck", PackCompressionPolicy.Release);
        AddMeshDescriptor(
            builder,
            ACCELERATED_MESH_PATH,
            referenceCollisionEntry ? COLLISION_PAYLOAD_PATH : null,
            topology);
        AddMeshDescriptor(builder, BRUTE_MESH_PATH, collisionPayloadPath: null, topology);
        builder.Add(MESH_PAYLOAD_PATH, AssetEntryType.Raw, CreateMeshPayload(), 16);
        if (withCollisionPayload) {
            builder.Add(
                COLLISION_PAYLOAD_PATH,
                AssetEntryType.Raw,
                collisionPayload
                    ?? (corruptCollision ? new byte[16] : CreateCollisionPayload()),
                16);
        }

        return builder.Build();
    }

    private static void AddMeshDescriptor(
        PackBuilder builder,
        string meshPath,
        string? collisionPayloadPath,
        PrimitiveTopology topology) {
        builder.Add(
            meshPath,
            AssetEntryType.Mesh,
            AssetSerialization.Serialize(new MeshAssetDescriptor {
                Payload = MESH_PAYLOAD_PATH,
                CollisionPayload = collisionPayloadPath,
                VertexCount = VERTEX_COUNT,
                IndexCount = INDEX_COUNT,
                Bounds = [0f, 0f, 0f, 1f, 1f, 0f],
                Topology = topology,
            }),
            1);
    }

    private static byte[] CreateMeshPayload()
        => CatalogTestData.CreateMeshPayload(_vertices, _indices);

    private static byte[] CreateCollisionPayload() {
        Vector3[] positions = new Vector3[_vertices.Length];
        for (int index = 0; index < _vertices.Length; index++) {
            positions[index] = _vertices[index].Position;
        }

        MeshCollisionData? collision = MeshCollisionBuilder.Build(
            positions,
            _indices,
            PrimitiveTopology.TriangleList);
        Assert.NotNull(collision);
        return MeshCollisionCodec.Encode(collision!);
    }

    private static Ray CreateRay(ref uint state, int index) {
        if ((index & 1) == 0) {
            return new Ray(
                new Vector3(
                    0.05f + (NextFloat(ref state) * 0.4f),
                    0.05f + (NextFloat(ref state) * 0.4f),
                    2f),
                -Vector3.UnitZ);
        }

        Vector3 origin = new(
            (NextFloat(ref state) * 3f) - 1f,
            (NextFloat(ref state) * 3f) - 1f,
            (NextFloat(ref state) * 3f) - 1f);
        Vector3 direction = new(
            (NextFloat(ref state) * 2f) - 1f,
            (NextFloat(ref state) * 2f) - 1f,
            (NextFloat(ref state) * 2f) - 1f);
        float lengthSquared = direction.LengthSquared();
        if (lengthSquared > 0f) {
            direction /= MathF.Sqrt(lengthSquared);
        } else {
            direction = -Vector3.UnitZ;
        }

        return new Ray(origin, direction);
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

/// <summary>
/// The mesh descriptor shape before the collision payload member existed. The loader must deserialize
/// it with a null <see cref="MeshAssetDescriptor.CollisionPayload"/>.
/// </summary>
[GenerateShape]
internal sealed partial class LegacyMeshAssetDescriptor {
    public required string Payload { get; init; }

    public required uint VertexCount { get; init; }

    public required uint IndexCount { get; init; }

    public required float[] Bounds { get; init; }

    public required PrimitiveTopology Topology { get; init; }
}
