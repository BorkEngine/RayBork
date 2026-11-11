namespace RayBork.Internal;

/// <summary>
/// The platform host seam implemented by the SDL3 backend.
/// </summary>
/// <remarks>
/// <see cref="Initialize"/> and <see cref="Pump"/> run on the process main thread.
/// <see cref="CaptureInput"/>, <see cref="SetGamepadVibration"/>, and
/// <see cref="StopGamepadVibration"/> run on the tick thread; the vibration calls queue work to the
/// main thread rather than touching the device directly.
/// </remarks>
internal interface IEnginePlatform : IDisposable {
    /// <summary>
    /// Gets the platform window.
    /// </summary>
    IPlatformWindow Window { get; }

    /// <summary>
    /// Initializes the platform and creates the window. Runs on the main thread.
    /// </summary>
    /// <param name="options">The startup options.</param>
    /// <param name="onExitRequested">Invoked when the platform or window requests a graceful exit.</param>
    void Initialize(PlatformOptions options, Action onExitRequested);

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
