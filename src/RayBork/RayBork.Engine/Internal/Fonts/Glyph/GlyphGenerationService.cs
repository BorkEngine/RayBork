using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The app-lifetime background service that generates and uploads MSDF glyph textures for every
/// <see cref="FontGlyphPipeline"/> on one dedicated font thread.
/// </summary>
/// <remarks>
/// <para>
/// The service owns exactly one <see cref="Thread"/> named <c>RayBork.Font</c> and a FIFO queue of
/// glyph requests. <see cref="Enqueue"/> is safe from any thread and deduplicates one
/// <c>(pipeline, glyph)</c> pair across queued and in-flight work, so a draw that misses the cache
/// every frame adds one request, not one per frame. The loop takes one request at a time: it resolves
/// the outline, computes the layout and MSDF bitmap, calls the pipeline's texture factory, and inserts
/// the uploaded texture. The upload may complete asynchronously; the loop blocks on that task on the
/// font thread, so generation stays serialized and ordered.
/// </para>
/// <para>
/// Preconditions. The app starts the service before the tick thread starts and before any font is
/// loaded; a pipeline that requests work from a service that was never started fails its glyph and
/// logs one warning. The factory task must complete on a thread that never waits for
/// <c>RayBork.Font</c>; the production path is the GPU upload bridge completed by the render thread.
/// <see cref="Stop"/> must run after the tick thread exits, before the asset catalog stops, and before
/// the render loop joins, so the render thread still services the upload bridge and can complete or
/// cancel the in-flight glyph upload. Never call <see cref="Stop"/> from the font thread (it throws)
/// or from the factory's completion thread, which would deadlock the join the factory is needed for.
/// </para>
/// <para>
/// A pipeline may be disposed before <see cref="Stop"/>: a game can dispose a font at any time, and the
/// catalog stops the service only when the app shuts down. Early disposal is supported.
/// <see cref="FontGlyphPipeline.Dispose"/> calls <see cref="Unregister"/>, which removes that pipeline's
/// queued requests and settles their waiters; a request that was already generating finishes and its
/// late texture is disposed by the pipeline's insert path instead of being cached. The service never
/// enqueues new work for a disposed pipeline.
/// </para>
/// <para>
/// A generation or upload failure is logged at warning level and marks the glyph failed for the font's
/// lifetime; the service never retries it and the rest of the text keeps rendering. One font-data
/// failure is reported once per glyph: when the metrics path already logged it, the service does not
/// log the same terminal glyph again. The MSDF generator bounds the work of one request, so the thread
/// cannot be pinned by a single glyph beyond those documented limits.
/// </para>
/// <para>
/// Lock order: <see cref="Enqueue"/> takes the service lock and then reads the pipeline's disposed
/// flag; no path takes the pipeline lock while taking the service lock. The acquire path takes the
/// pipeline, then <see cref="FrameAssetRetention"/>, then the asset's own lock. The service calls a
/// pipeline only for that disposed read while holding its lock; cancellation callbacks and joins run
/// outside it.
/// </para>
/// <para>
/// Shutdown order: <see cref="Stop"/> cancels the stop token (which cancels an in-flight upload),
/// settles the waiters of queued requests, joins the font thread, settles the idle waiters, and
/// releases the service resources. It is idempotent, holds no lock across the join, and throws
/// <see cref="InvalidOperationException"/> when called from the font thread instead of deadlocking on a
/// self-join. A second concurrent caller waits for the first join to finish. A pipeline disposed
/// earlier already unregistered itself, so no queued request can point at it when the service stops.
/// </para>
/// </remarks>
internal sealed class GlyphGenerationService : IDisposable
{
    /// <summary>The generation thread and logger category name.</summary>
    internal const string THREAD_NAME = "RayBork.Font";

