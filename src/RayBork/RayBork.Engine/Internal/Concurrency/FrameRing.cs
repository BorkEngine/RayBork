namespace RayBork.Internal;

/// <summary>
/// A three-slot frame handoff ring for one tick producer and one render consumer.
/// </summary>
/// <remarks>
/// <para>
/// The producer records into a writable slot with <see cref="TryAcquireWritable"/> or
/// <see cref="AcquireWritable"/>, then publishes it as the newest ready frame. The consumer takes
/// the newest ready frame with <see cref="TryAcquireNewest"/> or <see cref="AcquireNewest"/> and
/// releases it after rendering. Publishing supersedes an older ready frame instead of queueing it,
/// so the consumer never renders a backlog, and never touches a slot that is being rendered. The
/// producer blocks only when no slot is free, which bounds run-ahead.
/// </para>
/// <para>
/// A slot owns at most one <see cref="FrameAssetRetention"/> set. The producer attaches it to the
/// recording lease, the set follows the frame through Ready and Rendering, and the ring releases it
/// exactly once when the frame is released, superseded, or abandoned. The retention release runs
/// after the ring lock is released because a target's release may queue GPU destruction.
/// </para>
/// <para>
/// A slot also owns one reusable <see cref="RenderFramePacket"/>. A writable acquisition resets the
/// packet and returns it through the lease; publishing seals it; the consumer reads it through the
/// rendering lease. The packet's pooled pass and draw buffers are therefore reused per slot and never
/// shared by two frames, and the tick thread cannot mutate a packet that is ready or rendering.
/// </para>
/// <para>
/// State is guarded by a <see cref="Lock"/>; blocking waits use <see cref="AutoResetEvent"/> and
/// never hold the lock. <see cref="ReadySignal"/> lets the render loop wait on frame readiness and
/// GPU upload work at the same time. Blocking acquisitions return <c>null</c> when the ring is
/// complete and drained; invalid or stale leases throw <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// The ring also tracks the highest frame index the render thread finished presenting with
/// <see cref="MarkPresented"/>, so a FIFO-paced producer can wait for the frame it published with
/// <see cref="WaitForPresentation"/>. A newer presentation satisfies an older wait because a
/// superseded frame is no longer needed. Completion and cancellation both release the wait.
/// </para>
/// </remarks>
internal sealed class FrameRing {
    private const int SLOT_COUNT = 3;

    private readonly Lock _gate = new();
    private readonly Slot[] _slots = new Slot[SLOT_COUNT];
    private readonly RenderFramePacket[] _packets = new RenderFramePacket[SLOT_COUNT];
    private readonly AutoResetEvent _writableAvailable = new(false);
    private readonly AutoResetEvent _readyAvailable = new(false);
    private readonly AutoResetEvent _presentedAvailable = new(false);

    private long _nextFrameIndex = 1;
    private long _highestPresentedFrameIndex;
    private bool _completed;

    /// <summary>
    /// Initializes the ring and its three reusable frame packets.
    /// </summary>
    internal FrameRing() {
        for (int index = 0; index < _packets.Length; index++) {
            _packets[index] = new RenderFramePacket(frameIndex: 0);
        }
    }

    /// <summary>
    /// Gets the signal that is set when a ready frame becomes available or the ring completes. The
    /// render loop waits on it together with the GPU asset bridge signal.
    /// </summary>
    internal WaitHandle ReadySignal => _readyAvailable;

    /// <summary>
    /// Gets whether the producer completed the ring. A completed ring still has to be drained by the
    /// consumer.
    /// </summary>
    internal bool IsCompleted {
        get {
            lock (_gate) {
                return _completed;
            }
        }
    }

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
    /// Attaches the frame's engine-asset retention set to a recording lease. A slot stores at most one
    /// set; attach once per frame, before publishing.
    /// </summary>
    /// <param name="lease">A recording lease for this ring.</param>
    /// <param name="retention">The set owned by the frame, or the frame's own new set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="retention"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a recording lease, or the slot already owns a retention set.
    /// </exception>
    internal void AttachRetention(FrameLease lease, FrameAssetRetention retention) {
        ArgumentNullException.ThrowIfNull(retention);

        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(AttachRetention));
            if (_slots[slotIndex].Retention is not null) {
                throw new InvalidOperationException(
                    $"Frame ring {nameof(AttachRetention)} requires a recording slot without a " +
                    $"retention set, but slot {slotIndex} already owns one.");
            }

