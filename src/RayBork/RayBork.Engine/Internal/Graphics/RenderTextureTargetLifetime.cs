namespace RayBork.Internal;

/// <summary>
/// The lifetime of one render target's GPU images, shared by the public texture and render-thread
/// resources.
/// </summary>
/// <remarks>
/// The public <see cref="RenderTexture"/> holds the single owner reference and every frame that
/// writes or samples the target adds a hold; destruction is queued once both are gone.
/// <see cref="LastUseFrameSlot"/> makes a later frame wait for the previous user's fence.
/// </remarks>
internal sealed class RenderTextureTargetLifetime : IAssetRetentionTarget
{
    private readonly Lock _gate = new();
    private readonly Action<RenderTextureTargetLifetime> _queueDestruction;
    private int _frameHoldCount;
    private int _lastUseFrameSlot = -1;
    private int _sampleCount;
    private RenderTextureDescription _description;
    private bool _disposed;
    private bool _destroyQueued;

    /// <summary>
    /// Initializes the lifetime with one owner reference and a destruction callback that runs once.
    /// </summary>
    internal RenderTextureTargetLifetime(
        long targetId,
        RenderTextureDescription description,
        int sampleCount,
        Action<RenderTextureTargetLifetime> queueDestruction
    )
    {
        ArgumentNullException.ThrowIfNull(queueDestruction);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);

        TargetId = targetId;
        _description = description;
        _sampleCount = sampleCount;
        _queueDestruction = queueDestruction;
    }

    /// <summary>Gets the unique render-thread target id. It is never reused.</summary>
    internal long TargetId { get; }

    /// <summary>Gets the current description; a resize updates only the width and height.</summary>
    internal RenderTextureDescription Description
    {
        get
        {
            lock (_gate)
            {
                return _description;
            }
        }
    }

    /// <summary>Gets whether a recorded or in-flight frame still holds the target.</summary>
    internal bool IsInUse
    {
        get
        {
            lock (_gate)
            {
                return _frameHoldCount > 0;
            }
        }
    }

    /// <summary>Replaces the description after a resize; format and depth stay fixed.</summary>
    internal void UpdateDescription(RenderTextureDescription description)
    {
        lock (_gate)
        {
            _description = description;
        }
    }

    /// <summary>Gets the effective MSAA sample count of the current images.</summary>
    internal int SampleCount
    {
        get
        {
            lock (_gate)
            {
                return _sampleCount;
            }
        }
    }

    /// <summary>Replaces the effective sample count after a device recovery.</summary>
    internal void SetSampleCount(int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        lock (_gate)
        {
            _sampleCount = sampleCount;
        }
    }

    /// <summary>Marks the target destroyed when shutdown destroys every live target.</summary>
    internal void MarkDestroyed()
    {
        lock (_gate)
        {
            _disposed = true;
            _destroyQueued = true;
        }
    }

    /// <summary>Gets whether the target was released or destroyed at shutdown.</summary>
    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>Gets the frame slot that last used the target, or <c>-1</c>.</summary>
    internal int LastUseFrameSlot
    {
        get
        {
            lock (_gate)
            {
                return _lastUseFrameSlot;
            }
        }
    }

    /// <summary>Records the frame slot about to use the target, after its fence wait.</summary>
    internal void SetLastUseFrameSlot(int frameSlot)
    {
        lock (_gate)
        {
            _lastUseFrameSlot = frameSlot;
        }
    }

    /// <summary>Releases the owner reference and queues destruction after the last hold.</summary>
    internal void ReleaseOwner()
    {
        bool queue;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            queue = TryClaimDestructionLocked();
        }

        if (queue)
        {
            _queueDestruction(this);
        }
    }

    /// <inheritdoc/>
    public void AddFrameHold()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _frameHoldCount++;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The frame-hold count is already zero.</exception>
    public void ReleaseFrameHold()
    {
        bool queue;
        lock (_gate)
        {
            if (_frameHoldCount <= 0)
            {
                throw new InvalidOperationException(
                    "The render target frame-hold count is already zero; every frame hold is released "
                        + "exactly once."
                );
            }

            _frameHoldCount--;
            queue = TryClaimDestructionLocked();
        }

        if (queue)
        {
            _queueDestruction(this);
        }
    }

    private bool TryClaimDestructionLocked()
    {
        if (!_disposed || _frameHoldCount != 0 || _destroyQueued)
        {
            return false;
        }

        _destroyQueued = true;
        return true;
    }
}
