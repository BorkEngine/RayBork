using Silk.NET.Core;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// Resolves the instance-extension commands that the M2 renderer needs.
/// </summary>
/// <remarks>
/// <para>
/// The project references <c>Silk.NET.Vulkan</c> without the optional
/// <c>Silk.NET.Vulkan.Extensions.*</c> packages, so the extension entry points are resolved through
/// <c>vkGetInstanceProcAddr</c> instead of generated wrappers. Each command is resolved on demand;
/// instance creation and swapchain support querying happen only at startup and on swapchain
/// recreation, never per frame.
/// </para>
/// <para>
/// Every method runs on the render thread. A missing command is a fatal initialization failure
/// reported as <see cref="GraphicsException"/>.
/// </para>
/// </remarks>
internal static unsafe class VulkanNativeFunctions {
    private const string GET_SURFACE_SUPPORT = "vkGetPhysicalDeviceSurfaceSupportKHR";
    private const string GET_SURFACE_CAPABILITIES = "vkGetPhysicalDeviceSurfaceCapabilitiesKHR";
    private const string GET_SURFACE_FORMATS = "vkGetPhysicalDeviceSurfaceFormatsKHR";
    private const string GET_SURFACE_PRESENT_MODES = "vkGetPhysicalDeviceSurfacePresentModesKHR";
    private const string CREATE_SWAPCHAIN = "vkCreateSwapchainKHR";
    private const string DESTROY_SWAPCHAIN = "vkDestroySwapchainKHR";
    private const string GET_SWAPCHAIN_IMAGES = "vkGetSwapchainImagesKHR";
    private const string ACQUIRE_NEXT_IMAGE = "vkAcquireNextImageKHR";
    private const string QUEUE_PRESENT = "vkQueuePresentKHR";
    private const string CREATE_DEBUG_MESSENGER = "vkCreateDebugUtilsMessengerEXT";
    private const string DESTROY_DEBUG_MESSENGER = "vkDestroyDebugUtilsMessengerEXT";

    /// <summary>
    /// Gets whether a queue family can present to the surface.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="instance">The instance that owns the surface.</param>
    /// <param name="device">The physical device.</param>
    /// <param name="queueFamilyIndex">The queue-family index to test.</param>
    /// <param name="surface">The target surface.</param>
    /// <returns><see langword="true"/> when the family supports presentation.</returns>
    internal static bool GetSurfaceSupport(
        Vk vulkan,
        Instance instance,
        PhysicalDevice device,
        uint queueFamilyIndex,
        SurfaceKHR surface) {
        delegate* unmanaged<PhysicalDevice, uint, SurfaceKHR, Bool32*, Result> function =
            (delegate* unmanaged<PhysicalDevice, uint, SurfaceKHR, Bool32*, Result>)Resolve(
                vulkan,
                instance,
                GET_SURFACE_SUPPORT);
        Bool32 supported;
        Result result = function(device, queueFamilyIndex, surface, &supported);
        ThrowIfFailed(result, GET_SURFACE_SUPPORT);
        return supported.Value != 0;
    }

