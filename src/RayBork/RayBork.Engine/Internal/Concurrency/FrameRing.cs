namespace RayBork.Internal;

/// <summary>A three-slot frame handoff ring for one tick producer and one render consumer.</summary>
/// <remarks>
/// Publishing supersedes an older ready frame instead of queueing it, and the producer blocks when no
/// slot is free, which bounds run-ahead. A slot owns one reusable <see cref="RenderFramePacket"/> and at
/// most one <see cref="FrameAssetRetention"/> set, released exactly once after the ring lock; state is
/// guarded by a <see cref="Lock"/>.
/// </remarks>
internal sealed class FrameRing
{
    private const int SLOT_COUNT = 3;

    private readonly Lock _gate = new();
    private readonly Slot[] _slots = new Slot[SLOT_COUNT];
    private readonly RenderFramePacket[] _packets = new RenderFramePacket[SLOT_COUNT];
    private readonly AutoResetEvent _writableAvailable = new(false);
    private readonly AutoResetEvent _readyAvailable = new(false);
    private readonly AutoResetEvent _presentedAvailable = new(false);

    private long _nextFrameIndex = 1;
    private long _highestPresentedFrameIndex;
    private int _presentationWaiterCount;
    private bool _completed;

    /// <summary>Initializes the ring and its three reusable frame packets.</summary>
    internal FrameRing()
    {
        for (int index = 0; index < _packets.Length; index++)
        {
            _packets[index] = new RenderFramePacket(frameIndex: 0);
        }
    }

    /// <summary>Gets the signal set when a ready frame is available or the ring completes.</summary>
    internal WaitHandle ReadySignal => _readyAvailable;

    /// <summary>
    /// Gets the number of threads blocked in <see cref="WaitForPresentation"/>; test-only.
    /// </summary>
    internal int PresentationWaiterCount => Volatile.Read(ref _presentationWaiterCount);

    /// <summary>Gets whether the producer completed the ring; ready frames still need draining.</summary>
    internal bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    /// <summary>Tries to reserve a free slot for recording; fails when none is free.</summary>
    internal bool TryAcquireWritable(out FrameLease lease)
    {
        lock (_gate)
        {
            return TryAcquireWritableLocked(out lease);
        }
    }

    /// <summary>Attaches the frame's retention set to a recording lease before publishing.</summary>
    /// <remarks>A slot stores at most one set, so attach once per frame.</remarks>
    internal void AttachRetention(FrameLease lease, FrameAssetRetention retention)
    {
        ArgumentNullException.ThrowIfNull(retention);

        lock (_gate)
        {
            int slotIndex = ValidateLeaseLocked(
                lease,
                SlotState.Recording,
                nameof(AttachRetention)
            );
            if (_slots[slotIndex].Retention is not null)
            {
                throw new InvalidOperationException(
                    $"Frame ring {nameof(AttachRetention)} requires a recording slot without a "
                        + $"retention set, but slot {slotIndex} already owns one."
                );
            }

            _slots[slotIndex] = new Slot(SlotState.Recording, lease.FrameIndex, retention);
        }
    }

