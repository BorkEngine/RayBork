namespace RayBork.Internal;

/// <summary>Tracks whether the calling thread is the engine tick thread.</summary>
/// <remarks>
/// The tick runner attaches the tick thread before <see cref="IGame.Setup"/> and detaches it after
/// <see cref="IGame.Shutdown"/>; the flag is thread-static and requires a balanced pair.
/// </remarks>
internal static class TickThread
{
    [ThreadStatic]
    private static bool _isAttached;

    /// <summary>Gets whether the calling thread is attached as the tick thread.</summary>
    internal static bool IsCurrent => _isAttached;

    /// <summary>Attaches the calling thread as the tick thread.</summary>
    /// <exception cref="InvalidOperationException">The calling thread is already attached.</exception>
    internal static void Attach()
    {
        if (_isAttached)
        {
            throw new InvalidOperationException(
                "The calling thread is already attached as the tick thread."
            );
        }

        _isAttached = true;
    }

    /// <summary>Detaches the calling thread from the tick-thread role.</summary>
    /// <exception cref="InvalidOperationException">The calling thread is not attached.</exception>
    internal static void Detach()
    {
        if (!_isAttached)
        {
            throw new InvalidOperationException(
                "The calling thread is not attached as the tick thread."
            );
        }

        _isAttached = false;
    }

    /// <summary>Requires the calling thread to be the attached tick thread.</summary>
    /// <param name="operation">The operation name used in the exception message.</param>
    /// <exception cref="ArgumentException"><paramref name="operation"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The calling thread is not attached.</exception>
    internal static void RequireCurrent(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (!_isAttached)
        {
            throw new InvalidOperationException(
                $"'{operation}' is tick-thread only; call it from IGame.Setup, IGame.Tick, or "
                    + "IGame.Shutdown."
            );
        }
    }
}
