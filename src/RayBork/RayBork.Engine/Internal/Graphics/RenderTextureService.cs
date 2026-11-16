namespace RayBork.Internal;

/// <summary>
/// The thread-safe handoff between the tick thread and the render thread for render texture creation and
/// destruction.
/// </summary>
/// <remarks>
/// <para>
/// Any thread may call <see cref="CreateAsync"/>. The request is queued with one completion task and the
/// render loop's render thread services the queue before it acquires the next frame, so the call returns
/// after the render thread allocated the images. Awaiting it on the tick thread suspends the tick's
/// continuation through the engine synchronization context; the render thread still services the request,
/// so the tick thread never deadlocks. Cancellation before completion completes the task as canceled and
/// a target created for a canceled request is destroyed instead of leaking.
/// </para>
/// <para>
/// The service owns no GPU object. It hands target lifetimes to the renderer and receives the target's
/// destruction request when the public <see cref="RenderTexture"/> and every frame that used it released
/// the target. <see cref="Stop"/> rejects later requests and fails the queued ones with
/// <see cref="ObjectDisposedException"/>, which is what a call after app shutdown observes.
/// </para>
/// </remarks>
internal sealed class RenderTextureService : IDisposable {
    /// <summary>The maximum number of queued creation requests.</summary>
    internal const int MAX_PENDING_CREATES = 256;

    private readonly Lock _gate = new();
    private readonly Queue<CreateRequest> _creates = new();
    private readonly Queue<RenderTextureTargetLifetime> _destroys = new();
    private readonly AutoResetEvent _workAvailable = new(false);
    private long _nextTargetId;
    private bool _stopped;
    private bool _closed;
    private bool _disposed;

    /// <summary>
    /// Gets the wait handle the render loop waits on with the frame-ring and GPU asset signals. It is
    /// signaled while a creation or destruction request is queued.
    /// </summary>
    internal WaitHandle Signal => _workAvailable;

    /// <summary>
    /// Gets whether the service already stopped accepting requests.
    /// </summary>
    internal bool IsStopped {
        get {
            lock (_gate) {
                return _stopped;
            }
        }
    }

    /// <summary>
    /// Gets whether a creation or destruction request is queued. The render loop uses it to keep
    /// polling the render thread while no frame is published.
    /// </summary>
    internal bool HasPendingWork {
        get {
            lock (_gate) {
                return _creates.Count > 0 || _destroys.Count > 0;
            }
        }
    }

    /// <summary>
    /// Queues one render texture creation request. Safe from any thread.
    /// </summary>
    /// <param name="description">The validated description.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// The created render texture once the render thread allocated its images; a canceled task when the
    /// token was canceled before completion.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The service already stopped.</exception>
    /// <exception cref="InvalidOperationException">The creation queue is full.</exception>
    internal Task<RenderTexture> CreateAsync(
        RenderTextureDescription description,
        CancellationToken cancellationToken) {
        RenderTextureValidation.Validate(description);
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromCanceled<RenderTexture>(cancellationToken);
        }

        CreateRequest request = new(this, description, cancellationToken);
        lock (_gate) {
            if (_stopped || _closed) {
                return Task.FromException<RenderTexture>(CreateStoppedException());
            }

            if (_creates.Count >= MAX_PENDING_CREATES) {
                return Task.FromException<RenderTexture>(new InvalidOperationException(
                    $"The render texture creation queue is full ({MAX_PENDING_CREATES} pending "
                        + "requests); the render thread is not draining it."));
            }

            _creates.Enqueue(request);
        }

        if (cancellationToken.CanBeCanceled) {
            request.RegisterCancellation();
        }

