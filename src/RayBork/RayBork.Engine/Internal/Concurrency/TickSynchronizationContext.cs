namespace RayBork.Internal;

/// <summary>
/// A <see cref="SynchronizationContext"/> that queues posted callbacks for the tick thread.
/// </summary>
/// <remarks>
/// Callbacks run only while the owning thread pumps. <see cref="Dispose"/> must run on that thread;
/// queued callbacks are discarded and later posts throw <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class TickSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Queue<WorkItem> _queue = new();
    private readonly AutoResetEvent _workAvailable = new(false);

    private bool _disposed;

    /// <summary>Queues <paramref name="d"/> and wakes a blocked pump.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="d"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context was disposed.</exception>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Enqueue(new WorkItem(d, state));
            _workAvailable.Set();
        }
    }

    /// <summary>Runs one queued callback, blocking until one is posted.</summary>
    /// <exception cref="ObjectDisposedException">The context was disposed.</exception>
    internal void PumpOne()
    {
        while (true)
        {
            ThrowIfDisposed();
            if (TryPumpOne())
            {
                return;
            }
            _workAvailable.WaitOne();
        }
    }

    /// <summary>Runs one queued callback if one is pending, without blocking.</summary>
    internal bool TryPumpOne()
    {
        WorkItem item;
        lock (_gate)
        {
            if (_disposed || _queue.Count == 0)
            {
                return false;
            }
            item = _queue.Dequeue();
        }

        item.Callback(item.State);
        return true;
    }

    /// <summary>Pumps queued callbacks until <paramref name="task"/> completes.</summary>
    /// <param name="task">The setup or shutdown task to pump until.</param>
    /// <exception cref="ObjectDisposedException">The context was disposed while pumping.</exception>
    internal void PumpUntilComplete(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ThrowIfDisposed();

        using PumpWait wait = new();
        _ = task.ContinueWith(
            static (_, state) => ((PumpWait)state!).Signal(),
            wait,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        WaitHandle[] waitHandles = [_workAvailable, wait.CompletedHandle];

        while (!task.IsCompleted)
        {
            ThrowIfDisposed();
            if (TryPumpOne())
            {
                continue;
            }
            WaitHandle.WaitAny(waitHandles);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>Discards queued callbacks and rejects later posts; call on the owning thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _queue.Clear();
        }

        _workAvailable.Dispose();
    }

    private void ThrowIfDisposed()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private readonly struct WorkItem
    {
        public WorkItem(SendOrPostCallback callback, object? state)
        {
            Callback = callback;
            State = state;
        }

        public SendOrPostCallback Callback { get; }

        public object? State { get; }
    }

    /// <summary>Per-pump completion wake-up.</summary>
    /// <remarks>The continuation may outlive the pump, so signaling shares the dispose lock.</remarks>
    private sealed class PumpWait : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly AutoResetEvent _completed = new(false);

        private bool _disposed;

        public WaitHandle CompletedHandle
        {
            get
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return _completed;
                }
            }
        }

        public void Signal()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _completed.Set();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _completed.Dispose();
            }
        }
    }
}
