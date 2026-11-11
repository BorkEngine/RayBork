using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 logical device, queues, and capability snapshot owned by the render thread.
/// </summary>
/// <remarks>
/// <para>
/// The device is created from a platform-owned surface. Creation enumerates every physical device,
/// selects one with the pure <see cref="VulkanPhysicalDeviceSelector"/>, chooses queue families with
/// <see cref="VulkanQueueFamilySelection"/>, requires a usable swapchain surface, and creates one
/// logical device with <c>VK_KHR_swapchain</c> enabled.
/// </para>
/// <para>
/// M2 enables no optional device features beyond Vulkan 1.2 core. Descriptor indexing is probed for
/// the later bindless texture table but is not required.
/// </para>
/// <para>
/// Every member is render-thread only. Dispose the device before the surface and the instance.
/// </para>
/// </remarks>
internal sealed class VulkanDevice : IDisposable {
    /// <summary>
    /// The prefix of the message thrown when the loader reports no physical device at all.
    /// </summary>
    internal const string NO_PHYSICAL_DEVICE_MESSAGE_PREFIX = "No Vulkan physical device was found";

    private const string SWAPCHAIN_EXTENSION_NAME = "VK_KHR_swapchain";
    private const float QUEUE_PRIORITY = 1.0f;

    private readonly VulkanInstance _instance;
    private readonly ILogger _logger;

    private Vk? _vulkan;
    private Device _device;
    private bool _disposed;

    private VulkanDevice(
        VulkanInstance instance,
        Vk vulkan,
        PhysicalDevice physicalDevice,
        Device device,
        Queue graphicsQueue,
        Queue presentQueue,
        VulkanDeviceCapabilities capabilities,
        ILogger logger) {
        _instance = instance;
        _vulkan = vulkan;
        PhysicalDevice = physicalDevice;
        _device = device;
        GraphicsQueue = graphicsQueue;
        PresentQueue = presentQueue;
        Capabilities = capabilities;
        _logger = logger;
    }

    /// <summary>
    /// Gets the loaded Vulkan API.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    internal Vk Vulkan => _vulkan ?? throw new ObjectDisposedException(nameof(VulkanDevice));

    /// <summary>
    /// Gets the selected physical device.
    /// </summary>
    internal PhysicalDevice PhysicalDevice { get; }

    /// <summary>
    /// Gets the native <c>VkDevice</c> handle. It is the default handle after disposal.
    /// </summary>
    internal Device Device => _device;

    /// <summary>
    /// Gets the queue used for graphics work.
    /// </summary>
    internal Queue GraphicsQueue { get; }

    /// <summary>
    /// Gets the queue used for presentation. It is the same handle as <see cref="GraphicsQueue"/> when
    /// one family supports both operations.
    /// </summary>
    internal Queue PresentQueue { get; }

    /// <summary>
    /// Gets the device capability snapshot captured at creation.
    /// </summary>
    internal VulkanDeviceCapabilities Capabilities { get; }