    /// <summary>
    /// Gets the surface capabilities.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The physical device.</param>
    /// <param name="surface">The target surface.</param>
    /// <returns>The surface capabilities.</returns>
    internal static SurfaceCapabilitiesKHR GetSurfaceCapabilities(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface) {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_CAPABILITIES);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, SurfaceCapabilitiesKHR*, Result> function =
            (delegate* unmanaged<PhysicalDevice, SurfaceKHR, SurfaceCapabilitiesKHR*, Result>)Resolve(
                vulkan,
                instance,
                GET_SURFACE_CAPABILITIES);
        SurfaceCapabilitiesKHR capabilities;
        Result result = function(device, surface, &capabilities);
        ThrowIfFailed(result, GET_SURFACE_CAPABILITIES);
        return capabilities;
    }

    /// <summary>
    /// Gets the surface formats.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The physical device.</param>
    /// <param name="surface">The target surface.</param>
    /// <returns>The formats the surface supports for a swapchain.</returns>
    internal static SurfaceFormatKHR[] GetSurfaceFormats(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface) {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_FORMATS);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, SurfaceFormatKHR*, Result> function =
            (delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, SurfaceFormatKHR*, Result>)Resolve(
                vulkan,
                instance,
                GET_SURFACE_FORMATS);
        uint count = 0;
        ThrowIfFailed(function(device, surface, &count, null), GET_SURFACE_FORMATS);
        if (count == 0) {
            return [];
        }

        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* formatsPointer = formats) {
            ThrowIfFailed(function(device, surface, &count, formatsPointer), GET_SURFACE_FORMATS);
        }

        if (count != formats.Length) {
            Array.Resize(ref formats, (int)count);
        }

        return formats;
    }

    /// <summary>
    /// Gets the surface present modes.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The physical device.</param>
    /// <param name="surface">The target surface.</param>
    /// <returns>The present modes the surface supports.</returns>
    internal static PresentModeKHR[] GetSurfacePresentModes(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface) {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_PRESENT_MODES);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, PresentModeKHR*, Result> function =
            (delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, PresentModeKHR*, Result>)Resolve(
                vulkan,
                instance,
                GET_SURFACE_PRESENT_MODES);
        uint count = 0;
        ThrowIfFailed(function(device, surface, &count, null), GET_SURFACE_PRESENT_MODES);
        if (count == 0) {
            return [];
        }

        PresentModeKHR[] presentModes = new PresentModeKHR[count];
        fixed (PresentModeKHR* presentModesPointer = presentModes) {
            ThrowIfFailed(function(device, surface, &count, presentModesPointer), GET_SURFACE_PRESENT_MODES);
        }

        if (count != presentModes.Length) {
            Array.Resize(ref presentModes, (int)count);
        }

        return presentModes;
    }

    /// <summary>
    /// Creates a swapchain for the window surface.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread. The caller passes an old swapchain handle in
    /// <paramref name="createInfo"/> when it replaces an existing swapchain.
    /// </remarks>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The logical device that owns the swapchain.</param>
    /// <param name="createInfo">The swapchain configuration.</param>
    /// <returns>The created swapchain.</returns>
    /// <exception cref="GraphicsException">
    /// The command is unavailable, creation failed, or the driver returned a zero handle. The
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static SwapchainKHR CreateSwapchain(
        Vk vulkan,
        Device device,
        SwapchainCreateInfoKHR createInfo) {
        delegate* unmanaged<Device, SwapchainCreateInfoKHR*, AllocationCallbacks*, SwapchainKHR*, Result> function =
            (delegate* unmanaged<Device, SwapchainCreateInfoKHR*, AllocationCallbacks*, SwapchainKHR*, Result>)ResolveDevice(
                vulkan,
                device,
                CREATE_SWAPCHAIN);
        SwapchainKHR swapchain;
        ThrowIfFailed(function(device, &createInfo, null, &swapchain), CREATE_SWAPCHAIN);
        if (swapchain.Handle == 0) {
            throw new GraphicsException(
                "The Vulkan command vkCreateSwapchainKHR returned a zero swapchain handle.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return swapchain;
    }

    /// <summary>
    /// Destroys a swapchain created by <see cref="CreateSwapchain"/>.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The logical device that owns the swapchain.</param>
    /// <param name="swapchain">The swapchain to destroy.</param>
    internal static void DestroySwapchain(Vk vulkan, Device device, SwapchainKHR swapchain) {
        delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void> function =
            (delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void>)ResolveDevice(
                vulkan,
                device,
                DESTROY_SWAPCHAIN);
        function(device, swapchain, null);
    }

    /// <summary>
    /// Gets the images a swapchain owns.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The logical device that owns the swapchain.</param>
    /// <param name="swapchain">The swapchain to query.</param>
    /// <returns>The swapchain images, in the driver's order.</returns>
    /// <exception cref="GraphicsException">
    /// The command is unavailable, the query failed, or the swapchain owns no image. The diagnostic
    /// code is <c>RB3001</c>.
    /// </exception>
    internal static Image[] GetSwapchainImages(Vk vulkan, Device device, SwapchainKHR swapchain) {
        delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result> function =
            (delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result>)ResolveDevice(
                vulkan,
                device,
                GET_SWAPCHAIN_IMAGES);
        uint count = 0;
        ThrowIfFailed(function(device, swapchain, &count, null), GET_SWAPCHAIN_IMAGES);
        if (count == 0) {
            throw new GraphicsException(
                "The Vulkan command vkGetSwapchainImagesKHR returned no swapchain images.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        Image[] images = new Image[count];
        fixed (Image* imagesPointer = images) {
            ThrowIfFailed(function(device, swapchain, &count, imagesPointer), GET_SWAPCHAIN_IMAGES);
        }

        if (count != images.Length) {
            Array.Resize(ref images, (int)count);
        }

        return images;
    }

    /// <summary>
    /// Acquires the next presentation image and signals <paramref name="semaphore"/> when it is
    /// available.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The logical device that owns the swapchain.</param>
    /// <param name="swapchain">The swapchain to acquire from.</param>
    /// <param name="timeout">The timeout in nanoseconds, or <see cref="ulong.MaxValue"/> to wait forever.</param>
    /// <param name="semaphore">The semaphore the command signals when the image is available.</param>
    /// <param name="imageIndex">The acquired image index.</param>
    /// <returns>
    /// The raw Vulkan result. <see cref="Result.Success"/>, <see cref="Result.SuboptimalKhr"/>, and
    /// the out-of-date and surface-lost errors are status, not exceptions.
    /// </returns>
    /// <exception cref="GraphicsException">
    /// The command is unavailable. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static Result AcquireNextImage(
        Vk vulkan,
        Device device,
        SwapchainKHR swapchain,
        ulong timeout,
        Semaphore semaphore,
        out uint imageIndex) {
        delegate* unmanaged<Device, SwapchainKHR, ulong, Semaphore, Fence, uint*, Result> function =
            (delegate* unmanaged<Device, SwapchainKHR, ulong, Semaphore, Fence, uint*, Result>)ResolveDevice(
                vulkan,
                device,
                ACQUIRE_NEXT_IMAGE);
        uint index = 0;
        Result result = function(device, swapchain, timeout, semaphore, default, &index);
        imageIndex = index;
        return result;
    }

    /// <summary>
    /// Queues a presentation operation for the swapchain.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="device">The logical device that owns the queue.</param>
    /// <param name="queue">The queue that presents the image.</param>
    /// <param name="presentInfo">The image, wait semaphore, and target swapchain.</param>
    /// <returns>
    /// The raw Vulkan result. <see cref="Result.Success"/>, <see cref="Result.SuboptimalKhr"/>, and
    /// the out-of-date and surface-lost errors are status, not exceptions.
    /// </returns>
    /// <exception cref="GraphicsException">
    /// The command is unavailable. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static Result Present(Vk vulkan, Device device, Queue queue, PresentInfoKHR presentInfo) {
        delegate* unmanaged<Queue, PresentInfoKHR*, Result> function =
            (delegate* unmanaged<Queue, PresentInfoKHR*, Result>)ResolveDevice(
                vulkan,
                device,
                QUEUE_PRESENT);
        return function(queue, &presentInfo);
    }

    /// <summary>
    /// Creates the debug-utils messenger for the validation callback.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="instance">The instance that owns the messenger.</param>
    /// <param name="createInfo">The messenger callback and filter configuration.</param>
    /// <returns>The created messenger.</returns>
    internal static DebugUtilsMessengerEXT CreateDebugMessenger(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerCreateInfoEXT createInfo) {
        delegate* unmanaged<Instance, DebugUtilsMessengerCreateInfoEXT*, AllocationCallbacks*, DebugUtilsMessengerEXT*, Result> function =
            (delegate* unmanaged<Instance, DebugUtilsMessengerCreateInfoEXT*, AllocationCallbacks*, DebugUtilsMessengerEXT*, Result>)Resolve(
                vulkan,
                instance,
                CREATE_DEBUG_MESSENGER);
        DebugUtilsMessengerEXT messenger;
        ThrowIfFailed(function(instance, &createInfo, null, &messenger), CREATE_DEBUG_MESSENGER);
        return messenger;
    }

    /// <summary>
    /// Destroys a debug-utils messenger created by <see cref="CreateDebugMessenger"/>.
    /// </summary>
    /// <param name="vulkan">The loaded Vulkan API.</param>
    /// <param name="instance">The instance that owns the messenger.</param>
    /// <param name="messenger">The messenger to destroy.</param>
    internal static void DestroyDebugMessenger(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerEXT messenger) {
        delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void> function =
            (delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void>)Resolve(
                vulkan,
                instance,
                DESTROY_DEBUG_MESSENGER);
        function(instance, messenger, null);
    }

    private static IntPtr Resolve(Vk vulkan, Instance instance, string name) {
        IntPtr address = vulkan.GetInstanceProcAddr(instance, name);
        if (address == IntPtr.Zero) {
            throw new GraphicsException(
                $"The Vulkan loader did not expose the required instance command {name}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return address;
    }

    /// <summary>
    /// Resolves a device-level command through <c>vkGetDeviceProcAddr</c>.
    /// </summary>
    /// <remarks>
    /// The command is resolved on demand; swapchain commands run at startup, on recreation, and once
    /// per acquired or presented image, never in a draw loop. Some loaders expose the swapchain
    /// commands only through the instance dispatch table, so the instance table is the fallback.
    /// </remarks>
    private static IntPtr ResolveDevice(Vk vulkan, Device device, string name) {
        IntPtr address = vulkan.GetDeviceProcAddr(device, name);
        if (address == IntPtr.Zero) {
            Instance? instance = vulkan.CurrentInstance;
            if (instance.HasValue) {
                address = vulkan.GetInstanceProcAddr(instance.Value, name);
            }
        }

        if (address == IntPtr.Zero) {
            throw new GraphicsException(
                $"The Vulkan loader did not expose the required device command {name}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return address;
    }

    private static Instance RequireCurrentInstance(Vk vulkan, string operation) {
        Instance? instance = vulkan.CurrentInstance;
        if (instance is null) {
            throw new GraphicsException(
                $"Cannot resolve {operation} because the Vulkan instance is not current.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return instance.Value;
    }

    private static void ThrowIfFailed(Result result, string operation) {
        if (result == Result.Success || result == Result.Incomplete) {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }
}
