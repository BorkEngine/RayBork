namespace RayBork.Internal;

/// <summary>The platform host seam implemented by the SDL3 backend.</summary>
/// <remarks>
/// <see cref="Initialize"/>, <see cref="Pump"/>, and Vulkan surface calls run on the main
/// thread. <see cref="CaptureInput"/> and vibration run on the tick thread; vibration queues work
/// to the main thread, and the app marshals render-thread Vulkan requests.
/// </remarks>
internal interface IEnginePlatform : IDisposable
{
    /// <summary>Gets the platform window.</summary>
    IPlatformWindow Window { get; }

    /// <summary>Gets an any-thread snapshot of the SDL3 window HDR state.</summary>
    /// <remarks>
    /// Refreshed on the main thread after initialization, HDR changes, and display changes. Returns
    /// SDR before initialization, after disposal, and when SDL reports no valid properties.
    /// </remarks>
    WindowHdrState WindowHdrState { get; }

    /// <summary>Initializes the platform, creates the window, and sets the exit callback.</summary>
    /// <param name="options">The startup options.</param>
    /// <param name="onExitRequested">Invoked on a platform or window exit request.</param>
    void Initialize(PlatformOptions options, Action onExitRequested);

    /// <summary>Gets the de-duplicated, sorted Vulkan instance extensions SDL requires.</summary>
    IReadOnlyList<string> GetVulkanInstanceExtensions();

    /// <summary>Creates the native Vulkan surface; the caller owns the handle.</summary>
    IntPtr CreateVulkanSurface(IntPtr instance);

    /// <summary>Destroys a created Vulkan surface; zero or unknown handles are ignored.</summary>
    void DestroyVulkanSurface(IntPtr instance, IntPtr surface);

    /// <summary>Pumps platform events and applies queued window commands.</summary>
    void Pump(TimeSpan maxWait);

    /// <summary>Captures the input snapshot for the next tick.</summary>
    IInput CaptureInput();

    /// <summary>Queues gamepad vibration with <c>[0, 1]</c> motor intensities.</summary>
    void SetGamepadVibration(
        int instanceId,
        float lowFrequency,
        float highFrequency,
        TimeSpan duration
    );

    /// <summary>Queues a stop of gamepad vibration.</summary>
    void StopGamepadVibration(int instanceId);
}
