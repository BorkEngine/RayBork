using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Contexts;

namespace RayBork.Internal;

/// <summary>
/// Loads the platform Vulkan loader and resolves its exported commands for Silk.NET.
/// </summary>
/// <remarks>
/// <para>
/// The engine constructs <see cref="Silk.NET.Vulkan.Vk"/> from this context instead of calling
/// <c>Vk.GetApi()</c>. Silk.NET's default context locates native assets through
/// <c>System.Reflection.Assembly.Location</c> and <c>DependencyContext</c>, which are unavailable in
/// a Native AOT single-file app and produce IL3000/IL3002 warnings. Loading the loader by name and
/// resolving exports through <see cref="NativeLibrary"/> keeps the publish graph AOT-safe.
/// </para>
/// <para>
/// The render thread creates the loader with the instance that owns it and disposes it after the
/// Silk.NET API created from it. The loader is not thread-safe: Silk.NET resolves entry points
/// while it is alive, and <see cref="Dispose"/> must not race those calls.
/// </para>
/// </remarks>
internal sealed class VulkanLoader : INativeContext, IDisposable {
    /// <summary>
    /// The prefix of the message thrown when the platform Vulkan loader cannot be loaded.
    /// </summary>
    internal const string LOADER_FAILURE_MESSAGE_PREFIX = "Failed to load";

    /// <summary>
    /// The Windows Vulkan loader library name.
    /// </summary>
    internal const string WINDOWS_LIBRARY_NAME = "vulkan-1.dll";

    /// <summary>
    /// The Linux Vulkan loader library name.
    /// </summary>
    internal const string LINUX_LIBRARY_NAME = "libvulkan.so.1";

    private IntPtr _handle;
    private bool _disposed;

    private VulkanLoader(IntPtr handle) {
        _handle = handle;
    }

    /// <summary>
    /// Loads the platform Vulkan loader.
    /// </summary>
    /// <param name="logger">The logger that records the loaded library.</param>
    /// <returns>The loaded loader.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="GraphicsException">
    /// The platform loader library is not installed. The message names the library and the platform;
    /// the diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanLoader Load(ILogger logger) {
        ArgumentNullException.ThrowIfNull(logger);
        string libraryName = OperatingSystem.IsWindows() ? WINDOWS_LIBRARY_NAME : LINUX_LIBRARY_NAME;
        if (!NativeLibrary.TryLoad(libraryName, out IntPtr handle)) {
            throw new GraphicsException(
                $"{LOADER_FAILURE_MESSAGE_PREFIX} the Vulkan loader {libraryName} on {RuntimeInformation.OSDescription}. Install a Vulkan 1.2 driver.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        logger.LogInformation(
            "Vulkan loader {LibraryName} loaded on {Platform}.",
            libraryName,
            RuntimeInformation.OSDescription);
        return new VulkanLoader(handle);
    }

    /// <summary>
    /// Resolves a command exported by the Vulkan loader.
    /// </summary>
    /// <param name="proc">The command name, such as <c>vkGetInstanceProcAddr</c>.</param>
    /// <param name="slot">
    /// The Silk.NET resolver slot. It is ignored because the slot only indexes Silk.NET's per-context
    /// entry-point cache; every command comes from the single loader library by export name.
    /// </param>
    /// <returns>
    /// The command address, or <see cref="IntPtr.Zero"/> when the loader does not export the command.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The loader has been disposed.</exception>
    public IntPtr GetProcAddress(string proc, int? slot) =>
        TryGetProcAddress(proc, out IntPtr address, slot) ? address : IntPtr.Zero;

    /// <summary>
    /// Tries to resolve a command exported by the Vulkan loader.
    /// </summary>
    /// <param name="proc">The command name, such as <c>vkGetInstanceProcAddr</c>.</param>
    /// <param name="address">The command address, or <see cref="IntPtr.Zero"/> when it is absent.</param>
    /// <param name="slot">
    /// The Silk.NET resolver slot. It is ignored because the slot only indexes Silk.NET's per-context
    /// entry-point cache; every command comes from the single loader library by export name.
    /// </param>
    /// <returns><see langword="true"/> when the loader exports the command.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="proc"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="proc"/> is empty.</exception>
    /// <exception cref="ObjectDisposedException">The loader has been disposed.</exception>
    public bool TryGetProcAddress(string proc, out IntPtr address, int? slot) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(proc);
        return NativeLibrary.TryGetExport(_handle, proc, out address);
    }

    /// <summary>
    /// Frees the native loader handle. The call is idempotent.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread after the <see cref="Silk.NET.Vulkan.Vk"/> object created from this
    /// context has been disposed. Disposing twice is safe and frees the handle exactly once.
    /// </remarks>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero) {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
