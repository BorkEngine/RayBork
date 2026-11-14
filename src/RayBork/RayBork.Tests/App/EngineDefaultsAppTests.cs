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
        // owns (the defaults plus the primitive program and meshes) is released by shutdown.
        Assert.Equal(12, worker.UploadCallCount);
        Assert.Equal(12, worker.DestroyCalls.Count);
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

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PrimitiveAssetsArePreloadedBeforeSetupAndReleasedAfterShutdown() {
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        PrimitiveAssets? setupPrimitives = null;
        PrimitiveAssets? tickPrimitives = null;
        int uploadCountAtSetup = 0;
        int destroyCountAtSetup = -1;
        int destroyCountInTick = -1;
        bool meshesUsableInTick = false;
        bool materialsUsableInTick = false;
        RayBorkApp? app = null;
        ScriptedGame game = new();
        game.SetupHandler = context => {
            setupPrimitives = app!.Cameras?.Primitives;
            uploadCountAtSetup = worker.UploadCallCount;
            destroyCountAtSetup = worker.DestroyCalls.Count;
            return Task.CompletedTask;
        };
        game.TickHandler = context => {
            tickPrimitives = app!.Cameras?.Primitives;
            destroyCountInTick = worker.DestroyCalls.Count;
            meshesUsableInTick = tickPrimitives is not null
                && !tickPrimitives.Cube.IsDisposed
                && !tickPrimitives.Sphere.IsDisposed
                && !tickPrimitives.Plane.IsDisposed
                && !tickPrimitives.Quad.IsDisposed
                && !tickPrimitives.Triangle.IsDisposed
                && !tickPrimitives.Line.IsDisposed
                && !tickPrimitives.WireCube.IsDisposed
                && !tickPrimitives.WireSphere.IsDisposed
                && tickPrimitives.Cube.IsProcedural
                && tickPrimitives.Cube.Topology == PrimitiveTopology.TriangleList;
            materialsUsableInTick = tickPrimitives is not null
                && tickPrimitives.PrimitiveMaterial.Path == "/shaders/primitive"
                && tickPrimitives.MeshMaterial.Path == "/shaders/mesh"
                && !tickPrimitives.PrimitiveMaterial.IsDisposed
                && !tickPrimitives.MeshMaterial.IsDisposed;
            context.RequestExit();
        };

        app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(2)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(setupPrimitives);
        Assert.Same(setupPrimitives, tickPrimitives);
        // The three defaults, the primitive program, and the eight primitive meshes upload before
        // Setup; nothing is destroyed until shutdown.
        Assert.Equal(12, uploadCountAtSetup);
        Assert.Equal(0, destroyCountAtSetup);
        Assert.Equal(0, destroyCountInTick);
        Assert.True(meshesUsableInTick);
        Assert.True(materialsUsableInTick);

        PrimitiveAssets created = Assert.IsType<PrimitiveAssets>(setupPrimitives);
        Assert.True(created.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = created.Cube);
        Assert.Throws<ObjectDisposedException>(() => _ = created.PrimitiveMaterial);
        Assert.Null(app.Cameras!.Primitives);
        Assert.Equal(12, worker.DestroyCalls.Count);

        created.Dispose();
        Assert.Equal(12, worker.DestroyCalls.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AFailedPrimitiveMeshUploadFailsStartupAndReleasesThePartialLoad() {
        FakeAssetGpuWorker worker = new();
        int meshUploads = 0;
        worker.UploadHandler = upload => {
            if (upload.Kind == AssetGpuUploadKind.Mesh
                && Interlocked.Increment(ref meshUploads) == 3) {
                throw new InvalidOperationException("Injected primitive mesh upload failure.");
            }

            return new FakeGpuAssetResource(upload, worker.NextHandle());
        };
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(2)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        AssetLoadException thrown;
        try {
            thrown = Assert.Throws<AssetLoadException>(app.Run);
        } finally {
            app.Dispose();
        }

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("/shaders/primitive", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.ShutdownCount);
        // The three defaults, the primitive program, and two completed primitive meshes were created
        // before the third mesh upload failed. The mesh program is shared with the defaults, so only
        // six resources exist and every one of them is released by the failed startup.
        Assert.Equal(7, worker.UploadCallCount);
        Assert.Equal(6, worker.DestroyCalls.Count);
    }
}
