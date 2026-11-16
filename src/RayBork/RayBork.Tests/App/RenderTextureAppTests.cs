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
    public void CreateRenderTextureAsyncHonorsCancellationBeforeCompletion() {
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
}
