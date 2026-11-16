namespace RayBork.Internal;

/// <summary>
/// The lifetime of one render target's GPU images, shared by the public <see cref="RenderTexture"/>,
/// the tick-thread camera retention, and the render-thread target resource.
/// </summary>
/// <remarks>
/// <para>
/// The public <see cref="RenderTexture"/> holds the single owner reference. A frame that writes or
/// samples the target adds a frame hold through the frame's retention set; the frame ring releases it
/// after the render thread released the frame slot. The target is queued for destruction exactly once,
/// after both the owner and every frame hold are gone, so a target that is the destination of a recorded
/// frame is never destroyed mid-frame.
/// </para>
/// <para>
/// <see cref="LastUseFrameSlot"/> tracks the last frame slot that wrote or sampled the target. The
/// renderer waits for that slot's fence before it reuses the target in a later frame, so a render target
/// is never overwritten or sampled while the previous frame that used it is still in flight. The owner
/// and frame-hold counts are thread-safe; the last-use slot is render-thread state guarded by the same
/// lock for the reads the renderer makes outside it.
/// </para>
/// </remarks>
internal sealed class RenderTextureTargetLifetime : IAssetRetentionTarget {
    private readonly Lock _gate = new();
    private readonly Action<RenderTextureTargetLifetime> _queueDestruction;
    private int _frameHoldCount;
    private int _lastUseFrameSlot = -1;
    private bool _disposed;
    private bool _destroyQueued;

    /// <summary>
    /// Initializes the lifetime with one owner reference, owned by the <see cref="RenderTexture"/>.
    /// </summary>
    /// <param name="targetId">The unique render-thread target id; it is never reused.</param>
    /// <param name="description">The validated description the target was created from.</param>
    /// <param name="queueDestruction">
    /// Queues the target for destruction on the render thread. It runs exactly once, after the owner and
    /// every frame hold are gone.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="queueDestruction"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="targetId"/> is not positive.</exception>
    internal RenderTextureTargetLifetime(
        long targetId,
        RenderTextureDescription description,
        Action<RenderTextureTargetLifetime> queueDestruction) {
        ArgumentNullException.ThrowIfNull(queueDestruction);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetId);

        TargetId = targetId;
        Description = description;
        _queueDestruction = queueDestruction;
    }

    /// <summary>Gets the unique render-thread target id. It is never reused.</summary>
    internal long TargetId { get; }

    /// <summary>Gets the description the target was created from.</summary>
    internal RenderTextureDescription Description { get; }

    /// <summary>Gets whether the owner reference was released. It stays readable after disposal.</summary>
    internal bool IsDisposed {
        get {
            lock (_gate) {
                return _disposed;
            }
        }
    }

    /// <summary>Gets the frame slot that last used the target, or <c>-1</c> when it was never used.</summary>
    internal int LastUseFrameSlot {
        get {
            lock (_gate) {
                return _lastUseFrameSlot;
            }
        }
    }

    /// <summary>
    /// Records the frame slot that is about to use the target. The renderer calls it after it waited for
    /// the previous slot's fence, before it records the frame.
    /// </summary>
    /// <param name="frameSlot">The current frame slot.</param>
    internal void SetLastUseFrameSlot(int frameSlot) {
        lock (_gate) {
            _lastUseFrameSlot = frameSlot;
        }
    }

    /// <summary>
    /// Releases the owner reference. The target is queued for destruction once no frame hold remains.
    /// Idempotent.
    /// </summary>
    internal void ReleaseOwner() {
        bool queue;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            queue = TryClaimDestructionLocked();
        }

        if (queue) {
            _queueDestruction(this);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The owner reference was already released.</exception>
    public void AddFrameHold() {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _frameHoldCount++;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The frame-hold count is already zero.</exception>
    public void ReleaseFrameHold() {
        bool queue;
        lock (_gate) {
            if (_frameHoldCount <= 0) {
                throw new InvalidOperationException(
                    "The render target frame-hold count is already zero; every frame hold is released "
                        + "exactly once.");
            }

            _frameHoldCount--;
            queue = TryClaimDestructionLocked();
        }

        if (queue) {
            _queueDestruction(this);
        }
    }

    private bool TryClaimDestructionLocked() {
        if (!_disposed || _frameHoldCount != 0 || _destroyQueued) {
            return false;
        }

        _destroyQueued = true;
        return true;
    }
}
