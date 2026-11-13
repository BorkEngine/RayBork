using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Procedural;

public sealed class ProceduralAppTests {
    private const int FAST_TARGET_FPS = 1000;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetupBuildsProceduralAssetsOnAWorkerThreadAndShutdownDisposesThem() {
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        Mesh? mesh = null;
        Texture? texture = null;
        EngineContext? setupContext = null;
        MeshBuilder? leftoverBuilder = null;
        bool meshWasProcedural = false;
        string? meshPath = null;
        int meshVertexCount = 0;
        bool textureWasProcedural = false;
        string? texturePath = null;
        int textureMipLevels = 0;
        TextureFormat textureFormat = TextureFormat.Rgba8Unorm;
        ScriptedGame game = new();
        game.SetupHandler = async context => {
            setupContext = context;
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            Task<Mesh> meshBuild = Task.Run(
                () => {
                    MeshBuilder builder = context.CreateMeshBuilder();
                    builder.SetVertices([
                        new Vertex(System.Numerics.Vector3.Zero),
                        new Vertex(new System.Numerics.Vector3(1f, 0f, 0f)),
                        new Vertex(new System.Numerics.Vector3(0f, 1f, 0f)),
                    ]);
                    builder.SetIndices([0, 1, 2]);
                    builder.ComputeNormals();
                    return builder.BuildAsync(cancellationToken);
                });
            Task<Texture> textureBuild = Task.Run(
                () => {
                    TextureBuilder builder = context.CreateTextureBuilder(2, 2);
                    builder.SetPixels([
                        Color.Red,
                        Color.Green,
                        Color.Blue,
                        Color.White,
                    ]);
                    return builder.BuildAsync(cancellationToken);
                });

            mesh = await meshBuild;
            texture = await textureBuild;
            leftoverBuilder = context.CreateMeshBuilder();
        };
        game.TickHandler = context => context.RequestExit();
        game.ShutdownHandler = () => {
            meshWasProcedural = mesh!.IsProcedural;
            meshPath = mesh.Path;
            meshVertexCount = mesh.VertexCount;
            textureWasProcedural = texture!.IsProcedural;
            texturePath = texture.Path;
            textureMipLevels = texture.MipLevels;
            textureFormat = texture.Format;
            mesh.Dispose();
            texture.Dispose();
            return Task.CompletedTask;
        };

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(FAST_TARGET_FPS)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(setupContext);
        Assert.NotNull(mesh);
        Assert.True(meshWasProcedural);
        Assert.StartsWith("/_procedural/mesh/", meshPath, StringComparison.Ordinal);
        Assert.Equal(3, meshVertexCount);
        Assert.NotNull(texture);
        Assert.True(textureWasProcedural);
        Assert.StartsWith("/_procedural/texture/", texturePath, StringComparison.Ordinal);
        Assert.Equal(2, textureMipLevels);
        Assert.Equal(TextureFormat.Rgba8Srgb, textureFormat);
        Assert.True(mesh!.IsDisposed);
        Assert.True(texture.IsDisposed);
        // Two procedural assets plus the twelve engine assets the app preloads before Setup.
        Assert.Equal(14, worker.UploadCallCount);
        Assert.Equal(14, worker.DestroyCalls.Count);

        Assert.Throws<ObjectDisposedException>(() => setupContext!.CreateMeshBuilder());
        Assert.Throws<ObjectDisposedException>(() => setupContext!.CreateTextureBuilder(1, 1));
        Assert.Throws<ObjectDisposedException>(() => leftoverBuilder!.ComputeBounds());
        Assert.Throws<ObjectDisposedException>(
            () => leftoverBuilder!.BuildAsync(TestContext.Current.CancellationToken)
                .GetAwaiter()
                .GetResult());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickCanStartABuildAndShutdownCanAwaitItWithoutBlockingTheTick() {
        FakeAssetGpuWorker worker = new();
        FakeRendererBackend renderer = new() { AssetGpuWorker = worker };
        FakeEnginePlatform platform = new();
        Task<Mesh>? build = null;
        Mesh? mesh = null;
        string? path = null;
        bool meshWasProcedural = false;
        ScriptedGame game = new();
        game.SetupHandler = context => {
            MeshBuilder builder = context.CreateMeshBuilder();
            builder.SetVertices([
                new Vertex(System.Numerics.Vector3.Zero),
                new Vertex(new System.Numerics.Vector3(1f, 0f, 0f)),
                new Vertex(new System.Numerics.Vector3(0f, 1f, 0f)),
            ]);
            builder.SetIndices([0, 1, 2]);
            build = builder.BuildAsync(TestContext.Current.CancellationToken);
            return Task.CompletedTask;
        };
        game.TickHandler = context => {
            Assert.NotNull(build);
            path = context.Assets.Exists("/_procedural/mesh/1") ? "resolvable" : "reserved";
            context.RequestExit();
        };
        game.ShutdownHandler = async () => {
            mesh = await build!;
            meshWasProcedural = mesh.IsProcedural;
            mesh.Dispose();
        };

        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(FAST_TARGET_FPS)
            .WithLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .Build(_ => platform, () => renderer, TimeProvider.System);
        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.Equal("reserved", path);
        Assert.NotNull(mesh);
        Assert.True(meshWasProcedural);
        Assert.True(mesh!.IsDisposed);
        // One procedural mesh plus the twelve engine assets the app preloads before Setup.
        Assert.Equal(13, worker.UploadCallCount);
        Assert.Equal(13, worker.DestroyCalls.Count);
    }
}