    /// <summary>
    /// Creates the logical device and its queues.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread after <see cref="VulkanInstance.Create"/> and after the platform has
    /// created the native surface.
    /// </remarks>
    /// <param name="instance">The created Vulkan instance.</param>
    /// <param name="surface">The non-zero native <c>VkSurfaceKHR</c> handle.</param>
    /// <param name="logger">The renderer logger.</param>
    /// <returns>The created device.</returns>
    /// <exception cref="GraphicsException">
    /// The surface is invalid, no physical device is available or suitable, the surface cannot
    /// support a swapchain, or logical-device creation failed. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static unsafe VulkanDevice Create(VulkanInstance instance, IntPtr surface, ILogger logger) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(logger);
        ObjectDisposedException.ThrowIf(instance.Handle.Handle == 0, instance);
        if (surface == IntPtr.Zero) {
            throw new GraphicsException(
                "Cannot create the Vulkan device because the native surface handle is zero. The platform must create the surface first.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        Vk vulkan = instance.Vulkan;
        Instance instanceHandle = instance.Handle;
        SurfaceKHR surfaceHandle = new() { Handle = unchecked((ulong)surface.ToInt64()) };
        PhysicalDevice[] physicalDevices = EnumeratePhysicalDevices(vulkan, instanceHandle);
        if (physicalDevices.Length == 0) {
            throw new GraphicsException(
                $"{NO_PHYSICAL_DEVICE_MESSAGE_PREFIX}. Install or enable a Vulkan 1.2 driver on {RuntimeInformation.OSDescription}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        List<VulkanPhysicalDeviceCandidate> candidates = new(physicalDevices.Length);
        List<VulkanQueueFamilyInfo[]> queueFamiliesByDevice = new(physicalDevices.Length);
        for (int index = 0; index < physicalDevices.Length; index++) {
            PhysicalDevice physicalDevice = physicalDevices[index];
            PhysicalDeviceProperties properties = vulkan.GetPhysicalDeviceProperties(physicalDevice);
            string name = Marshal.PtrToStringUTF8((IntPtr)properties.DeviceName) ?? string.Empty;
            VulkanQueueFamilyInfo[] queueFamilies = ProbeQueueFamilies(
                vulkan,
                instanceHandle,
                physicalDevice,
                surfaceHandle);
            bool supportsGraphics = Array.Exists(
                queueFamilies,
                static family => family.SupportsGraphics);
            bool supportsPresent = Array.Exists(
                queueFamilies,
                static family => family.SupportsPresent);
            bool hasSwapchain = HasDeviceExtension(vulkan, physicalDevice, SWAPCHAIN_EXTENSION_NAME);
            VulkanPhysicalDeviceCandidate candidate = new(
                index,
                name,
                properties.ApiVersion,
                properties.DeviceType,
                supportsGraphics,
                supportsPresent,
                hasSwapchain);
            candidates.Add(candidate);
            queueFamiliesByDevice.Add(queueFamilies);
            logger.LogDebug(
                "Vulkan physical device [{Index}] {Name}: API {ApiVersion}, type {DeviceType}, graphics {Graphics}, present {Present}, swapchain {Swapchain}.",
                index,
                name,
                VulkanVersion.Format(properties.ApiVersion),
                properties.DeviceType,
                supportsGraphics,
                supportsPresent,
                hasSwapchain);
        }

        int selectedIndex = VulkanPhysicalDeviceSelector.Select(candidates);
        VulkanPhysicalDeviceCandidate selected = candidates[selectedIndex];
        PhysicalDevice selectedDevice = physicalDevices[selectedIndex];
        (uint graphicsFamily, uint presentFamily) = VulkanQueueFamilySelection.Select(queueFamiliesByDevice[selectedIndex]);
        logger.LogInformation(
            "Vulkan physical device selected: [{Index}] {Name} (API {ApiVersion}, {DeviceType}, graphics queue family {GraphicsQueue}, present queue family {PresentQueue}).",
            selected.Index,
            selected.Name,
            VulkanVersion.Format(selected.ApiVersion),
            selected.DeviceType,
            graphicsFamily,
            presentFamily);

        VulkanSwapchainSupport swapchain = VulkanSwapchainSupport.Query(vulkan, selectedDevice, surfaceHandle);
        foreach (VulkanSurfaceFormat surfaceFormat in swapchain.Formats) {
            logger.LogDebug(
                "Vulkan surface format: {Format} / {ColorSpace}.",
                VulkanFormatInfo.Describe(surfaceFormat.Format),
                surfaceFormat.ColorSpace);
        }

        if (!swapchain.IsUsable) {
            throw new GraphicsException(
                $"The window surface cannot support a swapchain: it advertises {swapchain.Formats.Count} format(s) and {swapchain.PresentModes.Count} present mode(s). Ensure the window is mapped and the driver supports VK_KHR_surface.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        Device device = CreateLogicalDevice(
            vulkan,
            selectedDevice,
            graphicsFamily,
            presentFamily);
        try {
            Queue graphicsQueue = vulkan.GetDeviceQueue(device, graphicsFamily, 0);
            Queue presentQueue = presentFamily == graphicsFamily
                ? graphicsQueue
                : vulkan.GetDeviceQueue(device, presentFamily, 0);
            VulkanDeviceCapabilities capabilities = VulkanDeviceCapabilities.Query(
                vulkan,
                selectedDevice,
                graphicsFamily,
                presentFamily,
                instance.SwapchainColorSpaceExtensionEnabled,
                swapchain);
            VulkanDevice result = new(
                instance,
                vulkan,
                selectedDevice,
                device,
                graphicsQueue,
                presentQueue,
                capabilities,
                logger);
            instance.ThrowIfValidationError("creating the Vulkan device");
            logger.LogInformation(
                "Vulkan device created: {Name} (API {ApiVersion}, {VendorId}:{DeviceId}, {SampleCount} sample counts, descriptor indexing {DescriptorIndexing}).",
                capabilities.DeviceName,
                VulkanVersion.Format(capabilities.ApiVersion),
                capabilities.VendorId,
                capabilities.DeviceId,
                capabilities.SupportedSampleCounts.Count,
                capabilities.DescriptorIndexing);
            return result;
        } catch {
            vulkan.DestroyDevice(device, null);
            throw;
        }
    }

    /// <summary>
    /// Projects the device state into the immutable public <see cref="EngineInfo"/> value.
    /// </summary>
    /// <remarks>
    /// The swapchain-dependent fields stay empty or zero until the swapchain task selects the
    /// surface format, HDR mode, sample count, and present mode.
    /// </remarks>
    /// <returns>The engine information snapshot.</returns>
    internal EngineInfo CreateEngineInfo() => new(
        "RayBork",
        EngineVersion.Current,
        VulkanVersion.FormatRendererName(Capabilities.ApiVersion),
        Capabilities.DeviceName,
        Capabilities.ApiVersion,
        string.Empty,
        false,
        0,
        PresentMode.Mailbox,
        _instance.ValidationLayersEnabled);

    /// <summary>
    /// Waits for the device to become idle and destroys it. The call is idempotent.
    /// </summary>
    public unsafe void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (_device.Handle != 0) {
            Vk vulkan = Vulkan;
            Result waitResult = vulkan.DeviceWaitIdle(_device);
            if (waitResult != Result.Success) {
                _logger.LogWarning("Vulkan device wait-idle before disposal failed with {Result}.", waitResult);
            }

            vulkan.DestroyDevice(_device, null);
            _device = default;
        }

        _vulkan = null;
        _logger.LogDebug("Vulkan device disposed.");
        if (_instance.Handle.Handle != 0) {
            _instance.ThrowIfValidationError("destroying the Vulkan device");
        }
    }

    private static unsafe PhysicalDevice[] EnumeratePhysicalDevices(Vk vulkan, Instance instance) {
        uint count = 0;
        Result result = vulkan.EnumeratePhysicalDevices(instance, &count, null);
        ThrowIfFailed(result, "vkEnumeratePhysicalDevices");
        if (count == 0) {
            return [];
        }

        PhysicalDevice[] devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* devicesPointer = devices) {
            result = vulkan.EnumeratePhysicalDevices(instance, &count, devicesPointer);
        }

        ThrowIfFailed(result, "vkEnumeratePhysicalDevices");
        if (count != devices.Length) {
            Array.Resize(ref devices, (int)count);
        }

        return devices;
    }

    private static unsafe VulkanQueueFamilyInfo[] ProbeQueueFamilies(
        Vk vulkan,
        Instance instance,
        PhysicalDevice physicalDevice,
        SurfaceKHR surface) {
        uint count = 0;
        vulkan.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, null);
        if (count == 0) {
            return [];
        }

        QueueFamilyProperties[] properties = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* propertiesPointer = properties) {
            vulkan.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, propertiesPointer);
        }

