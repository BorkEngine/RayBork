using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using RayBork.Tests.Assets;
using RayBork.Tests.Assets.Catalog;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests;

public sealed class AssetLifecycleTests {
    private const int FAST_TARGET_FPS = 1000;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetupLoadsEveryAssetKindAndShutdownDisposesThem() {
        using TempPackDirectory directory = new();
        string packPath = directory.GetPath("game.pck");
        File.WriteAllBytes(packPath, CatalogTestData.BuildStandardPack());
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();

        Mesh? mesh = null;
        Texture? texture = null;
        Shader? shader = null;
        CatalogTestSettings? settings = null;
        EngineContext? setupContext = null;
        bool catalogHadAllPaths = false;
        bool metadataReadableBeforeShutdown = false;

        ScriptedGame game = new();
        game.SetupHandler = async context => {
            setupContext = context;
            catalogHadAllPaths = context.Assets.Exists("/meshes/quad")
                && context.Assets.Exists("/textures/albedo")
                && context.Assets.Exists("/shaders/basic")
                && context.Assets.Exists("/data/settings");
            mesh = await context.Assets.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken);
            texture = await context.Assets.Load<Texture>("/textures/albedo", TestContext.Current.CancellationToken);
            shader = await context.Assets.Load<Shader>("/shaders/basic", TestContext.Current.CancellationToken);
            settings = await context.Assets.Load<CatalogTestSettings>(
                "/data/settings",
                TestContext.Current.CancellationToken);
        };
        game.TickHandler = context => {
            metadataReadableBeforeShutdown = mesh!.VertexCount == 3
                && texture!.Width == 4
                && shader!.Stages == (ShaderStageMask.Vertex | ShaderStageMask.Fragment);
            context.RequestExit();
        };
        game.ShutdownHandler = () => {
            Assert.False(mesh!.IsDisposed);
            mesh.Dispose();
            texture!.Dispose();
            shader!.Dispose();
            return Task.CompletedTask;
        };

        RayBorkApp app = BuildApp(game, platform, renderer, packPath);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(setupContext);
        Assert.True(catalogHadAllPaths);
        Assert.True(metadataReadableBeforeShutdown);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
        Assert.Equal("catalog", settings!.Name);
        Assert.True(mesh!.IsDisposed);
        Assert.True(texture!.IsDisposed);
        Assert.True(shader!.IsDisposed);
        // Three game assets plus the twelve engine assets the app preloads before Setup.
        Assert.Equal(15, worker.UploadCallCount);
        Assert.Equal(15, worker.DestroyCalls.Count);
        Assert.Throws<ObjectDisposedException>(() => setupContext!.Assets.Exists("/meshes/quad"));
        Assert.Throws<ObjectDisposedException>(
            () => {
                _ = setupContext!.Assets.Load<Mesh>(
                    "/meshes/quad",
                    TestContext.Current.CancellationToken);
            });
        Assert.True(platform.IsDisposed);
        Assert.Equal(1, renderer.DisposeCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppRunsWithoutPacksAndTheCatalogIsDisposedAfterRun() {
        FakeRendererBackend renderer = new();
        FakeEnginePlatform platform = new();
        EngineContext? setupContext = null;
        bool exists = true;
        ScriptedGame game = new();
        game.SetupHandler = context => {
            setupContext = context;
            exists = context.Assets.Exists("/anything");
            return Task.CompletedTask;
        };
        game.TickHandler = context => context.RequestExit();

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(FAST_TARGET_FPS)
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.False(exists);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
        Assert.NotNull(setupContext);
        Assert.Throws<ObjectDisposedException>(() => setupContext!.Assets.Exists("/anything"));
        Assert.Equal(1, renderer.InitializeCallCount);
        Assert.True(platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CatalogStopDisposesAssetsTheGameForgotToRelease() {
        using TempPackDirectory directory = new();
        string packPath = directory.GetPath("game.pck");
        File.WriteAllBytes(packPath, CatalogTestData.BuildStandardPack());
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        Mesh? mesh = null;

        ScriptedGame game = new();
        game.SetupHandler = async context => {
            mesh = await context.Assets.Load<Mesh>(
                "/meshes/quad",
                TestContext.Current.CancellationToken);
        };
        game.TickHandler = context => context.RequestExit();

        RayBorkApp app = BuildApp(game, platform, renderer, packPath);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(mesh);
        Assert.True(mesh!.IsDisposed);
        // The leaked mesh plus the twelve engine assets, all released by shutdown.
        Assert.Equal(13, worker.DestroyCalls.Count);
        Assert.Throws<ObjectDisposedException>(() => mesh.Dispose());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingPackIsAStartupFailureThatSkipsGameCallbacksAndCleansUp() {
        using TempPackDirectory directory = new();
        string missing = directory.GetPath("missing.pck");
        FakeRendererBackend renderer = new();
        FakeEnginePlatform platform = new();
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };

        RayBorkApp app = BuildApp(game, platform, renderer, missing);
        AssetPackException thrown;
        try {
            thrown = Assert.Throws<AssetPackException>(() => app.Run());
        } finally {
            app.Dispose();
        }

        Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.ShutdownCount);
        Assert.Equal(1, renderer.DisposeCallCount);
        Assert.True(renderer.SurfaceDestroyed);
        Assert.True(platform.IsDisposed);
        Assert.True(platform.InitializeCalled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CorruptPackIsAStartupFailureBeforeSetup() {
        using TempPackDirectory directory = new();
        string packPath = directory.GetPath("corrupt.pck");
        File.WriteAllBytes(packPath, CatalogTestData.BuildStandardPack()[..20]);
        FakeRendererBackend renderer = new();
        FakeEnginePlatform platform = new();
        ScriptedGame game = new() {
            SetupHandler = _ => Task.FromException(new InvalidOperationException("Setup must not run.")),
            TickHandler = context => context.RequestExit(),
        };

        RayBorkApp app = BuildApp(game, platform, renderer, packPath);
        AssetPackException thrown;
        try {
            thrown = Assert.Throws<AssetPackException>(() => app.Run());
        } finally {
            app.Dispose();
        }

        Assert.Equal(DiagnosticCodes.ASSET_PACK_READ_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(1, renderer.DisposeCallCount);
        Assert.True(platform.IsDisposed);
    }

    private static RayBorkApp BuildApp(
        ScriptedGame game,
        FakeEnginePlatform platform,
        FakeRendererBackend renderer,
        string packPath) => new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(FAST_TARGET_FPS)
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .WithAssetsPack(packPath)
            .WithAssetType<CatalogTestSettings>()
            .Build(_ => platform, () => renderer, TimeProvider.System);
}