            _slots[slotIndex] = new Slot(SlotState.Recording, lease.FrameIndex, retention);
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
        List<FrameAssetRetention>? superseded = null;
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Publish));
            lease.Packet?.Seal();
            for (int i = 0; i < SLOT_COUNT; i++) {
                if (i != slotIndex && _slots[i].State == SlotState.Ready) {
                    if (_slots[i].Retention is { } retention) {
                        (superseded ??= []).Add(retention);
                    }

                    _slots[i] = new Slot(SlotState.Free, _slots[i].FrameIndex);
                    freedSlot = true;
                }
            }
            _slots[slotIndex] = new Slot(SlotState.Ready, lease.FrameIndex, _slots[slotIndex].Retention);
        }

        // Release the superseded holds before the freed slot is advertised, so a new tick cannot
        // re-retain an asset while the previous frame is still releasing it. The ring lock is already
        // released because a target's release may queue GPU destruction.
        ReleaseRetention(superseded);
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
    /// Releases a rendering lease so the slot can be recorded again, and releases the frame's
    /// retention set outside the ring lock.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a rendering lease for this ring.
    /// </exception>
    internal void Release(FrameLease lease) {
        FrameAssetRetention? retention;
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Rendering, nameof(Release));
            retention = _slots[slotIndex].Retention;
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        // Release the holds before the slot is advertised as free for the same reason as Publish.
        retention?.Release();
        _writableAvailable.Set();
    }

    /// <summary>
    /// Returns a recording lease to the free pool without publishing it, and releases its retention
    /// set outside the ring lock. Used when a tick fails before it can produce a frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="lease"/> is not a recording lease for this ring.
    /// </exception>
    internal void Abandon(FrameLease lease) {
        FrameAssetRetention? retention;
        lock (_gate) {
            int slotIndex = ValidateLeaseLocked(lease, SlotState.Recording, nameof(Abandon));
            retention = _slots[slotIndex].Retention;
            _slots[slotIndex] = new Slot(SlotState.Free, _slots[slotIndex].FrameIndex);
        }

        retention?.Release();
        _writableAvailable.Set();
    }

    /// <summary>
    /// Stops the producer: later writable acquisitions fail, waiting producers are woken, and the
    /// consumer may still drain the frames that are already ready. Complete is idempotent. Ready
    /// frames keep their retention set until the consumer drains them, because the final frame may
    /// still be presented during teardown.
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
            _presentedAvailable.Set();
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

    /// <summary>
    /// Records that the render thread finished the presentation attempt for <paramref name="frameIndex"/>.
    /// The mark is monotonic: a frame at or below the highest marked index counts as presented, so a
    /// superseded frame is satisfied by the newer frame that replaced it. Safe and idempotent from any
    /// thread; advances the presentation waiters only when the highest index moves.
    /// </summary>
    /// <remarks>
    /// The render loop calls this after every present attempt, including a recovered failure, and
    /// before it releases the lease. Marking the finished attempt rather than the successful
    /// presentation keeps a FIFO-paced tick from waiting forever when the renderer drops or recovers a
    /// frame.
    /// </remarks>
    /// <param name="frameIndex">The frame index that finished its presentation attempt.</param>
    internal void MarkPresented(long frameIndex) {
        bool advanced = false;
        lock (_gate) {
            if (frameIndex > _highestPresentedFrameIndex) {
                _highestPresentedFrameIndex = frameIndex;
                advanced = true;
            }
        }

        if (advanced) {
            _presentedAvailable.Set();
        }
    }

    /// <summary>
    /// Blocks until <paramref name="frameIndex"/> or a newer frame is marked presented, or until the
    /// ring completes. No lock is held while waiting.
    /// </summary>
    /// <remarks>
    /// The wait is used by the FIFO presentation policy: one tick waits for the presentation of the
    /// frame it published, so the tick rate follows the display refresh. A MAILBOX or IMMEDIATE
    /// renderer does not wait. A ring that is already complete, or that completes while waiting,
    /// releases the wait so shutdown cannot deadlock even when the render thread never presents the
    /// frame.
    /// </remarks>
    /// <param name="frameIndex">The frame index whose presentation the caller waits for.</param>
    /// <param name="cancellationToken">The stop token that releases the wait during shutdown.</param>
    /// <returns>
    /// <see langword="true"/> when the frame or a newer one was presented, or when the ring is
    /// complete; <see langword="false"/> when <paramref name="cancellationToken"/> was canceled first.
    /// </returns>
    internal bool WaitForPresentation(long frameIndex, CancellationToken cancellationToken) {
        while (true) {
            lock (_gate) {
                if (_highestPresentedFrameIndex >= frameIndex || _completed) {
                    return true;
                }
            }

            if (cancellationToken.IsCancellationRequested) {
                return false;
            }

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((AutoResetEvent)state!).Set(),
                _presentedAvailable);
            _presentedAvailable.WaitOne();
        }
    }

    private static void ReleaseRetention(List<FrameAssetRetention>? retentions) {
        if (retentions is null) {
            return;
        }

        foreach (FrameAssetRetention retention in retentions) {
            retention.Release();
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
                RenderFramePacket packet = _packets[i];
                packet.Reset(frameIndex);
                _slots[i] = new Slot(SlotState.Recording, frameIndex);
                lease = new FrameLease(i, frameIndex, packet);
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

        _slots[newestSlotIndex] = new Slot(SlotState.Rendering, newestFrameIndex, _slots[newestSlotIndex].Retention);
        lease = new FrameLease(newestSlotIndex, newestFrameIndex, _packets[newestSlotIndex]);
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
        public Slot(SlotState state, long frameIndex, FrameAssetRetention? retention = null) {
            State = state;
            FrameIndex = frameIndex;
            Retention = retention;
        }

        public SlotState State { get; }

        public long FrameIndex { get; }

        public FrameAssetRetention? Retention { get; }
    }
}
