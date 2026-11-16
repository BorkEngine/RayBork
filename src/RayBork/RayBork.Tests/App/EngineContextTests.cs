using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;

namespace RayBork.Tests;

/// <summary>
/// Tests the <see cref="EngineContext"/> wiring of the app-lifetime runtime tone-mapping settings.
/// </summary>
public sealed class EngineContextTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConstructorRejectsNullDependenciesAndExposesTheTonemappingInstance() {
        using CatalogTestHarness harness = new(userPacks: [BuildEngineDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);
        try {
            FakeEnginePlatform platform = new();
            CameraService cameras = new(platform.Window);
            TonemappingSettings tonemapping = new(1f, 4f, 2f);
            using RenderTextureService renderTextures = new();

            ArgumentNullException thrown = Assert.Throws<ArgumentNullException>(
                () => CreateContext(platform, harness.Catalog, defaults, cameras, null!, renderTextures));
            Assert.Equal("tonemapping", thrown.ParamName);

            EngineContext context = CreateContext(
                platform,
                harness.Catalog,
                defaults,
                cameras,
                tonemapping,
                renderTextures);
            Assert.Same(tonemapping, context.Tonemapping);
        } finally {
            defaults.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CreateCameraForwardsTheRenderTargetToTheCameraService() {
        using CatalogTestHarness harness = new(userPacks: [BuildEngineDefaultsPack()]);
        EngineDefaults defaults = await EngineDefaults.LoadAsync(harness.Catalog);
        using RenderTextureService renderTextures = new();
        using FakeRendererBackend renderer = new();
        Task<RenderTexture> create = renderTextures.CreateAsync(
            new RenderTextureDescription(16, 16),
            CancellationToken.None);
        renderer.ProcessRenderTextureRequests(renderTextures);
        using RenderTexture target = await create;
        try {
            FakeEnginePlatform platform = new();
            CameraService cameras = new(platform.Window);
            EngineContext context = CreateContext(
                platform,
                harness.Catalog,
                defaults,
                cameras,
                new TonemappingSettings(0f, 16.29f, 1.25f),
                renderTextures);
            RenderFramePacket packet = new(1);
            TickThread.Attach();
            try {
                packet.BeginRecording(0.0, 0.0);
                cameras.BeginFrame(packet, 0.0, 0.0);
                using Camera camera = context.CreateCamera(
                    Transform.Identity,
                    CameraProjection.Perspective,
                    60f,
                    0f,
                    ClearMode.ColorAndDepth,
                    null,
                    0.1f,
                    100f,
                    null,
                    target);

                Assert.Same(target, camera.Target);
                Assert.Equal(target.TargetId, packet.Passes[0].TargetId);
            } finally {
                cameras.AbandonFrame();
                packet.Retention.Release();
                TickThread.Detach();
            }
        } finally {
            defaults.Dispose();
        }
    }

    private static EngineContext CreateContext(
        IEnginePlatform platform,
        IAssetCatalog catalog,
        EngineDefaults defaults,
        CameraService cameras,
        TonemappingSettings tonemapping,
        RenderTextureService renderTextures) => new(
            platform,
            NullLoggerFactory.Instance,
            catalog,
            defaults,
            new FakeAppControl(),
            new FakeInput(),
            0.25,
            static () => 0.0,
            new RenderStatus(),
            cameras,
            tonemapping,
            renderTextures);

    private static byte[] BuildEngineDefaultsPack() => CatalogTestData.BuildPack([
        ("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor())),
        ("shaders/basic-vert", AssetEntryType.Raw, CatalogTestData.CreateShaderVertexPayload()),
        ("shaders/basic-frag", AssetEntryType.Raw, CatalogTestData.CreateShaderFragmentPayload()),
        ("textures/white", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/white-ktx2"))),
        ("textures/white-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
        ("textures/error", AssetEntryType.Texture, AssetSerialization.Serialize(CreateTextureDescriptor("textures/error-ktx2"))),
        ("textures/error-ktx2", AssetEntryType.Raw, CatalogTestData.CreateTexturePayload()),
        .. CatalogTestData.CreateFontEntries(),
        .. CatalogTestData.CreateMonoFontEntries(),
    ]);

    private static TextureAssetDescriptor CreateTextureDescriptor(string payload) => new() {
        Payload = payload,
        Width = 1,
        Height = 1,
        MipLevels = 1,
        Format = TextureFormat.Rgba8Srgb,
        IsSrgb = true,
        Filter = TextureFilter.Linear,
        Wrap = TextureWrap.Clamp,
        MaxAnisotropy = 1f,
    };

    private sealed class FakeAppControl : IAppControl {
        public bool IsExitRequested { get; private set; }

        public void RequestExit() => IsExitRequested = true;
    }
}
