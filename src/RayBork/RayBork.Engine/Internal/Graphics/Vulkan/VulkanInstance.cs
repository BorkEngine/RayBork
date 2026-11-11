using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 instance owned by the render thread.
/// </summary>
/// <remarks>
/// <para>
/// The render thread creates the instance after the main thread has created the window and supplied
/// the platform's required surface extensions. Creation requests Vulkan 1.2, validates the platform
/// extensions against the loader, optionally enables the Khronos validation layer and its debug-utils
/// messenger, and throws when validation reported an error while creating the instance.
/// </para>
/// <para>
/// The instance is not thread-safe. The render thread creates and uses it, and its
/// <see cref="Dispose"/> only completes the render-thread phase. After the render thread has stopped,
/// the main thread calls <see cref="DisposeFromMainThread"/>, which destroys the debug messenger, the
/// instance, the Silk.NET API, and the native loader.
/// </para>
/// </remarks>
internal sealed class VulkanInstance : IDisposable {
    /// <summary>
    /// The Vulkan API version the renderer requests from the loader.
    /// </summary>
    internal static readonly uint RequestedApiVersion = VulkanVersion.Pack(1, 2, 0);

    private const string ENGINE_NAME = "RayBork";
    private const string DEBUG_UTILS_EXTENSION_NAME = "VK_EXT_debug_utils";
    private const string SWAPCHAIN_COLORSPACE_EXTENSION_NAME = "VK_EXT_swapchain_colorspace";

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
        ILogger logger) {
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

    /// <summary>
    /// Gets the loaded Vulkan API.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The instance has been disposed.</exception>
    internal Vk Vulkan => _vulkan ?? throw new ObjectDisposedException(nameof(VulkanInstance));

    /// <summary>
    /// Gets the native <c>VkInstance</c> handle. It is the default handle after disposal.
    /// </summary>
    internal Instance Handle => _handle;

    /// <summary>
    /// Gets the packed Vulkan API version requested at creation, which is Vulkan 1.2.0.
    /// </summary>
    internal uint ApiVersion => RequestedApiVersion;

    /// <summary>
    /// Gets whether the <c>VK_LAYER_KHRONOS_validation</c> layer is active.
    /// </summary>
    internal bool ValidationLayersEnabled { get; }

    /// <summary>
    /// Gets whether the debug-utils messenger that records validation errors is active.
    /// </summary>
    internal bool DebugMessengerEnabled { get; }

    /// <summary>
    /// Gets whether <c>VK_EXT_swapchain_colorspace</c> was enabled on the instance. HDR presentation
    /// needs the extended color spaces that this extension advertises.
    /// </summary>
    internal bool SwapchainColorSpaceExtensionEnabled { get; }

    /// <summary>
    /// Creates the Vulkan instance.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread. The platform surface extensions are required; the validation layer,
    /// the debug-utils messenger, and <c>VK_EXT_swapchain_colorspace</c> are optional and degrade to
    /// a warning when unavailable. Without the color-space extension, HDR presentation falls back to
    /// SDR.
    /// </remarks>
    /// <param name="options">The instance options captured at startup.</param>
    /// <returns>The created instance.</returns>
    /// <exception cref="GraphicsException">
    /// The Vulkan loader is unavailable, a required platform extension is missing, instance creation
    /// failed, or validation reported an error. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static unsafe VulkanInstance Create(VulkanInstanceOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.ApplicationName);
        ArgumentNullException.ThrowIfNull(options.ApplicationVersion);
        ArgumentNullException.ThrowIfNull(options.SurfaceExtensions);
        ArgumentNullException.ThrowIfNull(options.Logger);

        VulkanLoader loader = VulkanLoader.Load(options.Logger);
        Vk vulkan;
        try {
            vulkan = new(loader);
        } catch {
            loader.Dispose();
            throw;
        }

        Instance handle = default;
        VulkanValidation? validation = null;
        DebugUtilsMessengerEXT debugMessenger = default;
        bool validationLayersEnabled = false;
        try {
            IReadOnlyList<string> availableExtensions = EnumerateInstanceExtensions(vulkan);
            VulkanInstanceExtensions.Require(availableExtensions, options.SurfaceExtensions);

            validationLayersEnabled = options.RequestValidation
                && EnumerateInstanceLayers(vulkan).Contains(VulkanValidation.LayerName, StringComparer.Ordinal);
            if (options.RequestValidation && !validationLayersEnabled) {
                options.Logger.LogWarning(
                    "The Vulkan validation layer {Layer} is not available; validation stays disabled.",
                    VulkanValidation.LayerName);
            }

            bool debugUtilsAvailable = availableExtensions.Contains(DEBUG_UTILS_EXTENSION_NAME, StringComparer.Ordinal);
            bool createDebugMessenger = validationLayersEnabled && debugUtilsAvailable;
            if (validationLayersEnabled && !debugUtilsAvailable) {
                options.Logger.LogWarning(
                    "The {Extension} instance extension is not available; validation messages cannot be captured.",
                    DEBUG_UTILS_EXTENSION_NAME);
            }

            bool swapchainColorSpaceExtensionEnabled = availableExtensions.Contains(
                SWAPCHAIN_COLORSPACE_EXTENSION_NAME,
                StringComparer.Ordinal);
            string[] enabledExtensions = BuildEnabledExtensions(
                options.SurfaceExtensions,
                createDebugMessenger,
                swapchainColorSpaceExtensionEnabled);
            string[] enabledLayers = validationLayersEnabled ? [VulkanValidation.LayerName] : [];
            handle = CreateInstanceHandle(vulkan, options, enabledExtensions, enabledLayers);
            vulkan.CurrentInstance = handle;

            if (swapchainColorSpaceExtensionEnabled) {
                options.Logger.LogInformation(
                    "Vulkan swapchain color-space extension {Extension} enabled.",
                    SWAPCHAIN_COLORSPACE_EXTENSION_NAME);
            } else {
                options.Logger.LogWarning(
                    "The {Extension} instance extension is not available; HDR presentation falls back to SDR.",
                    SWAPCHAIN_COLORSPACE_EXTENSION_NAME);
            }

            if (createDebugMessenger) {
                validation = new VulkanValidation(options.Logger);
                debugMessenger = VulkanNativeFunctions.CreateDebugMessenger(
                    vulkan,
                    handle,
                    validation.CreateMessengerCreateInfo());
            }

            VulkanInstance instance = new(
                vulkan,
                loader,
                handle,
                validation,
                debugMessenger,
                validationLayersEnabled,
                swapchainColorSpaceExtensionEnabled,
                options.Logger);
            instance.ThrowIfValidationError("creating the Vulkan instance");
            options.Logger.LogInformation(
                "Vulkan instance created: API {ApiVersion}; validation {Validation}; debug messenger {DebugMessenger}; swapchain color space {ColorSpace}.",
                VulkanVersion.Format(instance.ApiVersion),
                instance.ValidationLayersEnabled ? "enabled" : "disabled",
                instance.DebugMessengerEnabled ? "enabled" : "disabled",
                instance.SwapchainColorSpaceExtensionEnabled ? "enabled" : "disabled");
            return instance;
        } catch {
            try {
                DestroyMessengerBestEffort(vulkan, handle, debugMessenger);
                validation?.Dispose();
                if (handle.Handle != 0) {
                    vulkan.DestroyInstance(handle, null);
                    vulkan.CurrentInstance = null;
                }
            } finally {
                loader.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Returns and clears the validation errors recorded since the previous call.
    /// </summary>
    /// <returns>The recorded error messages, or an empty list when validation is inactive.</returns>
    internal IReadOnlyList<string> ConsumeValidationErrors() =>
        _validation?.ConsumeErrors() ?? [];

    /// <summary>
    /// Throws when validation errors were recorded since the previous call.
    /// </summary>
    /// <param name="operation">The operation that was in progress, for the diagnostic message.</param>
    /// <exception cref="GraphicsException">
    /// At least one validation error was recorded. The message lists every error and the diagnostic
    /// code is <c>RB3001</c>.
    /// </exception>
    internal void ThrowIfValidationError(string operation) {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        _validation?.ThrowIfErrors(operation);
    }

    /// <summary>
    /// Completes the render-thread phase of disposal. The instance and loader stay alive until the
    /// main thread calls <see cref="DisposeFromMainThread"/>. The call is idempotent.
    /// </summary>
    /// <remarks>
    /// Runs on the render thread after every surface, device, and swapchain created from the instance
    /// has been destroyed. It deliberately does not destroy the instance: the Vulkan loader unloads
    /// driver libraries that registered thread-local state on the render thread, and running that
    /// teardown on the stopping render thread crashes when the thread exits.
    /// </remarks>
    public void Dispose() {
        if (_renderThreadDisposed) {
            return;
        }

        _renderThreadDisposed = true;
        _logger.LogDebug("Vulkan instance render-thread disposal completed; teardown is deferred to the main thread.");
    }

    /// <summary>
    /// Destroys the debug messenger, the instance, the Silk.NET API, and the native loader. The call
    /// is idempotent and safe after the render-thread <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Main-thread only. It runs after the render thread has stopped and no device, swapchain, or
    /// surface references the instance. Destroying the instance and loader here, rather than on the
    /// render thread, keeps driver thread-local destructors valid until that thread has exited.
    /// </remarks>
    internal unsafe void DisposeFromMainThread() {
        if (_tornDown) {
            return;
        }

        _tornDown = true;
        Vk? vulkan = _vulkan;
        try {
            if (vulkan is not null) {
                if (_debugMessenger.Handle != 0) {
                    DestroyMessengerBestEffort(vulkan, _handle, _debugMessenger);
                    _debugMessenger = default;
                }

                _validation?.Dispose();
                _validation = null;
                if (_handle.Handle != 0) {
                    vulkan.DestroyInstance(_handle, null);
                    _handle = default;
                }

                vulkan.CurrentInstance = null;
                if (vulkan is IDisposable disposable) {
                    disposable.Dispose();
                }
            }
        } finally {
            _vulkan = null;
            _loader?.Dispose();
            _loader = null;
            _logger.LogDebug("Vulkan instance disposed on the main thread.");
        }
    }

    private static unsafe IReadOnlyList<string> EnumerateInstanceExtensions(Vk vulkan) {
        uint count = 0;
        Result result = vulkan.EnumerateInstanceExtensionProperties((string?)null, &count, null);
        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceExtensionProperties");
        if (count == 0) {
            return [];
        }

        ExtensionProperties[] properties = new ExtensionProperties[count];
        fixed (ExtensionProperties* propertiesPointer = properties) {
            result = vulkan.EnumerateInstanceExtensionProperties((string?)null, &count, propertiesPointer);
        }

        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceExtensionProperties");
        List<string> names = new((int)count);
        for (int index = 0; index < count; index++) {
            string? name;
            fixed (byte* namePointer = properties[index].ExtensionName) {
                name = Marshal.PtrToStringUTF8((IntPtr)namePointer);
            }

            if (!string.IsNullOrEmpty(name)) {
                names.Add(name);
            }
        }

        return names;
    }

    private static unsafe IReadOnlyList<string> EnumerateInstanceLayers(Vk vulkan) {
        uint count = 0;
        Result result = vulkan.EnumerateInstanceLayerProperties(&count, null);
        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceLayerProperties");
        if (count == 0) {
            return [];
        }

        LayerProperties[] properties = new LayerProperties[count];
        fixed (LayerProperties* propertiesPointer = properties) {
            result = vulkan.EnumerateInstanceLayerProperties(&count, propertiesPointer);
        }

        ThrowIfEnumerateFailed(result, "vkEnumerateInstanceLayerProperties");
        List<string> names = new((int)count);
        for (int index = 0; index < count; index++) {
            string? name;
            fixed (byte* namePointer = properties[index].LayerName) {
                name = Marshal.PtrToStringUTF8((IntPtr)namePointer);
            }

            if (!string.IsNullOrEmpty(name)) {
                names.Add(name);
            }
        }

        return names;
    }

    private static unsafe Instance CreateInstanceHandle(
        Vk vulkan,
        VulkanInstanceOptions options,
        IReadOnlyList<string> enabledExtensions,
        IReadOnlyList<string> enabledLayers) {
        IntPtr applicationName = Marshal.StringToCoTaskMemUTF8(options.ApplicationName);
        IntPtr engineName = Marshal.StringToCoTaskMemUTF8(ENGINE_NAME);
        IntPtr[] extensionPointers = AllocateUtf8Pointers(enabledExtensions);
        IntPtr[] layerPointers = AllocateUtf8Pointers(enabledLayers);
        try {
            fixed (IntPtr* extensionPointer = extensionPointers)
            fixed (IntPtr* layerPointer = layerPointers) {
                ApplicationInfo applicationInfo = new() {
                    SType = StructureType.ApplicationInfo,
                    PApplicationName = (byte*)applicationName,
                    ApplicationVersion = PackApplicationVersion(options.ApplicationVersion),
                    PEngineName = (byte*)engineName,
                    EngineVersion = PackApplicationVersion(EngineVersion.Current),
                    ApiVersion = RequestedApiVersion,
                };
                InstanceCreateInfo createInfo = new() {
                    SType = StructureType.InstanceCreateInfo,
                    PApplicationInfo = &applicationInfo,
                    EnabledExtensionCount = (uint)enabledExtensions.Count,
                    PpEnabledExtensionNames = (byte**)extensionPointer,
                    EnabledLayerCount = (uint)enabledLayers.Count,
                    PpEnabledLayerNames = (byte**)layerPointer,
                };
                Result result = vulkan.CreateInstance(&createInfo, null, out Instance handle);
                if (result != Result.Success) {
                    throw new GraphicsException(
                        $"Vulkan instance creation failed with {result}.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null);
                }

                if (handle.Handle == 0) {
                    throw new GraphicsException(
                        "Vulkan instance creation returned a zero instance handle.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null);
                }

                return handle;
            }
        } finally {
            FreeUtf8Pointer(applicationName);
            FreeUtf8Pointer(engineName);
            FreeUtf8Pointers(extensionPointers);
            FreeUtf8Pointers(layerPointers);
        }
    }

    private static string[] BuildEnabledExtensions(
        IReadOnlyList<string> surfaceExtensions,
        bool createDebugMessenger,
        bool enableSwapchainColorSpace) {
        List<string> enabled = [.. surfaceExtensions];
        if (createDebugMessenger && !enabled.Contains(DEBUG_UTILS_EXTENSION_NAME, StringComparer.Ordinal)) {
            enabled.Add(DEBUG_UTILS_EXTENSION_NAME);
        }

        if (enableSwapchainColorSpace && !enabled.Contains(SWAPCHAIN_COLORSPACE_EXTENSION_NAME, StringComparer.Ordinal)) {
            enabled.Add(SWAPCHAIN_COLORSPACE_EXTENSION_NAME);
        }

        return [.. enabled];
    }

    private static uint PackApplicationVersion(Version version) =>
        VulkanVersion.Pack(version.Major, version.Minor, Math.Max(0, version.Build));

    private static IntPtr[] AllocateUtf8Pointers(IReadOnlyList<string> values) {
        IntPtr[] pointers = new IntPtr[values.Count];
        for (int index = 0; index < values.Count; index++) {
            pointers[index] = Marshal.StringToCoTaskMemUTF8(values[index]);
        }

        return pointers;
    }

    private static void FreeUtf8Pointers(IntPtr[] pointers) {
        foreach (IntPtr pointer in pointers) {
            FreeUtf8Pointer(pointer);
        }
    }

    private static void FreeUtf8Pointer(IntPtr pointer) {
        if (pointer != IntPtr.Zero) {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static void ThrowIfEnumerateFailed(Result result, string operation) {
        if (result != Result.Success) {
            throw new GraphicsException(
                $"The Vulkan command {operation} failed with {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }
    }

    private static void DestroyMessengerBestEffort(
        Vk vulkan,
        Instance instance,
        DebugUtilsMessengerEXT messenger) {
        if (messenger.Handle == 0 || instance.Handle == 0) {
            return;
        }

        try {
            VulkanNativeFunctions.DestroyDebugMessenger(vulkan, instance, messenger);
        } catch (GraphicsException) {
            // Cleanup must not replace the original creation failure.
        }
    }
}
