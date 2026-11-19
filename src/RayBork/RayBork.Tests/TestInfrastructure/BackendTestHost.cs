using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using Silk.NET.Core.Loader;

namespace RayBork.Tests;

/// <summary>
/// Owns one SDL Vulkan window, a direct surface provider, and an initialized
/// <see cref="Vulkan12Backend"/> for one GPU test.
/// </summary>
/// <remarks>
/// <see cref="TryCreate"/> skips the test when the environment lacks a Vulkan surface and
/// throws on programming errors. <see cref="SdlPlatform"/> serializes process-global SDL setup.
/// </remarks>
internal sealed class BackendTestHost : IDisposable
{
    private const int SERVICE_TIMEOUT_MILLISECONDS = 30_000;

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
        WindowSurfaceState initialState
    )
    {
        _sdlHost = sdlHost;
        _settings = settings;
        _instanceExtensions = instanceExtensions;
        _state = initialState;
        _surfaceProvider = new DirectSurfaceProvider(sdlHost.Platform);
        _backend = new Vulkan12Backend();
    }

    /// <summary>Gets the SDL host that owns the Vulkan window.</summary>
    internal SdlTestHost SdlHost => _sdlHost;

    /// <summary>Gets the SDL platform used to pump events and queue window commands.</summary>
    internal SdlPlatform Platform => _sdlHost.Platform;

    /// <summary>Gets the initialized renderer backend.</summary>
    internal Vulkan12Backend Backend => _backend;

    /// <summary>Gets the backend's Vulkan instance for validation checks.</summary>
    internal VulkanInstance Instance => _backend.Instance;

    /// <summary>Gets the backend's logical device for readback probes.</summary>
    internal VulkanDevice Device => _backend.Device;

    /// <summary>Gets the backend's current scene target for readback probes.</summary>
    internal VulkanRenderTarget SceneTarget => _backend.SceneTarget;

    /// <summary>Gets the request service the host services on its render thread.</summary>
    internal RenderTextureService RenderTextures { get; } = new();

    /// <summary>Creates a render texture through the real service handoff.</summary>
    internal RenderTexture CreateRenderTexture(RenderTextureDescription description)
    {
        Task<RenderTexture> request = RenderTextures.CreateAsync(
            description,
            CancellationToken.None
        );
        _backend.ProcessRenderTextureRequests(RenderTextures);
        return request.GetAwaiter().GetResult();
    }

    /// <summary>Services one pending request or destruction round.</summary>
    internal void ProcessRenderTextures() => _backend.ProcessRenderTextureRequests(RenderTextures);

    /// <summary>Queues a color readback and services it on this thread as the render thread.</summary>
    internal Task<Color[,]> ReadColorsAsync(RenderTexture target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Task<Color[,]> request = target.ReadColorsAsync();
        ProcessRenderTextures();
        return request;
    }

    /// <summary>Queues a display-referred pixel readback and services it on this thread.</summary>
    internal Task<byte[]> ReadPixelsAsync(RenderTexture target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Task<byte[]> request = target.ReadPixelsAsync();
        ProcessRenderTextures();
        return request;
    }

    /// <summary>Services the queue while another thread runs the blocking resize.</summary>
    /// <exception cref="TimeoutException">The resize neither queued nor completed in time.</exception>
    internal void ResizeRenderTexture(RenderTexture target, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Drain earlier queued work and its wakeup so the signal below belongs to this resize.
        ProcessRenderTextures();
        while (RenderTextures.Signal.WaitOne(0))
        {
            // A create or destroy already woke the loop; the resize needs its own wakeup.
        }

        Exception? failure = null;
        using ManualResetEventSlim completed = new(false);
        Thread tick = new(() =>
        {
            try
            {
                target.Resize(width, height);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                try
                {
                    completed.Set();
                }
                catch (ObjectDisposedException)
                {
                    // A timeout on this thread disposes the event while a blocked resize is still unwinding.
                }
            }
        })
        {
            IsBackground = true,
            Name = "RayBork.Test.RenderTextureResize",
        };
        tick.Start();

        int signaled = WaitHandle.WaitAny(
            [RenderTextures.Signal, completed.WaitHandle],
            SERVICE_TIMEOUT_MILLISECONDS
        );
        if (signaled == WaitHandle.WaitTimeout)
        {
            throw new TimeoutException(
                $"The resize of render texture {target.TargetId} neither queued nor completed within "
                    + $"{SERVICE_TIMEOUT_MILLISECONDS} ms."
            );
        }

        if (signaled == 0)
        {
            ProcessRenderTextures();
        }

        if (!tick.Join(SERVICE_TIMEOUT_MILLISECONDS))
        {
            throw new TimeoutException(
                $"The blocking resize of render texture {target.TargetId} did not return within "
                    + $"{SERVICE_TIMEOUT_MILLISECONDS} ms."
            );
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Gets the provider that creates and destroys the native surface.</summary>
    internal DirectSurfaceProvider SurfaceProvider => _surfaceProvider;

    /// <summary>Gets the render settings the host was created with.</summary>
    internal RenderSettings Settings => _settings;

    /// <summary>Gets or sets the window surface snapshot the backend's provider returns.</summary>
    internal WindowSurfaceState State
    {
        get => _state;
        set => _state = value;
    }

    /// <summary>Creates the host for a GPU test, skipping when unavailable.</summary>
    internal static BackendTestHost CreateOrSkip(
        int sampleCount = 4,
        PresentMode presentMode = PresentMode.Mailbox,
        WindowHdrState? hdr = null,
        TonemappingMode tonemapping = TonemappingMode.AgX
    )
    {
        if (
            !TryCreate(
                out BackendTestHost? host,
                out string skipReason,
                sampleCount,
                presentMode,
                hdr,
                tonemapping
            )
        )
        {
            Assert.Skip(skipReason);
        }

        return host!;
    }

    /// <summary>Tries to create and initialize the host, reporting why it was skipped.</summary>
    internal static bool TryCreate(
        out BackendTestHost? host,
        out string skipReason,
        int sampleCount = 4,
        PresentMode presentMode = PresentMode.Mailbox,
        WindowHdrState? hdr = null,
        TonemappingMode tonemapping = TonemappingMode.AgX
    )
    {
        host = null;
        skipReason = string.Empty;
        if (
            !SdlTestHost.TryCreateVulkan(
                out SdlTestHost? sdlHost,
                out skipReason,
                minimalTestMode: true
            )
        )
        {
            return false;
        }

        BackendTestHost? candidate = null;
        bool succeeded = false;
        try
        {
            IReadOnlyList<string> extensions;
            try
            {
                extensions = sdlHost!.Platform.GetVulkanInstanceExtensions();
            }
            catch (PlatformException exception)
            {
                skipReason =
                    $"SDL cannot provide the Vulkan instance extensions: {exception.Message}";
                return false;
            }

            WindowSurfaceState initialState = new(
                sdlHost!.Platform.Window.RenderWidth,
                sdlHost.Platform.Window.RenderHeight,
                sdlHost.Platform.Window.IsMinimized,
                sdlHost.Platform.Window.IsFocused,
                hdr ?? sdlHost.Platform.WindowHdrState
            );
            RenderSettings settings = CreateSettings(sampleCount, presentMode, tonemapping);
            candidate = new BackendTestHost(sdlHost, settings, extensions, initialState);
            try
            {
                candidate.Initialize();
            }
            catch (GraphicsException exception) when (IsEnvironmentalFailure(exception))
            {
                skipReason = exception.Message;
                return false;
            }

            host = candidate;
            candidate = null;
            succeeded = true;
            return true;
        }
        finally
        {
            if (!succeeded)
            {
                if (candidate is not null)
                {
                    candidate.Dispose();
                }
                else
                {
                    sdlHost?.Dispose();
                }
            }
        }
    }

    /// <summary>Refreshes the platform size, focus, and minimization snapshot.</summary>
    internal void SyncWithWindow()
    {
        _state = _state with
        {
            RenderWidth = Platform.Window.RenderWidth,
            RenderHeight = Platform.Window.RenderHeight,
            IsMinimized = Platform.Window.IsMinimized,
            IsFocused = Platform.Window.IsFocused,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RenderTextures.Close(null);
        _backend.Dispose();
        RenderTextures.Dispose();
        _teardownQueue.RunPending(NullLogger.Instance);
        _sdlHost.Dispose();
    }

    private void Initialize()
    {
        RendererBackendOptions options = new()
        {
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

    private static RenderSettings CreateSettings(
        int sampleCount,
        PresentMode presentMode,
        TonemappingMode tonemapping
    )
    {
        return new RenderSettings
        {
            PresentMode = presentMode,
            SampleCount = sampleCount,
            Tonemapping = tonemapping,
            Exposure = 0f,
            ValidationEnabled = true,
        };
    }

    private static bool IsEnvironmentalFailure(GraphicsException exception) =>
        exception.InnerException
            is DllNotFoundException
                or SymbolLoadingException
                or TypeInitializationException
                or PlatformException
        || exception.Message.StartsWith(
            VulkanLoader.LOADER_FAILURE_MESSAGE_PREFIX,
            StringComparison.Ordinal
        )
        || exception.Message.StartsWith(
            VulkanDevice.NO_PHYSICAL_DEVICE_MESSAGE_PREFIX,
            StringComparison.Ordinal
        )
        || (exception.InnerException is GraphicsException inner && IsEnvironmentalFailure(inner));
}
