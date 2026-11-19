namespace RayBork.Internal;

/// <summary>
/// Queues render texture creation, resize, readback, and destruction between the tick thread and
/// the render thread.
/// </summary>
/// <remarks>
/// The render thread services the queue before it acquires the next frame; readback waits for the
/// target's last-use fence. A stopped service fails later and queued requests with
/// <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class RenderTextureService : IDisposable
{
    /// <summary>The maximum number of queued creation requests.</summary>
    internal const int MAX_PENDING_CREATES = 256;

    private readonly Lock _gate = new();
    private readonly Queue<CreateRequest> _creates = new();
    private readonly Queue<ResizeRequest> _resizes = new();
    private readonly Queue<ReadbackRequest> _readbacks = new();
    private readonly Queue<RenderTextureTargetLifetime> _destroys = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private long _nextTargetId;
    private bool _stopped;
    private bool _closed;
    private bool _disposed;

    /// <summary>Gets the wait handle the render loop waits on while a request is queued.</summary>
    internal WaitHandle Signal => _workAvailable;

    /// <summary>Gets whether the service stopped accepting requests.</summary>
    internal bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>Gets whether any request is queued.</summary>
    internal bool HasPendingWork
    {
        get
        {
            lock (_gate)
            {
                return _creates.Count > 0
                    || _resizes.Count > 0
                    || _readbacks.Count > 0
                    || _destroys.Count > 0;
            }
        }
    }

    /// <summary>Queues a creation request; safe from any thread.</summary>
    /// <returns>Created texture, or a canceled task when the token was already canceled.</returns>
    /// <exception cref="InvalidOperationException">The creation queue is full.</exception>
    internal Task<RenderTexture> CreateAsync(
        RenderTextureDescription description,
        CancellationToken cancellationToken
    )
    {
        // A stopped service answers with ObjectDisposedException before any argument or cancellation
        // check, so a call after shutdown reports the documented state instead of a validation error.
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException<RenderTexture>(CreateStoppedException());
            }
        }

        RenderTextureValidation.Validate(description);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<RenderTexture>(cancellationToken);
        }

        CreateRequest request = new(this, description, cancellationToken);
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException<RenderTexture>(CreateStoppedException());
            }

            if (_creates.Count >= MAX_PENDING_CREATES)
            {
                return Task.FromException<RenderTexture>(
                    new InvalidOperationException(
                        $"The render texture creation queue is full ({MAX_PENDING_CREATES} pending "
                            + "requests); the render thread is not draining it."
                    )
                );
            }

            _creates.Enqueue(request);
        }

        if (cancellationToken.CanBeCanceled)
        {
            request.RegisterCancellation();
        }

        _workAvailable.Set();
        return request.Completion.Task;
    }

    /// <summary>Queues a resize; an unchanged size completes without recreating anything.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, exceeds <see cref="RenderTextureValidation.MAX_DIMENSION"/>, or
    /// exceeds the device maximum on the render thread.
    /// </exception>
    /// <exception cref="InvalidOperationException">The target is in use by a frame.</exception>
    internal Task ResizeAsync(RenderTextureTargetLifetime lifetime, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        // A stopped service answers with ObjectDisposedException before any argument or target state
        // check, so a call after shutdown reports the documented state instead of a validation error.
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException(CreateStoppedException());
            }
        }

        if (lifetime.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(RenderTexture),
                "The render texture is disposed; it cannot be resized."
            );
        }

        RenderTextureValidation.ValidateDimensions(width, height);
        RenderTextureDescription description = lifetime.Description;
        if (description.Width == width && description.Height == height)
        {
            // An unchanged size is a no-op, even while a frame holds the target: nothing is recreated.
            return Task.CompletedTask;
        }

        if (lifetime.IsInUse)
        {
            throw new InvalidOperationException(
                "The render texture is in use by a recorded or in-flight frame; a camera targets it, a "
                    + "draw samples one of its views, or the recorded frame still references it. Resize it "
                    + "only after the frame that uses it completed."
            );
        }

        ResizeRequest request = new(lifetime, width, height);
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException(CreateStoppedException());
            }

            _resizes.Enqueue(request);
        }

        _workAvailable.Set();
        return request.Completion.Task;
    }

    /// <summary>Queues a display-referred RGBA8 readback of the resolved color image.</summary>
    /// <returns>Row-major top-left pixels; opaque black when no frame wrote content.</returns>
    internal Task<byte[]> ReadPixelsAsync(
        RenderTextureTargetLifetime lifetime,
        CancellationToken cancellationToken
    ) => QueueReadback<byte[]>(lifetime, cancellationToken);

    /// <summary>Queues a scene-linear, HDR-preserving readback of the color image.</summary>
    /// <returns>Raw colors indexed <c>[y, x]</c>; opaque black if never written.</returns>
    internal Task<Color[,]> ReadColorsAsync(
        RenderTextureTargetLifetime lifetime,
        CancellationToken cancellationToken
    ) => QueueReadback<Color[,]>(lifetime, cancellationToken);

    private Task<T> QueueReadback<T>(
        RenderTextureTargetLifetime lifetime,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        // A stopped service answers with ObjectDisposedException before any target or cancellation
        // check, matching creation and resize after shutdown.
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException<T>(CreateStoppedException());
            }
        }

        if (lifetime.IsDisposed)
        {
            return Task.FromException<T>(CreateDisposedTargetException());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        ReadbackRequest<T> request = new(this, lifetime, cancellationToken);
        lock (_gate)
        {
            if (_stopped || _closed)
            {
                return Task.FromException<T>(CreateStoppedException());
            }

            _readbacks.Enqueue(request);
        }

        if (cancellationToken.CanBeCanceled)
        {
            request.RegisterCancellation();
        }

        SignalWork();
        return request.Completion.Task;
    }

    /// <summary>Releases the owner reference; destruction follows the last frame hold.</summary>
    internal static void ReleaseTarget(RenderTexture target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Lifetime.ReleaseOwner();
    }

    /// <summary>Gets the next unique target id; render-thread only and never reused.</summary>
    internal long NextTargetId() => Interlocked.Increment(ref _nextTargetId);

    /// <summary>Wakes the render loop; best effort once the wait handle is disposed.</summary>
    internal void SignalWork()
    {
        try
        {
            _workAvailable.Set();
        }
        catch (ObjectDisposedException)
        {
            // The app already shut down; no render thread remains to service the request.
        }
    }

    /// <summary>Queues a released lifetime for destruction on the render thread.</summary>
    internal void QueueDestruction(RenderTextureTargetLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lock (_gate)
        {
            if (_stopped || _closed || _disposed)
            {
                return;
            }

            _destroys.Enqueue(lifetime);
        }

        _workAvailable.Set();
    }

    /// <summary>Removes and returns every queued creation request; render-thread only.</summary>
    internal CreateRequest[] DrainCreates()
    {
        lock (_gate)
        {
            if (_creates.Count == 0)
            {
                return [];
            }

            CreateRequest[] requests = [.. _creates];
            _creates.Clear();
            return requests;
        }
    }

    /// <summary>Removes and returns every queued resize request; render-thread only.</summary>
    internal ResizeRequest[] DrainResizes()
    {
        lock (_gate)
        {
            if (_resizes.Count == 0)
            {
                return [];
            }

            ResizeRequest[] requests = [.. _resizes];
            _resizes.Clear();
            return requests;
        }
    }

    /// <summary>Removes and returns every queued readback request; render-thread only.</summary>
    internal ReadbackRequest[] DrainReadbacks()
    {
        lock (_gate)
        {
            if (_readbacks.Count == 0)
            {
                return [];
            }

            ReadbackRequest[] requests = [.. _readbacks];
            _readbacks.Clear();
            return requests;
        }
    }

    /// <summary>Removes and returns queued destruction requests; render-thread only.</summary>
    internal RenderTextureTargetLifetime[] DrainDestroys()
    {
        lock (_gate)
        {
            if (_destroys.Count == 0)
            {
                return [];
            }

            RenderTextureTargetLifetime[] lifetimes = [.. _destroys];
            _destroys.Clear();
            return lifetimes;
        }
    }

    /// <summary>Requeues a lifetime a submitted frame still uses; the next turn retries.</summary>
    internal void RequeueDestruction(RenderTextureTargetLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lock (_gate)
        {
            if (_stopped || _closed || _disposed)
            {
                return;
            }

            _destroys.Enqueue(lifetime);
        }
    }

    /// <summary>
    /// Stops the service; later and queued requests fail with <see cref="ObjectDisposedException"/>.
    /// </summary>
    internal void Stop()
    {
        CreateRequest[] creates;
        ResizeRequest[] resizes;
        ReadbackRequest[] readbacks;
        lock (_gate)
        {
            if (_stopped || _closed || _disposed)
            {
                return;
            }

            _stopped = true;
            creates = [.. _creates];
            _creates.Clear();
            resizes = [.. _resizes];
            _resizes.Clear();
            readbacks = [.. _readbacks];
            _readbacks.Clear();
        }

        Exception exception = CreateStoppedException();
        foreach (CreateRequest request in creates)
        {
            request.DisposeRegistration();
            request.Completion.TrySetException(exception);
        }

        foreach (ResizeRequest request in resizes)
        {
            request.Completion.TrySetException(exception);
        }

        foreach (ReadbackRequest request in readbacks)
        {
            request.DisposeRegistration();
            request.TrySetException(exception);
        }
    }

    /// <summary>Stops the service at loop end, publishing the loop failure if any.</summary>
    internal void Close(Exception? failure)
    {
        CreateRequest[] creates;
        ResizeRequest[] resizes;
        ReadbackRequest[] readbacks;
        lock (_gate)
        {
            if (_closed || _disposed)
            {
                return;
            }

            _closed = true;
            _stopped = true;
            creates = [.. _creates];
            _creates.Clear();
            resizes = [.. _resizes];
            _resizes.Clear();
            readbacks = [.. _readbacks];
            _readbacks.Clear();
            _destroys.Clear();
        }

        Exception exception = failure ?? CreateStoppedException();
        foreach (CreateRequest request in creates)
        {
            request.DisposeRegistration();
            request.Completion.TrySetException(exception);
        }

        foreach (ResizeRequest request in resizes)
        {
            request.Completion.TrySetException(exception);
        }

        foreach (ReadbackRequest request in readbacks)
        {
            request.DisposeRegistration();
            request.TrySetException(exception);
        }
    }

    /// <summary>Disposes the wait handle after the render loop joined; idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopped = true;
        }

        _workAvailable.Dispose();
    }

    private static ObjectDisposedException CreateStoppedException() =>
        new(
            nameof(RenderTextureService),
            "The app has stopped; render textures cannot be created, resized, or used after shutdown."
        );

    private static ObjectDisposedException CreateDisposedTargetException() =>
        new(nameof(RenderTexture), "The render texture is disposed; it cannot be read back.");

    /// <summary>One queued readback request and its typed completion.</summary>
    /// <remarks>
    /// Cancellation before the render thread starts cancels the task; later cancellation is best
    /// effort and only discards the result.
    /// </remarks>
    internal abstract class ReadbackRequest
    {
        private readonly RenderTextureService _owner;
        private readonly Lock _registrationGate = new();
        private CancellationTokenRegistration _registration;
        private bool _registrationDisposed;

        protected ReadbackRequest(
            RenderTextureService owner,
            RenderTextureTargetLifetime lifetime,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(owner);
            ArgumentNullException.ThrowIfNull(lifetime);

            _owner = owner;
            Lifetime = lifetime;
            CancellationToken = cancellationToken;
        }

        /// <summary>Gets the target lifetime to read.</summary>
        internal RenderTextureTargetLifetime Lifetime { get; }

        /// <summary>Gets the target id the request addresses.</summary>
        internal long TargetId => Lifetime.TargetId;

        /// <summary>Gets the caller's cancellation token.</summary>
        internal CancellationToken CancellationToken { get; }

        /// <summary>Gets whether the completion task already finished.</summary>
        internal abstract bool IsCompleted { get; }

        /// <summary>
        /// Registers the caller's cancellation callback unless shutdown already completed it.
        /// </summary>
        internal void RegisterCancellation()
        {
            lock (_registrationGate)
            {
                if (_registrationDisposed)
                {
                    return;
                }

                _registration = CancellationToken.Register(
                    static state => ((ReadbackRequest)state!).Cancel(),
                    this
                );
            }
        }

        /// <summary>Disposes the cancellation registration; safe if none was registered.</summary>
        internal void DisposeRegistration()
        {
            CancellationTokenRegistration registration;
            lock (_registrationGate)
            {
                _registrationDisposed = true;
                registration = _registration;
                _registration = default;
            }

            registration.Dispose();
        }

        /// <summary>Completes the request as canceled by the caller's token.</summary>
        internal abstract bool TrySetCanceled();

        /// <summary>Completes the request with a failure.</summary>
        internal abstract bool TrySetException(Exception exception);

        private void Cancel()
        {
            if (TrySetCanceled())
            {
                // Wake the render thread so it can skip a queued request. The service may already be
                // disposed while a token is still live, so the wake is best effort.
                _owner.SignalWork();
            }
        }
    }

    /// <summary>A typed readback request for pixels or scene-linear colors.</summary>
    internal sealed class ReadbackRequest<T> : ReadbackRequest
    {
        internal ReadbackRequest(
            RenderTextureService owner,
            RenderTextureTargetLifetime lifetime,
            CancellationToken cancellationToken
        )
            : base(owner, lifetime, cancellationToken)
        {
            Completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        /// <summary>Gets the completion task source.</summary>
        internal TaskCompletionSource<T> Completion { get; }

        /// <inheritdoc/>
        internal override bool IsCompleted => Completion.Task.IsCompleted;

        /// <inheritdoc/>
        internal override bool TrySetCanceled() => Completion.TrySetCanceled(CancellationToken);

        /// <inheritdoc/>
        internal override bool TrySetException(Exception exception) =>
            Completion.TrySetException(exception);
    }

    /// <summary>One queued resize request and its completion.</summary>
    internal sealed class ResizeRequest
    {
        internal ResizeRequest(RenderTextureTargetLifetime lifetime, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(lifetime);

            Lifetime = lifetime;
            Width = width;
            Height = height;
            Completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        /// <summary>Gets the target lifetime to recreate.</summary>
        internal RenderTextureTargetLifetime Lifetime { get; }

        /// <summary>Gets the target id the request addresses.</summary>
        internal long TargetId => Lifetime.TargetId;

        /// <summary>Gets the new width in pixels.</summary>
        internal int Width { get; }

        /// <summary>Gets the new height in pixels.</summary>
        internal int Height { get; }

        /// <summary>Gets the completion task source.</summary>
        internal TaskCompletionSource Completion { get; }
    }

    /// <summary>One queued creation request and its completion.</summary>
    internal sealed class CreateRequest
    {
        private readonly RenderTextureService _owner;
        private CancellationTokenRegistration _registration;

        internal CreateRequest(
            RenderTextureService owner,
            RenderTextureDescription description,
            CancellationToken cancellationToken
        )
        {
            _owner = owner;
            Description = description;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<RenderTexture>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        /// <summary>Gets the validated description.</summary>
        internal RenderTextureDescription Description { get; }

        /// <summary>Gets the caller's cancellation token.</summary>
        internal CancellationToken CancellationToken { get; }

        /// <summary>Gets the completion task source.</summary>
        internal TaskCompletionSource<RenderTexture> Completion { get; }

        internal void RegisterCancellation()
        {
            _registration = CancellationToken.Register(
                static state => ((CreateRequest)state!).Cancel(),
                this
            );
        }

        internal void DisposeRegistration() => _registration.Dispose();

        private void Cancel()
        {
            if (Completion.TrySetCanceled(CancellationToken))
            {
                // Wake the render thread so it can skip the queued request or destroy a target it
                // already created.
                _owner._workAvailable.Set();
            }
        }
    }
}
