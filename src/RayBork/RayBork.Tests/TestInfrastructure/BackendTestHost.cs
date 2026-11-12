using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using Silk.NET.Core.Loader;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL Vulkan window, a direct surface provider, and an initialized
/// <see cref="Vulkan12Backend"/> for one GPU test.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryCreate"/> applies the same environmental-skip policy as <see cref="GpuTestHost"/>:
/// SDL cannot initialize a Vulkan window, the Vulkan loader is missing, no physical device supports
/// the surface, or the platform cannot create the surface. A programming or capability failure throws
/// so the test fails instead of disappearing.
/// </para>
/// <para>
/// The host exposes the backend's device, scene target, and instance for readback and validation
/// probes. Those are diagnostics, not renderer seam members. Every GPU test that uses this host joins
/// the serialized <c>SDL</c> collection because SDL's video driver hint and Vulkan initialization are
/// process global.
/// </para>
/// </remarks>
internal sealed class BackendTestHost : IDisposable {
    private readonly SdlTestHost _sdlHost;
    private readonly RenderSettings _settings;
    private readonly IReadOnlyList<string> _instanceExtensions;
    private readonly DirectSurfaceProvider _surfaceProvider;
    private readonly Vulkan12Backend _backend;
    private readonly MainThreadTeardownQueue _teardownQueue = new();

    private WindowSurfaceState _state;
    private bool _disposed;

    private BackendTestHost(
        SdlTestHost sdlHost,
        RenderSettings settings,
        IReadOnlyList<string> instanceExtensions,
        WindowSurfaceState initialState) {
        _sdlHost = sdlHost;
        _settings = settings;
        _instanceExtensions = instanceExtensions;
        _state = initialState;
        _surfaceProvider = new DirectSurfaceProvider(sdlHost.Platform);
        _backend = new Vulkan12Backend();
    }

    /// <summary>
    /// Gets the SDL host that owns the Vulkan window.
    /// </summary>
    internal SdlTestHost SdlHost => _sdlHost;

    /// <summary>
    /// Gets the SDL platform. Tests use it to pump events and queue window commands.
    /// </summary>
    internal SdlPlatform Platform => _sdlHost.Platform;

    /// <summary>
    /// Gets the initialized renderer backend.
    /// </summary>
    internal Vulkan12Backend Backend => _backend;

    /// <summary>
    /// Gets the backend's Vulkan instance for validation checks.
    /// </summary>
    internal VulkanInstance Instance => _backend.Instance;

    /// <summary>
    /// Gets the backend's logical device for readback probes.
    /// </summary>
    internal VulkanDevice Device => _backend.Device;

    /// <summary>
    /// Gets the backend's current scene target for readback probes.
    /// </summary>
    internal VulkanSceneTarget SceneTarget => _backend.SceneTarget;

    /// <summary>
    /// Gets the direct surface provider the backend creates and destroys the native surface through.
    /// </summary>
    /// <remarks>
    /// Recovery tests use its create and destroy counters to prove that a lost surface was replaced.
    /// </remarks>
    internal DirectSurfaceProvider SurfaceProvider => _surfaceProvider;

    /// <summary>
    /// Gets the render settings the host was created with.
    /// </summary>
    internal RenderSettings Settings => _settings;

    /// <summary>
    /// Gets or sets the window surface snapshot the backend's provider returns.
    /// </summary>
    /// <remarks>
    /// The initial value is the platform window's size, focus, minimization state, and cached HDR
    /// state, unless the test supplied an HDR override. Tests that resize the window call
    /// <see cref="SyncWithWindow"/> after the platform applied the change.
    /// </remarks>
    internal WindowSurfaceState State {
        get => _state;
        set => _state = value;
    }

