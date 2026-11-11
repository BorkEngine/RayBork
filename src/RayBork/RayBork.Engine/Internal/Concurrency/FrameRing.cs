namespace RayBork.Internal;

/// <summary>
/// A three-slot frame handoff ring for one tick producer and one render consumer.
/// </summary>
/// <remarks>
/// The producer records into a writable slot with <see cref="TryAcquireWritable"/> or
/// <see cref="AcquireWritable"/>, then publishes it as the newest ready frame. The consumer takes
/// the newest ready frame with <see cref="TryAcquireNewest"/> or <see cref="AcquireNewest"/> and
/// releases it after rendering. Publishing supersedes an older ready frame instead of queueing it,
/// so the consumer never renders a backlog, and never touches a slot that is being rendered. The
/// producer blocks only when no slot is free, which bounds run-ahead.
/// <para>
/// State is guarded by a <see cref="Lock"/>; blocking waits use <see cref="AutoResetEvent"/> and
/// never hold the lock. Blocking acquisitions return <c>null</c> when the ring is complete and
/// drained; invalid or stale leases throw <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
internal sealed class FrameRing {
    private const int SLOT_COUNT = 3;

    private readonly Lock _gate = new();
    private readonly Slot[] _slots = new Slot[SLOT_COUNT];
    private readonly AutoResetEvent _writableAvailable = new(false);
    private readonly AutoResetEvent _readyAvailable = new(false);

    private long _nextFrameIndex = 1;
    private bool _completed;

    /// <summary>
    /// Tries to reserve a free slot for recording. Returns <c>false</c> when every slot is occupied
    /// or the ring is complete.
    /// </summary>
    internal bool TryAcquireWritable(out FrameLease lease) {
        lock (_gate) {
            return TryAcquireWritableLocked(out lease);
        }
    }

