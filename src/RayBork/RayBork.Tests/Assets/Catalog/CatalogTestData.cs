using Microsoft.Extensions.Logging.Abstractions;
using PolyType;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;
using RayBork.Tests.Fonts;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// A custom data type shaped for the catalog tests. It is registered through
/// <see cref="LoaderAssetTypeRegistration{T}"/>, exactly like the builder registers a game type.
/// </summary>
[GenerateShape]
internal sealed partial class CatalogTestSettings {
    public required string Name { get; init; }

    public required int Difficulty { get; init; }
}

/// <summary>
/// The pre-M6 font descriptor shape: the runtime descriptor plus the removed <c>Ranges</c> member. A
/// legacy pack serialized this shape, and the runtime must still load it because unknown descriptor
/// members are ignored.
/// </summary>
[GenerateShape]
internal sealed partial class LegacyFontAssetDescriptor {
    public required string FacePayload { get; init; }

    public required string Format { get; init; }

    public required uint ByteLength { get; init; }

    public required string[] Ranges { get; init; }

    public required int MsdfPixelSize { get; init; }

    public required int MsdfRange { get; init; }

    public required int MsdfPadding { get; init; }
}

/// <summary>
/// Deterministic descriptors and pack inputs shared by the catalog tests.
/// </summary>
internal static class CatalogTestData {
    internal const string CUSTOM_DATA_FAILURE_DESCRIPTION = "custom data could not be deserialized";

    internal const string MESH_PATH = "meshes/quad";
    internal const string MESH_PAYLOAD_PATH = "meshes/quad-mesh";
    internal const string TEXTURE_PATH = "textures/albedo";
    internal const string TEXTURE_PAYLOAD_PATH = "textures/albedo-ktx2";
    internal const string SHADER_PATH = "shaders/basic";
    internal const string SHADER_VERTEX_PAYLOAD_PATH = "shaders/basic-vert";
    internal const string SHADER_FRAGMENT_PAYLOAD_PATH = "shaders/basic-frag";
    internal const string DATA_PATH = "data/settings";
    internal const string MODEL_PATH = "models/castle";

    /// <summary>
    /// The catalog font tests serve the synthetic face at the engine's default font path, so the same
    /// pack also satisfies <see cref="EngineDefaults.LoadAsync"/>.
    /// </summary>
    internal const string FONT_PATH = "fonts/fira-sans";
    internal const string FONT_PAYLOAD_PATH = "fonts/fira-sans-face";

    /// <summary>The catalog font tests serve a second synthetic face at the engine's mono font path.</summary>
    internal const string MONO_FONT_PATH = "fonts/jetbrains-mono";
    internal const string MONO_FONT_PAYLOAD_PATH = "fonts/jetbrains-mono-face";

    /// <summary>The glyph id of the square <c>A</c> glyph the font test face maps.</summary>
    internal const int FONT_GLYPH_ID = 1;

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

