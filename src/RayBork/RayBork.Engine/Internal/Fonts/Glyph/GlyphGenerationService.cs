using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The app-lifetime service that generates and uploads MSDF glyph textures for every
/// <see cref="FontGlyphPipeline"/> on one dedicated font thread.
/// </summary>
/// <remarks>
/// The service owns one <c>RayBork.Font</c> thread and a FIFO queue that deduplicates each
/// (pipeline, glyph) pair across queued and in-flight work. <see cref="Stop"/> must run after the
/// tick thread exits and before the render loop joins, never from the font thread.
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

    /// <summary>Creates the service; the app starts it before loading a font.</summary>
    internal GlyphGenerationService(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _logger = loggerFactory.CreateLogger(LOGGER_CATEGORY);
    }

    /// <summary>Gets whether the service started and has not stopped accepting work.</summary>
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

    /// <summary>
    /// Gets the font thread, or <see langword="null"/> before <see cref="Start"/>; for tests.
    /// </summary>
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
    /// Gets the number of concurrent <see cref="Stop"/> callers waiting for the first stop; for tests.
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

    /// <summary>Starts the font thread and begins accepting requests.</summary>
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

    /// <summary>Queues one glyph generation request; safe from any thread.</summary>
    /// <returns>
    /// <see cref="GlyphEnqueueResult.Queued"/>, <see cref="GlyphEnqueueResult.AlreadyPending"/>, or a
    /// rejection before <see cref="Start"/> or while stopping.
    /// </returns>
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

    /// <summary>Removes every queued request of one pipeline when it disposes.</summary>
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

    /// <summary>Gets a task that completes when the queue is empty and nothing is in flight.</summary>
    /// <remarks>Test hook.</remarks>
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
    /// Stops the loop before it dequeues the next request; an in-flight request finishes or observes
    /// the stop token.
    /// </summary>
    /// <remarks>Test hook; pair with <see cref="Resume"/>.</remarks>
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
    /// Lets a paused loop dequeue again and signals it for work that arrived while paused.
    /// </summary>
    /// <remarks>Test hook; a no-op after <see cref="Stop"/>.</remarks>
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
    /// Stops the service, cancels queued and in-flight work, and joins the font thread; idempotent.
    /// </summary>
    /// <remarks>
    /// A second concurrent caller waits for the first stop. The join is unbounded, so the factory must
    /// observe the stop token.
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
    /// Generates and uploads one glyph on the font thread; every failure is terminal for the glyph.
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