    /// <summary>The logger category for generation diagnostics.</summary>
    internal const string LOGGER_CATEGORY = "RayBork.Font";

    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly Queue<GlyphRequest> _queue = [];
    private readonly HashSet<GlyphRequestKey> _pending = [];
    private readonly List<TaskCompletionSource> _idleWaiters = [];
    private readonly AutoResetEvent _wake = new(false);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly TaskCompletionSource _stopCompletion = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private Thread? _generationThread;
    private bool _started;
    private bool _stopRequested;
    private bool _paused;
    private bool _disposed;
    private int _stopWaiterCount;
    private GlyphRequest? _active;

    /// <summary>
    /// Initializes the service. The app creates one instance for its lifetime and starts it before
    /// loading a font.
    /// </summary>
    /// <param name="loggerFactory">The app logger factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="loggerFactory"/> is <c>null</c>.</exception>
    internal GlyphGenerationService(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(LOGGER_CATEGORY);
    }

    /// <summary>
    /// Gets whether the service started and has not stopped accepting work. Before
    /// <see cref="Start"/> and after <see cref="Stop"/> it is <see langword="false"/>.
    /// </summary>
    internal bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _started && !_stopRequested;
            }
        }
    }

    /// <summary>Gets the font thread, or <see langword="null"/> before <see cref="Start"/>. Diagnostic, for tests.</summary>
    internal Thread? GenerationThread
    {
        get
        {
            lock (_gate)
            {
                return _generationThread;
            }
        }
    }

    /// <summary>
    /// Gets the number of concurrent <see cref="Stop"/> callers waiting for the first stop to finish.
    /// Diagnostic, for tests.
    /// </summary>
    internal int StopWaiterCount
    {
        get
        {
            lock (_gate)
            {
                return _stopWaiterCount;
            }
        }
    }

    /// <summary>
    /// Starts the font thread. The service accepts requests from that point until <see cref="Stop"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service already stopped.</exception>
    /// <exception cref="InvalidOperationException">The service was already started.</exception>
    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException(
                    "The glyph generation service is already started."
                );
            }

            Thread thread = new(RunLoop) { IsBackground = true, Name = THREAD_NAME };
            thread.Start();
            _generationThread = thread;
            _started = true;
        }
    }

    /// <summary>
    /// Queues one glyph generation request. Safe from any thread.
    /// </summary>
    /// <param name="pipeline">The font pipeline that owns the glyph.</param>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <returns>
    /// <see cref="GlyphEnqueueResult.Queued"/> for a new request,
    /// <see cref="GlyphEnqueueResult.AlreadyPending"/> when the pair is already queued or in flight,
    /// <see cref="GlyphEnqueueResult.RejectedNotStarted"/> before <see cref="Start"/>, and
    /// <see cref="GlyphEnqueueResult.RejectedStopping"/> while stopping or after the pipeline was
    /// disposed.
    /// </returns>
    /// <remarks>
    /// The service lock is held while the pipeline's disposed flag is read; no engine path takes the
    /// pipeline lock before the service lock, so the two locks cannot deadlock.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pipeline"/> is <c>null</c>.</exception>
    internal GlyphEnqueueResult Enqueue(FontGlyphPipeline pipeline, int glyphId)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        lock (_gate)
        {
            if (pipeline.IsDisposed || _disposed || _stopRequested)
            {
                return GlyphEnqueueResult.RejectedStopping;
            }

            if (!_started)
            {
                return GlyphEnqueueResult.RejectedNotStarted;
            }

            GlyphRequestKey key = new(pipeline, glyphId);
            if (!_pending.Add(key))
            {
                return GlyphEnqueueResult.AlreadyPending;
            }

            _queue.Enqueue(new GlyphRequest(pipeline, glyphId));
            _wake.Set();
            return GlyphEnqueueResult.Queued;
        }
    }

    /// <summary>
    /// Removes every queued request of one pipeline. <see cref="FontGlyphPipeline.Dispose"/> calls it so
    /// no disposed pipeline keeps queue entries; an in-flight request releases its texture through the
    /// disposed pipeline's insert path instead.
    /// </summary>
    /// <param name="pipeline">The pipeline that is disposing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipeline"/> is <c>null</c>.</exception>
    internal void Unregister(FontGlyphPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        List<GlyphRequest> removed = [];
        TaskCompletionSource[] idleWaiters;
        lock (_gate)
        {
            if (_queue.Count > 0)
            {
                int count = _queue.Count;
                for (int index = 0; index < count; index++)
                {
                    GlyphRequest request = _queue.Dequeue();
                    if (ReferenceEquals(request.Pipeline, pipeline))
                    {
                        _pending.Remove(new GlyphRequestKey(request.Pipeline, request.GlyphId));
                        removed.Add(request);
                    }
                    else
                    {
                        _queue.Enqueue(request);
                    }
                }
            }

            idleWaiters = _queue.Count == 0 && _active is null ? TakeIdleWaitersLocked() : [];
        }

        foreach (GlyphRequest request in removed)
        {
            request.Pipeline.MarkGlyphFailed(request.GlyphId);
        }

        CompleteIdleWaiters(idleWaiters);
    }

    /// <summary>
    /// Gets a task that completes when the queue is empty and nothing is in flight.
    /// </summary>
    /// <returns>A completed task when the service is already idle, or the pending idle task.</returns>
    /// <remarks>Test hook. Pausing does not count as idleness when requests are queued.</remarks>
    internal Task WhenIdleAsync()
    {
        lock (_gate)
        {
            if (_queue.Count == 0 && _active is null)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _idleWaiters.Add(waiter);
            return waiter.Task;
        }
    }

    /// <summary>
    /// Stops the loop before it dequeues the next request. A request already generating or uploading
    /// finishes or observes the stop token.
    /// </summary>
    /// <remarks>
    /// Test hook. Pair it with <see cref="Resume"/>; the pause is not a cancellation.
    /// </remarks>
    internal void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _stopRequested)
            {
                return;
            }

            _paused = true;
        }
    }

    /// <summary>
    /// Lets a paused loop dequeue again and signals it for any work that arrived while paused.
    /// </summary>
    /// <remarks>Test hook. After <see cref="Stop"/> it is a no-op.</remarks>
    internal void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _stopRequested)
            {
                return;
            }

            _paused = false;
            _wake.Set();
        }
    }

    /// <summary>
    /// Stops the service, cancels queued and in-flight work, and joins the font thread. Idempotent.
    /// </summary>
    /// <remarks>
    /// A second concurrent caller waits for the first stop to finish, including its join. The join is
    /// unbounded: the factory must observe the stop token so an in-flight upload can be canceled.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Called from the font thread.</exception>
    internal void Stop()
    {
        GlyphRequest[] queued;
        TaskCompletionSource[] idleWaiters;
        Thread? thread;
        bool waitForFirstStop = false;
        lock (_gate)
        {
            if (ReferenceEquals(Thread.CurrentThread, _generationThread))
            {
                throw new InvalidOperationException(
                    "The glyph generation service cannot be stopped from its own font thread."
                );
            }

            if (_stopRequested)
            {
                waitForFirstStop = true;
                queued = [];
                idleWaiters = [];
                thread = null;
            }
            else
            {
                _disposed = true;
                _stopRequested = true;
                queued = [.. _queue];
                _queue.Clear();
                _pending.Clear();
                thread = _generationThread;
            }
        }

        if (waitForFirstStop)
        {
            lock (_gate)
            {
                _stopWaiterCount++;
            }

            try
            {
                _stopCompletion.Task.GetAwaiter().GetResult();
            }
            finally
            {
                lock (_gate)
                {
                    _stopWaiterCount--;
                }
            }

            return;
        }

        try
        {
            try
            {
                _stopSource.Cancel();
            }
            catch (AggregateException)
            {
                // A factory cancellation callback must never stop shutdown; the join below still
                // observes the canceled token.
            }

            lock (_gate)
            {
                _wake.Set();
            }

            // Queued requests never ran, so their waiters would otherwise hang. Settle them before the
            // join; the in-flight request settles its own waiter when it observes the stop token.
            foreach (GlyphRequest request in queued)
            {
                request.Pipeline.MarkGlyphFailed(request.GlyphId);
            }

            thread?.Join();

            lock (_gate)
            {
                idleWaiters = TakeIdleWaitersLocked();
            }

            CompleteIdleWaiters(idleWaiters);
        }
        finally
        {
            try
            {
                _stopSource.Dispose();
                _wake.Dispose();
            }
            finally
            {
                _stopCompletion.TrySetResult();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Same as <see cref="Stop"/>.</remarks>
    public void Dispose() => Stop();

    private void RunLoop()
    {
        while (true)
        {
            _wake.WaitOne();
            while (true)
            {
                GlyphRequest request;
                lock (_gate)
                {
                    if (_stopRequested)
                    {
                        return;
                    }

                    if (_paused || _queue.Count == 0)
                    {
                        break;
                    }

                    request = _queue.Dequeue();
                    _active = request;
                }

                Execute(request);

                TaskCompletionSource[] idleWaiters;
                lock (_gate)
                {
                    _pending.Remove(new GlyphRequestKey(request.Pipeline, request.GlyphId));
                    _active = null;
                    idleWaiters = _queue.Count == 0 ? TakeIdleWaitersLocked() : [];
                }

                CompleteIdleWaiters(idleWaiters);
            }
        }
    }

    /// <summary>
    /// Generates and uploads one glyph on the font thread. Every failure is terminal for the glyph: it
    /// is logged once (unless the metrics path already reported the same outline failure) and the
    /// pipeline never retries it.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "One glyph generation failure is recorded on its request; the service must keep serving the remaining glyphs."
    )]
    private void Execute(GlyphRequest request)
    {
        FontGlyphPipeline pipeline = request.Pipeline;
        int glyphId = request.GlyphId;
        try
        {
            _stopSource.Token.ThrowIfCancellationRequested();
            FontGlyph glyph = pipeline.Face.GetGlyph(glyphId);
            FontGlyphSettings settings = pipeline.Settings;
            MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
                glyph.Bounds,
                settings.UnitsPerEm,
                settings.MsdfPixelSize,
                settings.MsdfRange,
                settings.MsdfPadding
            );
            if (layout.IsEmpty)
            {
                pipeline.CompleteEmpty(glyphId);
                return;
            }

            byte[] bitmap = MsdfGenerator.Generate(glyph.Outline, layout);
            Texture texture = pipeline
                .TextureFactory.CreateAsync(
                    layout.TextureWidth,
                    layout.TextureHeight,
                    bitmap,
                    _stopSource.Token
                )
                .GetAwaiter()
                .GetResult();
            if (texture is null)
            {
                throw new InvalidOperationException(
                    $"The glyph texture factory returned no texture for glyph {glyphId}."
                );
            }

            pipeline.Insert(glyphId, texture);
        }
        catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
        {
            pipeline.MarkGlyphFailed(glyphId);
        }
        catch (Exception exception)
        {
            if (!pipeline.HasLoggedMetricsFailure(glyphId))
            {
                _logger.LogWarning(
                    exception,
                    "The glyph {GlyphId} could not be generated; it stays failed for this font.",
                    glyphId
                );
            }

            pipeline.MarkGlyphFailed(glyphId);
        }
    }

    private TaskCompletionSource[] TakeIdleWaitersLocked()
    {
        if (_idleWaiters.Count == 0)
        {
            return [];
        }

        TaskCompletionSource[] waiters = [.. _idleWaiters];
        _idleWaiters.Clear();
        return waiters;
    }

    private static void CompleteIdleWaiters(TaskCompletionSource[] waiters)
    {
        foreach (TaskCompletionSource waiter in waiters)
        {
            waiter.TrySetResult();
        }
    }

    private readonly record struct GlyphRequestKey(FontGlyphPipeline Pipeline, int GlyphId);

    private readonly record struct GlyphRequest(FontGlyphPipeline Pipeline, int GlyphId);
}
