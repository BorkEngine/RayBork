namespace RayBork.Internal;

/// <summary>
/// A sealed frame handed from the tick thread to the renderer.
/// </summary>
/// <remarks>
/// <para>
/// The tick thread records camera passes into a packet owned by its frame-ring slot, seals it, and
/// publishes it. The render thread consumes the packet and then releases the slot; the ring reuses the
/// same packet, pass records, and pooled draw buffers for a later frame. A packet never references a
/// live game object: it carries value snapshots of the camera matrices, clear state, viewport, and the
/// draw records, plus engine-asset references held by <see cref="Retention"/> until the frame ends.
/// </para>
/// <para>
/// <see cref="BeginRecording"/>, <see cref="BeginPass"/>, and <see cref="RentDrawRecord"/> are
/// tick-thread only and legal only while the owning ring slot is recording. Read members are safe on
/// the render thread once the packet is sealed. The per-slot buffers mean the tick thread cannot
/// mutate a packet that is ready or rendering: the only writer is the slot's recording phase, which
/// ends before the packet is published. <see cref="Reset"/> runs only after the render thread released
/// the slot, so no consumer can observe the reset.
/// </para>
/// <para>
/// The <see cref="RenderFramePacket(long)"/> constructor creates an empty packet for the renderer
/// backend and the GPU tests; a packet produced by the frame ring has its frame time and passes
/// populated by the tick runner and the camera recorder.
/// </para>
/// </remarks>
internal sealed class RenderFramePacket {
    private CameraPass[] _passes = new CameraPass[2];
    private RenderDrawRecord[] _recordPool = new RenderDrawRecord[8];
    private int _passCount;
    private int _recordCount;
    private bool _sealed;

    /// <summary>
    /// Initializes an empty packet for one frame index. The frame ring reuses its slot packets with
    /// <see cref="Reset"/> instead.
    /// </summary>
    /// <param name="frameIndex">The tick frame index assigned by the frame ring.</param>
    internal RenderFramePacket(long frameIndex) {
        for (int index = 0; index < _passes.Length; index++) {
            _passes[index] = new CameraPass();
        }

        for (int index = 0; index < _recordPool.Length; index++) {
            _recordPool[index] = new RenderDrawRecord();
        }

        FrameIndex = frameIndex;
        Retention = new FrameAssetRetention();
    }

    /// <summary>Gets the tick frame index assigned by the frame ring.</summary>
    internal long FrameIndex { get; private set; }

    /// <summary>Gets the absolute tick time in seconds, as captured from <see cref="EngineContext"/>.</summary>
    internal double Time { get; private set; }

    /// <summary>Gets the clamped tick delta time in seconds, as captured from <see cref="EngineContext"/>.</summary>
    internal double DeltaTime { get; private set; }

    /// <summary>
    /// Gets the engine-asset retention set for this frame. The tick runner attaches it to the recording
    /// lease; the frame ring releases it when the frame is released, superseded, or abandoned.
    /// </summary>
    internal FrameAssetRetention Retention { get; private set; }

    /// <summary>Gets the camera passes in creation order.</summary>
    internal ReadOnlySpan<CameraPass> Passes => _passes.AsSpan(0, _passCount);

    /// <summary>Gets the number of camera passes recorded into the frame.</summary>
    internal int PassCount => _passCount;

    /// <summary>Gets whether the packet rejects new recording.</summary>
    internal bool IsSealed => _sealed;

    /// <summary>
    /// Resets the packet for a new recording phase and assigns the frame index. The ring calls it only
    /// when the slot is free, after the previous frame was released or abandoned; the previous retention
    /// set was already released by the ring and is replaced with a fresh set.
    /// </summary>
    /// <param name="frameIndex">The tick frame index assigned by the frame ring.</param>
    internal void Reset(long frameIndex) {
        for (int index = 0; index < _passCount; index++) {
            _passes[index].Reset();
        }

        for (int index = 0; index < _recordCount; index++) {
            _recordPool[index].Reset();
        }

        FrameIndex = frameIndex;
        Time = 0.0;
        DeltaTime = 0.0;
        _passCount = 0;
        _recordCount = 0;
        _sealed = false;
        Retention = new FrameAssetRetention();
    }

    /// <summary>
    /// Starts a frame's recording phase with the tick's time snapshot.
    /// </summary>
    /// <param name="time">Absolute seconds since the app started.</param>
    /// <param name="deltaTime">Clamped seconds since the previous tick.</param>
    /// <exception cref="InvalidOperationException">The packet is sealed.</exception>
    internal void BeginRecording(double time, double deltaTime) {
        if (_sealed) {
            throw new InvalidOperationException("The frame packet is sealed; recording cannot start.");
        }

        Time = time;
        DeltaTime = deltaTime;
    }

    /// <summary>
    /// Creates a camera pass snapshot and appends it to the frame in creation order.
    /// </summary>
    /// <returns>The pass to populate and record draws into.</returns>
    /// <exception cref="InvalidOperationException">The packet is sealed.</exception>
    internal CameraPass BeginPass() {
        if (_sealed) {
            throw new InvalidOperationException("The frame packet is sealed; a pass cannot be recorded.");
        }

        if (_passCount == _passes.Length) {
            int previousLength = _passes.Length;
            Array.Resize(ref _passes, checked(_passes.Length * 2));
            for (int index = previousLength; index < _passes.Length; index++) {
                _passes[index] = new CameraPass();
            }
        }

        CameraPass pass = _passes[_passCount];
        pass.Reset();
        _passCount++;
        return pass;
    }

    /// <summary>
    /// Rents one pooled draw record for the current recording phase.
    /// </summary>
    /// <returns>The record to populate.</returns>
    /// <exception cref="InvalidOperationException">The packet is sealed.</exception>
    internal RenderDrawRecord RentDrawRecord() {
        if (_sealed) {
            throw new InvalidOperationException("The frame packet is sealed; a draw cannot be recorded.");
        }

        if (_recordCount == _recordPool.Length) {
            int previousLength = _recordPool.Length;
            Array.Resize(ref _recordPool, checked(_recordPool.Length * 2));
            for (int index = previousLength; index < _recordPool.Length; index++) {
                _recordPool[index] = new RenderDrawRecord();
            }
        }

        return _recordPool[_recordCount++];
    }

    /// <summary>
    /// Stops recording and seals every active pass. Idempotent.
    /// </summary>
    internal void Seal() {
        for (int index = 0; index < _passCount; index++) {
            _passes[index].Seal();
        }

        _sealed = true;
    }
}
