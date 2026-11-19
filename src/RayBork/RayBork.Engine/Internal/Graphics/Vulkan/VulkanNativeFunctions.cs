using Silk.NET.Core;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// Resolves the instance- and device-extension commands the renderer needs.
/// </summary>
/// <remarks>
/// Commands are resolved through <c>vkGetInstanceProcAddr</c> instead of generated extension
/// wrappers; a missing command throws <see cref="GraphicsException"/>. Render-thread only.
/// </remarks>
internal static unsafe class VulkanNativeFunctions
{
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

    /// <summary>Gets whether a queue family can present to the surface.</summary>
    internal static bool GetSurfaceSupport(
        Vk vulkan,
        Instance instance,
        PhysicalDevice device,
        uint queueFamilyIndex,
        SurfaceKHR surface
    )
    {
        delegate* unmanaged<PhysicalDevice, uint, SurfaceKHR, Bool32*, Result> function =
            (delegate* unmanaged<PhysicalDevice, uint, SurfaceKHR, Bool32*, Result>)Resolve(
                vulkan,
                instance,
                GET_SURFACE_SUPPORT
            );
        Bool32 supported;
        Result result = function(device, queueFamilyIndex, surface, &supported);
        ThrowIfFailed(result, GET_SURFACE_SUPPORT);
        return supported.Value != 0;
    }

