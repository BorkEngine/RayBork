namespace RayBork.Internal;

/// <summary>
/// A <see cref="SynchronizationContext"/> that queues posted callbacks for the tick thread.
/// Callbacks run only while the owning thread pumps: <see cref="PumpUntilComplete"/> pumps while an
/// asynchronous setup or shutdown task is pending, and <see cref="TryPumpOne"/> drains without
/// blocking. <see cref="Post"/> may be called from any thread and wakes the pump.
/// </summary>
/// <remarks>
/// The pump is single-threaded and tick-thread affine. Call <see cref="Dispose"/> on the owning
/// thread after pumping stops; queued callbacks are discarded and later posts throw
/// <see cref="ObjectDisposedException"/>.
/// </remarks>
internal sealed class TickSynchronizationContext : SynchronizationContext, IDisposable {
    private readonly Lock _gate = new();
    private readonly Queue<WorkItem> _queue = new();
    private readonly AutoResetEvent _workAvailable = new(false);

    private bool _disposed;

    /// <summary>
    /// Queues <paramref name="d"/> and wakes a blocked pump.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="d"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context was disposed.</exception>
    public override void Post(SendOrPostCallback d, object? state) {
        ArgumentNullException.ThrowIfNull(d);

        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _queue.Enqueue(new WorkItem(d, state));
            _workAvailable.Set();
        }
    }

    /// <summary>
    /// Runs one queued callback, blocking until one is posted. Callback exceptions propagate from
    /// this method.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The context was disposed.</exception>
    internal void PumpOne() {
        while (true) {
            ThrowIfDisposed();
            if (TryPumpOne()) {
                return;
            }
            _workAvailable.WaitOne();
        }
    }

    /// <summary>
    /// Runs one queued callback if a callback is pending, without blocking. Returns <c>false</c>
    /// when the queue is empty or the context is disposed. Callback exceptions propagate.
    /// </summary>
    internal bool TryPumpOne() {
        WorkItem item;
        lock (_gate) {
            if (_disposed || _queue.Count == 0) {
                return false;
            }
            item = _queue.Dequeue();
        }

        item.Callback(item.State);
        return true;
    }

    /// <summary>
    /// Pumps queued callbacks until <paramref name="task"/> completes, waiting on the queue without
    /// spinning when it is empty. The task's exception is rethrown unwrapped.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The context was disposed while pumping.</exception>
    internal void PumpUntilComplete(Task task) {
        ArgumentNullException.ThrowIfNull(task);
        ThrowIfDisposed();

        using PumpWait wait = new();
        _ = task.ContinueWith(
            static (_, state) => ((PumpWait)state!).Signal(),
            wait,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        WaitHandle[] waitHandles = [_workAvailable, wait.CompletedHandle];

        while (!task.IsCompleted) {
            ThrowIfDisposed();
            if (TryPumpOne()) {
                continue;
            }
            WaitHandle.WaitAny(waitHandles);
        }

        task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Discards queued callbacks and rejects later posts. Dispose from the owning thread only.
    /// </summary>
    public void Dispose() {
        lock (_gate) {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _queue.Clear();
        }

        _workAvailable.Dispose();
    }

    private void ThrowIfDisposed() {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private readonly struct WorkItem {
        public WorkItem(SendOrPostCallback callback, object? state) {
            Callback = callback;
            State = state;
        }

        public SendOrPostCallback Callback { get; }

        public object? State { get; }
    }

    /// <summary>
    /// A per-pump completion wake-up. The continuation may run after the pump has torn the wait
    /// down, so signaling is guarded by the same lock that disposes the event.
    /// </summary>
    private sealed class PumpWait : IDisposable {
        private readonly Lock _gate = new();
        private readonly AutoResetEvent _completed = new(false);

        private bool _disposed;

        public WaitHandle CompletedHandle {
            get {
                lock (_gate) {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return _completed;
                }
            }
        }

        public void Signal() {
            lock (_gate) {
                if (_disposed) {
                    return;
                }
                _completed.Set();
            }
        }

        public void Dispose() {
            lock (_gate) {
                if (_disposed) {
                    return;
                }
                _disposed = true;
                _completed.Dispose();
            }
        }
    }
}
