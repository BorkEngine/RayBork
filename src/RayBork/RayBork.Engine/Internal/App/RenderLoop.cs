namespace RayBork.Internal;

/// <summary>
/// Consumes the newest published frame until the ring completes. In M1 there is no GPU work, so the
/// loop releases each lease immediately.
/// </summary>
/// <remarks>
/// The loop owns one render thread. Exceptions are captured in <see cref="Failure"/> instead of
/// escaping the thread. <see cref="Join"/> is safe only after <see cref="Start"/> returned, and the
/// failure may be read after the join.
/// </remarks>
internal sealed class RenderLoop {
    private const string THREAD_NAME = "RayBork.Render";

    private readonly FrameRing _frameRing;
    private readonly Thread _thread;

    private volatile Exception? _failure;
    private int _started;

    /// <summary>
    /// Initializes the loop for <paramref name="frameRing"/> without starting the render thread.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="frameRing"/> is <c>null</c>.</exception>
    internal RenderLoop(FrameRing frameRing) {
        ArgumentNullException.ThrowIfNull(frameRing);
        _frameRing = frameRing;
        _thread = new Thread(RunThread) {
            IsBackground = true,
            Name = THREAD_NAME,
        };
    }

    /// <summary>
    /// Gets the render thread. It is created by the constructor and runs after <see cref="Start"/>.
    /// </summary>
    internal Thread Thread => _thread;

    /// <summary>
    /// Gets the render thread name.
    /// </summary>
    internal string ThreadName => _thread.Name ?? THREAD_NAME;

    /// <summary>
    /// Gets the unexpected failure that stopped the loop, or <see langword="null"/>. Read it after
    /// <see cref="Join"/>.
    /// </summary>
    internal Exception? Failure => _failure;

    /// <summary>
    /// Starts the render thread. The loop can be started once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The loop was already started.</exception>
    internal void Start() {
        if (Interlocked.Exchange(ref _started, 1) != 0) {
            throw new InvalidOperationException("The render loop has already been started.");
        }

        _thread.Start();
    }

    /// <summary>
    /// Blocks until the render thread exits. Call it after the frame ring is complete.
    /// </summary>
    internal void Join() => _thread.Join();

    private void RunThread() {
        try {
            while (true) {
                FrameLease? lease = _frameRing.AcquireNewest(CancellationToken.None);
                if (lease is null) {
                    return;
                }

                try {
                    // M1 has no GPU work: the newest frame is consumed and released immediately.
                } finally {
                    _frameRing.Release(lease.Value);
                }
            }
        } catch (Exception exception) {
            _failure = exception;
        }
    }
}
