using Microsoft.Extensions.Logging.Abstractions;
using PolyType;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// A custom data type shaped for the catalog tests. It is registered through
/// <see cref="AssetTypeRegistration.Create{T}"/>, exactly like the builder registers a game type.
/// </summary>
[GenerateShape]
internal sealed partial class CatalogTestSettings {
    public required string Name { get; init; }

    public required int Difficulty { get; init; }
}

/// <summary>
/// Deterministic descriptors and pack inputs shared by the catalog tests.
/// </summary>
internal static class CatalogTestData {
    internal const string MESH_PATH = "meshes/quad";
    internal const string MESH_PAYLOAD_PATH = "meshes/quad-mesh";
    internal const string TEXTURE_PATH = "textures/albedo";
    internal const string TEXTURE_PAYLOAD_PATH = "textures/albedo-ktx2";
    internal const string SHADER_PATH = "shaders/basic";
    internal const string SHADER_VERTEX_PAYLOAD_PATH = "shaders/basic-vert";
    internal const string SHADER_FRAGMENT_PAYLOAD_PATH = "shaders/basic-frag";
    internal const string DATA_PATH = "data/settings";
    internal const string MODEL_PATH = "models/castle";
    internal const string FONT_PATH = "fonts/inter";

    internal const uint MESH_VERTEX_COUNT = 3;
    internal const uint MESH_INDEX_COUNT = 3;

    internal static MeshAssetDescriptor CreateMeshDescriptor() => new() {
        Payload = MESH_PAYLOAD_PATH,
        VertexCount = MESH_VERTEX_COUNT,
        IndexCount = MESH_INDEX_COUNT,
        Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
        Topology = PrimitiveTopology.TriangleList,
    };

    internal static byte[] CreateMeshPayload()
        => new byte[(MESH_VERTEX_COUNT * MeshAssetDescriptor.VERTEX_STRIDE) + (MESH_INDEX_COUNT * sizeof(uint))];

    internal static TextureAssetDescriptor CreateTextureDescriptor() => new() {
        Payload = TEXTURE_PAYLOAD_PATH,
        Width = 4,
        Height = 2,
        MipLevels = 3,
        Format = TextureFormat.Rgba8Srgb,
        IsSrgb = true,
        Filter = TextureFilter.Linear,
        Wrap = TextureWrap.Repeat,
        MaxAnisotropy = 1f,
    };

    internal static byte[] CreateTexturePayload() => CreatePattern(128, 5);

    internal static ShaderAssetDescriptor CreateShaderDescriptor() => new() {
        VertexPayload = SHADER_VERTEX_PAYLOAD_PATH,
        FragmentPayload = SHADER_FRAGMENT_PAYLOAD_PATH,
        VertexEntry = "main_vertex",
        FragmentEntry = "main_fragment",
        Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
        DefaultRenderState = RenderState.AlphaBlend,
        Uniforms = [
            new ShaderUniformAsset {
                Name = "uTint",
                Type = ShaderUniformType.Vector4,
                Default = [1f, 1f, 1f, 1f],
            },
        ],
        Textures = ["mainTexture"],
    };

    internal static byte[] CreateShaderVertexPayload() => CreatePattern(32, 11);

    internal static byte[] CreateShaderFragmentPayload() => CreatePattern(48, 13);

    internal static CatalogTestSettings CreateSettings() => new() {
        Name = "catalog",
        Difficulty = 7,
    };

    internal static byte[] CreateDataPayload() => AssetSerialization.Serialize(CreateSettings());

    internal static byte[] CreatePattern(int length, int seed) {
        byte[] payload = new byte[length];
        new Random(seed).NextBytes(payload);
        return payload;
    }

