using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using Silk.NET.Core.Loader;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL host, Vulkan instance, native surface, and Vulkan device for one GPU test.
/// </summary>
/// <remarks>
/// <see cref="TryCreate"/> reports only environmental unavailability as a skip reason: SDL cannot
/// initialize a Vulkan window, the Vulkan loader is missing, or the loader reports no physical
/// device. A programming or capability failure throws so the test fails instead of disappearing.
/// The host joins the serialized <c>SDL</c> collection because SDL's video driver hint and Vulkan
/// initialization are process global.
/// </remarks>
internal sealed class GpuTestHost : IDisposable {
    private const string APPLICATION_NAME = "RayBork GPU Test";

    private readonly SdlTestHost _sdlHost;
    private readonly VulkanInstance _instance;
    private readonly IntPtr _surface;
    private readonly VulkanDevice _device;
    private bool _disposed;

    private GpuTestHost(
        SdlTestHost sdlHost,
        VulkanInstance instance,
        IntPtr surface,
        VulkanDevice device) {
        _sdlHost = sdlHost;
        _instance = instance;
        _surface = surface;
        _device = device;
    }

    /// <summary>
    /// Gets the SDL host that owns the Vulkan-capable window.
    /// </summary>
    internal SdlTestHost SdlHost => _sdlHost;

    /// <summary>
    /// Gets the SDL platform that created the surface.
    /// </summary>
    internal SdlPlatform Platform => _sdlHost.Platform;

    /// <summary>
    /// Gets the Vulkan instance.
    /// </summary>
    internal VulkanInstance Instance => _instance;

    /// <summary>
    /// Gets the native surface handle.
    /// </summary>
    internal IntPtr Surface => _surface;

    /// <summary>
    /// Gets the Vulkan device created for the surface.
    /// </summary>
    internal VulkanDevice Device => _device;

    /// <summary>
    /// Creates the host for a GPU test, skipping the test when the host is environmentally
    /// unavailable.
    /// </summary>
    /// <param name="requestValidation">Whether the instance requests the validation layer.</param>
    /// <returns>The created host.</returns>
    internal static GpuTestHost CreateOrSkip(bool requestValidation = true) {
        if (!TryCreate(out GpuTestHost? host, out string skipReason, requestValidation)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    /// <summary>
    /// Tries to create the host.
    /// </summary>
    /// <param name="host">The created host, or <see langword="null"/> when unavailable.</param>
    /// <param name="skipReason">A specific reason when <paramref name="host"/> is <see langword="null"/>.</param>
    /// <param name="requestValidation">Whether the instance requests the validation layer.</param>
    /// <returns><see langword="true"/> when the host was created.</returns>
    internal static bool TryCreate(
        out GpuTestHost? host,
        out string skipReason,
        bool requestValidation = true) {
        host = null;
        skipReason = string.Empty;
        if (!SdlTestHost.TryCreateVulkan(out SdlTestHost? createdHost, out skipReason)) {
            return false;
        }

        SdlTestHost sdlHost = createdHost!;
        SdlTestHost? cleanupHost = sdlHost;
        VulkanInstance? instance = null;
        IntPtr surface = IntPtr.Zero;
        VulkanDevice? device = null;
        try {
            IReadOnlyList<string> extensions;
            try {
                extensions = sdlHost.Platform.GetVulkanInstanceExtensions();
            } catch (PlatformException exception) {
                skipReason = $"SDL cannot provide the Vulkan instance extensions: {exception.Message}";
                return false;
            }

            try {
                instance = VulkanInstance.Create(new VulkanInstanceOptions {
                    ApplicationName = APPLICATION_NAME,
                    ApplicationVersion = EngineVersion.Current,
                    SurfaceExtensions = extensions,
                    RequestValidation = requestValidation,
                    Logger = NullLogger.Instance,
                });
            } catch (GraphicsException exception) when (IsEnvironmentalFailure(exception)) {
                skipReason = exception.Message;
                return false;
            }

            try {
                surface = sdlHost.Platform.CreateVulkanSurface(instance.Handle.Handle);
            } catch (PlatformException exception) {
                skipReason = $"SDL cannot create the Vulkan surface: {exception.Message}";
                return false;
            }

            try {
                device = VulkanDevice.Create(instance, surface, NullLogger.Instance);
            } catch (GraphicsException exception) when (IsEnvironmentalFailure(exception)) {
                skipReason = exception.Message;
                return false;
            }

            host = new GpuTestHost(sdlHost, instance, surface, device);
            cleanupHost = null;
            instance = null;
            surface = IntPtr.Zero;
            device = null;
            return true;
        } finally {
            device?.Dispose();
            if (instance is not null && surface != IntPtr.Zero) {
                cleanupHost!.Platform.DestroyVulkanSurface(instance.Handle.Handle, surface);
            }

            instance?.Dispose();
            cleanupHost?.Dispose();
        }
    }

    /// <summary>
    /// Gets whether the Vulkan loader can be loaded, without requiring a GPU or a window.
    /// </summary>
    /// <returns><see langword="true"/> when the loader loads.</returns>
    internal static bool IsVulkanLoaderAvailable() {
        try {
            return Vk.GetApi() is not null;
        } catch (Exception exception) when (exception is DllNotFoundException or SymbolLoadingException or TypeInitializationException) {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _device.Dispose();
        _sdlHost.Platform.DestroyVulkanSurface(_instance.Handle.Handle, _surface);
        _instance.Dispose();
        _sdlHost.Dispose();
    }

    private static bool IsEnvironmentalFailure(GraphicsException exception) =>
        exception.InnerException is DllNotFoundException or SymbolLoadingException or TypeInitializationException
        || exception.Message.StartsWith(VulkanInstance.LOADER_FAILURE_MESSAGE_PREFIX, StringComparison.Ordinal)
        || exception.Message.StartsWith(VulkanDevice.NO_PHYSICAL_DEVICE_MESSAGE_PREFIX, StringComparison.Ordinal);
}
