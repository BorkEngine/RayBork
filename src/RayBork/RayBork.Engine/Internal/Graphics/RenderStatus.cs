namespace RayBork.Internal;

/// <summary>
/// Holds the renderer status values behind <see cref="EngineContext.Info"/> and
/// <see cref="EngineContext.Stats"/>.
/// </summary>
/// <remarks>
/// The render thread writes and the tick thread reads; each property copies one value type, so
/// <see cref="Info"/> and <see cref="Stats"/> are independent, non-transactional snapshots.
/// </remarks>
internal sealed class RenderStatus
{
    private readonly Lock _gate = new();
    private EngineInfo _info = CreateDefaultInfo();
    private RenderStats _stats;

    /// <summary>Gets the last written engine information.</summary>
    internal EngineInfo Info
    {
        get
        {
            lock (_gate)
            {
                return _info;
            }
        }
    }

    /// <summary>Gets the last written statistics, describing the last completed frame.</summary>
    internal RenderStats Stats
    {
        get
        {
            lock (_gate)
            {
                return _stats;
            }
        }
    }

    /// <summary>Creates the default information used before the renderer starts.</summary>
    internal static EngineInfo CreateDefaultInfo() =>
        new(
            "RayBork",
            EngineVersion.Current,
            string.Empty,
            string.Empty,
            0u,
            string.Empty,
            false,
            0,
            PresentMode.Mailbox,
            false
        );

    /// <summary>Replaces the engine information on the render thread.</summary>
    /// <param name="info">The new value.</param>
    internal void SetInfo(EngineInfo info)
    {
        lock (_gate)
        {
            _info = info;
        }
    }

    /// <summary>Replaces the render statistics on the render thread.</summary>
    /// <param name="stats">The new value.</param>
    internal void SetStats(RenderStats stats)
    {
        lock (_gate)
        {
            _stats = stats;
        }
    }
}
