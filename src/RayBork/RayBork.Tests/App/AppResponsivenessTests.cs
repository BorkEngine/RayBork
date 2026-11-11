namespace RayBork.Tests;

public sealed class AppResponsivenessTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MainThreadKeepsPumpingWhileATickBlocks() {
        using ManualResetEventSlim tickStarted = new(false);
        using ManualResetEventSlim tickRelease = new(false);
        using ManualResetEventSlim pumpedWhileTickBlocked = new(false);

        ScriptedGame game = new();
        using AppTestHost host = new(game);
        host.Platform.PumpHook = platform => {
            if (tickStarted.IsSet) {
                pumpedWhileTickBlocked.Set();
            }
        };
        game.TickHandler = context => {
            tickStarted.Set();
            tickRelease.Wait(AppTestHost.TIMEOUT);
            context.RequestExit();
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
            Assert.True(pumpedWhileTickBlocked.Wait(AppTestHost.TIMEOUT, TestContext.Current.CancellationToken));

            tickRelease.Set();

            Assert.True(runThread.Join(AppTestHost.TIMEOUT));
            Assert.Null(runFailure);
            Assert.Equal(1, game.TickCount);
            Assert.Equal(1, game.ShutdownCount);
        } finally {
            tickRelease.Set();
            runThread.Join(AppTestHost.TIMEOUT);
        }
    }
}