        if (count != properties.Length) {
            Array.Resize(ref properties, (int)count);
        }

        VulkanQueueFamilyInfo[] families = new VulkanQueueFamilyInfo[properties.Length];
        for (int index = 0; index < properties.Length; index++) {
            QueueFamilyProperties family = properties[index];
            bool supportsGraphics = family.QueueCount > 0
                && (family.QueueFlags & QueueFlags.GraphicsBit) != 0;
            bool supportsPresent = family.QueueCount > 0
                && VulkanNativeFunctions.GetSurfaceSupport(
                    vulkan,
                    instance,
                    physicalDevice,
                    (uint)index,
                    surface);
            families[index] = new VulkanQueueFamilyInfo(
                (uint)index,
                supportsGraphics,
                supportsPresent,
                family.QueueCount);
        }

        return families;
    }

    private static unsafe bool HasDeviceExtension(
        Vk vulkan,
        PhysicalDevice physicalDevice,
        string extensionName) {
        uint count = 0;
        Result result = vulkan.EnumerateDeviceExtensionProperties(
            physicalDevice,
            (string?)null,
            &count,
            null);
        ThrowIfFailed(result, "vkEnumerateDeviceExtensionProperties");
        if (count == 0) {
            return false;
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPointer = properties) {
            result = vulkan.EnumerateDeviceExtensionProperties(
                physicalDevice,
                (string?)null,
                &count,
                propertiesPointer);
        }

        ThrowIfFailed(result, "vkEnumerateDeviceExtensionProperties");
        for (int index = 0; index < count; index++) {
            string? name;
            fixed (byte* namePointer = properties[index].ExtensionName) {
                name = Marshal.PtrToStringUTF8((IntPtr)namePointer);
            }

            if (string.Equals(name, extensionName, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static unsafe Device CreateLogicalDevice(
        Vk vulkan,
        PhysicalDevice physicalDevice,
        uint graphicsFamily,
        uint presentFamily) {
        List<uint> families = [graphicsFamily];
        if (presentFamily != graphicsFamily) {
            families.Add(presentFamily);
        }

        float[] priorities = new float[families.Count];
        Array.Fill(priorities, QUEUE_PRIORITY);
        DeviceQueueCreateInfo[] queueCreateInfos = new DeviceQueueCreateInfo[families.Count];
        byte[] swapchainExtension = "VK_KHR_swapchain\0"u8.ToArray();
        fixed (DeviceQueueCreateInfo* queueCreateInfosPointer = queueCreateInfos)
        fixed (float* prioritiesPointer = priorities)
        fixed (byte* swapchainExtensionPointer = swapchainExtension) {
            for (int index = 0; index < families.Count; index++) {
                queueCreateInfos[index] = new DeviceQueueCreateInfo {
                    SType = StructureType.DeviceQueueCreateInfo,
                    QueueFamilyIndex = families[index],
                    QueueCount = 1,
                    PQueuePriorities = &prioritiesPointer[index],
                };
            }

            byte* extensionNamePointer = swapchainExtensionPointer;
            DeviceCreateInfo createInfo = new() {
                SType = StructureType.DeviceCreateInfo,
                QueueCreateInfoCount = (uint)families.Count,
                PQueueCreateInfos = queueCreateInfosPointer,
                EnabledLayerCount = 0,
                PpEnabledLayerNames = null,
                EnabledExtensionCount = 1,
                PpEnabledExtensionNames = &extensionNamePointer,
                PEnabledFeatures = null,
            };
            Result result = vulkan.CreateDevice(physicalDevice, &createInfo, null, out Device device);
            ThrowIfFailed(result, "vkCreateDevice");
            if (device.Handle == 0) {
                throw new GraphicsException(
                    "Vulkan logical-device creation returned a zero device handle.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            return device;
        }
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
