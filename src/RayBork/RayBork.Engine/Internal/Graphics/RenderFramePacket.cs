namespace RayBork.Internal;

/// <summary>A sealed frame handed from the tick thread to the renderer.</summary>
/// <remarks>
/// The tick thread records camera passes into the packet owned by its ring slot, seals it, and
/// publishes it; the slot reuses the packet and its pooled buffers after the render thread releases it.
/// A packet holds value snapshots and the frame's <see cref="Retention"/> set, never a live game object.
/// </remarks>
internal sealed class RenderFramePacket
{
    private CameraPass[] _passes = new CameraPass[2];
    private RenderDrawRecord[] _recordPool = new RenderDrawRecord[8];
    private int _passCount;
    private int _recordCount;
    private bool _sealed;

    /// <summary>Initializes an empty packet for one frame index.</summary>
    /// <remarks>The frame ring reuses its slot packets with <see cref="Reset"/>.</remarks>
    internal RenderFramePacket(long frameIndex)
    {
        for (int index = 0; index < _passes.Length; index++)
        {
            _passes[index] = new CameraPass();
        }

        for (int index = 0; index < _recordPool.Length; index++)
        {
            _recordPool[index] = new RenderDrawRecord();
        }

        FrameIndex = frameIndex;
        Retention = new FrameAssetRetention();
    }

    /// <summary>Gets the tick frame index assigned by the frame ring.</summary>
    internal long FrameIndex { get; private set; }

    /// <summary>Gets the absolute tick time in seconds.</summary>
    internal double Time { get; private set; }

    /// <summary>Gets the clamped tick delta in seconds.</summary>
    internal double DeltaTime { get; private set; }

    /// <summary>
    /// Gets the engine-asset retention set, which the ring releases when the frame is released,
    /// superseded, or abandoned.
    /// </summary>
    internal FrameAssetRetention Retention { get; private set; }

    /// <summary>Gets or sets the frame's tone-mapping snapshot, captured before sealing.</summary>
    /// <remarks>
    /// The tick thread copies the live settings here; the render thread reads only this value.
    /// </remarks>
    internal TonemappingSnapshot Tonemapping { get; set; } = TonemappingSnapshot.Default;

    /// <summary>Gets the camera passes in creation order.</summary>
    /// <remarks>The packet owns these pooled pass records and reuses them on later frames.</remarks>
    internal ReadOnlySpan<CameraPass> Passes => _passes.AsSpan(0, _passCount);

    /// <summary>Gets the number of camera passes recorded into the frame.</summary>
    internal int PassCount => _passCount;

    /// <summary>Gets whether the packet rejects new recording.</summary>
    internal bool IsSealed => _sealed;

    /// <summary>Resets the packet for a new recording phase and assigns the frame index.</summary>
    /// <remarks>
    /// The ring calls it only while the slot is free; the previous retention set is already released.
    /// </remarks>
    internal void Reset(long frameIndex)
    {
        for (int index = 0; index < _passCount; index++)
        {
            _passes[index].Reset();
        }

        for (int index = 0; index < _recordCount; index++)
        {
            _recordPool[index].Reset();
        }

        FrameIndex = frameIndex;
        Time = 0.0;
        DeltaTime = 0.0;
        Tonemapping = TonemappingSnapshot.Default;
        _passCount = 0;
        _recordCount = 0;
        _sealed = false;
        Retention = new FrameAssetRetention();
    }

    /// <summary>Starts a frame's recording phase with the tick's time snapshot.</summary>
    internal void BeginRecording(double time, double deltaTime)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "The frame packet is sealed; recording cannot start."
            );
        }

        Time = time;
        DeltaTime = deltaTime;
    }

    /// <summary>Appends a camera pass in creation order and returns it.</summary>
    internal CameraPass BeginPass()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "The frame packet is sealed; a pass cannot be recorded."
            );
        }

        if (_passCount == _passes.Length)
        {
            int previousLength = _passes.Length;
            Array.Resize(ref _passes, checked(_passes.Length * 2));
            for (int index = previousLength; index < _passes.Length; index++)
            {
                _passes[index] = new CameraPass();
            }
        }

        CameraPass pass = _passes[_passCount];
        pass.Reset();
        _passCount++;
        return pass;
    }

    /// <summary>Rents one pooled draw record for the current recording phase.</summary>
    internal RenderDrawRecord RentDrawRecord()
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                "The frame packet is sealed; a draw cannot be recorded."
            );
        }

        if (_recordCount == _recordPool.Length)
        {
            int previousLength = _recordPool.Length;
            Array.Resize(ref _recordPool, checked(_recordPool.Length * 2));
            for (int index = previousLength; index < _recordPool.Length; index++)
            {
                _recordPool[index] = new RenderDrawRecord();
            }
        }

        return _recordPool[_recordCount++];
    }

    /// <summary>Seals every active pass and stops recording; idempotent.</summary>
    internal void Seal()
    {
        for (int index = 0; index < _passCount; index++)
        {
            _passes[index].Seal();
        }

        _sealed = true;
    }
}
