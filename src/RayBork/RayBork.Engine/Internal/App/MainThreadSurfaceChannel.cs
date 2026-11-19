using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>
/// Marshals native Vulkan surface creation and destruction from the render thread to the main
/// thread that owns the window.
/// </summary>
/// <remarks>
/// The render thread enqueues one request and blocks until the main thread services it, the channel
/// closes, or the wait times out. <see cref="TryService"/> captures platform failures into the
/// request result instead of throwing.
/// </remarks>
internal sealed class MainThreadSurfaceChannel : IRendererSurfaceProvider
{
    private const int REQUEST_TIMEOUT_MILLISECONDS = 30_000;

    private readonly Lock _gate = new();
    private readonly AutoResetEvent _requestQueued = new(false);

    private SurfaceRequest? _pending;
    private bool _closed;

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// The channel is closed, the wait timed out, or the platform failed; <c>RB3001</c>.
    /// </exception>
    public IntPtr CreateSurface(IntPtr instance)
    {
        IntPtr surface = Send(new SurfaceRequest(SurfaceOperation.Create, instance, IntPtr.Zero));
        if (surface == IntPtr.Zero)
        {
            throw new GraphicsException(
                "The main thread returned a zero Vulkan surface handle while creating the surface.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        return surface;
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// The channel is closed, the wait timed out, or the platform failed; <c>RB3001</c>.
    /// </exception>
    public void DestroySurface(IntPtr instance, IntPtr surface)
    {
        if (surface == IntPtr.Zero)
        {
            lock (_gate)
            {
                ThrowIfClosedLocked(SurfaceOperation.Destroy);
            }

            return;
        }

        Send(new SurfaceRequest(SurfaceOperation.Destroy, instance, surface));
    }

    /// <summary>Services at most one pending request on the calling main thread.</summary>
    /// <param name="platform">The initialized platform that owns the window.</param>
    /// <returns><see langword="true"/> when a request was serviced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="platform"/> is <c>null</c>.</exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "The main thread services a surface request queued by the render thread; any failure is recorded on the request so the render thread observes it instead of the main thread dying."
    )]
    internal bool TryService(IEnginePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (!_requestQueued.WaitOne(0))
        {
            return false;
        }

        SurfaceRequest? request;
        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            request = _pending;
            _pending = null;
        }

        if (request is null)
        {
            return false;
        }

        try
        {
            if (request.Operation == SurfaceOperation.Create)
            {
                request.Surface = platform.CreateVulkanSurface(request.Instance);
            }
            else
            {
                platform.DestroyVulkanSurface(request.Instance, request.Surface);
            }
        }
        catch (Exception exception)
        {
            request.Failure = exception;
        }

        request.Completion.Set();
        return true;
    }

    /// <summary>Closes the channel, failing a pending request; idempotent.</summary>
    public void Close()
    {
        SurfaceRequest? pending;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            pending = _pending;
            _pending = null;
        }

        if (pending is not null)
        {
            pending.Failure = new ObjectDisposedException(
                nameof(MainThreadSurfaceChannel),
                "The main-thread surface channel was closed."
            );
            pending.Completion.Set();
        }
    }

    private IntPtr Send(SurfaceRequest request)
    {
        lock (_gate)
        {
            ThrowIfClosedLocked(request.Operation);
            if (_pending is not null)
            {
                throw new InvalidOperationException(
                    "The main-thread surface channel already has a pending request; only the render thread may issue one at a time."
                );
            }

            _pending = request;
        }

        _requestQueued.Set();
        if (!request.Completion.Wait(REQUEST_TIMEOUT_MILLISECONDS))
        {
            lock (_gate)
            {
                if (ReferenceEquals(_pending, request))
                {
                    _pending = null;
                }
            }

            throw CreateFailure(
                request.Operation,
                new TimeoutException(
                    $"The main thread did not service the request within {REQUEST_TIMEOUT_MILLISECONDS} milliseconds."
                )
            );
        }

        if (request.Failure is not null)
        {
            throw CreateFailure(request.Operation, request.Failure);
        }

        return request.Surface;
    }

    private void ThrowIfClosedLocked(SurfaceOperation operation)
    {
        if (_closed)
        {
            throw CreateClosedFailure(Describe(operation));
        }
    }

    private static GraphicsException CreateClosedFailure(string operation) =>
        new(
            $"The main-thread surface channel is closed; cannot {operation}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            new ObjectDisposedException(nameof(MainThreadSurfaceChannel))
        );

    private static GraphicsException CreateFailure(SurfaceOperation operation, Exception failure) =>
        new(
            $"The main thread failed to {Describe(operation)}: {failure.Message}",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            failure
        );

    private static string Describe(SurfaceOperation operation) =>
        operation == SurfaceOperation.Create
            ? "create the Vulkan surface"
            : "destroy the Vulkan surface";

    private enum SurfaceOperation
    {
        Create,
        Destroy,
    }

    private sealed class SurfaceRequest
    {
        internal SurfaceRequest(SurfaceOperation operation, IntPtr instance, IntPtr surface)
        {
            Operation = operation;
            Instance = instance;
            Surface = surface;
        }

        internal SurfaceOperation Operation { get; }

        internal IntPtr Instance { get; }

        internal IntPtr Surface { get; set; }

        internal Exception? Failure { get; set; }

        internal ManualResetEventSlim Completion { get; } = new(false);
    }
}
