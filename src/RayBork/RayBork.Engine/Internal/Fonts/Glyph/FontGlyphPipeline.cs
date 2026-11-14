using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The per-font glyph pipeline: layout metrics without a texture, a bounded cache of generated MSDF
/// glyph textures, and the bridge to the app-lifetime <see cref="GlyphGenerationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The later <c>Font</c> asset owns one pipeline per parsed face. The tick thread calls only
/// <see cref="GetMetrics"/> and <see cref="Acquire"/>; the font thread of the generation service is the
/// only thread that resolves outlines, generates MSDF bitmaps, and inserts finished textures. The
/// metrics cache and the texture cache are guarded by one <see cref="Lock"/>, which is never held
/// across an <c>await</c> or a blocking wait, and the pipeline never blocks the calling thread.
/// </para>
/// <para>
/// <see cref="Acquire"/> is the draw path. A miss marks the glyph pending, records a waiter, and
/// enqueues exactly one generation request; the tick thread keeps rendering the rest of the run and
/// the glyph appears on a later frame. A hit returns the cached texture and records the caller's use
/// stamp, which the eviction rule reads. The stamp is a monotonically non-decreasing frame or tick
/// counter: every <see cref="Acquire"/> in one frame passes the same value, so a glyph recorded in the
/// current frame is protected from eviction until the next frame. The first frame that requests more
/// new glyphs than the cache budget allows overflows temporarily; the next insert at a later stamp
/// trims the cache back to the budget. The metrics cache is not budgeted: it holds one small entry per
/// glyph the app asked about, bounded by the face's glyph count (at most 65,535).
/// </para>
/// <para>
/// Cancellation and failure are terminal per glyph for the font's lifetime: a failed generation or
/// upload leaves the glyph failed and is never retried, and an empty glyph (such as a space) becomes
/// ready without a texture. <see cref="WhenGlyphReadyAsync"/> completes for a ready, empty, failed, or
/// shut-down glyph; callers inspect <see cref="IsGlyphReady"/> to tell the outcomes apart.
/// </para>
/// <para>
/// Lifetime: <see cref="GlyphGenerationService.Stop"/> must run before any pipeline is disposed; the
/// reverse order would leave queued work pointing at a disposed texture owner. Disposal is idempotent,
/// unregisters the pipeline from the service, disposes every cached texture exactly once, and
/// completes every pending waiter. Frame retention keeps a texture that a submitted frame still
/// references alive even when eviction releases the pipeline's owner reference.
/// </para>
/// </remarks>
internal sealed class FontGlyphPipeline : IDisposable {
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

    private long _latestStamp;
    private long _nextSequence;
    private bool _disposed;

    /// <summary>
    /// Initializes a pipeline with the production texture budget.
    /// </summary>
    /// <param name="face">The parsed face; its <c>unitsPerEm</c> must match the settings.</param>
    /// <param name="settings">The face's validated MSDF parameters.</param>
    /// <param name="textureFactory">The factory that uploads a generated bitmap.</param>
    /// <param name="service">The app-lifetime generation service that runs the requests.</param>
    /// <param name="logger">The font logger; it receives one warning per glyph with unreadable metrics.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="settings"/> declares a different <c>unitsPerEm</c> than <paramref name="face"/>.
    /// </exception>
    internal FontGlyphPipeline(
        SfntFace face,
        FontGlyphSettings settings,
        IGlyphTextureFactory textureFactory,
        GlyphGenerationService service,
        ILogger logger)
        : this(face, settings, textureFactory, service, MAX_CACHED_TEXTURES, logger) {
    }

    /// <summary>
    /// Initializes a pipeline with an explicit texture budget. Tests use a small budget; production
    /// code uses <see cref="MAX_CACHED_TEXTURES"/>.
    /// </summary>
    /// <param name="face">The parsed face; its <c>unitsPerEm</c> must match the settings.</param>
    /// <param name="settings">The face's validated MSDF parameters.</param>
    /// <param name="textureFactory">The factory that uploads a generated bitmap.</param>
    /// <param name="service">The app-lifetime generation service that runs the requests.</param>
    /// <param name="textureBudget">The maximum number of cached textures; it must be positive.</param>
    /// <param name="logger">The font logger; it receives one warning per glyph with unreadable metrics.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textureBudget"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="settings"/> declares a different <c>unitsPerEm</c> than <paramref name="face"/>.
    /// </exception>
    internal FontGlyphPipeline(
        SfntFace face,
        FontGlyphSettings settings,
        IGlyphTextureFactory textureFactory,
        GlyphGenerationService service,
        int textureBudget,
        ILogger logger) {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(textureFactory);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBudget);
        if (settings.UnitsPerEm != face.UnitsPerEm) {
            throw new ArgumentException(
                $"The glyph settings declare {settings.UnitsPerEm} units per em but the face declares "
                    + $"{face.UnitsPerEm}.",
                nameof(settings));
        }

