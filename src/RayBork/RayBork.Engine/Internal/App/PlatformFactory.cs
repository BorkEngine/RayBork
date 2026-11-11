using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// Creates the platform for one <see cref="RayBorkApp.Run"/> call.
/// </summary>
/// <remarks>
/// The production factory creates an <see cref="SdlPlatform"/>. Tests substitute a fake platform
/// through the internal <see cref="RayBorkAppBuilder"/> overload, so the public builder never
/// initializes SDL.
/// </remarks>
/// <param name="loggerFactory">The app logger factory, never <see langword="null"/>.</param>
/// <returns>The uninitialized platform. The app initializes and disposes it.</returns>
internal delegate IEnginePlatform PlatformFactory(ILoggerFactory loggerFactory);
