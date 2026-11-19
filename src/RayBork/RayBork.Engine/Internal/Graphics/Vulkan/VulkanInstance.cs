using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 instance owned by the render thread.
/// </summary>
/// <remarks>
/// Creation requires the platform surface extensions and optionally enables validation and
/// swapchain color space. Disposal is split: <see cref="Dispose"/> on the render thread, then
/// <see cref="DisposeFromMainThread"/> after that thread stops.
/// </remarks>
internal sealed class VulkanInstance : IDisposable
{
    /// <summary>The Vulkan API version requested from the loader.</summary>
    internal static readonly uint RequestedApiVersion = VulkanVersion.Pack(1, 2, 0);

    private const string ENGINE_NAME = "RayBork";
    private const string DEBUG_UTILS_EXTENSION_NAME = "VK_EXT_debug_utils";
    private const string SWAPCHAIN_COLORSPACE_EXTENSION_NAME = "VK_EXT_swapchain_colorspace";

    private static readonly Lock _processGate = new();

    private readonly ILogger _logger;

    private Vk? _vulkan;
    private VulkanLoader? _loader;
    private Instance _handle;
    private VulkanValidation? _validation;
    private DebugUtilsMessengerEXT _debugMessenger;
    private bool _renderThreadDisposed;
    private bool _tornDown;

    private VulkanInstance(
        Vk vulkan,
        VulkanLoader loader,
        Instance handle,
        VulkanValidation? validation,
        DebugUtilsMessengerEXT debugMessenger,
        bool validationLayersEnabled,
        bool swapchainColorSpaceExtensionEnabled,
        ILogger logger
    )
    {
        _vulkan = vulkan;
        _loader = loader;
        _handle = handle;
        _validation = validation;
        _debugMessenger = debugMessenger;
        ValidationLayersEnabled = validationLayersEnabled;
        DebugMessengerEnabled = debugMessenger.Handle != 0;
        SwapchainColorSpaceExtensionEnabled = swapchainColorSpaceExtensionEnabled;
        _logger = logger;
    }

    /// <summary>Gets the loaded Vulkan API; reading it after disposal throws.</summary>
    internal Vk Vulkan => _vulkan ?? throw new ObjectDisposedException(nameof(VulkanInstance));

    /// <summary>Gets the native <c>VkInstance</c> handle, default after disposal.</summary>
    internal Instance Handle => _handle;

    /// <summary>Gets the packed Vulkan 1.2.0 API version requested at creation.</summary>
    internal static uint ApiVersion => RequestedApiVersion;

    /// <summary>Gets whether the validation layer is active.</summary>
    internal bool ValidationLayersEnabled { get; }

    /// <summary>Gets whether the debug-utils messenger is active.</summary>
    internal bool DebugMessengerEnabled { get; }

    /// <summary>Gets whether the swapchain color-space extension is enabled for HDR.</summary>
    internal bool SwapchainColorSpaceExtensionEnabled { get; }

    /// <summary>
    /// Creates the Vulkan instance, requiring the platform surface extensions.
    /// </summary>
    internal static unsafe VulkanInstance Create(VulkanInstanceOptions options)
    {
        lock (_processGate)
        {
            return CreateCore(options);
        }
    }

    private static unsafe VulkanInstance CreateCore(VulkanInstanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ApplicationName);
        ArgumentNullException.ThrowIfNull(options.ApplicationVersion);
        ArgumentNullException.ThrowIfNull(options.SurfaceExtensions);
        ArgumentNullException.ThrowIfNull(options.Logger);

        VulkanLoader loader = VulkanLoader.Load(options.Logger);
        Vk vulkan;
        try
        {
            vulkan = new(loader);
        }
        catch
        {
            loader.Dispose();
            throw;
        }

