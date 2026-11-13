using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetCatalogLoadTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadsMeshMetadataAndCreatesOneUpload() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken);

        try {
            Assert.Equal("/meshes/quad", mesh.Path);
            Assert.False(mesh.IsProcedural);
            Assert.Equal(3, mesh.VertexCount);
            Assert.Equal(3, mesh.IndexCount);
            Assert.Equal(3, mesh.Vertices.Length);
            Assert.Equal(3, mesh.Indices.Length);
            Assert.Equal(PrimitiveTopology.TriangleList, mesh.Topology);
            Assert.Equal(new System.Numerics.Vector3(-1f, -2f, -3f), mesh.Bounds.Min);
            Assert.Equal(new System.Numerics.Vector3(1f, 2f, 3f), mesh.Bounds.Max);
            Assert.False(mesh.IsDisposed);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            MeshGpuUpload upload = Assert.IsType<MeshGpuUpload>(harness.Worker.UploadCalls[0]);
            Assert.Equal("meshes/quad", upload.Path);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadedMeshRoundTripsThePackedCpuVerticesAndIndices() {
        Vertex[] vertices = [
            new(
                new System.Numerics.Vector3(1.5f, -2.5f, 3.5f),
                new System.Numerics.Vector3(0f, 0f, 1f),
                new System.Numerics.Vector2(0.125f, 0.25f),
                new System.Numerics.Vector2(0.375f, 0.5f),
                new System.Numerics.Vector2(0.625f, 0.75f),
                new System.Numerics.Vector2(0.875f, 1f),
                new Color(0.2f, 0.4f, 0.6f, 0.8f),
                new System.Numerics.Vector4(1f, 0f, 0f, -1f)),
            new(new System.Numerics.Vector3(-1f, 2f, -3f)),
            new(new System.Numerics.Vector3(0f, 0f, 0f), normal: new System.Numerics.Vector3(0f, 1f, 0f)),
        ];
        uint[] indices = [2, 1, 0];
        MeshAssetDescriptor descriptor = new() {
            Payload = "meshes/cpu-mesh",
            VertexCount = 3,
            IndexCount = 3,
            Bounds = [-1f, -3f, -3f, 1.5f, 2f, 3.5f],
            Topology = PrimitiveTopology.TriangleList,
        };
        byte[] pack = CatalogTestData.BuildPack(
            ("meshes/cpu", AssetEntryType.Mesh, AssetSerialization.Serialize(descriptor)),
            ("meshes/cpu-mesh", AssetEntryType.Raw, CatalogTestData.CreateMeshPayload(vertices, indices)));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/cpu", TestContext.Current.CancellationToken);

        try {
            Assert.False(mesh.IsProcedural);
            Assert.Equal(3, mesh.Vertices.Length);
            for (int index = 0; index < vertices.Length; index++) {
                Vertex expected = vertices[index];
                Vertex actual = mesh.Vertices.Span[index];
                Assert.Equal(expected.Position, actual.Position);
                Assert.Equal(expected.Normal, actual.Normal);
                Assert.Equal(expected.Uv, actual.Uv);
                Assert.Equal(expected.Uv1, actual.Uv1);
                Assert.Equal(expected.Uv2, actual.Uv2);
                Assert.Equal(expected.Uv3, actual.Uv3);
                Assert.Equal(expected.Color, actual.Color);
                Assert.Equal(expected.Tangent, actual.Tangent);
            }

            Assert.Equal(indices, mesh.Indices.ToArray());
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReservedProceduralPathIsNeverResolvedFromAPack() {
        byte[] pack = CatalogTestData.BuildPack(
            ("_procedural/mesh/7", AssetEntryType.Mesh, AssetSerialization.Serialize(CatalogTestData.CreateMeshDescriptor())),
            ("_procedural/mesh/7-mesh", AssetEntryType.Raw, CatalogTestData.CreateMeshPayload()));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        Assert.False(harness.Catalog.Exists("/_procedural/mesh/7"));
        AssetNotFoundException loadFailure = await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.Load<Mesh>("/_procedural/mesh/7", TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_NOT_FOUND, loadFailure.DiagnosticCode);
        await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.LoadData("/_procedural/mesh/7", TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadsTextureMetadata() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        Texture texture = await harness.Catalog.Load<Texture>("/textures/albedo", TestContext.Current.CancellationToken);

        try {
            Assert.Equal("/textures/albedo", texture.Path);
            Assert.Equal(4, texture.Width);
            Assert.Equal(2, texture.Height);
            Assert.Equal(3, texture.MipLevels);
            Assert.Equal(TextureFormat.Rgba8Srgb, texture.Format);
            Assert.True(texture.IsSrgb);
            Assert.Equal(new SamplerState(TextureFilter.Linear, TextureWrap.Repeat, 1f), texture.Sampler);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.IsType<TextureGpuUpload>(harness.Worker.UploadCalls[0]);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadsShaderMetadataFromTheDescriptor() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        Shader shader = await harness.Catalog.Load<Shader>("/shaders/basic", TestContext.Current.CancellationToken);

        try {
            Assert.Equal("/shaders/basic", shader.Path);
            Assert.Equal(ShaderStageMask.Vertex | ShaderStageMask.Fragment, shader.Stages);
            Assert.Equal(RenderState.AlphaBlend, shader.DefaultRenderState);
            ShaderUniform uniform = Assert.Single(shader.Uniforms);
            Assert.Equal(new ShaderUniform("uTint", ShaderUniformType.Vector4), uniform);
            ShaderTextureBinding binding = Assert.Single(shader.Textures);
            Assert.Equal(new ShaderTextureBinding("mainTexture", 0), binding);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.IsType<ShaderGpuUpload>(harness.Worker.UploadCalls[0]);
        } finally {
            shader.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SamePathAndTypeReturnsTheSameObjectAndOneUpload() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Mesh first = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        Mesh second = await harness.Catalog.Load<Mesh>("meshes/quad", cancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, harness.Worker.UploadCallCount);

        first.Dispose();
        Assert.False(second.IsDisposed);
        second.Dispose();
        Assert.True(first.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadDataReturnsDescriptorBytesForTypedAssets() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        byte[] descriptor = await harness.Catalog.LoadData(
            "/meshes/quad",
            TestContext.Current.CancellationToken);

        Assert.Equal(AssetSerialization.Serialize(CatalogTestData.CreateMeshDescriptor()), descriptor);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadDataReturnsMessagePackBytesForDataEntriesAndAFreshArrayPerCall() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        byte[] first = await harness.Catalog.LoadData("/data/settings", cancellationToken);
        byte[] second = await harness.Catalog.LoadData("/data/settings", cancellationToken);

        Assert.Equal(CatalogTestData.CreateDataPayload(), first);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CustomDataReturnsAFreshInstanceAndCreatesNoUpload() {
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [CatalogTestData.SettingsRegistration]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        CatalogTestSettings first = await harness.Catalog.Load<CatalogTestSettings>("/data/settings", cancellationToken);
        CatalogTestSettings second = await harness.Catalog.Load<CatalogTestSettings>("data/settings", cancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal("catalog", first.Name);
        Assert.Equal(7, first.Difficulty);
        Assert.Equal(7, second.Difficulty);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CustomDataLoadInvokesTheRegisteredLoaderWithTheCanonicalContext() {
        RecordingAssetLoader loader = new();
        AssetTypeRegistration registration = new LoaderAssetTypeRegistration<CatalogTestSettings>(loader);
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [registration]);

        CatalogTestSettings settings = await harness.Catalog.Load<CatalogTestSettings>(
            "/data/settings",
            TestContext.Current.CancellationToken);

        Assert.Equal("catalog", settings.Name);
        Assert.Equal(7, settings.Difficulty);
        Assert.Equal("/data/settings", loader.Path);
        Assert.Equal(CatalogTestData.CreateDataPayload(), loader.Payload);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EngineLoadWaitsForTheDeferredGpuUploadWhileOtherLoadsComplete() {
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [CatalogTestData.SettingsRegistration]);
        harness.Worker.DeferUploads = true;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<Texture> load = harness.Catalog.Load<Texture>("/textures/albedo", cancellationToken);
        FakeGpuAssetUploadOperation upload = harness.Worker.WaitForUpload(_timeout);
        Assert.False(load.IsCompleted);

        byte[] descriptor = await harness.Catalog.LoadData("/meshes/quad", cancellationToken);
        Assert.Equal(AssetSerialization.Serialize(CatalogTestData.CreateMeshDescriptor()), descriptor);
        CatalogTestSettings settings = await harness.Catalog.Load<CatalogTestSettings>(
            "/data/settings",
            cancellationToken);
        Assert.Equal("catalog", settings.Name);
        Assert.False(load.IsCompleted);

        upload.Complete();
        Texture texture = await load.WaitAsync(_timeout, cancellationToken);

        try {
            Assert.Equal("/textures/albedo", texture.Path);
            Assert.Equal(4, texture.Width);
            Assert.Equal(1, harness.Worker.UploadCallCount);
        } finally {
            texture.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationWhileTheUploadIsPendingAcquiresNoReferenceAndAllowsARetry() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        harness.Worker.DeferUploads = true;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = new();

        Task<Mesh> canceled = harness.Catalog.Load<Mesh>("/meshes/quad", cancellation.Token);
        FakeGpuAssetUploadOperation firstUpload = harness.Worker.WaitForUpload(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        // A canceled waiter never cancels the shared upload for the other potential waiters.
        Assert.False(firstUpload.IsCanceled);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        firstUpload.Complete();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "A load with no remaining waiters must destroy the uploaded resource.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        Task<Mesh> retryTask = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        FakeGpuAssetUploadOperation retryUpload = harness.Worker.WaitForUpload(1, _timeout);
        retryUpload.Complete();
        Mesh retry = await retryTask.WaitAsync(_timeout, cancellationToken);

        try {
            Assert.Equal(3, retry.VertexCount);
            Assert.Equal(2, harness.Worker.UploadCallCount);
        } finally {
            retry.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnregisteredTypeThrowsNotSupportedNamingWithAssetType() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        NotSupportedException thrown = Assert.Throws<NotSupportedException>(
            () => {
                _ = harness.Catalog.Load<CatalogTestSettings>(
                    "/data/settings",
                    TestContext.Current.CancellationToken);
            });

        Assert.Contains("WithAssetType<T>()", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExistsMatchesTheCanonicalPathAndRejectsInvalidInput() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        Assert.True(harness.Catalog.Exists("/MESHES/Quad"));
        Assert.True(harness.Catalog.Exists("meshes/quad"));
        Assert.False(harness.Catalog.Exists("/meshes/absent"));
        Assert.Throws<ArgumentNullException>(() => harness.Catalog.Exists(null!));
        Assert.Throws<ArgumentException>(() => harness.Catalog.Exists("meshes//quad"));
        Assert.Throws<ArgumentException>(() => harness.Catalog.Exists("meshes\\quad"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("meshes//quad")]
    [InlineData("meshes/quad/")]
    [InlineData("meshes/.")]
    [InlineData("meshes/..")]
    [InlineData("meshes\\quad")]
    [InlineData("meshes/quad.glb")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LoadRejectsInvalidPaths(string path) {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        Assert.Throws<ArgumentException>(
            () => {
                _ = harness.Catalog.Load<Mesh>(path, TestContext.Current.CancellationToken);
            });
        Assert.Throws<ArgumentException>(
            () => {
                _ = harness.Catalog.LoadData(path, TestContext.Current.CancellationToken);
            });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MissingPathThrowsAssetNotFound() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetNotFoundException thrown = await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.Load<Mesh>("/meshes/absent", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_NOT_FOUND, thrown.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TypeMismatchThrowsAssetTypeMismatch() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetTypeMismatchException thrown = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Texture>("/meshes/quad", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ModelAndFontEntriesRequestedAsMeshThrowTypeMismatch() {
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.MODEL_PATH, AssetEntryType.Model, [1, 2, 3]),
            (CatalogTestData.FONT_PATH, AssetEntryType.Font, [4, 5, 6]));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetTypeMismatchException model = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Mesh>("/models/castle", TestContext.Current.CancellationToken));
        AssetTypeMismatchException font = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Mesh>("/fonts/inter", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, model.DiagnosticCode);
        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, font.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuiltInPackIsMountedBelowUserPacks() {
        byte[] builtIn = CatalogTestData.BuildPack(
            ("textures/shared", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTexture(8))),
            ("textures/shared-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
            ("textures/builtin-only", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTexture(16))),
            ("textures/builtin-only-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()));
        byte[] user = CatalogTestData.BuildPack(
            ("textures/shared", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTexture(4))),
            ("textures/shared-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()));
        using CatalogTestHarness harness = new(builtIn, [user]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Texture shadowed = await harness.Catalog.Load<Texture>("/textures/shared", cancellationToken);
        Texture builtInOnly = await harness.Catalog.Load<Texture>("/textures/builtin-only", cancellationToken);

        try {
            Assert.Equal(4, shadowed.Width);
            Assert.Equal(16, builtInOnly.Width);
            Assert.True(harness.Catalog.Exists("/textures/builtin-only"));
        } finally {
            shadowed.Dispose();
            builtInOnly.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RelativePackPathsResolveAgainstTheAppBaseDirectory() {
        string fileName = "relative-catalog-" + Guid.NewGuid().ToString("N") + ".pck";
        string fullPath = Path.Combine(AppContext.BaseDirectory, fileName);
        File.WriteAllBytes(fullPath, CatalogTestData.BuildStandardPack());
        GpuAssetBridge bridge = new();
        bridge.AttachWorker(new FakeAssetGpuWorker());
        AssetCatalog catalog = new(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            bridge,
            ReadOnlyMemory<byte>.Empty,
            [fileName],
            []);

        try {
            catalog.Start();

            Assert.True(catalog.Exists("/meshes/quad"));
        } finally {
            catalog.Stop();
            bridge.Close(null);
            File.Delete(fullPath);
        }
    }

    private static TextureAssetDescriptor CreateTexture(uint width) => new() {
        Payload = width == 16 ? "textures/builtin-only-ktx2" : "textures/shared-ktx2",
        Width = width,
        Height = 2,
        MipLevels = 1,
        Format = TextureFormat.Rgba8Srgb,
        IsSrgb = true,
        Filter = TextureFilter.Linear,
        Wrap = TextureWrap.Repeat,
        MaxAnisotropy = 1f,
    };

    internal static void WaitUntil(Func<bool> condition, string message) => Assert.True(
        SpinWait.SpinUntil(condition, _timeout),
        message);

    private sealed class RecordingAssetLoader : IAssetLoader<CatalogTestSettings> {
        internal string? Path { get; private set; }

        internal byte[]? Payload { get; private set; }

        public AssetEntryType EntryType => AssetEntryType.Data;

        public async Task<CatalogTestSettings> LoadAsync(AssetLoadContext context) {
            Path = context.Path;
            byte[] payload = await context.LoadPayloadAsync().ConfigureAwait(false);
            Payload = payload;
            return AssetSerialization.Deserialize<CatalogTestSettings>(payload);
        }
    }
}
