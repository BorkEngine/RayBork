using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// An <see cref="IRendererSurfaceProvider"/> that calls the platform directly on the caller's
/// thread.
/// </summary>
/// <remarks>
/// GPU tests use this provider on the test thread. App tests use it to stand in for the real
/// render-thread-to-main-thread marshalling without depending on the app's message pump.
/// </remarks>
internal sealed class DirectSurfaceProvider : IRendererSurfaceProvider {
    private readonly IEnginePlatform _platform;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectSurfaceProvider"/> class.
    /// </summary>
    /// <param name="platform">The initialized platform that owns the window.</param>
    internal DirectSurfaceProvider(IEnginePlatform platform) {
        ArgumentNullException.ThrowIfNull(platform);
        _platform = platform;
    }

    /// <inheritdoc />
    public IntPtr CreateSurface(IntPtr instance) {
        try {
            IntPtr surface = _platform.CreateVulkanSurface(instance);
            if (surface == IntPtr.Zero) {
                throw new GraphicsException(
                    "The platform returned a zero Vulkan surface handle.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            return surface;
        } catch (Exception exception) when (exception is PlatformException or InvalidOperationException or ObjectDisposedException) {
            throw new GraphicsException(
                $"Direct Vulkan surface creation failed: {exception.Message}",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                exception);
        }
    }

    /// <inheritdoc />
    public void DestroySurface(IntPtr instance, IntPtr surface) {
        if (surface == IntPtr.Zero) {
            return;
        }

        try {
            _platform.DestroyVulkanSurface(instance, surface);
        } catch (Exception exception) when (exception is PlatformException or InvalidOperationException or ObjectDisposedException) {
            throw new GraphicsException(
                $"Direct Vulkan surface destruction failed: {exception.Message}",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                exception);
        }
    }
}
