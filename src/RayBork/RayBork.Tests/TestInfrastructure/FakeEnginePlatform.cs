using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// A fake <see cref="IEnginePlatform"/> that records lifecycle calls and exposes deterministic hooks
/// for pumping, input capture, and startup failure.
/// </summary>
internal sealed class FakeEnginePlatform : IEnginePlatform {
    private static readonly IntPtr _defaultVulkanSurface = new(0x5EED);

    private readonly List<IntPtr> _createdSurfaceInstances = [];
    private readonly List<IntPtr> _destroyedSurfaces = [];
    private Action? _onExitRequested;

    internal FakeEnginePlatform() {
        FakeWindow = new FakePlatformWindow(RaiseExitRequested);
    }

    public FakePlatformWindow FakeWindow { get; }

    public IPlatformWindow Window => FakeWindow;

    /// <summary>
    /// Gets or sets the cached window HDR state reported by the platform.
    /// </summary>
    public WindowHdrState WindowHdrState { get; set; } = WindowHdrState.Sdr;

    public PlatformOptions? Options { get; private set; }

    public int InitializeCount { get; private set; }

    /// <summary>
    /// Gets whether <see cref="Initialize"/> has been called.
    /// </summary>
    public bool InitializeCalled { get; private set; }

    public int PumpCount { get; private set; }

    public int CaptureInputCount { get; private set; }

    public int DisposeCount { get; private set; }

    public bool IsDisposed => DisposeCount > 0;

    public Exception? InitializeException { get; set; }

    public Exception? CaptureInputException { get; set; }

    public Action? InitializeHook { get; set; }

    public Action<FakeEnginePlatform>? PumpHook { get; set; }

    public List<VibrationCall> VibrationCalls { get; } = [];

    /// <summary>
    /// Gets or sets the instance extensions returned by <see cref="GetVulkanInstanceExtensions"/>.
    /// </summary>
    public IReadOnlyList<string> VulkanInstanceExtensions { get; set; } =
        OperatingSystem.IsWindows()
            ? ["VK_KHR_surface", "VK_KHR_win32_surface"]
            : ["VK_KHR_surface", "VK_KHR_xlib_surface"];

    /// <summary>
    /// Gets or sets the surface handle returned by <see cref="CreateVulkanSurface"/>.
    /// </summary>
    public IntPtr VulkanSurface { get; set; } = _defaultVulkanSurface;

    /// <summary>
    /// Gets the Vulkan instance handles passed to successful <see cref="CreateVulkanSurface"/> calls.
    /// </summary>
    public IReadOnlyList<IntPtr> CreatedSurfaceInstances => _createdSurfaceInstances;

    /// <summary>
    /// Gets the non-zero surface handles passed to <see cref="DestroyVulkanSurface"/> calls.
    /// </summary>
    public IReadOnlyList<IntPtr> DestroyedSurfaces => _destroyedSurfaces;

    /// <summary>
    /// Gets the number of <see cref="CreateVulkanSurface"/> calls, including calls that threw.
    /// </summary>
    public int CreateSurfaceCallCount { get; private set; }

    /// <summary>
    /// Gets the managed thread id of the last <see cref="CreateVulkanSurface"/> call.
    /// </summary>
    public int? CreateSurfaceThreadId { get; private set; }

    /// <summary>
    /// Gets the managed thread id of the last <see cref="DestroyVulkanSurface"/> call.
    /// </summary>
    public int? DestroySurfaceThreadId { get; private set; }

    /// <summary>
    /// Gets or sets the exception that <see cref="CreateVulkanSurface"/> throws instead of returning
    /// a surface handle.
    /// </summary>
    public Exception? SurfaceCreationException { get; set; }

    public void Initialize(PlatformOptions options, Action onExitRequested) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onExitRequested);

        InitializeCount++;
        InitializeCalled = true;
        Options = options;
        _onExitRequested = onExitRequested;
        FakeWindow.ApplyStartupOptions(options);
        InitializeHook?.Invoke();

        if (InitializeException is not null) {
            throw InitializeException;
        }
    }

    /// <summary>
    /// Returns <see cref="VulkanInstanceExtensions"/> when the platform is initialized.
    /// </summary>
    /// <exception cref="PlatformException">The platform is not initialized or has been disposed.</exception>
    public IReadOnlyList<string> GetVulkanInstanceExtensions() {
        if (IsDisposed || !InitializeCalled) {
            throw new PlatformException("The platform has not been initialized.");
        }

        return VulkanInstanceExtensions;
    }

    /// <summary>
    /// Records the call and returns <see cref="VulkanSurface"/>, or throws
    /// <see cref="SurfaceCreationException"/> when one is configured.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The platform has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The platform is not initialized.</exception>
    public IntPtr CreateVulkanSurface(IntPtr instance) {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!InitializeCalled) {
            throw new InvalidOperationException("The platform has not been initialized.");
        }

        CreateSurfaceCallCount++;
        CreateSurfaceThreadId = Environment.CurrentManagedThreadId;
        if (SurfaceCreationException is not null) {
            throw SurfaceCreationException;
        }

        _createdSurfaceInstances.Add(instance);
        return VulkanSurface;
    }

    /// <summary>
    /// Records a non-zero surface handle as destroyed.
    /// </summary>
    public void DestroyVulkanSurface(IntPtr instance, IntPtr surface) {
        if (surface == IntPtr.Zero) {
            return;
        }

        DestroySurfaceThreadId = Environment.CurrentManagedThreadId;
        _destroyedSurfaces.Add(surface);
    }

    public void Pump(TimeSpan maxWait) {
        PumpCount++;
        PumpHook?.Invoke(this);
        FakeWindow.ApplyPendingCommands();
    }

    public IInput CaptureInput() {
        CaptureInputCount++;
        if (CaptureInputException is not null) {
            throw CaptureInputException;
        }

        return new FakeInput();
    }

    public void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration) {
        VibrationCalls.Add(new VibrationCall(instanceId, lowFrequency, highFrequency, duration, Stop: false));
    }

    public void StopGamepadVibration(int instanceId) {
        VibrationCalls.Add(new VibrationCall(instanceId, 0f, 0f, TimeSpan.Zero, Stop: true));
    }

    public void Dispose() => DisposeCount++;

    internal void RaiseExitRequested() => _onExitRequested?.Invoke();

    internal readonly record struct VibrationCall(
        int InstanceId,
        float LowFrequency,
        float HighFrequency,
        TimeSpan Duration,
        bool Stop);
}
