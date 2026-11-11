using RayBork.Internal;

namespace RayBork.Tests;

public sealed class RendererIntegrationTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BackendInitializesBeforeSetupAndPublishesInfo() {
        using AppTestHost host = new(new ScriptedGame());
        EngineInfo expectedInfo = host.FakeRenderer.Info;
        bool initializedBeforeSetup = false;
        EngineInfo infoDuringSetup = default;

        host.Game.SetupHandler = context => {
            initializedBeforeSetup = host.FakeRenderer.InitializeCallCount == 1;
            infoDuringSetup = context.Info;
            return Task.CompletedTask;
        };
        host.Game.TickHandler = context => context.RequestExit();

        host.App.Run();

        Assert.True(initializedBeforeSetup);
        Assert.Equal(expectedInfo, infoDuringSetup);
        Assert.Equal(1, host.FakeRenderer.InitializeCallCount);
        Assert.NotNull(host.Platform.Options);
        Assert.True(host.Platform.Options.EnableVulkan);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurfaceIsCreatedOnMainThreadBeforeSetupAndDestroyedAfterShutdown() {
        int mainThreadId = Environment.CurrentManagedThreadId;
        using AppTestHost host = new(new ScriptedGame());
        bool surfaceCreatedBeforeSetup = false;
        bool surfaceDestroyedDuringShutdown = true;

        host.Game.SetupHandler = _ => {
            surfaceCreatedBeforeSetup = host.FakeRenderer.SurfaceHandle != IntPtr.Zero;
            return Task.CompletedTask;
        };
        host.Game.TickHandler = context => context.RequestExit();
        host.Game.ShutdownHandler = () => {
            surfaceDestroyedDuringShutdown = host.FakeRenderer.SurfaceDestroyed;
            return Task.CompletedTask;
        };

        host.App.Run();

        Assert.True(surfaceCreatedBeforeSetup);
        Assert.False(surfaceDestroyedDuringShutdown);
        Assert.True(host.FakeRenderer.SurfaceDestroyed);
        Assert.Equal(mainThreadId, host.Platform.CreateSurfaceThreadId);
        Assert.Equal(mainThreadId, host.Platform.DestroySurfaceThreadId);
        Assert.Equal(host.FakeRenderer.SurfaceHandle, Assert.Single(host.Platform.DestroyedSurfaces));
        Assert.Equal(1, host.Platform.CreateSurfaceCallCount);
        Assert.Same(host.Platform.VulkanInstanceExtensions, host.FakeRenderer.Options!.InstanceExtensions);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishedFramesAreSubmittedAndPresentedInOrder() {
        using ManualResetEventSlim firstFrameSubmitted = new(false);
        using ManualResetEventSlim secondFrameSubmitted = new(false);
        RenderStats capturedStats = default;

        using AppTestHost host = new(new ScriptedGame());
        host.FakeRenderer.OnSubmit = _ => {
            int count = host.FakeRenderer.SubmitFrames.Count;
            if (count >= 1) {
                firstFrameSubmitted.Set();
            }

            if (count >= 2) {
                secondFrameSubmitted.Set();
            }
        };
        host.Game.TickHandler = context => {
            if (context.FrameCount == 2) {
                // The first frame was published after the previous tick, so the renderer must
                // consume it; waiting here keeps the assertion independent of thread scheduling.
                Assert.True(firstFrameSubmitted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
                return;
            }

            if (context.FrameCount >= 3) {
                // The second Submit happens after the first Present and Stats publication, so the
                // status snapshot read below is a completed frame even when the renderer runs ahead.
                Assert.True(secondFrameSubmitted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
                capturedStats = context.Stats;
                context.RequestExit();
            }
        };

        host.App.Run();

        long[] frames = [.. host.FakeRenderer.SubmitFrames];
        Assert.NotEmpty(frames);
        for (int i = 1; i < frames.Length; i++) {
            Assert.True(frames[i] > frames[i - 1], $"Submitted frames must be monotonic; frame {frames[i]} followed {frames[i - 1]}.");
        }

        Assert.Equal(frames.Length, host.FakeRenderer.PresentCount);
        Assert.Equal(1, capturedStats.DrawCalls);
        Assert.InRange(capturedStats.FrameIndex, 1L, frames[^1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RenderStatusInfoEqualsBackendInfoAfterRun() {
        using AppTestHost host = new(new ScriptedGame());
        host.Game.TickHandler = context => context.RequestExit();

        host.App.Run();

        Assert.Equal(host.FakeRenderer.Info, host.App.RenderStatus.Info);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InitializeFailureThrowsBeforeSetupAndDisposesBackendAndPlatform() {
        InvalidOperationException initializeException = new("initialize failed");
        using AppTestHost host = new(new ScriptedGame());
        host.FakeRenderer.InitializeException = initializeException;

        GraphicsException thrown = Assert.Throws<GraphicsException>(() => host.App.Run());

        Assert.Same(initializeException, thrown.InnerException);
        Assert.Equal(0, host.Game.SetupCount);
        Assert.Equal(0, host.Game.TickCount);
        Assert.Equal(0, host.Game.ShutdownCount);
        Assert.Equal(1, host.FakeRenderer.InitializeCallCount);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
        Assert.True(host.FakeRenderer.SurfaceDestroyed);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SurfaceCreationFailureIsReportedAsGraphicsException() {
        PlatformException surfaceException = new("surface creation failed");
        using AppTestHost host = new(new ScriptedGame());
        host.Platform.SurfaceCreationException = surfaceException;

        GraphicsException thrown = Assert.Throws<GraphicsException>(() => host.App.Run());

        Assert.Same(surfaceException, thrown.InnerException);
        Assert.Equal(0, host.Game.SetupCount);
        Assert.Equal(0, host.Game.ShutdownCount);
        Assert.Equal(1, host.FakeRenderer.InitializeCallCount);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
        Assert.False(host.FakeRenderer.SurfaceDestroyed);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickFailureRunsShutdownThenDisposesBackend() {
        InvalidOperationException tickException = new("tick failed");
        using AppTestHost host = new(new ScriptedGame());
        int disposeCountDuringShutdown = -1;
        host.Game.TickHandler = _ => throw tickException;
        host.Game.ShutdownHandler = () => {
            disposeCountDuringShutdown = host.FakeRenderer.DisposeCallCount;
            return Task.CompletedTask;
        };

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        Assert.Same(tickException, thrown.InnerException);
        Assert.Equal(1, host.Game.ShutdownCount);
        Assert.Equal(0, disposeCountDuringShutdown);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SubmitFailureStopsTheAppAndRethrows() {
        InvalidOperationException submitException = new("submit failed");
        using AppTestHost host = new(new ScriptedGame());
        host.FakeRenderer.SubmitException = submitException;

        RayBorkException thrown = Assert.Throws<RayBorkException>(() => host.App.Run());

        Assert.Same(submitException, thrown.InnerException);
        Assert.True(host.Game.TickCount >= 1);
        Assert.Equal(1, host.Game.ShutdownCount);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
        Assert.True(host.FakeRenderer.SurfaceDestroyed);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequestExitPublishesTheFinalFrameBeforeShutdown() {
        using AppTestHost host = new(new ScriptedGame());
        host.Game.TickHandler = context => context.RequestExit();

        host.App.Run();

        Assert.Equal(1, host.Game.TickCount);
        Assert.Equal(1, host.Game.ShutdownCount);
        Assert.Equal(new[] { 1L }, host.FakeRenderer.SubmitFrames);
        Assert.Equal(1, host.FakeRenderer.PresentCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MinimizedWindowIsVisibleToTheBackend() {
        using AppTestHost host = new(new ScriptedGame());
        host.Platform.InitializeHook = () => host.Platform.FakeWindow.IsMinimized = true;
        host.Game.TickHandler = context => {
            if (context.FrameCount >= 3) {
                context.RequestExit();
            }
        };

        host.App.Run();

        Assert.True(host.FakeRenderer.LastWindowState.IsMinimized);
        Assert.Equal(3, host.Game.TickCount);
        Assert.Equal(1, host.Game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeFromAnotherThreadWhileRenderingStopsCleanly() {
        using ManualResetEventSlim tickStarted = new(false);
        using ManualResetEventSlim releaseTick = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.TickHandler = _ => {
            tickStarted.Set();
            releaseTick.Wait(AppTestHost.TIMEOUT);
        };

        Exception? runFailure = null;
        Thread runThread = new(() => {
            try {
                host.App.Run();
            } catch (Exception exception) {
                runFailure = exception;
            }
        });

        runThread.Start();
        try {
            Assert.True(tickStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            Exception? disposeFailure = null;
            Thread disposeThread = new(() => {
                try {
                    host.App.Dispose();
                } catch (Exception exception) {
                    disposeFailure = exception;
                }
            });
            disposeThread.Start();

            releaseTick.Set();

            Assert.True(disposeThread.Join(AppTestHost.TIMEOUT));
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
            Assert.Null(disposeFailure);
            Assert.True(host.App.IsDisposed);
            Assert.False(host.App.IsRunning);
            Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
            Assert.True(host.FakeRenderer.SurfaceDestroyed);
            Assert.True(host.Platform.IsDisposed);
        } finally {
            releaseTick.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }
}