        _face = face;
        _settings = settings;
        _textureFactory = textureFactory;
        _service = service;
        _textureBudget = textureBudget;
        _logger = logger;
    }

    /// <summary>Gets the parsed face. The generation service reads it on the font thread.</summary>
    internal SfntFace Face => _face;

    /// <summary>Gets the face's MSDF settings. The generation service reads them on the font thread.</summary>
    internal FontGlyphSettings Settings => _settings;

    /// <summary>
    /// Gets the texture factory. The generation service calls it on the font thread after it generated
    /// a bitmap.
    /// </summary>
    internal IGlyphTextureFactory TextureFactory => _textureFactory;

    /// <summary>Gets the number of cached textures. Diagnostic, for tests.</summary>
    /// <remarks>
    /// The count can temporarily exceed the texture budget after one frame requested more new glyphs
    /// than the cache holds while no cached texture was older than the inserting stamp. The next insert
    /// at a later stamp trims it back to the budget.
    /// </remarks>
    internal int CachedGlyphCount {
        get {
            lock (_gate) {
                return _cachedTextures.Count;
            }
        }
    }

    /// <summary>
    /// Gets the layout metrics of one glyph, in em units, without generating a texture.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>
    /// The glyph's advance from the face and its padded quad from the MSDF layout. An empty glyph has
    /// the advance only, with a zero bearing and size.
    /// </returns>
    /// <remarks>
    /// The result is cached per glyph. A glyph whose stored box is inverted or whose outline cannot be
    /// resolved logs one warning and falls back to the face advance with a zero bearing and size, so a
    /// malformed or unsupported glyph draws nothing but still advances the pen. The method is
    /// thread-safe and never touches the GPU.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="ObjectDisposedException">The pipeline is disposed.</exception>
    internal TextGlyphMetrics GetMetrics(int glyphId) {
        ValidateGlyphId(glyphId);
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_glyphs.TryGetValue(glyphId, out GlyphEntry? cached) && cached.HasMetrics) {
                return cached.Metrics;
            }
        }

        (TextGlyphMetrics metrics, Exception? failure) = ComputeMetrics(glyphId);
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GlyphEntry entry = GetOrCreateLocked(glyphId);
            if (entry.HasMetrics) {
                return entry.Metrics;
            }

            entry.HasMetrics = true;
            entry.Metrics = metrics;
        }

        if (failure is not null) {
            _logger.LogWarning(
                failure,
                "The metrics of glyph {GlyphId} use the face advance and a zero quad; the outline could not be resolved.",
                glyphId);
        }

        return metrics;
    }

    /// <summary>
    /// Acquires the cached texture of one glyph for the current frame, or enqueues its generation.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="stamp">
    /// The caller's non-negative, monotonically non-decreasing use stamp, normally the frame counter.
    /// Every <see cref="Acquire"/> in one frame passes the same value.
    /// </param>
    /// <returns>
    /// The cached texture when the glyph is ready, or <see langword="null"/> when it is pending, empty,
    /// failed, or absent. A miss enqueues one generation request and never blocks.
    /// </returns>
    /// <remarks>
    /// The returned texture stays valid for the caller this frame because eviction never takes an entry
    /// used at the inserting stamp. The record path must use this method rather than
    /// <see cref="TryGetTexture"/>, which does not record a use stamp. The generation service must be
    /// running; a request rejected by a stopped service leaves the glyph failed instead of blocking.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="glyphId"/> is outside the face, or <paramref name="stamp"/> is negative.
    /// </exception>
    internal Texture? Acquire(int glyphId, long stamp) {
        ValidateGlyphId(glyphId);
        ArgumentOutOfRangeException.ThrowIfNegative(stamp);

        bool enqueue = false;
        lock (_gate) {
            if (_disposed) {
                return null;
            }

            if (stamp > _latestStamp) {
                _latestStamp = stamp;
            }

            GlyphEntry entry = GetOrCreateLocked(glyphId);
            switch (entry.State) {
                case GlyphState.Ready:
                    if (stamp > entry.LastUseStamp) {
                        entry.LastUseStamp = stamp;
                    }

                    return entry.Texture;
                case GlyphState.Pending:
                case GlyphState.Empty:
                case GlyphState.Failed:
                    return null;
                default:
                    entry.State = GlyphState.Pending;
                    entry.Ready = CreateWaiter();
                    enqueue = true;
                    break;
            }
        }

        if (enqueue && !_service.Enqueue(this, glyphId)) {
            CancelGlyph(glyphId);
        }

        return null;
    }

    /// <summary>
    /// Peeks at the cached texture of one glyph without recording a use stamp.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="texture">The cached texture, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a texture is cached for the glyph.</returns>
    /// <remarks>
    /// This is a diagnostic peek for preload checks and tests. A frame that records the texture must use
    /// <see cref="Acquire"/> so the glyph cannot be evicted while that frame is recording.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    internal bool TryGetTexture(int glyphId, out Texture? texture) {
        ValidateGlyphId(glyphId);
        lock (_gate) {
            if (!_disposed
                && _glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                && entry.State == GlyphState.Ready
                && entry.Texture is not null) {
                texture = entry.Texture;
                return true;
            }

            texture = null;
            return false;
        }
    }

    /// <summary>
    /// Gets whether one glyph finished generation. An empty glyph is ready even though it has no
    /// texture, because nothing will ever be drawn for it.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns><see langword="true"/> for a ready empty or textured glyph; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    internal bool IsGlyphReady(int glyphId) {
        ValidateGlyphId(glyphId);
        lock (_gate) {
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
    /// The pending readiness task, or a completed task when the glyph is already terminal or absent.
    /// Inspect <see cref="IsGlyphReady"/> after the task completes to distinguish ready from failed.
    /// </returns>
    /// <remarks>
    /// Test hook. Call <see cref="Acquire"/> first when the glyph is not generated yet; an absent glyph
    /// has no pending request, so its task is already complete.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    internal Task WhenGlyphReadyAsync(int glyphId) {
        ValidateGlyphId(glyphId);
        lock (_gate) {
            if (_disposed
                || !_glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                || entry.State is GlyphState.Ready or GlyphState.Empty or GlyphState.Failed) {
                return Task.CompletedTask;
            }

            entry.Ready ??= CreateWaiter();
            return entry.Ready.Task;
        }
    }

    /// <summary>
    /// Inserts one generated texture. The generation service calls it on the font thread.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <param name="texture">
    /// The uploaded texture. The pipeline takes ownership of its one owner reference.
    /// </param>
    /// <remarks>
    /// The insert stamp is the highest use stamp acquired so far. The method evicts least-recently-used
    /// textures whose last use is older than that stamp until the cache fits the budget, and allows a
    /// temporary overflow when every cached texture was used at the inserting stamp. A redundant or
    /// late texture for an already terminal glyph is disposed instead of leaked. Evicted textures are
    /// disposed outside the cache lock.
    /// </remarks>
    internal void Insert(int glyphId, Texture texture) {
        ArgumentNullException.ThrowIfNull(texture);

        Texture? redundant = null;
        List<Texture>? evicted = null;
        TaskCompletionSource? waiter = null;
        lock (_gate) {
            GlyphEntry entry = GetOrCreateLocked(glyphId);
            if (_disposed
                || entry.Texture is not null
                || entry.State is GlyphState.Ready or GlyphState.Empty or GlyphState.Failed) {
                redundant = texture;
            } else {
                while (_cachedTextures.Count >= _textureBudget) {
                    GlyphEntry? victim = FindEvictionCandidateLocked(_latestStamp);
                    if (victim is null) {
                        break;
                    }

                    _cachedTextures.Remove(victim.GlyphId);
                    evicted ??= [];
                    evicted.Add(victim.Texture!);
                    victim.Texture = null;
                    victim.State = GlyphState.Absent;
                    victim.LastUseStamp = 0;
                    victim.Sequence = 0;
                }

                entry.Texture = texture;
                entry.State = GlyphState.Ready;
                entry.LastUseStamp = _latestStamp;
                entry.Sequence = ++_nextSequence;
                _cachedTextures[glyphId] = entry;
                waiter = entry.Ready;
                entry.Ready = null;
            }
        }

        redundant?.Dispose();
        if (evicted is not null) {
            foreach (Texture evictedTexture in evicted) {
                evictedTexture.Dispose();
            }
        }

        waiter?.TrySetResult();
    }

    /// <summary>
    /// Marks one generated glyph ready without a texture. The generation service calls it on the font
    /// thread for an empty glyph such as a space.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    internal void CompleteEmpty(int glyphId) {
        TaskCompletionSource? waiter;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            GlyphEntry entry = GetOrCreateLocked(glyphId);
            if (entry.State is GlyphState.Ready or GlyphState.Empty or GlyphState.Failed) {
                return;
            }

            entry.State = GlyphState.Empty;
            waiter = entry.Ready;
            entry.Ready = null;
        }

        waiter?.TrySetResult();
    }

    /// <summary>
    /// Marks one glyph failed for the font's lifetime. The generation service calls it after a
    /// generation or upload failure so later draws do not retry it.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    internal void MarkGlyphFailed(int glyphId) => CompleteGlyph(glyphId, GlyphState.Failed);

    /// <summary>
    /// Marks one pending glyph failed because the generation service stopped before it ran. Same
    /// terminal state as <see cref="MarkGlyphFailed"/>, without a failure log.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    internal void CancelGlyph(int glyphId) => CompleteGlyph(glyphId, GlyphState.Failed);

    /// <inheritdoc/>
    /// <remarks>
    /// Idempotent. Unregisters the pipeline from the generation service, disposes every cached texture
    /// exactly once, and completes every pending waiter. The service must be stopped first.
    /// </remarks>
    public void Dispose() {
        List<Texture> textures = [];
        List<TaskCompletionSource> waiters = [];
        lock (_gate) {
            if (_disposed) {
                return;
            }

            _disposed = true;
            foreach (GlyphEntry entry in _cachedTextures.Values) {
                textures.Add(entry.Texture!);
                entry.Texture = null;
                entry.State = GlyphState.Absent;
                entry.LastUseStamp = 0;
                entry.Sequence = 0;
            }

            _cachedTextures.Clear();
            foreach (GlyphEntry entry in _glyphs.Values) {
                if (entry.Ready is not null) {
                    waiters.Add(entry.Ready);
                    entry.Ready = null;
                }

                if (entry.State == GlyphState.Pending) {
                    entry.State = GlyphState.Failed;
                }
            }

            _glyphs.Clear();
        }

        _service.Unregister(this);
        foreach (Texture texture in textures) {
            texture.Dispose();
        }

        foreach (TaskCompletionSource waiter in waiters) {
            waiter.TrySetResult();
        }
    }

    private (TextGlyphMetrics Metrics, Exception? Failure) ComputeMetrics(int glyphId) {
        float advanceX = _face.GetAdvanceWidth(glyphId) / (float)_settings.UnitsPerEm;
        try {
            FontGlyph glyph = _face.GetGlyph(glyphId);
            FontBounds bounds = glyph.Bounds;
            if (bounds.XMax < bounds.XMin || bounds.YMax < bounds.YMin) {
                throw new FontFormatException(
                    $"the glyph {glyphId} stores the inverted bounds ({bounds.XMin}, {bounds.YMin})-({bounds.XMax}, {bounds.YMax})");
            }

            MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
                bounds,
                _settings.UnitsPerEm,
                _settings.MsdfPixelSize,
                _settings.MsdfRange,
                _settings.MsdfPadding);
            return (
                new TextGlyphMetrics(
                    glyphId,
                    advanceX,
                    layout.BearingXEm,
                    layout.BearingYEm,
                    layout.WidthEm,
                    layout.HeightEm),
                null);
        } catch (Exception exception) when (exception is FontUnsupportedException
            or FontFormatException
            or MsdfLimitException) {
            return (new TextGlyphMetrics(glyphId, advanceX, 0f, 0f, 0f, 0f), exception);
        }
    }

    private void CompleteGlyph(int glyphId, GlyphState state) {
        TaskCompletionSource? waiter;
        lock (_gate) {
            if (_disposed
                || !_glyphs.TryGetValue(glyphId, out GlyphEntry? entry)
                || entry.State != GlyphState.Pending) {
                return;
            }

            entry.State = state;
            waiter = entry.Ready;
            entry.Ready = null;
        }

        waiter?.TrySetResult();
    }

    private GlyphEntry? FindEvictionCandidateLocked(long insertingStamp) {
        GlyphEntry? candidate = null;
        foreach (GlyphEntry entry in _cachedTextures.Values) {
            if (entry.Texture is null || entry.LastUseStamp >= insertingStamp) {
                continue;
            }

            if (candidate is null
                || entry.LastUseStamp < candidate.LastUseStamp
                || (entry.LastUseStamp == candidate.LastUseStamp && entry.Sequence < candidate.Sequence)) {
                candidate = entry;
            }
        }

        return candidate;
    }

    private GlyphEntry GetOrCreateLocked(int glyphId) {
        if (_glyphs.TryGetValue(glyphId, out GlyphEntry? entry)) {
            return entry;
        }

        entry = new GlyphEntry(glyphId);
        _glyphs.Add(glyphId, entry);
        return entry;
    }

    private void ValidateGlyphId(int glyphId) {
        if ((uint)glyphId < (uint)_face.GlyphCount) {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(glyphId),
            glyphId,
            $"The face has {_face.GlyphCount} glyph(s).");
    }

    private static TaskCompletionSource CreateWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private enum GlyphState {
        Absent,
        Pending,
        Ready,
        Empty,
        Failed,
    }

    private sealed class GlyphEntry {
        internal GlyphEntry(int glyphId) => GlyphId = glyphId;

        internal int GlyphId { get; }

        internal GlyphState State;

        internal TextGlyphMetrics Metrics;

        internal bool HasMetrics;

        internal Texture? Texture;

        internal long LastUseStamp;

        internal long Sequence;

        internal TaskCompletionSource? Ready;
    }
}
