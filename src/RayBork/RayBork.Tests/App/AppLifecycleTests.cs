using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class AppLifecycleTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetupTickAndShutdownRunOnTheNamedTickThread() {
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
        Assert.Equal(game.SetupThreadId, game.FirstTickThreadId);
        Assert.Equal(game.SetupThreadId, game.LastTickThreadId);
        Assert.Equal(game.SetupThreadId, game.ShutdownThreadId);
        Assert.Equal("RayBork.Tick", game.SetupThreadName);
        Assert.Equal("RayBork.Tick", game.ShutdownThreadName);
        Assert.Equal("RayBork.Tick", host.App.TickThreadName);
        Assert.Equal("RayBork.Render", host.App.RenderThreadName);
        Assert.NotNull(host.App.TickThread);
        Assert.NotNull(host.App.RenderThread);
        Assert.False(host.App.TickThread!.IsAlive);
        Assert.False(host.App.RenderThread!.IsAlive);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetupAndShutdownContinuationsResumeOnTheTickThread() {
        ScriptedGame game = new() {
            YieldInSetup = true,
            YieldInShutdown = true,
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Equal(game.SetupThreadId, game.SetupCompletionThreadId);
        Assert.Equal(game.ShutdownThreadId, game.ShutdownCompletionThreadId);
        Assert.Equal(game.SetupThreadId, game.ShutdownThreadId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FirstTickHasZeroDeltaOneFrameAndAdvancingTime() {
        ScriptedGame game = new();
        game.TickHandler = context => {
            if (context.FrameCount >= 5) {
                context.RequestExit();
            }
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Equal(0.0, game.FirstTickDeltaTime);
        Assert.True(game.FirstTickIsFirstTick);
        Assert.Equal(1L, game.FirstTickFrameCount);
        Assert.True(game.FirstTickTime > 0.0, "Setup sees elapsed time because the clock starts before platform initialization.");
        Assert.True(game.LastTickTime > game.FirstTickTime);
        Assert.Equal(5L, game.LastTickFrameCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequestExitFromATickCompletesTheTickAndStops() {
        ScriptedGame game = new();
        game.TickHandler = context => context.RequestExit();
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
        Assert.Equal(game.FirstTickThreadId, game.ShutdownThreadId);
        Assert.False(host.App.IsRunning);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowCloseRequestsExit() {
        ScriptedGame game = new();
        game.TickHandler = context => context.Window.Close();
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.True(host.Platform.FakeWindow.CloseRequested);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequestExitBeforeRunStillRunsSetupAndShutdown() {
        ScriptedGame game = new();
        using AppTestHost host = new(game);

        host.App.RequestExit();
        host.App.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RequestExitDuringSetupSkipsTicksButRunsShutdown() {
        ScriptedGame game = new();
        game.SetupHandler = context => {
            context.RequestExit();
            return Task.CompletedTask;
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlatformReceivesConfiguredStartupOptions() {
        FakeEnginePlatform platform = new();
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTitle("RayBork Window")
            .WithSize(640, 360)
            .WithResizable()
            .WithWindowState(WindowState.Hidden)
            .WithLoggerFactory(NullLoggerFactory.Instance)
            .WithTargetFps(AppTestHost.FAST_TARGET_FPS)
            .Build(_ => platform, TimeProvider.System);

        try {
            app.Run();
        } finally {
            app.Dispose();
        }

        Assert.NotNull(platform.Options);
        Assert.Equal("RayBork Window", platform.Options.Title);
        Assert.Equal(640, platform.Options.Width);
        Assert.Equal(360, platform.Options.Height);
        Assert.True(platform.Options.Resizable);
        Assert.Equal(WindowState.Hidden, platform.Options.WindowState);
        Assert.Equal(1, platform.InitializeCount);
        Assert.True(platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetupFailureSkipsShutdownAndBecomesGameException() {
        InvalidOperationException setupException = new("setup failed");
        ScriptedGame game = new() {
            SetupHandler = _ => Task.FromException(setupException),
        };
        using AppTestHost host = new(game);

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        Assert.Equal(DiagnosticCodes.GAME_CALLBACK_FAILED, thrown.DiagnosticCode);
        Assert.Same(setupException, thrown.InnerException);
        Assert.Equal(0, game.TickCount);
        Assert.Equal(0, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickFailureStillRunsShutdown() {
        InvalidOperationException tickException = new("tick failed");
        ScriptedGame game = new();
        game.TickHandler = _ => throw tickException;
        using AppTestHost host = new(game);

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        Assert.Equal(DiagnosticCodes.GAME_CALLBACK_FAILED, thrown.DiagnosticCode);
        Assert.Same(tickException, thrown.InnerException);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickAndShutdownFailuresProduceAnAggregateInnerException() {
        InvalidOperationException tickException = new("tick failed");
        ApplicationException shutdownException = new("shutdown failed");
        ScriptedGame game = new() {
            ShutdownHandler = () => Task.FromException(shutdownException),
        };
        game.TickHandler = _ => throw tickException;
        using AppTestHost host = new(game);

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        Assert.Equal(DiagnosticCodes.GAME_CALLBACK_FAILED, thrown.DiagnosticCode);
        AggregateException aggregate = Assert.IsType<AggregateException>(thrown.InnerException);
        Assert.Collection(
            aggregate.InnerExceptions,
            inner => Assert.Same(tickException, inner),
            inner => Assert.Same(shutdownException, inner));
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ShutdownOnlyFailureBecomesGameException() {
        ApplicationException shutdownException = new("shutdown failed");
        ScriptedGame game = new() {
            ShutdownHandler = () => Task.FromException(shutdownException),
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        Assert.Equal(DiagnosticCodes.GAME_CALLBACK_FAILED, thrown.DiagnosticCode);
        Assert.Same(shutdownException, thrown.InnerException);
        Assert.Equal(1, game.ShutdownCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlatformInitializationFailurePropagatesUnchanged() {
        PlatformException platformException = new("SDL initialization failed");
        using AppTestHost host = new(new ScriptedGame());
        host.Platform.InitializeException = platformException;

        Exception thrown = Assert.ThrowsAny<Exception>(() => host.App.Run());

        Assert.Same(platformException, thrown);
        Assert.Equal(0, host.Game.SetupCount);
        Assert.Equal(0, host.Game.ShutdownCount);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WindowInitializationFailurePropagatesUnchanged() {
        WindowException windowException = new("window creation failed");
        using AppTestHost host = new(new ScriptedGame());
        host.Platform.InitializeException = windowException;

        Exception thrown = Assert.ThrowsAny<Exception>(() => host.App.Run());

        Assert.Same(windowException, thrown);
        Assert.Equal(0, host.Game.SetupCount);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlatformPumpFailureBecomesPlatformExceptionWithOriginalInner() {
        InvalidOperationException pumpException = new("pump failed");
        using AppTestHost host = new(new ScriptedGame());
        host.Platform.PumpHook = _ => throw pumpException;

        PlatformException thrown = Assert.Throws<PlatformException>(() => host.App.Run());

        Assert.Same(pumpException, thrown.InnerException);
        Assert.True(host.Platform.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IsRunningIsTrueDuringCallbacksAndFalseAfterRun() {
        bool runningDuringInitialize = false;
        bool runningDuringSetup = false;
        bool runningDuringTick = false;
        bool runningDuringShutdown = false;

        using AppTestHost host = new(new ScriptedGame());
        ScriptedGame game = host.Game;
        host.Platform.InitializeHook = () => runningDuringInitialize = host.App.IsRunning;
        game.SetupHandler = _ => {
            runningDuringSetup = host.App.IsRunning;
            return Task.CompletedTask;
        };
        game.TickHandler = context => {
            runningDuringTick = host.App.IsRunning;
            context.RequestExit();
        };
        game.ShutdownHandler = () => {
            runningDuringShutdown = host.App.IsRunning;
            return Task.CompletedTask;
        };

        host.App.Run();

        Assert.True(runningDuringInitialize);
        Assert.True(runningDuringSetup);
        Assert.True(runningDuringTick);
        Assert.True(runningDuringShutdown);
        Assert.False(host.App.IsRunning);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IsExitingBecomesTrueAfterRequestExit() {
        bool exitingBeforeRequest = true;
        bool exitingAfterRequest = false;

        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.TickHandler = context => {
            exitingBeforeRequest = context.IsExiting;
            context.RequestExit();
            exitingAfterRequest = context.IsExiting;
        };

        host.App.Run();

        Assert.False(exitingBeforeRequest);
        Assert.True(exitingAfterRequest);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeFromTheTickThreadThrows() {
        bool threw = false;
        using AppTestHost host = new(new ScriptedGame());
        host.Game.TickHandler = context => {
            try {
                host.App.Dispose();
            } catch (InvalidOperationException) {
                threw = true;
            }

            context.RequestExit();
        };

        host.App.Run();

        Assert.True(threw);
        Assert.False(host.App.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeFromAnotherThreadStopsARunningApp() {
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
        } finally {
            releaseTick.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RunTwiceThrows() {
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);

        host.App.Run();

        Assert.Throws<InvalidOperationException>(() => host.App.Run());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RunAfterDisposeThrows() {
        using AppTestHost host = new(new ScriptedGame());

        host.App.Dispose();

        Assert.Throws<ObjectDisposedException>(() => host.App.Run());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeIsIdempotent() {
        using AppTestHost host = new(new ScriptedGame());

        host.App.Dispose();
        host.App.Dispose();

        Assert.True(host.App.IsDisposed);
    }
}