        _workAvailable.Set();
        return request.Completion.Task;
    }

    /// <summary>
    /// Releases the public render texture's owner reference. The target is queued for destruction after
    /// every frame that used it completed.
    /// </summary>
    /// <param name="target">The render texture to release.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
    internal static void ReleaseTarget(RenderTexture target) {
        ArgumentNullException.ThrowIfNull(target);

        target.Lifetime.ReleaseOwner();
    }

    /// <summary>
    /// Gets the next unique target id. Render-thread only; an id is never reused.
    /// </summary>
    /// <returns>The positive target id.</returns>
    internal long NextTargetId() => Interlocked.Increment(ref _nextTargetId);

    /// <summary>
    /// Queues one target lifetime for destruction on the render thread. The lifetime calls this exactly
    /// once, after its owner and every frame hold are gone.
    /// </summary>
    /// <param name="lifetime">The released target lifetime.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> is <c>null</c>.</exception>
    internal void QueueDestruction(RenderTextureTargetLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        lock (_gate) {
            if (_stopped || _closed || _disposed) {
                return;
            }

            _destroys.Enqueue(lifetime);
        }

        _workAvailable.Set();
    }

    /// <summary>
    /// Removes every queued creation request. The renderer completes each one after it creates or rejects
    /// the target. Render-thread only.
    /// </summary>
    /// <returns>The queued requests, in request order.</returns>
    internal CreateRequest[] DrainCreates() {
        lock (_gate) {
            if (_creates.Count == 0) {
                return [];
            }

            CreateRequest[] requests = [.. _creates];
            _creates.Clear();
            return requests;
        }
    }

    /// <summary>
    /// Removes every queued destruction request. Render-thread only.
    /// </summary>
    /// <returns>The queued target lifetimes, in destruction order.</returns>
    internal RenderTextureTargetLifetime[] DrainDestroys() {
        lock (_gate) {
            if (_destroys.Count == 0) {
                return [];
            }

            RenderTextureTargetLifetime[] lifetimes = [.. _destroys];
            _destroys.Clear();
            return lifetimes;
        }
    }

    /// <summary>
    /// Puts a target lifetime that is still in use by a submitted frame back into the destruction queue.
    /// The render loop keeps polling while pending work exists, so the next turn retries.
    /// </summary>
    /// <param name="lifetime">The target lifetime that is not idle yet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lifetime"/> is <c>null</c>.</exception>
    internal void RequeueDestruction(RenderTextureTargetLifetime lifetime) {
        ArgumentNullException.ThrowIfNull(lifetime);

        lock (_gate) {
            if (_stopped || _closed || _disposed) {
                return;
            }

            _destroys.Enqueue(lifetime);
        }
    }

    /// <summary>
    /// Stops the service: rejects later requests and fails every queued creation request with
    /// <see cref="ObjectDisposedException"/>. Idempotent.
    /// </summary>
    internal void Stop() {
        CreateRequest[] requests;
        lock (_gate) {
            if (_stopped || _closed || _disposed) {
                return;
            }

            _stopped = true;
            requests = [.. _creates];
            _creates.Clear();
        }

        Exception exception = CreateStoppedException();
        foreach (CreateRequest request in requests) {
            request.DisposeRegistration();
            request.Completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Stops the service when the render loop ends. A queued creation request receives the loop failure
    /// when there is one, or <see cref="ObjectDisposedException"/> for a graceful stop. Idempotent.
    /// </summary>
    /// <param name="failure">The render loop failure, or <see langword="null"/> for a graceful stop.</param>
    internal void Close(Exception? failure) {
        CreateRequest[] requests;
        lock (_gate) {
            if (_closed || _disposed) {
                return;
            }

            _closed = true;
            _stopped = true;
            requests = [.. _creates];
            _creates.Clear();
            _destroys.Clear();
        }

        Exception exception = failure ?? CreateStoppedException();
        foreach (CreateRequest request in requests) {
            request.DisposeRegistration();
            request.Completion.TrySetException(exception);
        }
    }

    /// <summary>
    /// Disposes the service's wait handle. The app calls it after the render loop joined. Idempotent.
    /// </summary>
    public void Dispose() {
        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _stopped = true;
        }

        _workAvailable.Dispose();
    }

    private static ObjectDisposedException CreateStoppedException() => new(
        nameof(RenderTextureService),
        "The app has stopped; render textures cannot be created or used after shutdown.");

    /// <summary>
    /// One queued creation request and its single completion task.
    /// </summary>
    internal sealed class CreateRequest {
        private readonly RenderTextureService _owner;
        private CancellationTokenRegistration _registration;

        internal CreateRequest(
            RenderTextureService owner,
            RenderTextureDescription description,
            CancellationToken cancellationToken) {
            _owner = owner;
            Description = description;
            CancellationToken = cancellationToken;
            Completion = new TaskCompletionSource<RenderTexture>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>Gets the validated description.</summary>
        internal RenderTextureDescription Description { get; }

        /// <summary>Gets the caller's cancellation token.</summary>
        internal CancellationToken CancellationToken { get; }

        /// <summary>Gets the completion task source.</summary>
        internal TaskCompletionSource<RenderTexture> Completion { get; }

        internal void RegisterCancellation() {
            _registration = CancellationToken.Register(
                static state => ((CreateRequest)state!).Cancel(),
                this);
        }

        internal void DisposeRegistration() => _registration.Dispose();

        private void Cancel() {
            if (Completion.TrySetCanceled(CancellationToken)) {
                // Wake the render thread so it can skip the queued request or destroy a target it
                // already created.
                _owner._workAvailable.Set();
            }
        }
    }
}
