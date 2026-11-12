using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using Silk.NET.Core.Loader;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL Vulkan window, a direct surface provider, and an initialized
/// <see cref="Vulkan12Backend"/> with a chosen <see cref="TonemappingMode"/>, for the HDR present
/// probes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BackendTestHost"/> fixes AgX in its render settings. The HDR present probes also cover
/// <see cref="TonemappingMode.None"/> as the direct HDR authoring path, so this host mirrors the same
/// initialization, skip policy, and teardown order with a configurable tone-mapping mode.
/// </para>
/// <para>
/// Every GPU test that uses this host joins the serialized <c>SDL</c> collection because SDL's video
/// driver hint and Vulkan initialization are process global. The host exposes the backend, device,
/// and instance as diagnostics, not renderer seam members.
/// </para>
/// </remarks>
internal sealed class HdrPresentTestHost : IDisposable {
    private readonly SdlTestHost _sdlHost;
    private readonly RenderSettings _settings;
    private readonly IReadOnlyList<string> _instanceExtensions;
    private readonly DirectSurfaceProvider _surfaceProvider;
    private readonly Vulkan12Backend _backend;
    private readonly MainThreadTeardownQueue _teardownQueue = new();

    private readonly WindowSurfaceState _state;
    private bool _disposed;

    private HdrPresentTestHost(
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

    /// <summary>Gets the initialized renderer backend.</summary>
    internal Vulkan12Backend Backend => _backend;

    /// <summary>Gets the backend's Vulkan device for the presented-pixel probe.</summary>
    internal VulkanDevice Device => _backend.Device;

    /// <summary>Gets the backend's Vulkan instance for validation checks.</summary>
    internal VulkanInstance Instance => _backend.Instance;

    /// <summary>Gets the render settings the host was created with.</summary>
    internal RenderSettings Settings => _settings;

    /// <summary>
    /// Creates the host for a GPU test, skipping the test when the host is environmentally
    /// unavailable.
    /// </summary>
    /// <param name="tonemapping">The tone mapping the renderer applies.</param>
    /// <param name="hdr">The window HDR state override.</param>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <returns>The created and initialized host.</returns>
    internal static HdrPresentTestHost CreateOrSkip(
        TonemappingMode tonemapping,
        WindowHdrState hdr,
        int sampleCount = 4) {
        if (!TryCreate(out HdrPresentTestHost? host, out string skipReason, tonemapping, hdr, sampleCount)) {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    /// <summary>
    /// Tries to create and initialize the host.
    /// </summary>
    /// <param name="host">The created host, or <see langword="null"/> when unavailable.</param>
    /// <param name="skipReason">A specific reason when <paramref name="host"/> is <see langword="null"/>.</param>
    /// <param name="tonemapping">The tone mapping the renderer applies.</param>
    /// <param name="hdr">The window HDR state override.</param>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <returns><see langword="true"/> when the host was created.</returns>
    internal static bool TryCreate(
        out HdrPresentTestHost? host,
        out string skipReason,
        TonemappingMode tonemapping,
        WindowHdrState hdr,
        int sampleCount = 4) {
        host = null;
        skipReason = string.Empty;
        if (!SdlTestHost.TryCreateVulkan(out SdlTestHost? sdlHost, out skipReason)) {
            return false;
        }

        HdrPresentTestHost? candidate = null;
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
                hdr);
            RenderSettings settings = new() {
                PresentMode = PresentMode.Mailbox,
                SampleCount = sampleCount,
                Tonemapping = tonemapping,
                Exposure = 0f,
                ValidationEnabled = true,
            };
            candidate = new HdrPresentTestHost(sdlHost, settings, extensions, initialState);
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
            WindowStateProvider = () => _state,
            Logger = NullLogger.Instance,
            TeardownQueue = _teardownQueue,
        };
        _backend.Initialize(options);
    }

    private static bool IsEnvironmentalFailure(GraphicsException exception) =>
        exception.InnerException is DllNotFoundException or SymbolLoadingException or TypeInitializationException or PlatformException
        || exception.Message.StartsWith(VulkanLoader.LOADER_FAILURE_MESSAGE_PREFIX, StringComparison.Ordinal)
        || exception.Message.StartsWith(VulkanDevice.NO_PHYSICAL_DEVICE_MESSAGE_PREFIX, StringComparison.Ordinal)
        || (exception.InnerException is GraphicsException inner && IsEnvironmentalFailure(inner));
}
