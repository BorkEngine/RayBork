namespace RayBork.Tests;

/// <summary>
/// App-level tests for creating render textures from <see cref="IGame.Setup"/> and
/// <see cref="IGame.Tick"/>, and for the clean shutdown failure.
/// </summary>
public sealed class RenderTextureAppTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CreateRenderTextureAsyncIsLegalInSetupAndTickAndFailsAfterShutdown() {
        RenderTexture? setupTarget = null;
        RenderTexture? tickTarget = null;
        EngineContext? context = null;
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.SetupHandler = async setupContext => {
            context = setupContext;
            setupTarget = await setupContext.CreateRenderTextureAsync(
                new RenderTextureDescription(32, 16, TextureFormat.Rgba16Float));
            Assert.False(setupTarget.IsDisposed);
            Assert.Equal(32, setupTarget.Width);
            Assert.Equal(16, setupTarget.Height);
        };
        game.TickHandler = tickContext => {
            tickTarget = tickContext.CreateRenderTextureAsync(
                new RenderTextureDescription(8, 8, TextureFormat.Rgba8Unorm, Depth: false, SampleCount: 1))
                .GetAwaiter()
                .GetResult();
            Assert.Equal(1, tickTarget.SampleCount);
            Assert.False(tickTarget.IsHdr);
            tickContext.RequestExit();
        };

        host.App.Run();

        Assert.NotNull(setupTarget);
        Assert.NotNull(tickTarget);
        Assert.NotNull(context);
        Assert.Equal(1, game.SetupCount);
        setupTarget.Dispose();
        tickTarget.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => context.CreateRenderTextureAsync(
                new RenderTextureDescription(8, 8),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateRenderTextureAsyncRejectsAPreCanceledToken() {
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.SetupHandler = async setupContext => {
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            // The test cancels its own token to prove the API honors cancellation before completion; the
            // xUnit test token would cancel the test instead.
#pragma warning disable xUnit1051
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => setupContext.CreateRenderTextureAsync(
                    new RenderTextureDescription(16, 16),
                    cancellation.Token));
#pragma warning restore xUnit1051
        };
        game.TickHandler = tickContext => tickContext.RequestExit();

        host.App.Run();

        Assert.Equal(1, game.SetupCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResizeRecreatesTheTargetAndUpdatesBothViewsInPlace() {
        RenderTexture? target = null;
        Texture? colorTexture = null;
        Texture? depthTexture = null;
        int sampleCount = 0;
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.SetupHandler = async setupContext => {
            target = await setupContext.CreateRenderTextureAsync(new RenderTextureDescription(32, 16));
            colorTexture = target.ColorTexture;
            depthTexture = target.DepthTexture;
            sampleCount = target.SampleCount;

            target.Resize(48, 24);

            Assert.Equal(48, target.Width);
            Assert.Equal(24, target.Height);
            Assert.Equal(sampleCount, target.SampleCount);
            Assert.Same(colorTexture, target.ColorTexture);
            Assert.Same(depthTexture, target.DepthTexture);
            Assert.Equal(48, colorTexture.Width);
            Assert.Equal(24, colorTexture.Height);
            Assert.NotNull(depthTexture);
            Assert.Equal(48, depthTexture.Width);
            Assert.Equal(24, depthTexture.Height);
            Assert.Equal(TextureFormat.Rgba16Float, colorTexture.Format);
            Assert.Equal(TextureFormat.Depth32Float, depthTexture.Format);
            Assert.Equal(string.Empty, colorTexture.Path);

            // An unchanged size is a no-op: it does not queue another request or throw.
            target.Resize(48, 24);
            Assert.Equal(48, target.Width);
        };
        game.TickHandler = tickContext => tickContext.RequestExit();

        host.App.Run();

        Assert.NotNull(target);
        // The target itself is still alive; the app's render texture service stopped at shutdown, so the
        // stopped-service path reports ObjectDisposedException.
        Assert.False(target.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => target.Resize(64, 32));
        target.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResizeDuringACameraTargetingTheTargetThrowsInvalidOperationException() {
        RenderTexture? target = null;
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.SetupHandler = async setupContext => {
            target = await setupContext.CreateRenderTextureAsync(new RenderTextureDescription(16, 16));
        };
        game.TickHandler = tickContext => {
            using (Camera camera = tickContext.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                0f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null,
                target)) {
                Assert.Same(target, camera.Target);
                Assert.Throws<InvalidOperationException>(() => target!.Resize(32, 32));
                Assert.Equal(16, target!.Width);
            }

            tickContext.RequestExit();
        };

        host.App.Run();

        Assert.NotNull(target);
        target.Dispose();
    }
}
