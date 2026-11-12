using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests;

/// <summary>
/// Tests the app wiring of <see cref="EngineDefaults"/>: the preloaded built-ins are available in
/// <see cref="IGame.Setup"/>, usable during ticks, and released during shutdown.
/// </summary>
public sealed class EngineDefaultsAppTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultsArePreloadedBeforeSetupAndReleasedAfterShutdown() {
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        EngineDefaults? defaults = null;
        Shader? setupMaterial = null;
        Shader? tickMaterial = null;
        bool whiteAndErrorPresent = false;
        bool programShared = false;
        bool materialUsableInTick = false;
        ScriptedGame game = new();
        game.SetupHandler = context => {
            defaults = context.Defaults;
            whiteAndErrorPresent =
                context.Defaults.White.Path == "/textures/white"
                && context.Defaults.Error.Path == "/textures/error";
            setupMaterial = context.Defaults.CreateMeshShader();
            tickMaterial = context.Defaults.CreateMeshShader();
            programShared = ReferenceEquals(setupMaterial.Program, tickMaterial.Program);
            return Task.CompletedTask;
        };
        game.TickHandler = context => {
            materialUsableInTick = setupMaterial!.Path == "/shaders/mesh" && !setupMaterial.IsDisposed;
            context.RequestExit();
        };
        game.ShutdownHandler = () => {
            setupMaterial!.Dispose();
            tickMaterial!.Dispose();
            return Task.CompletedTask;
        };

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(2)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(defaults);
        Assert.True(whiteAndErrorPresent);
        Assert.True(programShared);
        Assert.True(materialUsableInTick);
        Assert.True(setupMaterial!.IsDisposed);
        Assert.True(tickMaterial!.IsDisposed);

        // The two materials were preloaded without a second program upload, and everything the engine
        // owns is released by shutdown.
        Assert.Equal(3, worker.UploadCallCount);
        Assert.Equal(3, worker.DestroyCalls.Count);
        Assert.True(defaults!.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.White);
        Assert.Throws<ObjectDisposedException>(() => _ = defaults.Error);
        Assert.Throws<ObjectDisposedException>(defaults.CreateMeshShader);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultMeshMaterialDrawsWithTheMeshShaderProgram() {
        FakeRendererBackend renderer = new();
        FakeEnginePlatform platform = new();
        EngineContext? tickContext = null;
        Shader? material = null;
        Mesh? mesh = null;
        int drawCalls = 0;
        bool sawProgramOnTheRecord = false;
        ScriptedGame game = new();
        game.SetupHandler = async context => {
            material = context.Defaults.CreateMeshShader();
            MeshBuilder builder = context.CreateMeshBuilder();
            builder.SetVertices([
                new Vertex(System.Numerics.Vector3.Zero),
                new Vertex(new System.Numerics.Vector3(1f, 0f, 0f)),
                new Vertex(new System.Numerics.Vector3(0f, 1f, 0f)),
            ]);
            builder.SetIndices([0u, 1u, 2u]);
            mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        };
        game.TickHandler = context => {
            tickContext = context;
            using Camera camera = context.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                near: 0.1f,
                far: 100f);
            camera.DrawMesh(mesh!, Transform.Identity, material!);
            context.RequestExit();
        };
        game.ShutdownHandler = () => {
            mesh!.Dispose();
            material!.Dispose();
            return Task.CompletedTask;
        };
        renderer.OnSubmit = packet => {
            if (packet.PassCount == 0) {
                return;
            }

            drawCalls = packet.Passes[0].DrawCount;
            sawProgramOnTheRecord = ReferenceEquals(
                packet.Passes[0].Draws[0].Program,
                material!.Program.Lifetime);
        };

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(2)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(tickContext);
        Assert.Equal(1, drawCalls);
        Assert.True(sawProgramOnTheRecord);
        Assert.True(material!.IsDisposed);
        Assert.True(mesh!.IsDisposed);
    }
}
