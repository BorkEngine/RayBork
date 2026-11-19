using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The per-font glyph pipeline: MSDF layout metrics, a bounded texture cache, and the bridge to the
/// app-lifetime <see cref="GlyphGenerationService"/>.
/// </summary>
/// <remarks>
/// The tick thread records frames, acquires textures, and reads metrics; only the service's font
/// thread inserts and evicts textures. One <see cref="Lock"/> guards the glyph state, and the cache
/// is hard-capped at <see cref="MAX_CACHED_TEXTURES"/>.
/// </remarks>
internal sealed class FontGlyphPipeline : IDisposable
{
    /// <summary>The maximum number of generated glyph textures one font caches.</summary>
    internal const int MAX_CACHED_TEXTURES = 512;

    private readonly SfntFace _face;
    private readonly FontGlyphSettings _settings;
    private readonly IGlyphTextureFactory _textureFactory;
    private readonly GlyphGenerationService _service;
    private readonly ILogger _logger;
    private readonly int _textureBudget;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, GlyphEntry> _glyphs = [];
    private readonly Dictionary<int, GlyphEntry> _cachedTextures = [];

    private long _frameIndex;
    private long _nextSequence;
    private bool _disposed;
    private bool _loggedNotStartedRejection;
    private bool _loggedStoppingRejection;

    /// <summary>
    /// Creates a pipeline with the production texture budget; the face's units per em must match the
    /// settings.
    /// </summary>
    internal FontGlyphPipeline(
        SfntFace face,
        FontGlyphSettings settings,
        IGlyphTextureFactory textureFactory,
        GlyphGenerationService service,
        ILogger logger
    )
        : this(face, settings, textureFactory, service, MAX_CACHED_TEXTURES, logger) { }

    /// <summary>
    /// Creates a pipeline with an explicit texture budget for tests; the budget must be positive and
    /// the face's units per em must match the settings.
    /// </summary>
    internal FontGlyphPipeline(
        SfntFace face,
        FontGlyphSettings settings,
        IGlyphTextureFactory textureFactory,
        GlyphGenerationService service,
        int textureBudget,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(textureFactory);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBudget);
        if (settings.UnitsPerEm != face.UnitsPerEm)
        {
            throw new ArgumentException(
                $"The glyph settings declare {settings.UnitsPerEm} units per em but the face declares "
                    + $"{face.UnitsPerEm}.",
                nameof(settings)
            );
        }