    /// <summary>
    /// Seals a recording lease as the newest ready frame, superseding any older ready frame. Both
    /// superseded and completed frames are released for reuse; a rendering slot is never touched.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a recording lease for this ring.
    /// </exception>
    internal void Publish(FrameLease lease) {
        bool freedSlot = false;
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Publish));
            for (int i = 0; i < SLOT_COUNT; i++) {
                if (i != slotIndex && _slots[i].State == SlotState.Ready) {
                    _slots[i] = new Slot(SlotState.Free, _slots[i].FrameIndex);
                    freedSlot = true;
                }
            }
            _slots[slotIndex] = new Slot(SlotState.Ready, lease.FrameIndex);
        }

        _readyAvailable.Set();
        if (freedSlot) {
            _writableAvailable.Set();
        }
    }

    /// <summary>
    /// Tries to take the newest ready frame for rendering. Returns <c>false</c> when no ready frame
    /// is available, including after the ring is complete and drained.
    /// </summary>
    internal bool TryAcquireNewest(out FrameLease lease) {
        lock (_gate) {
            return TryAcquireNewestLocked(out lease);
        }
    }

    /// <summary>
    /// Releases a rendering lease so the slot can be recorded again.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a rendering lease for this ring.
    /// </exception>
    internal void Release(FrameLease lease) {
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Rendering, nameof(Release));
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        _writableAvailable.Set();
    }

    /// <summary>
    /// Returns a recording lease to the free pool without publishing it. Used when a tick fails
    /// before it can produce a frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a recording lease for this ring.
    /// </exception>
    internal void Abandon(FrameLease lease) {
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Abandon));
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        _writableAvailable.Set();
    }

    /// <summary>
    /// Stops the producer: later writable acquisitions fail, waiting producers are woken, and the
    /// consumer may still drain the frames that are already ready. Complete is idempotent.
    /// </summary>
    internal void Complete() {
        bool completedNow;
        lock (_gate) {
            completedNow = !_completed;
            _completed = true;
        }

        if (completedNow) {
            _writableAvailable.Set();
            _readyAvailable.Set();
        }
    }

    /// <summary>
    /// Blocks until a free slot is available or the ring is complete, then reserves the slot for
    /// recording. Completion and cancellation are distinguished: cancellation is reported as
    /// <see cref="OperationCanceledException"/> even when the ring is already complete.
    /// </summary>
    /// <returns>A recording lease, or <c>null</c> when the ring is complete.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    internal FrameLease? AcquireWritable(CancellationToken cancellationToken) {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) {
                if (TryAcquireWritableLocked(out FrameLease lease)) {
                    return lease;
                }
                if (_completed) {
                    cancellationToken.ThrowIfCancellationRequested();
                    return null;
                }
            }

            WaitForSignal(_writableAvailable, cancellationToken);
        }
    }

    /// <summary>
    /// Blocks until a ready frame is available. A complete ring drains its last ready frame before
    /// reporting completion, and cancellation is reported as <see cref="OperationCanceledException"/>
    /// even when the ring is already complete and drained.
    /// </summary>
    /// <returns>
    /// A rendering lease, or <c>null</c> when the ring is complete and no ready frame remains.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    internal FrameLease? AcquireNewest(CancellationToken cancellationToken) {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) {
                if (TryAcquireNewestLocked(out FrameLease lease)) {
                    return lease;
                }
                if (_completed) {
                    cancellationToken.ThrowIfCancellationRequested();
                    return null;
                }
            }

            WaitForSignal(_readyAvailable, cancellationToken);
        }
    }

    private bool TryAcquireWritableLocked(out FrameLease lease) {
        lease = default;
        if (_completed) {
            return false;
        }

        for (int i = 0; i < SLOT_COUNT; i++) {
            if (_slots[i].State == SlotState.Free) {
                long frameIndex = _nextFrameIndex++;
                _slots[i] = new Slot(SlotState.Recording, frameIndex);
                lease = new FrameLease(i, frameIndex);
                return true;
            }
        }

        return false;
    }

    private bool TryAcquireNewestLocked(out FrameLease lease) {
        lease = default;
        int newestSlotIndex = -1;
        long newestFrameIndex = -1;
        for (int i = 0; i < SLOT_COUNT; i++) {
            if (_slots[i].State == SlotState.Ready && _slots[i].FrameIndex > newestFrameIndex) {
                newestSlotIndex = i;
                newestFrameIndex = _slots[i].FrameIndex;
            }
        }

        if (newestSlotIndex < 0) {
            return false;
        }

        _slots[newestSlotIndex] = new Slot(SlotState.Rendering, newestFrameIndex);
        lease = new FrameLease(newestSlotIndex, newestFrameIndex);
        return true;
    }

    private int ValidateLeaseLocked(FrameLease lease, SlotState requiredState, string operation) {
        int slotIndex = lease.SlotIndex;
        if (slotIndex < 0 || slotIndex >= SLOT_COUNT) {
            throw new InvalidOperationException(
                $"Frame ring {operation} received a lease with invalid slot index {slotIndex}.");
        }

        Slot slot = _slots[slotIndex];
        if (slot.FrameIndex != lease.FrameIndex || slot.State != requiredState) {
            throw new InvalidOperationException(
                $"Frame ring {operation} requires a {requiredState} lease, but slot {slotIndex} holds " +
                $"frame {slot.FrameIndex} in state {slot.State}; the lease refers to frame {lease.FrameIndex}.");
        }

        return slotIndex;
    }

    private static void WaitForSignal(AutoResetEvent signal, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((AutoResetEvent)state!).Set(),
            signal);
        signal.WaitOne();
        cancellationToken.ThrowIfCancellationRequested();
    }

    private enum SlotState {
        Free,
        Recording,
        Ready,
        Rendering,
    }

    private readonly struct Slot {
        public Slot(SlotState state, long frameIndex) {
            State = state;
            FrameIndex = frameIndex;
        }

        public SlotState State { get; }

        public long FrameIndex { get; }
    }
}
