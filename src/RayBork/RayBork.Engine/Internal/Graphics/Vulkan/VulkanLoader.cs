using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Contexts;

namespace RayBork.Internal;

/// <summary>Loads the platform Vulkan loader and resolves its commands for Silk.NET.</summary>
/// <remarks>
/// Loading by name and resolving exports through <see cref="NativeLibrary"/> keeps the AOT
/// publish graph safe, unlike Silk.NET's default context. The loader is not thread-safe and
/// must outlive the <see cref="Silk.NET.Vulkan.Vk"/> created from it.
/// </remarks>
internal sealed class VulkanLoader : INativeContext, IDisposable
{
    /// <summary>The prefix of the message thrown when the loader cannot be loaded.</summary>
    internal const string LOADER_FAILURE_MESSAGE_PREFIX = "Failed to load";

    /// <summary>The Windows loader library name.</summary>
    internal const string WINDOWS_LIBRARY_NAME = "vulkan-1.dll";

    /// <summary>The Linux loader library name.</summary>
    internal const string LINUX_LIBRARY_NAME = "libvulkan.so.1";

    private IntPtr _handle;
    private bool _disposed;

    private VulkanLoader(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>Loads the platform Vulkan loader.</summary>
    /// <exception cref="GraphicsException">
    /// The platform loader is not installed; the message names the library and the code is
    /// <c>RB3001</c>.
    /// </exception>
    internal static VulkanLoader Load(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        string libraryName = OperatingSystem.IsWindows()
            ? WINDOWS_LIBRARY_NAME
            : LINUX_LIBRARY_NAME;
        if (!NativeLibrary.TryLoad(libraryName, out IntPtr handle))
        {
            throw new GraphicsException(
                $"{LOADER_FAILURE_MESSAGE_PREFIX} the Vulkan loader {libraryName} on {RuntimeInformation.OSDescription}. Install a Vulkan 1.2 driver.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        logger.LogInformation(
            "Vulkan loader {LibraryName} loaded on {Platform}.",
            libraryName,
            RuntimeInformation.OSDescription
        );
        return new VulkanLoader(handle);
    }

    /// <summary>Resolves a loader export by name; the Silk.NET slot is ignored.</summary>
    /// <returns>The command address, or <see cref="IntPtr.Zero"/> when it is not exported.</returns>
    public IntPtr GetProcAddress(string proc, int? slot) =>
        TryGetProcAddress(proc, out IntPtr address, slot) ? address : IntPtr.Zero;

    /// <summary>Tries to resolve a loader export by name, ignoring the Silk.NET slot.</summary>
    /// <returns><see langword="true"/> when the loader exports the command.</returns>
    /// <exception cref="ObjectDisposedException">The loader has been disposed.</exception>
    public bool TryGetProcAddress(string proc, out IntPtr address, int? slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(proc);
        return NativeLibrary.TryGetExport(_handle, proc, out address);
    }

    /// <summary>Frees the native loader handle. The call is idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeLibrary.Free(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
