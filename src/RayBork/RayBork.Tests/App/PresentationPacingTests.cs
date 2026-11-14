namespace RayBork.Tests;

public sealed class PresentationPacingTests {
    private static readonly TimeSpan _absenceCheck = TimeSpan.FromMilliseconds(500);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FifoPresentationPacesTheTickThread() {
        using ManualResetEventSlim presentStarted = new(false);
        using ManualResetEventSlim presentRelease = new(false);
        using ManualResetEventSlim secondTick = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game, targetFps: 0);
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
}
