using Silk.NET.Core;
using Silk.NET.Vulkan;

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
