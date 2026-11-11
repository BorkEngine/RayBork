using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// A FIFO handoff for work the render thread must run on the main thread after the render thread has
/// stopped.
/// </summary>
/// <remarks>
/// <para>
/// The render thread enqueues teardown work that cannot run there, such as destroying a Vulkan
/// instance whose loader unloads driver libraries that registered thread-local state on the render
/// thread. The main thread drains the queue after it has joined the render thread and before it
/// destroys the window.
/// </para>
/// <para>
/// <see cref="Enqueue"/> is safe from any thread. <see cref="RunPending"/> is main-thread-only: it
/// runs every queued action in FIFO order, captures and logs an action's exception at Error, and
/// never throws. The queue is reusable; draining an empty queue is a no-op.
/// </para>
/// </remarks>
internal sealed class MainThreadTeardownQueue {
    private readonly Lock _gate = new();
    private readonly Queue<Action> _pending = new();

    /// <summary>
    /// Appends a teardown action for the main thread to run. Safe from any thread.
    /// </summary>
    /// <param name="action">The main-thread work to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    internal void Enqueue(Action action) {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate) {
            _pending.Enqueue(action);
        }
    }

    /// <summary>
    /// Runs every queued action on the calling thread in FIFO order.
    /// </summary>
    /// <remarks>
    /// An action that throws is logged at Error and does not stop the remaining actions. The method
    /// never throws for a failed action.
    /// </remarks>
    /// <param name="logger">The logger that receives an action's failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    internal void RunPending(ILogger logger) {
        ArgumentNullException.ThrowIfNull(logger);
        while (true) {
            Action? action;
            lock (_gate) {
                if (_pending.Count == 0) {
                    return;
                }

                action = _pending.Dequeue();
            }

            try {
                action();
            } catch (Exception exception) {
                logger.LogError(exception, "A main-thread teardown action failed.");
            }
        }
    }
}
