namespace RayBork.Internal;

/// <summary>
/// Holds the renderer status values behind <see cref="EngineContext.Info"/> and
/// <see cref="EngineContext.Stats"/>.
/// </summary>
/// <remarks>
/// <para>
/// The render thread writes the values and the tick thread reads them. Each property returns a copy
/// of a value type, so <see cref="Info"/> and <see cref="Stats"/> are independent, non-transactional
/// snapshots: a reader can observe a newer <see cref="Stats"/> with an older <see cref="Info"/>.
/// </para>
/// <para>
/// The lock is held for a struct copy only and is never held across a wait, a callback, or an
/// <c>await</c>. A reader therefore blocks for at most one writer's copy.
/// </para>
/// </remarks>
internal sealed class RenderStatus {
    private readonly Lock _gate = new();
    private EngineInfo _info = CreateDefaultInfo();
    private RenderStats _stats;

    /// <summary>
    /// Gets the last written engine information. The render thread writes it at startup and after a
    /// recovery that changes the device or presentation state.
    /// </summary>
    internal EngineInfo Info {
        get {
            lock (_gate) {
                return _info;
            }
        }
    }

    /// <summary>
    /// Gets the last written render statistics, which describe the last completed frame.
    /// </summary>
    internal RenderStats Stats {
        get {
            lock (_gate) {
                return _stats;
            }
        }
    }

    /// <summary>
    /// Gets the default <see cref="EngineInfo"/> used before the renderer starts: the engine name and
    /// version with no backend, device, or surface selected.
    /// </summary>
    /// <returns>The default engine information.</returns>
    internal static EngineInfo CreateDefaultInfo() => new(
        "RayBork",
        EngineVersion.Current,
        string.Empty,
        string.Empty,
        0u,
        string.Empty,
        false,
        0,
        PresentMode.Mailbox,
        false);

    /// <summary>
    /// Replaces the engine information. Called on the render thread.
    /// </summary>
    /// <param name="info">The new value.</param>
    internal void SetInfo(EngineInfo info) {
        lock (_gate) {
            _info = info;
        }
    }

    /// <summary>
    /// Replaces the render statistics. Called on the render thread after a frame completes.
    /// </summary>
    /// <param name="stats">The new value.</param>
    internal void SetStats(RenderStats stats) {
        lock (_gate) {
            _stats = stats;
        }
    }
}