    internal static PackBuilder CreateStandardBuilder(string displayPath = "catalog.pck") {
        var builder = new PackBuilder(displayPath, PackCompressionPolicy.Release);
        builder.Add(MESH_PATH, AssetEntryType.Mesh, AssetSerialization.Serialize(CreateMeshDescriptor()), 1);
        builder.Add(MESH_PAYLOAD_PATH, AssetEntryType.Raw, CreateMeshPayload(), 16);
        builder.Add(TEXTURE_PATH, AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor()), 1);
        builder.Add(TEXTURE_PAYLOAD_PATH, AssetEntryType.Raw, CreateTexturePayload(), 64);
        builder.Add(SHADER_PATH, AssetEntryType.Shader, AssetSerialization.Serialize(CreateShaderDescriptor()), 1);
        builder.Add(SHADER_VERTEX_PAYLOAD_PATH, AssetEntryType.Raw, CreateShaderVertexPayload(), 16);
        builder.Add(SHADER_FRAGMENT_PAYLOAD_PATH, AssetEntryType.Raw, CreateShaderFragmentPayload(), 16);
        builder.Add(DATA_PATH, AssetEntryType.Data, CreateDataPayload(), 1);
        return builder;
    }

    internal static byte[] BuildStandardPack() => CreateStandardBuilder().Build();

    internal static byte[] BuildPack(params (string Path, AssetEntryType Type, byte[] Payload)[] entries) {
        var builder = new PackBuilder("test.pck", PackCompressionPolicy.Release);
        foreach ((string path, AssetEntryType type, byte[] payload) in entries) {
            builder.Add(path, type, payload);
        }

        return builder.Build();
    }

    internal static AssetTypeRegistration SettingsRegistration
        => AssetTypeRegistration.Create<CatalogTestSettings>();
}

/// <summary>
/// One started catalog with a GPU upload pump standing in for the render loop.
/// </summary>
/// <remarks>
/// The catalog is deterministic: the pump thread waits on the bridge signal instead of polling, and
/// <see cref="Dispose"/> stops the catalog before stopping the pump so every queued destruction is
/// serviced. Tests synchronize through the fake worker's call records and explicit events.
/// </remarks>
internal sealed class CatalogTestHarness : IDisposable {
    private readonly TempPackDirectory _directory = new();
    private readonly List<string> _packPaths = [];
    private readonly ManualResetEventSlim _pumpStop = new(false);
    private readonly Thread _pumpThread;

    internal CatalogTestHarness(
        byte[]? builtInPack = null,
        params byte[][] userPacks)
        : this(builtInPack, userPacks, []) {
    }

    internal CatalogTestHarness(
        byte[]? builtInPack,
        byte[][] userPacks,
        AssetTypeRegistration[] assetTypes) {
        ArgumentNullException.ThrowIfNull(userPacks);
        ArgumentNullException.ThrowIfNull(assetTypes);

        Bridge = new GpuAssetBridge();
        Worker = new FakeAssetGpuWorker();
        Bridge.AttachWorker(Worker);
        foreach (byte[] pack in userPacks) {
            string path = _directory.GetPath($"user-{_packPaths.Count}.pck");
            File.WriteAllBytes(path, pack);
            _packPaths.Add(path);
        }

        Catalog = new AssetCatalog(
            NullLoggerFactory.Instance,
            Bridge,
            builtInPack ?? ReadOnlyMemory<byte>.Empty,
            _packPaths,
            assetTypes);
        Catalog.Start();
        _pumpThread = new Thread(PumpLoop) {
            IsBackground = true,
            Name = "RayBork.Tests.AssetGpuPump",
        };
        _pumpThread.Start();
    }

    internal AssetCatalog Catalog { get; }

    internal GpuAssetBridge Bridge { get; }

    internal FakeAssetGpuWorker Worker { get; }

    public void Dispose() {
        Catalog.Stop();
        _pumpStop.Set();
        _pumpThread.Join();
        Bridge.Close(null);
        _pumpStop.Dispose();
        _directory.Dispose();
    }

    private void PumpLoop() {
        WaitHandle[] handles = [Bridge.Signal, _pumpStop.WaitHandle];
        while (true) {
            int index = WaitHandle.WaitAny(handles);
            if (index == 1) {
                return;
            }

            Bridge.ProcessPending(Worker);
        }
    }
}