        Instance handle = default;
        VulkanValidation? validation = null;
        DebugUtilsMessengerEXT debugMessenger = default;
        bool validationLayersEnabled = false;
        try
        {
            IReadOnlyList<string> availableExtensions = EnumerateInstanceExtensions(vulkan);
            VulkanInstanceExtensions.Require(availableExtensions, options.SurfaceExtensions);

            validationLayersEnabled =
                options.RequestValidation
                && EnumerateInstanceLayers(vulkan)
                    .Contains(VulkanValidation.LayerName, StringComparer.Ordinal);
            if (options.RequestValidation && !validationLayersEnabled)
            {
                options.Logger.LogWarning(
                    "The Vulkan validation layer {Layer} is not available; validation stays disabled.",
                    VulkanValidation.LayerName
                );
            }

            bool debugUtilsAvailable = availableExtensions.Contains(
                DEBUG_UTILS_EXTENSION_NAME,
                StringComparer.Ordinal
            );
            bool createDebugMessenger = validationLayersEnabled && debugUtilsAvailable;
            if (validationLayersEnabled && !debugUtilsAvailable)
            {
                options.Logger.LogWarning(
                    "The {Extension} instance extension is not available; validation messages cannot be captured.",
                    DEBUG_UTILS_EXTENSION_NAME
                );
            }

            bool swapchainColorSpaceExtensionEnabled = availableExtensions.Contains(
                SWAPCHAIN_COLORSPACE_EXTENSION_NAME,
                StringComparer.Ordinal
            );
            string[] enabledExtensions = BuildEnabledExtensions(
                options.SurfaceExtensions,
                createDebugMessenger,
                swapchainColorSpaceExtensionEnabled
            );
            string[] enabledLayers = validationLayersEnabled ? [VulkanValidation.LayerName] : [];
            handle = CreateInstanceHandle(vulkan, options, enabledExtensions, enabledLayers);
            vulkan.CurrentInstance = handle;

            if (swapchainColorSpaceExtensionEnabled)
            {
                options.Logger.LogInformation(
                    "Vulkan swapchain color-space extension {Extension} enabled.",
                    SWAPCHAIN_COLORSPACE_EXTENSION_NAME
                );
            }
            else
            {
                options.Logger.LogWarning(
                    "The {Extension} instance extension is not available; HDR presentation falls back to SDR.",
                    SWAPCHAIN_COLORSPACE_EXTENSION_NAME
                );
            }

            if (createDebugMessenger)
            {
                validation = new VulkanValidation(options.Logger);
                debugMessenger = VulkanNativeFunctions.CreateDebugMessenger(
                    vulkan,
                    handle,
                    validation.CreateMessengerCreateInfo()
                );
            }

            VulkanInstance instance = new(
                vulkan,
                loader,
                handle,
                validation,
                debugMessenger,
                validationLayersEnabled,
                swapchainColorSpaceExtensionEnabled,
                options.Logger
            );
            instance.ThrowIfValidationError("creating the Vulkan instance");
            options.Logger.LogInformation(
                "Vulkan instance created: API {ApiVersion}; validation {Validation}; debug messenger {DebugMessenger}; swapchain color space {ColorSpace}.",
                VulkanVersion.Format(VulkanInstance.ApiVersion),
                instance.ValidationLayersEnabled ? "enabled" : "disabled",
                instance.DebugMessengerEnabled ? "enabled" : "disabled",
                instance.SwapchainColorSpaceExtensionEnabled ? "enabled" : "disabled"
            );
            return instance;
        }
        catch
        {
            try
            {
                DestroyMessengerBestEffort(vulkan, handle, debugMessenger);
                validation?.Dispose();
                if (handle.Handle != 0)
                {
                    vulkan.DestroyInstance(handle, null);
                    vulkan.CurrentInstance = null;
                }
            }
            finally
            {
                loader.Dispose();
            }

            throw;
        }
    }

    /// <summary>Returns and clears validation errors recorded since the previous call.</summary>
    internal IReadOnlyList<string> ConsumeValidationErrors() => _validation?.ConsumeErrors() ?? [];

    /// <summary>Throws when validation recorded errors since the previous call.</summary>
    internal void ThrowIfValidationError(string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        _validation?.ThrowIfErrors(operation);
    }

    /// <summary>
    /// Completes render-thread disposal; instance teardown is deferred to the main thread.
    /// </summary>
    /// <remarks>
    /// The loader's driver teardown must not run on the exiting render thread.
    /// </remarks>
    public void Dispose()
    {
        if (_renderThreadDisposed)
        {
            return;
        }

        _renderThreadDisposed = true;
        _logger.LogDebug(
            "Vulkan instance render-thread disposal completed; teardown is deferred to the main thread."
        );
    }

    /// <summary>
    /// Destroys the debug messenger, instance, API, and loader after the render thread stops.
    /// </summary>
    /// <remarks>Idempotent, and safe after the render-thread <see cref="Dispose"/>.</remarks>
    internal unsafe void DisposeFromMainThread()
    {
        lock (_processGate)
        {
            DisposeFromMainThreadCore();
        }
    }

    private unsafe void DisposeFromMainThreadCore()
    {
        if (_tornDown)
        {
            return;
        }

        _tornDown = true;
        Vk? vulkan = _vulkan;
        try
        {
            if (vulkan is not null)
            {
                if (_debugMessenger.Handle != 0)
                {
                    DestroyMessengerBestEffort(vulkan, _handle, _debugMessenger);
                    _debugMessenger = default;
                }

                _validation?.Dispose();
                _validation = null;
                if (_handle.Handle != 0)
                {
                    vulkan.DestroyInstance(_handle, null);
                    _handle = default;
                }

                vulkan.CurrentInstance = null;
                if (vulkan is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        finally
        {
            _vulkan = null;
            _loader?.Dispose();
            _loader = null;
            _logger.LogDebug("Vulkan instance disposed on the main thread.");
        }
    }

    private static unsafe IReadOnlyList<string> EnumerateInstanceExtensions(Vk vulkan)
    {
        uint count = 0;
        Result result = vulkan.EnumerateInstanceExtensionProperties((string?)null, &count, null);
        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceExtensionProperties");
        if (count == 0)
        {
            return [];
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPointer = properties)
        {
            result = vulkan.EnumerateInstanceExtensionProperties(
                (string?)null,
                &count,
                propertiesPointer
            );
        }

        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceExtensionProperties");
        List<string> names = new((int)count);
        for (int index = 0; index < count; index++)
        {
            string? name;
            fixed (byte* namePointer = properties[index].ExtensionName)
            {
                name = Marshal.PtrToStringUTF8((IntPtr)namePointer);
            }

            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static unsafe IReadOnlyList<string> EnumerateInstanceLayers(Vk vulkan)
    {
        uint count = 0;
        Result result = vulkan.EnumerateInstanceLayerProperties(&count, null);
        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceLayerProperties");
        if (count == 0)
        {
            return [];
        }

        LayerProperties[] properties = new LayerProperties[count];
        fixed (LayerProperties* propertiesPointer = properties)
        {
            result = vulkan.EnumerateInstanceLayerProperties(&count, propertiesPointer);
        }

        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceLayerProperties");
        List<string> names = new((int)count);
        for (int index = 0; index < count; index++)
        {
            string? name;
            fixed (byte* namePointer = properties[index].LayerName)
            {
                name = Marshal.PtrToStringUTF8((IntPtr)namePointer);
            }

            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static unsafe Instance CreateInstanceHandle(
        Vk vulkan,
        VulkanInstanceOptions options,
        IReadOnlyList<string> enabledExtensions,
        IReadOnlyList<string> enabledLayers
    )
    {
        IntPtr applicationName = Marshal.StringToCoTaskMemUTF8(options.ApplicationName);
        IntPtr engineName = Marshal.StringToCoTaskMemUTF8(ENGINE_NAME);
        IntPtr[] extensionPointers = AllocateUtf8Pointers(enabledExtensions);
        IntPtr[] layerPointers = AllocateUtf8Pointers(enabledLayers);
        try
        {
            fixed (IntPtr* extensionPointer = extensionPointers)
            fixed (IntPtr* layerPointer = layerPointers)
            {
                ApplicationInfo applicationInfo = new()
                {
                    SType = StructureType.ApplicationInfo,
                    PApplicationName = (byte*)applicationName,
                    ApplicationVersion = PackApplicationVersion(options.ApplicationVersion),
                    PEngineName = (byte*)engineName,
                    EngineVersion = PackApplicationVersion(EngineVersion.Current),
                    ApiVersion = RequestedApiVersion,
                };
                InstanceCreateInfo createInfo = new()
                {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &applicationInfo,
                    EnabledExtensionCount = (uint)enabledExtensions.Count,
                    PpEnabledExtensionNames = (byte**)extensionPointer,
                    EnabledLayerCount = (uint)enabledLayers.Count,
                    PpEnabledLayerNames = (byte**)layerPointer,
                };
                Result result = vulkan.CreateInstance(&createInfo, null, out Instance handle);
                if (result != Result.Success)
                {
                    throw new GraphicsException(
                        $"Vulkan instance creation failed with {result}.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null
                    );
                }

                if (handle.Handle == 0)
                {
                    throw new GraphicsException(
                        "Vulkan instance creation returned a zero instance handle.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null
                    );
                }

                return handle;
            }
        }
        finally
        {
            FreeUtf8Pointer(applicationName);
            FreeUtf8Pointer(engineName);
            FreeUtf8Pointers(extensionPointers);
            FreeUtf8Pointers(layerPointers);
        }
    }

    private static string[] BuildEnabledExtensions(
        IReadOnlyList<string> surfaceExtensions,
        bool createDebugMessenger,
        bool enableSwapchainColorSpace
    )
    {
        List<string> enabled = [.. surfaceExtensions];
        if (
            createDebugMessenger
            && !enabled.Contains(DEBUG_UTILS_EXTENSION_NAME, StringComparer.Ordinal)
        )
        {
            enabled.Add(DEBUG_UTILS_EXTENSION_NAME);
        }

        if (
            enableSwapchainColorSpace
            && !enabled.Contains(SWAPCHAIN_COLORSPACE_EXTENSION_NAME, StringComparer.Ordinal)
        )
        {
            enabled.Add(SWAPCHAIN_COLORSPACE_EXTENSION_NAME);
        }

        return [.. enabled];
    }

    private static uint PackApplicationVersion(Version version) =>
        VulkanVersion.Pack(version.Major, version.Minor, Math.Max(0, version.Build));

    private static IntPtr[] AllocateUtf8Pointers(IReadOnlyList<string> values)
    {
        IntPtr[] pointers = new IntPtr[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            pointers[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
        }

        return pointers;
    }

    private static void FreeUtf8Pointers(IntPtr[] pointers)
    {
        foreach (IntPtr pointer in pointers)
        {
            FreeUtf8Pointer(pointer);
        }
    }

    private static void FreeUtf8Pointer(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static void ThrowIfEnumerateFailed(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new GraphicsException(
                $"The Vulkan command {operation} failed with {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }
    }

    private static void DestroyMessengerBestEffort(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerEXT messenger
    )
    {
        if (messenger.Handle == 0 || instance.Handle == 0)
        {
            return;
        }

        try
        {
            VulkanNativeFunctions.DestroyDebugMessenger(vulkan, instance, messenger);
        }
        catch (GraphicsException)
        {
            // Cleanup must not replace the original creation failure.
        }
    }
}
