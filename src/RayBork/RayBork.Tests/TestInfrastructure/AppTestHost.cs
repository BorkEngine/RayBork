using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RayBork.Tests;

/// <summary>
/// Builds an app on a <see cref="FakeEnginePlatform"/> with a fast tick loop, so lifecycle tests do
/// not touch SDL. The default logger is silent unless a test asks for the real default factory.
/// </summary>
internal sealed class AppTestHost : IDisposable {
    internal const int FAST_TARGET_FPS = 1000;
    internal static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(10);

    internal AppTestHost(
        ScriptedGame game,
        ILoggerFactory? loggerFactory = null,
        bool useDefaultLoggerFactory = false,
        LogLevel logLevel = LogLevel.Information,
        int targetFps = FAST_TARGET_FPS,
        double maxDeltaTime = 0.25) {
        ArgumentNullException.ThrowIfNull(game);

        Game = game;
        Platform = new FakeEnginePlatform();
        RayBorkAppBuilder builder = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTargetFps(targetFps)
            .WithMaxDeltaTime(maxDeltaTime)
            .WithLogLevel(logLevel);

        if (loggerFactory is not null) {
            builder = builder.WithLoggerFactory(loggerFactory);
        } else if (!useDefaultLoggerFactory) {
            builder = builder.WithLoggerFactory(NullLoggerFactory.Instance);
        }

        FakeEnginePlatform platform = Platform;
        App = builder.Build(_ => platform, TimeProvider.System);
    }

    internal ScriptedGame Game { get; }

    internal FakeEnginePlatform Platform { get; }

    internal RayBorkApp App { get; }

    public void Dispose() => App.Dispose();
}