    /// <summary>
    /// Writes an independent little-endian mesh payload for the given vertices and indices. The
    /// writer mirrors the bake layout instead of reusing the runtime codec, so the round-trip test
    /// exercises the decoder against a second encoder.
    /// </summary>
    internal static byte[] CreateMeshPayload(IReadOnlyList<Vertex> vertices, IReadOnlyList<uint> indices) {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        int byteCount = (vertices.Count * MeshAssetDescriptor.VERTEX_STRIDE) + (indices.Count * sizeof(uint));
        using MemoryStream stream = new(byteCount);
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true)) {
            foreach (Vertex vertex in vertices) {
                WriteVector3(writer, vertex.Position);
                WriteVector3(writer, vertex.Normal);
                WriteVector2(writer, vertex.Uv);
                WriteVector2(writer, vertex.Uv1);
                WriteVector2(writer, vertex.Uv2);
                WriteVector2(writer, vertex.Uv3);
                WriteColor(writer, vertex.Color);
                WriteVector4(writer, vertex.Tangent);
            }

            foreach (uint index in indices) {
                writer.Write(index);
            }
        }

        return stream.ToArray();
    }

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

    /// <summary>
    /// Builds the synthetic face the font tests load: a 1000-unit em with an 800/-200 ascender and
    /// descender, a <c>.notdef</c> triangle at glyph zero, and a mapped square <c>A</c> at glyph one
    /// with a 600-unit advance and one -100-unit kern pair for A followed by A.
    /// </summary>
    internal static byte[] CreateFontFace() {
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        int glyphId = builder.AddSimple(
            600,
            0,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(500, 0),
            new SyntheticPoint(500, 500),
            new SyntheticPoint(0, 500));
        return builder
            .WithCmap(4, (0x41, glyphId))
            .WithKernPairs((glyphId, glyphId, -100))
            .Build();
    }

    internal static FontAssetDescriptor CreateFontDescriptor(
        byte[] face,
        int msdfPixelSize = 32,
        int msdfRange = 4,
        int msdfPadding = 2,
        uint? byteLength = null,
        string facePayload = FONT_PAYLOAD_PATH) => new() {
            FacePayload = facePayload,
            Format = "ttf",
            ByteLength = byteLength ?? (uint)face.Length,
            MsdfPixelSize = msdfPixelSize,
            MsdfRange = msdfRange,
            MsdfPadding = msdfPadding,
        };

    /// <summary>
    /// Builds the font descriptor entry and its sibling face payload entry. A test that needs a broken
    /// pair passes its own descriptor or face bytes.
    /// </summary>
    internal static (string Path, AssetEntryType Type, byte[] Payload)[] CreateFontEntries(
        byte[]? face = null,
        FontAssetDescriptor? descriptor = null,
        string path = FONT_PATH,
        string payloadPath = FONT_PAYLOAD_PATH) {
        byte[] resolvedFace = face ?? CreateFontFace();
        FontAssetDescriptor resolvedDescriptor =
            descriptor ?? CreateFontDescriptor(resolvedFace, facePayload: payloadPath);
        return [
            (path, AssetEntryType.Font, AssetSerialization.Serialize(resolvedDescriptor)),
            (payloadPath, AssetEntryType.Raw, resolvedFace),
        ];
    }

    /// <summary>Builds the synthetic mono font descriptor entry and its sibling face payload entry.</summary>
    internal static (string Path, AssetEntryType Type, byte[] Payload)[] CreateMonoFontEntries(
        byte[]? face = null) {
        byte[] resolvedFace = face ?? CreateFontFace();
        return CreateFontEntries(
            resolvedFace,
            CreateFontDescriptor(resolvedFace, facePayload: MONO_FONT_PAYLOAD_PATH),
            MONO_FONT_PATH,
            MONO_FONT_PAYLOAD_PATH);
    }

    /// <summary>
    /// Serializes the pre-M6 font descriptor shape, including its removed <c>Ranges</c> member, for the
    /// legacy-pack regression test.
    /// </summary>
    internal static byte[] CreateLegacyFontDescriptorPayload(byte[] face) {
        ArgumentNullException.ThrowIfNull(face);

        return AssetSerialization.Serialize(new LegacyFontAssetDescriptor {
            FacePayload = FONT_PAYLOAD_PATH,
            Format = "ttf",
            ByteLength = (uint)face.Length,
            Ranges = ["U+0020-007E"],
            MsdfPixelSize = 32,
            MsdfRange = 4,
            MsdfPadding = 2,
        });
    }

    /// <summary>Opens a pack archive over the standard synthetic font entries.</summary>
    internal static PackArchive OpenFontArchive()
        => PackArchive.Open(BuildPack(CreateFontEntries()), "font-format.pck");

    internal static byte[] CreatePattern(int length, int seed) {
        byte[] payload = new byte[length];
        new Random(seed).NextBytes(payload);
        return payload;
    }

    internal static PackBuilder CreateStandardBuilder(string displayPath = "catalog.pck") {
        // Catalog behavior is independent of payload compression. The release policy pays a fixed
        // max-quality Zstandard cost per payload, which dominated this suite; PackArchiveTests keeps
        // the compressed-reader coverage.
        var builder = new PackBuilder(displayPath, PackCompressionPolicy.Debug);
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

    internal static byte[] BuildPack(params (string Path, AssetEntryType Type, byte[] Payload)[] entries)
        => BuildPack(PackCompressionPolicy.Debug, entries);

    internal static byte[] BuildPack(
        PackCompressionPolicy policy,
        params (string Path, AssetEntryType Type, byte[] Payload)[] entries) {
        // See CreateStandardBuilder: these fixtures exercise catalog semantics, not compression.
        var builder = new PackBuilder("test.pck", policy);
        foreach ((string path, AssetEntryType type, byte[] payload) in entries) {
            builder.Add(path, type, payload);
        }

        return builder.Build();
    }

    internal static AssetTypeRegistration SettingsRegistration
        => new LoaderAssetTypeRegistration<CatalogTestSettings>(
            new MessagePackAssetLoader<CatalogTestSettings>(),
            CUSTOM_DATA_FAILURE_DESCRIPTION);

    private static void WriteVector2(BinaryWriter writer, System.Numerics.Vector2 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static void WriteVector3(BinaryWriter writer, System.Numerics.Vector3 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteVector4(BinaryWriter writer, System.Numerics.Vector4 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }

    private static void WriteColor(BinaryWriter writer, Color value) {
        writer.Write(value.R);
        writer.Write(value.G);
        writer.Write(value.B);
        writer.Write(value.A);
    }
}

/// <summary>
/// One started catalog with a GPU upload pump standing in for the render loop.
/// </summary>
/// <remarks>
/// The catalog is deterministic: the pump thread waits on the bridge signal and the fake worker's
/// progress signal instead of polling, and <see cref="Dispose"/> stops the catalog before stopping the
/// pump so every queued destruction is serviced. Tests synchronize through the fake worker's call
/// records and explicit events.
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
        WaitHandle[] handles = [Bridge.Signal, _pumpStop.WaitHandle, Worker.Progress];
        while (true) {
            int index = WaitHandle.WaitAny(handles);
            if (index == 1) {
                return;
            }

            Bridge.ProcessPending(Worker);
        }
    }
}
