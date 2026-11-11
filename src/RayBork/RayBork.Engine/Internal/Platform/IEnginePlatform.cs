namespace RayBork.Internal;

/// <summary>
/// The platform host seam implemented by the SDL3 backend.
/// </summary>
/// <remarks>
/// <see cref="Initialize"/>, <see cref="Pump"/>, and the Vulkan surface operations run on the
/// process main thread. <see cref="CaptureInput"/>, <see cref="SetGamepadVibration"/>, and
/// <see cref="StopGamepadVibration"/> run on the tick thread; the vibration calls queue work to the
/// main thread rather than touching the device directly. The platform never blocks on the render
/// thread: the app marshals a render-thread Vulkan request to the main thread before calling the
/// surface operations.
/// </remarks>
internal interface IEnginePlatform : IDisposable {
    /// <summary>
    /// Gets the platform window.
    /// </summary>
    IPlatformWindow Window { get; }

    /// <summary>
    /// Gets a cached snapshot of the SDL3 window HDR state.
    /// </summary>
    /// <remarks>
    /// Safe to read from any thread. The cache is refreshed on the main thread after
    /// <see cref="Initialize"/> and when SDL reports a window HDR state change or a display change.
    /// Returns <c>WindowHdrState.Sdr</c> before initialization, after disposal, and when SDL cannot
    /// report a valid property set. Never throws.
    /// </remarks>
    WindowHdrState WindowHdrState { get; }

    /// <summary>
    /// Initializes the platform and creates the window. Runs on the main thread.
    /// </summary>
    /// <param name="options">The startup options.</param>
    /// <param name="onExitRequested">Invoked when the platform or window requests a graceful exit.</param>
    void Initialize(PlatformOptions options, Action onExitRequested);

    /// <summary>
    /// Gets the Vulkan instance extensions that SDL requires to create a surface for this platform's
    /// window.
    /// </summary>
    /// <remarks>
    /// Main-thread only, called between <see cref="Initialize"/> and <see cref="IDisposable.Dispose"/>. The M2
    /// app marshals the render thread's instance request to the main thread; the platform never
    /// blocks on the render thread.
    /// </remarks>
    /// <returns>
    /// The de-duplicated, ordinal-sorted instance extension names; the result always contains
    /// <c>VK_KHR_surface</c>.
    /// </returns>
    /// <exception cref="PlatformException">
    /// The platform is not initialized, the window was not created for Vulkan, or SDL returned no
    /// usable extensions.
    /// </exception>
    IReadOnlyList<string> GetVulkanInstanceExtensions();

    /// <summary>
    /// Creates the native Vulkan surface for the platform window.
    /// </summary>
    /// <remarks>
    /// Main-thread only, called between <see cref="Initialize"/> and <see cref="IDisposable.Dispose"/>. The M2
    /// app marshals the render thread's surface request to the main thread; the platform never
    /// blocks on the render thread. The caller owns the returned handle and destroys it with
    /// <see cref="DestroyVulkanSurface"/> before the platform is disposed.
    /// </remarks>
    /// <param name="instance">The Vulkan instance created with the extensions from <see cref="GetVulkanInstanceExtensions"/>.</param>
    /// <returns>The non-zero native surface handle.</returns>
    /// <exception cref="InvalidOperationException">
    /// The platform is not initialized or the window was not created for Vulkan.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The platform has been disposed.</exception>
    /// <exception cref="PlatformException">
    /// SDL failed to load the Vulkan loader or to create the surface.
    /// </exception>
    IntPtr CreateVulkanSurface(IntPtr instance);

    /// <summary>
    /// Destroys a native Vulkan surface created by <see cref="CreateVulkanSurface"/>.
    /// </summary>
    /// <remarks>
    /// Main-thread only, called between <see cref="Initialize"/> and <see cref="IDisposable.Dispose"/>. The M2
    /// app marshals the render thread's destroy request to the main thread; the platform never
    /// blocks on the render thread. A zero handle or a handle the platform did not create is
    /// ignored, so the call is safe to repeat and safe during shutdown before the window is
    /// destroyed.
    /// </remarks>
    /// <param name="instance">The Vulkan instance that created the surface.</param>
    /// <param name="surface">The native surface handle; <see cref="IntPtr.Zero"/> is a no-op.</param>
    void DestroyVulkanSurface(IntPtr instance, IntPtr surface);

    /// <summary>
    /// Pumps platform events and applies queued window commands. Runs on the main thread.
    /// </summary>
    /// <param name="maxWait">The maximum time to wait for an event before returning.</param>
    void Pump(TimeSpan maxWait);

    /// <summary>
    /// Captures the input snapshot for the next tick. Runs on the tick thread.
    /// </summary>
    /// <returns>The captured input snapshot.</returns>
    IInput CaptureInput();

    /// <summary>
    /// Queues gamepad vibration. Runs on the tick thread and is applied on the main thread.
    /// </summary>
    /// <param name="instanceId">The stable device instance id.</param>
    /// <param name="lowFrequency">The low-frequency motor intensity in the range <c>[0, 1]</c>.</param>
    /// <param name="highFrequency">The high-frequency motor intensity in the range <c>[0, 1]</c>.</param>
    /// <param name="duration">The vibration duration.</param>
    void SetGamepadVibration(int instanceId, float lowFrequency, float highFrequency, TimeSpan duration);

    /// <summary>
    /// Queues a stop of gamepad vibration. Runs on the tick thread and is applied on the main thread.
    /// </summary>
    /// <param name="instanceId">The stable device instance id.</param>
    void StopGamepadVibration(int instanceId);
}
