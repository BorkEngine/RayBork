using Microsoft.Extensions.Logging;

namespace RayBork.Tests;

public sealed class AppLoggingTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultFactoryFiltersAtTheConfiguredLevelAndIsDisposed() {
        bool informationEnabled = true;
        bool warningEnabled = false;
        ILoggerFactory? capturedFactory = null;

        ScriptedGame game = new();
        using AppTestHost host = new(game, useDefaultLoggerFactory: true, logLevel: LogLevel.Warning);
        game.SetupHandler = context => {
            capturedFactory = context.LoggerFactory;
            ILogger logger = context.LoggerFactory.CreateLogger("Test");
            informationEnabled = logger.IsEnabled(LogLevel.Information);
            warningEnabled = logger.IsEnabled(LogLevel.Warning);
            return Task.CompletedTask;
        };
        game.TickHandler = context => context.RequestExit();

        host.App.Run();

        Assert.False(informationEnabled);
        Assert.True(warningEnabled);
        Assert.NotNull(capturedFactory);
        Assert.Throws<ObjectDisposedException>(() => capturedFactory!.CreateLogger("AfterRun"));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CallerFactoryIsUsedAndNotDisposed() {
        bool informationEnabled = false;
        TrackingLoggerFactory loggerFactory = new();
        ScriptedGame game = new();
        using AppTestHost host = new(game, loggerFactory: loggerFactory, logLevel: LogLevel.Error);
        game.SetupHandler = context => {
            Assert.Same(loggerFactory, context.LoggerFactory);
            informationEnabled = context.LoggerFactory.CreateLogger("Game").IsEnabled(LogLevel.Information);
            return Task.CompletedTask;
        };
        game.TickHandler = context => context.RequestExit();

        host.App.Run();

        Assert.True(informationEnabled);
        Assert.Contains("RayBork.App", loggerFactory.Categories);
        Assert.Contains(
            loggerFactory.Entries,
            entry => entry.Category == "RayBork.App"
                && entry.Level == LogLevel.Information
                && entry.Message.Contains("starting", StringComparison.Ordinal));
        Assert.Equal(0, loggerFactory.DisposeCount);
        Assert.False(loggerFactory.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineLogsStartupAndExitThroughTheAppCategory() {
        TrackingLoggerFactory loggerFactory = new();
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game, loggerFactory: loggerFactory);

        host.App.Run();

        Assert.Contains(
            loggerFactory.Entries,
            entry => entry.Category == "RayBork.App"
                && entry.Level == LogLevel.Information
                && entry.Message.Contains("RayBork", StringComparison.Ordinal)
                && entry.Message.Contains("starting", StringComparison.Ordinal));
        Assert.Contains(
            loggerFactory.Entries,
            entry => entry.Category == "RayBork.App"
                && entry.Level == LogLevel.Information
                && entry.Message.Contains("exiting", StringComparison.Ordinal));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CallbackFailureIsLoggedBeforeItIsRethrown() {
        InvalidOperationException setupException = new("setup failed");
        TrackingLoggerFactory loggerFactory = new();
        ScriptedGame game = new() {
            SetupHandler = _ => Task.FromException(setupException),
        };
        using AppTestHost host = new(game, loggerFactory: loggerFactory);

        GameException thrown = Assert.Throws<GameException>(() => host.App.Run());

        List<TrackedLogEntry> appErrors = [
            .. loggerFactory.Entries.Where(
                entry => entry.Level == LogLevel.Error && entry.Category == "RayBork.App"),
        ];
        Assert.NotEmpty(appErrors);
        Assert.Same(thrown, appErrors[^1].Exception);
        Assert.Equal(0, loggerFactory.DisposeCount);
    }
}