    /// <summary>
    /// Seals a recording lease as the newest ready frame, superseding any older ready frame.
    /// </summary>
    /// <remarks>Superseded retention is released before the freed slot is advertised.</remarks>
    internal void Publish(FrameLease lease)
    {
        bool freedSlot = false;
        List<FrameAssetRetention>? superseded = null;
        lock (_gate)
        {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Publish));
            lease.Packet?.Seal();
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                if (i != slotIndex && _slots[i].State == SlotState.Ready)
                {
                    if (_slots[i].Retention is { } retention)
                    {
                        (superseded ??= []).Add(retention);
                    }

                    _slots[i] = new Slot(SlotState.Free, _slots[i].FrameIndex);
                    freedSlot = true;
                }
            }
            _slots[slotIndex] = new Slot(
                SlotState.Ready,
                lease.FrameIndex,
                _slots[slotIndex].Retention
            );
        }

        // Release the superseded holds before the freed slot is advertised, so a new tick cannot
        // re-retain an asset while the previous frame is still releasing it. The ring lock is already
        // released because a target's release may queue GPU destruction.
        ReleaseRetention(superseded);
        _readyAvailable.Set();
        if (freedSlot)
        {
            _writableAvailable.Set();
        }
    }

    /// <summary>Tries to take the newest ready frame for rendering.</summary>
    internal bool TryAcquireNewest(out FrameLease lease)
    {
        lock (_gate)
        {
            return TryAcquireNewestLocked(out lease);
        }
    }

    /// <summary>Releases a rendering lease and its retention set, freeing the slot for recording.</summary>
    internal void Release(FrameLease lease)
    {
        FrameAssetRetention? retention;
        lock (_gate)
        {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Rendering, nameof(Release));
            retention = _slots[slotIndex].Retention;
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        // Release the holds before the slot is advertised as free for the same reason as Publish.
        retention?.Release();
        _writableAvailable.Set();
    }

    /// <summary>Returns a recording lease without publishing it and releases its retention set.</summary>
    internal void Abandon(FrameLease lease)
    {
        FrameAssetRetention? retention;
        lock (_gate)
        {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Abandon));
            retention = _slots[slotIndex].Retention;
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        retention?.Release();
        _writableAvailable.Set();
    }

    /// <summary>Stops the producer; ready frames stay drainable with their retention sets.</summary>
    /// <remarks>Idempotent; the final frame may still be presented during teardown.</remarks>
    internal void Complete()
    {
        bool completedNow;
        lock (_gate)
        {
            completedNow = !_completed;
            _completed = true;
        }

        if (completedNow)
        {
            _writableAvailable.Set();
            _readyAvailable.Set();
            _presentedAvailable.Set();
        }
    }

    /// <summary>
    /// Blocks for a free slot, returning <see langword="null"/> when the ring is complete.
    /// </summary>
    internal FrameLease? AcquireWritable(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (TryAcquireWritableLocked(out FrameLease lease))
                {
                    return lease;
                }
                if (_completed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return null;
                }
            }

            WaitForSignal(_writableAvailable, cancellationToken);
        }
    }

    /// <summary>
    /// Blocks for a ready frame; a complete ring drains its last frame before returning
    /// <see langword="null"/>.
    /// </summary>
    internal FrameLease? AcquireNewest(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (TryAcquireNewestLocked(out FrameLease lease))
                {
                    return lease;
                }
                if (_completed)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return null;
                }
            }

            WaitForSignal(_readyAvailable, cancellationToken);
        }
    }

    /// <summary>Records that a frame's presentation attempt finished; the mark is monotonic.</summary>
    /// <remarks>
    /// Dropped and failed frames mark too, so FIFO pacing cannot wait forever;
    /// <see cref="RenderStats.PresentedFrames"/> counts only successful presentations.
    /// </remarks>
    internal void MarkPresented(long frameIndex)
    {
        bool advanced = false;
        lock (_gate)
        {
            if (frameIndex > _highestPresentedFrameIndex)
            {
                _highestPresentedFrameIndex = frameIndex;
                advanced = true;
            }
        }

        if (advanced)
        {
            _presentedAvailable.Set();
        }
    }

    /// <summary>Blocks until the frame, or a newer one, is finished with, or the ring completes.</summary>
    /// <remarks>
    /// A newer mark satisfies an older wait, and completion or cancellation releases it, so shutdown
    /// cannot deadlock. No lock is held while waiting.
    /// </remarks>
    /// <returns>
    /// <see langword="true"/> when finished or complete; otherwise <see langword="false"/>.
    /// </returns>
    internal bool WaitForPresentation(long frameIndex, CancellationToken cancellationToken)
    {
        while (true)
        {
            lock (_gate)
            {
                if (_highestPresentedFrameIndex >= frameIndex || _completed)
                {
                    return true;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            Interlocked.Increment(ref _presentationWaiterCount);
            try
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(
                    static state => ((AutoResetEvent)state!).Set(),
                    _presentedAvailable
                );
                _presentedAvailable.WaitOne();
            }
            finally
            {
                Interlocked.Decrement(ref _presentationWaiterCount);
            }
        }
    }

    private static void ReleaseRetention(List<FrameAssetRetention>? retentions)
    {
        if (retentions is null)
        {
            return;
        }

        foreach (FrameAssetRetention retention in retentions)
        {
            retention.Release();
        }
    }

    private bool TryAcquireWritableLocked(out FrameLease lease)
    {
        lease = default;
        if (_completed)
        {
            return false;
        }

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (_slots[i].State == SlotState.Free)
            {
                long frameIndex = _nextFrameIndex++;
                RenderFramePacket packet = _packets[i];
                packet.Reset(frameIndex);
                _slots[i] = new Slot(SlotState.Recording, frameIndex);
                lease = new FrameLease(i, frameIndex, packet);
                return true;
            }
        }

        return false;
    }

    private bool TryAcquireNewestLocked(out FrameLease lease)
    {
        lease = default;
        int newestSlotIndex = -1;
        long newestFrameIndex = -1;
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (_slots[i].State == SlotState.Ready && _slots[i].FrameIndex > newestFrameIndex)
            {
                newestSlotIndex = i;
                newestFrameIndex = _slots[i].FrameIndex;
            }
        }

        if (newestSlotIndex < 0)
        {
            return false;
        }

        _slots[newestSlotIndex] = new Slot(
            SlotState.Rendering,
            newestFrameIndex,
            _slots[newestSlotIndex].Retention
        );
        lease = new FrameLease(newestSlotIndex, newestFrameIndex, _packets[newestSlotIndex]);
        return true;
    }

    private int ValidateLeaseLocked(FrameLease lease, SlotState requiredState, string operation)
    {
        int slotIndex = lease.SlotIndex;
        if (slotIndex < 0 || slotIndex >= SLOT_COUNT)
        {
            throw new InvalidOperationException(
                $"Frame ring {operation} received a lease with invalid slot index {slotIndex}."
            );
        }

        Slot slot = _slots[slotIndex];
        if (slot.FrameIndex != lease.FrameIndex || slot.State != requiredState)
        {
            throw new InvalidOperationException(
                $"Frame ring {operation} requires a {requiredState} lease, but slot {slotIndex} holds "
                    + $"frame {slot.FrameIndex} in state {slot.State}; the lease refers to frame {lease.FrameIndex}."
            );
        }

        return slotIndex;
    }

    private static void WaitForSignal(AutoResetEvent signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((AutoResetEvent)state!).Set(),
            signal
        );
        signal.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private enum SlotState
    {
        Free,
        Recording,
        Ready,
        Rendering,
    }

    private readonly struct Slot
    {
        public Slot(SlotState state, long frameIndex, FrameAssetRetention? retention = null)
        {
            State = state;
            FrameIndex = frameIndex;
            Retention = retention;
        }

        public SlotState State { get; }

        public long FrameIndex { get; }

        public FrameAssetRetention? Retention { get; }
    }
}