    /// <summary>
    /// Creates the host for a GPU test, skipping the test when the host is environmentally
    /// unavailable.
    /// </summary>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <param name="presentMode">The requested present mode.</param>
    /// <param name="hdr">The HDR state override, or <see langword="null"/> for the platform's state.</param>
    /// <returns>The created and initialized host.</returns>
    internal static BackendTestHost CreateOrSkip(
        int sampleCount = 4,
        PresentMode presentMode = PresentMode.Mailbox,
        WindowHdrState? hdr = null) {
        if (!TryCreate(out BackendTestHost? host, out string skipReason, sampleCount, presentMode, hdr)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    /// <summary>
    /// Tries to create and initialize the host.
    /// </summary>
    /// <param name="host">The created host, or <see langword="null"/> when unavailable.</param>
    /// <param name="skipReason">A specific reason when <paramref name="host"/> is <see langword="null"/>.</param>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <param name="presentMode">The requested present mode.</param>
    /// <param name="hdr">The HDR state override, or <see langword="null"/> for the platform's state.</param>
    /// <returns><see langword="true"/> when the host was created.</returns>
    internal static bool TryCreate(
        out BackendTestHost? host,
        out string skipReason,
        int sampleCount = 4,
        PresentMode presentMode = PresentMode.Mailbox,
        WindowHdrState? hdr = null) {
        host = null;
        skipReason = string.Empty;
        if (!SdlTestHost.TryCreateVulkan(out SdlTestHost? sdlHost, out skipReason)) {
            return false;
        }

        BackendTestHost? candidate = null;
        bool succeeded = false;
        try {
            IReadOnlyList<string> extensions;
            try {
                extensions = sdlHost!.Platform.GetVulkanInstanceExtensions();
            } catch (PlatformException exception) {
                skipReason = $"SDL cannot provide the Vulkan instance extensions: {exception.Message}";
                return false;
            }

            WindowSurfaceState initialState = new(
                sdlHost!.Platform.Window.RenderWidth,
                sdlHost.Platform.Window.RenderHeight,
                sdlHost.Platform.Window.IsMinimized,
                sdlHost.Platform.Window.IsFocused,
                hdr ?? sdlHost.Platform.WindowHdrState);
            RenderSettings settings = CreateSettings(sampleCount, presentMode);
            candidate = new BackendTestHost(sdlHost, settings, extensions, initialState);
            try {
                candidate.Initialize();
            } catch (GraphicsException exception) when (IsEnvironmentalFailure(exception)) {
                skipReason = exception.Message;
                return false;
            }

            host = candidate;
            candidate = null;
            succeeded = true;
            return true;
        } finally {
            if (!succeeded) {
                if (candidate is not null) {
                    candidate.Dispose();
                } else {
                    sdlHost?.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Refreshes the size, focus, and minimization snapshot from the platform window.
    /// </summary>
    /// <remarks>
    /// The HDR state is left unchanged so a test can hold an HDR override while the window resizes.
    /// </remarks>
    internal void SyncWithWindow() {
        _state = _state with {
            RenderWidth = Platform.Window.RenderWidth,
            RenderHeight = Platform.Window.RenderHeight,
            IsMinimized = Platform.Window.IsMinimized,
            IsFocused = Platform.Window.IsFocused,
        };
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _backend.Dispose();
        _teardownQueue.RunPending(NullLogger.Instance);
        _sdlHost.Dispose();
    }

    private void Initialize() {
        RendererBackendOptions options = new() {
            Settings = _settings,
            InstanceExtensions = _instanceExtensions,
            SurfaceProvider = _surfaceProvider,
            WindowStateProvider = GetSurfaceState,
            Logger = NullLogger.Instance,
            TeardownQueue = _teardownQueue,
        };
        _backend.Initialize(options);
    }

    private WindowSurfaceState GetSurfaceState() => _state;

    private static RenderSettings CreateSettings(int sampleCount, PresentMode presentMode) => new() {
        PresentMode = presentMode,
        SampleCount = sampleCount,
        Tonemapping = TonemappingMode.AgX,
        Exposure = 0f,
        ValidationEnabled = true,
    };

    private static bool IsEnvironmentalFailure(GraphicsException exception) =>
        exception.InnerException is DllNotFoundException or SymbolLoadingException or TypeInitializationException or PlatformException
        || exception.Message.StartsWith(VulkanLoader.LOADER_FAILURE_MESSAGE_PREFIX, StringComparison.Ordinal)
        || exception.Message.StartsWith(VulkanDevice.NO_PHYSICAL_DEVICE_MESSAGE_PREFIX, StringComparison.Ordinal)
        || (exception.InnerException is GraphicsException inner && IsEnvironmentalFailure(inner));
}
