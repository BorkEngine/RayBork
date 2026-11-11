using Microsoft.Extensions.Logging;
using ZLogger;

namespace RayBork.Internal;

/// <summary>
/// Creates the engine's default logger factory.
/// </summary>
/// <remarks>
/// The default factory is a ZLogger console factory filtered at the requested level. The app owns it
/// and disposes it after platform shutdown; caller-supplied factories are never disposed here.
/// </remarks>
internal static class LoggingBootstrap {
    /// <summary>
    /// Creates the default ZLogger console factory with a minimum level of <paramref name="level"/>.
    /// </summary>
    /// <param name="level">The minimum level that the factory accepts.</param>
    /// <returns>A new factory that the caller must dispose.</returns>
    internal static ILoggerFactory CreateDefault(LogLevel level) {
        return LoggerFactory.Create(builder => {
            builder.SetMinimumLevel(level);
            builder.AddZLoggerConsole();
        });
    }
}
