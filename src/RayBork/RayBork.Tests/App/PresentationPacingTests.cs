using System.Diagnostics;

namespace RayBork.Tests;

public sealed class PresentationPacingTests {
    private static readonly TimeSpan _absenceCheck = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan _minimumLowTargetTickInterval = TimeSpan.FromMilliseconds(50);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FifoPresentationPacesTheTickThread() {
        using ManualResetEventSlim presentStarted = new(false);
        using ManualResetEventSlim presentRelease = new(false);
        using ManualResetEventSlim secondTick = new(false);

        ScriptedGame game = new();
        // A target above the display refresh: FIFO presentation must still pace the tick.
        using AppTestHost host = new(game, targetFps: 1000);
        host.FakeRenderer.Info = host.FakeRenderer.Info with { PresentMode = PresentMode.Fifo };
        host.FakeRenderer.OnPresent = () => {
            presentStarted.Set();
            presentRelease.Wait(AppTestHost.TIMEOUT);
        };
        game.TickHandler = context => {
            if (game.TickCount == 2) {
                secondTick.Set();
                context.RequestExit();
            }
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
            Assert.True(presentStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            // The first frame is inside Present. A FIFO-paced tick cannot finish another tick until
            // the render loop marks that frame presented.
            Assert.False(secondTick.Wait(_absenceCheck, TestContext.Current.CancellationToken));
            Assert.Equal(1, game.TickCount);

            presentRelease.Set();

            Assert.True(secondTick.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
            Assert.Equal(2, game.TickCount);
        } finally {
            presentRelease.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }

    [Theory]
    [InlineData(PresentMode.Mailbox)]
    [InlineData(PresentMode.Immediate)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DecoupledPresentationDoesNotPaceTheTickThread(PresentMode presentMode) {
        using ManualResetEventSlim presentStarted = new(false);
        using ManualResetEventSlim presentRelease = new(false);
        using ManualResetEventSlim secondTick = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game, targetFps: 0);
        host.FakeRenderer.Info = host.FakeRenderer.Info with { PresentMode = presentMode };
        host.FakeRenderer.OnPresent = () => {
            presentStarted.Set();
            presentRelease.Wait(AppTestHost.TIMEOUT);
        };
        game.TickHandler = context => {
            if (game.TickCount == 2) {
                secondTick.Set();
                context.RequestExit();
            }
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
            Assert.True(presentStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            // The first present is still blocked, but MAILBOX and IMMEDIATE keep the tick decoupled,
            // so the second tick runs and the renderer later presents the newest completed frame.
            Assert.True(secondTick.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
            Assert.Equal(2, game.TickCount);

            presentRelease.Set();

            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
        } finally {
            presentRelease.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StoppingTheAppReleasesAFifoPresentationWait() {
        using ManualResetEventSlim presentStarted = new(false);
        using ManualResetEventSlim presentRelease = new(false);
        using ManualResetEventSlim shutdownEntered = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game, targetFps: 0);
        host.FakeRenderer.Info = host.FakeRenderer.Info with { PresentMode = PresentMode.Fifo };
        host.FakeRenderer.OnPresent = () => {
            presentStarted.Set();
            presentRelease.Wait(AppTestHost.TIMEOUT);
        };
        game.ShutdownHandler = () => {
            shutdownEntered.Set();
            return Task.CompletedTask;
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
            Assert.True(presentStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            Exception? disposeFailure = null;
            Thread disposeThread = new(() => {
                try {
                    host.App.Dispose();
                } catch (Exception exception) {
                    disposeFailure = exception;
                }
            });
            disposeThread.Start();

            // The tick is waiting for a frame that the blocked Present never reports. The stop
            // request cancels the wait, so Shutdown runs without the presentation.
            Assert.True(shutdownEntered.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            presentRelease.Set();

            Assert.True(disposeThread.Join(AppTestHost.TIMEOUT));
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(disposeFailure);
            Assert.Null(runFailure);
            Assert.Equal(1, game.TickCount);
            Assert.Equal(1, game.ShutdownCount);
        } finally {
            presentRelease.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FifoWithALowTargetSleepsAfterPresentation() {
        using ManualResetEventSlim secondTick = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game, targetFps: 10);
        host.FakeRenderer.Info = host.FakeRenderer.Info with { PresentMode = PresentMode.Fifo };

        long firstTickStartedAt = 0;
        TimeSpan tickInterval = TimeSpan.Zero;
        game.TickHandler = context => {
            if (game.TickCount == 1) {
                firstTickStartedAt = Stopwatch.GetTimestamp();
            } else if (game.TickCount == 2) {
                tickInterval = Stopwatch.GetElapsedTime(firstTickStartedAt);
                secondTick.Set();
                context.RequestExit();
            }
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
            Assert.True(secondTick.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
            Assert.True(
                tickInterval >= _minimumLowTargetTickInterval,
                $"A 10 FPS target must sleep after presentation, but the tick interval was {tickInterval.TotalMilliseconds:0.0} ms.");
            Assert.Equal(2, game.TickCount);
        } finally {
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void APresentationModeChangeTakesEffectOnTheNextTick() {
        using ManualResetEventSlim firstPresented = new(false);
        using ManualResetEventSlim secondPresentStarted = new(false);
        using ManualResetEventSlim secondPresentRelease = new(false);
        using ManualResetEventSlim thirdTick = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game, targetFps: 0);
        int presentCount = 0;
        host.FakeRenderer.OnPresent = () => {
            int count = Interlocked.Increment(ref presentCount);
            if (count == 1) {
                firstPresented.Set();
            } else if (count == 2) {
                secondPresentStarted.Set();
                secondPresentRelease.Wait(AppTestHost.TIMEOUT);
            }
        };
        game.TickHandler = context => {
            if (game.TickCount == 2) {
                // The first frame was presented decoupled in MAILBOX. Switching to FIFO here is the
                // recovery/HDR-switch analogue and must pace the frame this tick publishes. The fake
                // backend and the live status both change, as the render loop would publish a
                // recovered Info; the tick reads the live status after it publishes.
                EngineInfo fifo = host.FakeRenderer.Info with { PresentMode = PresentMode.Fifo };
                host.FakeRenderer.Info = fifo;
                host.App.RenderStatus.SetInfo(fifo);
            } else if (game.TickCount == 3) {
                thirdTick.Set();
                context.RequestExit();
            }
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
            Assert.True(firstPresented.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
            Assert.True(secondPresentStarted.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            Assert.False(thirdTick.Wait(_absenceCheck, TestContext.Current.CancellationToken));
            Assert.Equal(2, game.TickCount);

            secondPresentRelease.Set();

            Assert.True(thirdTick.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));
            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
            Assert.Equal(3, game.TickCount);
        } finally {
            secondPresentRelease.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }
}
