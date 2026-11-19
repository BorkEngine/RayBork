using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>A FIFO handoff of teardown work that must run on the main thread.</summary>
/// <remarks>
/// The main thread drains it after joining the render thread and before destroying the window.
/// <see cref="Enqueue"/> is safe from any thread; <see cref="RunPending"/> logs a failing action and
/// never throws.
/// </remarks>
internal sealed class MainThreadTeardownQueue
{
    private readonly Lock _gate = new();
    private readonly Queue<Action> _pending = new();

    /// <summary>Appends teardown work for the main thread; safe from any thread.</summary>
    /// <param name="action">The main-thread work to run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    internal void Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate)
        {
            _pending.Enqueue(action);
        }
    }

    /// <summary>Runs every queued action on the calling thread in FIFO order.</summary>
    /// <remarks>A failing action is logged at Error; the remaining actions still run.</remarks>
    /// <param name="logger">The logger that receives an action's failure.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Teardown steps are queued by the render thread at shutdown; a failing step is logged and the remaining steps still run."
    )]
    internal void RunPending(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        while (true)
        {
            Action? action;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                action = _pending.Dequeue();
            }

            try
            {
                action();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A main-thread teardown action failed.");
            }
        }
    }
}