    /// <summary>Gets the surface capabilities.</summary>
    internal static SurfaceCapabilitiesKHR GetSurfaceCapabilities(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface
    )
    {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_CAPABILITIES);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, SurfaceCapabilitiesKHR*, Result> function =
            (delegate* unmanaged<
                PhysicalDevice,
                SurfaceKHR,
                SurfaceCapabilitiesKHR*,
                Result>)Resolve(vulkan, instance, GET_SURFACE_CAPABILITIES);
        SurfaceCapabilitiesKHR capabilities;
        Result result = function(device, surface, &capabilities);
        ThrowIfFailed(result, GET_SURFACE_CAPABILITIES);
        return capabilities;
    }

    /// <summary>Gets the formats the surface supports for a swapchain.</summary>
    internal static SurfaceFormatKHR[] GetSurfaceFormats(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface
    )
    {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_FORMATS);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, SurfaceFormatKHR*, Result> function =
            (delegate* unmanaged<
                PhysicalDevice,
                SurfaceKHR,
                uint*,
                SurfaceFormatKHR*,
                Result>)Resolve(vulkan, instance, GET_SURFACE_FORMATS);
        uint count = 0;
        ThrowIfFailed(function(device, surface, &count, null), GET_SURFACE_FORMATS);
        if (count == 0)
        {
            return [];
        }

        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* formatsPointer = formats)
        {
            ThrowIfFailed(function(device, surface, &count, formatsPointer), GET_SURFACE_FORMATS);
        }

        if (count != formats.Length)
        {
            Array.Resize(ref formats, (int)count);
        }

        return formats;
    }

    /// <summary>Gets the present modes the surface supports.</summary>
    internal static PresentModeKHR[] GetSurfacePresentModes(
        Vk vulkan,
        PhysicalDevice device,
        SurfaceKHR surface
    )
    {
        Instance instance = RequireCurrentInstance(vulkan, GET_SURFACE_PRESENT_MODES);
        delegate* unmanaged<PhysicalDevice, SurfaceKHR, uint*, PresentModeKHR*, Result> function =
            (delegate* unmanaged<
                PhysicalDevice,
                SurfaceKHR,
                uint*,
                PresentModeKHR*,
                Result>)Resolve(vulkan, instance, GET_SURFACE_PRESENT_MODES);
        uint count = 0;
        ThrowIfFailed(function(device, surface, &count, null), GET_SURFACE_PRESENT_MODES);
        if (count == 0)
        {
            return [];
        }

        PresentModeKHR[] presentModes = new PresentModeKHR[count];
        fixed (PresentModeKHR* presentModesPointer = presentModes)
        {
            ThrowIfFailed(
                function(device, surface, &count, presentModesPointer),
                GET_SURFACE_PRESENT_MODES
            );
        }

        if (count != presentModes.Length)
        {
            Array.Resize(ref presentModes, (int)count);
        }

        return presentModes;
    }

    /// <summary>Creates a swapchain for the window surface.</summary>
    /// <exception cref="GraphicsException">The command failed (<c>RB3001</c>).</exception>
    internal static SwapchainKHR CreateSwapchain(
        Vk vulkan,
        Device device,
        SwapchainCreateInfoKHR createInfo
    )
    {
        delegate* unmanaged<
            Device,
            SwapchainCreateInfoKHR*,
            AllocationCallbacks*,
            SwapchainKHR*,
            Result> function = (delegate* unmanaged<
            Device,
            SwapchainCreateInfoKHR*,
            AllocationCallbacks*,
            SwapchainKHR*,
            Result>)ResolveDevice(vulkan, device, CREATE_SWAPCHAIN);
        SwapchainKHR swapchain;
        ThrowIfFailed(function(device, &createInfo, null, &swapchain), CREATE_SWAPCHAIN);
        if (swapchain.Handle == 0)
        {
            throw new GraphicsException(
                "The Vulkan command vkCreateSwapchainKHR returned a zero swapchain handle.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return swapchain;
    }

    /// <summary>Destroys a swapchain created by <see cref="CreateSwapchain"/>.</summary>
    internal static void DestroySwapchain(Vk vulkan, Device device, SwapchainKHR swapchain)
    {
        delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void> function =
            (delegate* unmanaged<Device, SwapchainKHR, AllocationCallbacks*, void>)ResolveDevice(
                vulkan,
                device,
                DESTROY_SWAPCHAIN
            );
        function(device, swapchain, null);
    }

    /// <summary>Gets the images a swapchain owns, in the driver's order.</summary>
    /// <exception cref="GraphicsException">The command failed (<c>RB3001</c>).</exception>
    internal static Image[] GetSwapchainImages(Vk vulkan, Device device, SwapchainKHR swapchain)
    {
        delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result> function =
            (delegate* unmanaged<Device, SwapchainKHR, uint*, Image*, Result>)ResolveDevice(
                vulkan,
                device,
                GET_SWAPCHAIN_IMAGES
            );
        uint count = 0;
        ThrowIfFailed(function(device, swapchain, &count, null), GET_SWAPCHAIN_IMAGES);
        if (count == 0)
        {
            throw new GraphicsException(
                "The Vulkan command vkGetSwapchainImagesKHR returned no swapchain images.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        Image[] images = new Image[count];
        fixed (Image* imagesPointer = images)
        {
            ThrowIfFailed(function(device, swapchain, &count, imagesPointer), GET_SWAPCHAIN_IMAGES);
        }

        if (count != images.Length)
        {
            Array.Resize(ref images, (int)count);
        }

        return images;
    }

    /// <summary>Acquires the next presentation image and signals the semaphore.</summary>
    /// <returns>The raw Vulkan result; out-of-date and surface-lost are status.</returns>
    /// <exception cref="GraphicsException">The command is unavailable (<c>RB3001</c>).</exception>
    internal static Result AcquireNextImage(
        Vk vulkan,
        Device device,
        SwapchainKHR swapchain,
        ulong timeout,
        Semaphore semaphore,
        out uint imageIndex
    )
    {
        delegate* unmanaged<Device, SwapchainKHR, ulong, Semaphore, Fence, uint*, Result> function =
            (delegate* unmanaged<
                Device,
                SwapchainKHR,
                ulong,
                Semaphore,
                Fence,
                uint*,
                Result>)ResolveDevice(vulkan, device, ACQUIRE_NEXT_IMAGE);
        uint index = 0;
        Result result = function(device, swapchain, timeout, semaphore, default, &index);
        imageIndex = index;
        return result;
    }

    /// <summary>Queues a presentation operation for the swapchain.</summary>
    /// <returns>The raw Vulkan result; out-of-date and surface-lost are status.</returns>
    /// <exception cref="GraphicsException">The command is unavailable (<c>RB3001</c>).</exception>
    internal static Result Present(
        Vk vulkan,
        Device device,
        Queue queue,
        PresentInfoKHR presentInfo
    )
    {
        delegate* unmanaged<Queue, PresentInfoKHR*, Result> function = (delegate* unmanaged<
            Queue,
            PresentInfoKHR*,
            Result>)ResolveDevice(vulkan, device, QUEUE_PRESENT);
        return function(queue, &presentInfo);
    }

    /// <summary>Creates the debug-utils messenger for the validation callback.</summary>
    internal static DebugUtilsMessengerEXT CreateDebugMessenger(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerCreateInfoEXT createInfo
    )
    {
        delegate* unmanaged<
            Instance,
            DebugUtilsMessengerCreateInfoEXT*,
            AllocationCallbacks*,
            DebugUtilsMessengerEXT*,
            Result> function = (delegate* unmanaged<
            Instance,
            DebugUtilsMessengerCreateInfoEXT*,
            AllocationCallbacks*,
            DebugUtilsMessengerEXT*,
            Result>)Resolve(vulkan, instance, CREATE_DEBUG_MESSENGER);
        DebugUtilsMessengerEXT messenger;
        ThrowIfFailed(function(instance, &createInfo, null, &messenger), CREATE_DEBUG_MESSENGER);
        return messenger;
    }

    /// <summary>
    /// Destroys a debug-utils messenger created by <see cref="CreateDebugMessenger"/>.
    /// </summary>
    internal static void DestroyDebugMessenger(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerEXT messenger
    )
    {
        delegate* unmanaged<Instance, DebugUtilsMessengerEXT, AllocationCallbacks*, void> function =
            (delegate* unmanaged<
                Instance,
                DebugUtilsMessengerEXT,
                AllocationCallbacks*,
                void>)Resolve(vulkan, instance, DESTROY_DEBUG_MESSENGER);
        function(instance, messenger, null);
    }

    private static IntPtr Resolve(Vk vulkan, Instance instance, string name)
    {
        IntPtr address = vulkan.GetInstanceProcAddr(instance, name);
        if (address == IntPtr.Zero)
        {
            throw new GraphicsException(
                $"The Vulkan loader did not expose the required instance command {name}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return address;
    }

    /// <summary>Resolves a device-level command with an instance-table fallback.</summary>
    /// <remarks>Some loaders expose swapchain commands only through the instance table.</remarks>
    private static IntPtr ResolveDevice(Vk vulkan, Device device, string name)
    {
        IntPtr address = vulkan.GetDeviceProcAddr(device, name);
        if (address == IntPtr.Zero)
        {
            Instance? instance = vulkan.CurrentInstance;
            if (instance.HasValue)
            {
                address = vulkan.GetInstanceProcAddr(instance.Value, name);
            }
        }

        if (address == IntPtr.Zero)
        {
            throw new GraphicsException(
                $"The Vulkan loader did not expose the required device command {name}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return address;
    }

    private static Instance RequireCurrentInstance(Vk vulkan, string operation)
    {
        Instance? instance = vulkan.CurrentInstance;
        if (instance is null)
        {
            throw new GraphicsException(
                $"Cannot resolve {operation} because the Vulkan instance is not current.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return instance.Value;
    }

    private static void ThrowIfFailed(Result result, string operation)
    {
        if (result == Result.Success || result == Result.Incomplete)
        {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }
}