        _face = face;
        _settings = settings;
        _textureFactory = textureFactory;
        _service = service;
        _textureBudget = textureBudget;
        _logger = logger;
    }

    /// <summary>Gets the parsed face.</summary>
    internal SfntFace Face => _face;

    /// <summary>Gets the face's validated MSDF settings.</summary>
    internal FontGlyphSettings Settings => _settings;

    /// <summary>Gets the factory that uploads generated bitmaps.</summary>
    internal IGlyphTextureFactory TextureFactory => _textureFactory;

    /// <summary>Gets the cached texture count; zero after disposal. Diagnostic, for tests.</summary>
    internal int CachedGlyphCount
    {
        get
        {
            lock (_gate)
            {
                return _cachedTextures.Count;
            }
        }
    }

    /// <summary>Gets whether the pipeline is disposed.</summary>
    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>Records the frame index for the following <see cref="Acquire"/> calls.</summary>
    /// <param name="frameIndex">The non-negative tick frame index.</param>
    /// <remarks>
    /// Only the tick thread calls it, once per camera; the pipeline keeps the highest index, so a stale
    /// call is ignored.
    /// </remarks>
    internal void BeginFrame(long frameIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (frameIndex > _frameIndex)
            {
                _frameIndex = frameIndex;
            }
        }
    }

    /// <summary>
    /// Gets one glyph's layout metrics in em units without generating a texture.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>
    /// The face advance and padded quad; an empty glyph has the advance only and a zero quad.
    /// </returns>
    /// <remarks>
    /// A font-data failure falls back to the advance and a zero quad and is logged once per glyph.
    /// </remarks>
    internal TextGlyphMetrics GetMetrics(int glyphId)
    {
        ValidateGlyphId(glyphId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_glyphs.TryGetValue(glyphId, out GlyphEntry? cached) && cached.HasMetrics)
            {
                return cached.Metrics;
            }
        }

        (TextGlyphMetrics metrics, Exception? failure) = ComputeMetrics(glyphId);
        bool logFailure;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GlyphEntry entry = GetOrCreateLocked(glyphId);
            if (entry.HasMetrics)
            {
                return entry.Metrics;
            }

            entry.HasMetrics = true;
            entry.Metrics = metrics;
            logFailure = failure is not null && !entry.MetricsFailureLogged;
            if (logFailure)
            {
                entry.MetricsFailureLogged = true;
            }
        }

        if (logFailure)
        {
            _logger.LogWarning(
                failure,
                "The metrics of glyph {GlyphId} fall back to the face advance and a zero quad: {Reason}",
                glyphId,
                DescribeFailure(failure!)
            );
        }

        return metrics;
    }

    /// <summary>
    /// Acquires one glyph's cached texture for the current frame, or enqueues its generation.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="retention">
    /// The frame's retention set, which receives the texture's frame hold before the call returns.
    /// </param>
    /// <returns>
    /// The cached texture when ready; <see langword="null"/> when pending, empty, failed, absent, or
    /// disposed. A miss enqueues one request and never blocks.
    /// </returns>
    /// <remarks>
    /// Only the tick thread calls it; the recorder owns and releases the retention set. The added hold
    /// is idempotent per texture for the lifetime of the set.
    /// </remarks>
    internal Texture? Acquire(int glyphId, FrameAssetRetention retention)
    {
        ValidateGlyphId(glyphId);
        ArgumentNullException.ThrowIfNull(retention);

        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            GlyphEntry entry = GetOrCreateLocked(glyphId);
            switch (entry.State)
            {
                case GlyphState.Ready:
                    entry.LastUseStamp = _frameIndex;
                    retention.Add(entry.Texture!.Lifetime);
                    return entry.Texture;
                case GlyphState.Pending:
                case GlyphState.Empty:
                case GlyphState.Failed:
                    return null;
                default:
                    entry.State = GlyphState.Pending;
                    entry.Ready = CreateWaiter();
                    break;
            }
        }

        GlyphEnqueueResult enqueueResult = _service.Enqueue(this, glyphId);
        HandleEnqueueResult(glyphId, enqueueResult);
        return null;
    }

    /// <summary>
    /// Peeks at one glyph's cached texture without recording a use or adding a frame hold.
    /// </summary>
    /// <returns><see langword="true"/> when a texture is cached for the glyph.</returns>
    /// <remarks>Diagnostic and test peek; recording callers must use <see cref="Acquire"/>.</remarks>
    internal bool TryGetTexture(int glyphId, out Texture? texture)
    {
        ValidateGlyphId(glyphId);
        lock (_gate)
        {
            if (
                !_disposed
                && _glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                && entry.State == GlyphState.Ready
                && entry.Texture is not null
            )
            {
                texture = entry.Texture;
                return true;
            }

            texture = null;
            return false;
        }
    }

    /// <summary>
    /// Gets whether one glyph finished generation; an empty glyph counts as ready without a texture.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    internal bool IsGlyphReady(int glyphId)
    {
        ValidateGlyphId(glyphId);
        lock (_gate)
        {
            return !_disposed
                && _glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                && entry.State is GlyphState.Ready or GlyphState.Empty;
        }
    }

    /// <summary>
    /// Gets a task that completes when one glyph becomes ready, fails, or the pipeline shuts down.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>
    /// The pending settlement task, or a completed task when the glyph is already terminal or absent.
    /// </returns>
    /// <remarks>Test hook; call <see cref="Acquire"/> first for an ungenerated glyph.</remarks>
    internal Task WhenGlyphSettledAsync(int glyphId)
    {
        ValidateGlyphId(glyphId);
        lock (_gate)
        {
            if (
                _disposed
                || !_glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                || entry.State != GlyphState.Pending
            )
            {
                return Task.CompletedTask;
            }

            entry.Ready ??= CreateWaiter();
            return entry.Ready.Task;
        }
    }

    /// <summary>
    /// Inserts one generated texture on the font thread, taking ownership of its one owner reference.
    /// </summary>
    /// <remarks>
    /// The cache is hard-capped; the least-recently-used textures are evicted until the new entry fits
    /// and are disposed outside the lock. A redundant or late texture is disposed instead of cached.
    /// </remarks>
    internal void Insert(int glyphId, Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        Texture? redundant = null;
        List<Texture>? evicted = null;
        TaskCompletionSource? waiter = null;
        lock (_gate)
        {
            if (_disposed)
            {
                redundant = texture;
            }
            else
            {
                GlyphEntry entry = GetOrCreateLocked(glyphId);
                if (
                    entry.Texture is not null
                    || entry.State is GlyphState.Ready or GlyphState.Empty or GlyphState.Failed
                )
                {
                    redundant = texture;
                }
                else
                {
                    EvictForInsertLocked(ref evicted);
                    entry.Texture = texture;
                    entry.State = GlyphState.Ready;
                    entry.LastUseStamp = _frameIndex;
                    entry.Sequence = ++_nextSequence;
                    _cachedTextures[glyphId] = entry;
                    waiter = entry.Ready;
                    entry.Ready = null;
                }
            }
        }

        try
        {
            redundant?.Dispose();
            if (evicted is not null)
            {
                foreach (Texture evictedTexture in evicted)
                {
                    evictedTexture.Dispose();
                }
            }
        }
        finally
        {
            waiter?.TrySetResult();
        }
    }

    /// <summary>
    /// Marks one generated glyph ready without a texture, for an empty glyph such as a space.
    /// </summary>
    internal void CompleteEmpty(int glyphId)
    {
        TaskCompletionSource? waiter;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            GlyphEntry entry = GetOrCreateLocked(glyphId);
            if (entry.State is GlyphState.Ready or GlyphState.Empty or GlyphState.Failed)
            {
                return;
            }

            entry.State = GlyphState.Empty;
            waiter = entry.Ready;
            entry.Ready = null;
        }

        waiter?.TrySetResult();
    }

    /// <summary>
    /// Marks one pending glyph failed for the font's lifetime; later draws never retry it.
    /// </summary>
    internal void MarkGlyphFailed(int glyphId) => CompleteGlyph(glyphId, GlyphState.Failed);

    /// <summary>
    /// Gets whether the metrics path already logged a font-data failure for one glyph.
    /// </summary>
    internal bool HasLoggedMetricsFailure(int glyphId)
    {
        lock (_gate)
        {
            return _glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                && entry.MetricsFailureLogged;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Idempotent; requires the service to be stopped first. Unregisters the pipeline, disposes every
    /// cached texture once, and settles every pending waiter.
    /// </remarks>
    public void Dispose()
    {
        List<Texture> textures = [];
        List<TaskCompletionSource> waiters = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (GlyphEntry entry in _cachedTextures.Values)
            {
                textures.Add(entry.Texture!);
                entry.Texture = null;
                entry.State = GlyphState.Absent;
                entry.LastUseStamp = 0;
                entry.Sequence = 0;
            }

            _cachedTextures.Clear();
            foreach (GlyphEntry entry in _glyphs.Values)
            {
                if (entry.Ready is not null)
                {
                    waiters.Add(entry.Ready);
                    entry.Ready = null;
                }

                if (entry.State == GlyphState.Pending)
                {
                    entry.State = GlyphState.Failed;
                }
            }

            _glyphs.Clear();
        }

        _service.Unregister(this);
        try
        {
            foreach (Texture texture in textures)
            {
                texture.Dispose();
            }
        }
        finally
        {
            foreach (TaskCompletionSource waiter in waiters)
            {
                waiter.TrySetResult();
            }
        }
    }

    private (TextGlyphMetrics Metrics, Exception? Failure) ComputeMetrics(int glyphId)
    {
        float advanceX = _face.GetAdvanceWidth(glyphId) / (float)_settings.UnitsPerEm;
        try
        {
            FontGlyph glyph = _face.GetGlyph(glyphId);
            FontBounds bounds = glyph.Bounds;
            if (bounds.XMax < bounds.XMin || bounds.YMax < bounds.YMin)
            {
                throw new FontFormatException(
                    $"the glyph {glyphId} stores the inverted bounds ({bounds.XMin}, {bounds.YMin})-({bounds.XMax}, {bounds.YMax})"
                );
            }

            MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
                bounds,
                _settings.UnitsPerEm,
                _settings.MsdfPixelSize,
                _settings.MsdfRange,
                _settings.MsdfPadding
            );
            long texelCount = (long)layout.TextureWidth * layout.TextureHeight;
            if (texelCount > MsdfGenerator.MAX_TEXELS)
            {
                throw new MsdfLimitException(
                    $"the {layout.TextureWidth} by {layout.TextureHeight} glyph texture has {texelCount} texels; the limit is {MsdfGenerator.MAX_TEXELS}"
                );
            }

            return (
                new TextGlyphMetrics(
                    glyphId,
                    advanceX,
                    layout.BearingXEm,
                    layout.BearingYEm,
                    layout.WidthEm,
                    layout.HeightEm
                ),
                null
            );
        }
        catch (Exception exception)
            when (exception is FontUnsupportedException or FontFormatException or MsdfLimitException
            )
        {
            return (new TextGlyphMetrics(glyphId, advanceX, 0f, 0f, 0f, 0f), exception);
        }
    }

    private void HandleEnqueueResult(int glyphId, GlyphEnqueueResult result)
    {
        switch (result)
        {
            case GlyphEnqueueResult.Queued:
            case GlyphEnqueueResult.AlreadyPending:
                return;
            case GlyphEnqueueResult.RejectedNotStarted:
                LogRejectionOnce(notStarted: true);
                break;
            default:
                LogRejectionOnce(notStarted: false);
                break;
        }

        MarkGlyphFailed(glyphId);
    }

    private void LogRejectionOnce(bool notStarted)
    {
        bool log;
        lock (_gate)
        {
            if (notStarted)
            {
                log = !_loggedNotStartedRejection;
                _loggedNotStartedRejection = true;
            }
            else
            {
                log = !_loggedStoppingRejection;
                _loggedStoppingRejection = true;
            }
        }

        if (!log)
        {
            return;
        }

        if (notStarted)
        {
            _logger.LogWarning(
                "A glyph generation request was rejected because the RayBork.Font service was never started; load fonts only after the app starts the service."
            );
        }
        else
        {
            _logger.LogWarning(
                "A glyph generation request was rejected because the RayBork.Font service is stopping; the glyph is dropped."
            );
        }
    }

    private void CompleteGlyph(int glyphId, GlyphState state)
    {
        TaskCompletionSource? waiter;
        lock (_gate)
        {
            if (
                _disposed
                || !_glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                || entry.State != GlyphState.Pending
            )
            {
                return;
            }

            entry.State = state;
            waiter = entry.Ready;
            entry.Ready = null;
        }

        waiter?.TrySetResult();
    }

    private void EvictForInsertLocked(ref List<Texture>? evicted)
    {
        int required = _cachedTextures.Count - _textureBudget + 1;
        if (required <= 0)
        {
            return;
        }

        List<GlyphEntry> candidates = [.. _cachedTextures.Values];
        candidates.Sort(
            static (first, second) =>
            {
                int byUse = first.LastUseStamp.CompareTo(second.LastUseStamp);
                return byUse != 0 ? byUse : first.Sequence.CompareTo(second.Sequence);
            }
        );

        int count = Math.Min(required, candidates.Count);
        for (int index = 0; index < count; index++)
        {
            GlyphEntry victim = candidates[index];
            _cachedTextures.Remove(victim.GlyphId);
            evicted ??= [];
            evicted.Add(victim.Texture!);
            victim.Texture = null;
            victim.State = GlyphState.Absent;
            victim.LastUseStamp = 0;
            victim.Sequence = 0;
        }
    }

    private GlyphEntry GetOrCreateLocked(int glyphId)
    {
        if (_glyphs.TryGetValue(glyphId, out GlyphEntry? entry))
        {
            return entry;
        }

        entry = new GlyphEntry(glyphId);
        _glyphs.Add(glyphId, entry);
        return entry;
    }

    private void ValidateGlyphId(int glyphId)
    {
        if ((uint)glyphId < (uint)_face.GlyphCount)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(glyphId),
            glyphId,
            $"The face has {_face.GlyphCount} glyph(s)."
        );
    }

    private static string DescribeFailure(Exception failure) =>
        failure switch
        {
            FontFormatException format => format.Reason,
            FontUnsupportedException unsupported => unsupported.Reason,
            MsdfLimitException limit => limit.Reason,
            _ => failure.Message,
        };

    private static TaskCompletionSource CreateWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum GlyphState
    {
        Absent,
        Pending,
        Ready,
        Empty,
        Failed,
    }

    private sealed class GlyphEntry
    {
        internal GlyphEntry(int glyphId) => GlyphId = glyphId;

        internal int GlyphId { get; }

        internal GlyphState State;

        internal TextGlyphMetrics Metrics;

        internal bool HasMetrics;

        internal bool MetricsFailureLogged;

        internal Texture? Texture;

        internal long LastUseStamp;

        internal long Sequence;

        internal TaskCompletionSource? Ready;
    }
}
