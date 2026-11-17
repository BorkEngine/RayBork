using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The per-font glyph pipeline: layout metrics without a texture, a hard-bounded cache of generated
/// MSDF glyph textures, and the bridge to the app-lifetime <see cref="GlyphGenerationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Font</c> asset owns one pipeline per parsed face. The tick thread calls only
/// <see cref="BeginFrame"/>, <see cref="Acquire"/>, and <see cref="GetMetrics"/>; the font thread of the
/// generation service is the only thread that inserts and evicts textures. It resolves outlines,
/// generates MSDF bitmaps, and inserts finished textures. The tick thread updates the use stamps, the
/// glyph states, the waiters, and the frame index while it calls the three methods. One
/// <see cref="Lock"/> guards both caches and all of that state, and it is never held across an
/// <c>await</c> or a blocking wait.
/// </para>
/// <para>
/// <see cref="BeginFrame"/> records the frame index supplied by
/// <see cref="RenderFramePacket.FrameIndex"/> once per camera; the pipeline keeps the highest value, so
/// a repeated or stale call never moves the cache clock backwards. <see cref="Acquire"/> is the draw
/// path: a miss marks the glyph pending, records a waiter, and enqueues exactly one generation request;
/// a pending glyph draws nothing this call and appears on a later frame. A hit returns the cached
/// texture and adds its frame hold to the caller's <see cref="FrameAssetRetention"/> while the cache
/// lock is held, so eviction cannot release the owner reference between the lookup and the hold.
/// Acquire adds the hold and never releases it; the recorder owns the retention set and the frame ring
/// releases it after the frame completes.
/// </para>
/// <para>
/// The texture cache is hard-capped at <see cref="MAX_CACHED_TEXTURES"/>. Every insert that would
/// exceed the budget evicts the least-recently-used textures in one batch, regardless of the frame that
/// used them, and disposes their owner references outside the cache lock. Eviction can dispose a
/// texture wrapper in the same frame that acquired it; the already-recorded frame hold keeps the GPU
/// resource behind <see cref="Texture.Lifetime"/> alive until the frame ring releases it. Eviction and
/// disposal release only the owner reference; they never add or release a frame hold.
/// </para>
/// <para>
/// Failure is terminal per glyph for the font's lifetime: a failed generation or upload marks the
/// glyph failed and is never retried. An empty glyph (such as a space) becomes ready without a texture.
/// Metrics fall back to the face advance with a zero quad when an outline cannot be resolved or its
/// texture would exceed the generator's texel budget; the font-data failure is logged once and cached,
/// and the generation service does not log the same terminal glyph again.
/// </para>
/// <para>
/// Lock order: <see cref="GlyphGenerationService.Enqueue"/> takes the service lock and then the
/// pipeline's disposed flag; no path takes the pipeline lock while taking the service lock.
/// <see cref="Acquire"/> may hold the pipeline lock while adding a frame hold, so that order is
/// pipeline, then <see cref="FrameAssetRetention"/>, then the asset's own lock. No path takes the
/// pipeline lock while holding either of those.
/// </para>
/// <para>
/// A pipeline may be disposed while the service is still running, for example when a game disposes a
/// font before shutdown. <see cref="Dispose"/> unregisters the pipeline, so queued requests for it are
/// dropped and their waiters settle as failed; a request that was already generating finishes and its
/// late texture reaches <see cref="Insert"/>, which disposes that texture instead of caching it. After
/// disposal the service never enqueues new work for the pipeline. Disposal is idempotent, disposes
/// every cached texture exactly once, and settles every pending waiter.
/// </para>
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
    /// Initializes a pipeline with the production texture budget.
    /// </summary>
    /// <param name="face">The parsed face; its <c>unitsPerEm</c> must match the settings.</param>
    /// <param name="settings">The face's validated MSDF parameters.</param>
    /// <param name="textureFactory">The factory that uploads a generated bitmap.</param>
    /// <param name="service">The app-lifetime generation service that runs the requests.</param>
    /// <param name="logger">The font logger; it receives the per-glyph diagnostics.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="settings"/> declares a different <c>unitsPerEm</c> than <paramref name="face"/>.
    /// </exception>
    internal FontGlyphPipeline(
        SfntFace face,
        FontGlyphSettings settings,
        IGlyphTextureFactory textureFactory,
        GlyphGenerationService service,
        ILogger logger
    )
        : this(face, settings, textureFactory, service, MAX_CACHED_TEXTURES, logger) { }

    /// <summary>
    /// Initializes a pipeline with an explicit texture budget. Tests use a small budget; production
    /// code uses <see cref="MAX_CACHED_TEXTURES"/>.
    /// </summary>
    /// <param name="face">The parsed face; its <c>unitsPerEm</c> must match the settings.</param>
    /// <param name="settings">The face's validated MSDF parameters.</param>
    /// <param name="textureFactory">The factory that uploads a generated bitmap.</param>
    /// <param name="service">The app-lifetime generation service that runs the requests.</param>
    /// <param name="textureBudget">The maximum number of cached textures; it must be positive.</param>
    /// <param name="logger">The font logger; it receives the per-glyph diagnostics.</param>
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
    /// <remarks>It is zero after disposal.</remarks>
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

    /// <summary>Gets whether the pipeline is disposed. The generation service reads it before enqueueing.</summary>
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

    /// <summary>
    /// Records the frame that the following <see cref="Acquire"/> calls belong to.
    /// </summary>
    /// <param name="frameIndex">
    /// The tick frame index from <see cref="RenderFramePacket.FrameIndex"/>; it is non-negative.
    /// </param>
    /// <remarks>
    /// Single owner: only the tick thread calls this method, once per camera, before it records that
    /// camera's text. The pipeline keeps the highest value, so every camera in one tick may pass the
    /// same index and a stale index is ignored. The font thread never calls it. After disposal it is a
    /// no-op.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameIndex"/> is negative.</exception>
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
    /// Gets the layout metrics of one glyph, in em units, without generating a texture.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>
    /// The glyph's advance from the face and its padded quad from the MSDF layout. An empty glyph has
    /// the advance only, with a zero bearing and size.
    /// </returns>
    /// <remarks>
    /// The result is cached per glyph. A glyph whose stored box is inverted, whose outline cannot be
    /// resolved, or whose layout would exceed the generator's texel budget logs one warning for that
    /// font-data failure and falls back to the face advance with a zero bearing and size, so it draws
    /// nothing but still advances the pen; the generation service does not log the same terminal glyph
    /// again. The method is thread-safe, never touches the GPU, and never uses the cache clock.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="ObjectDisposedException">The pipeline is disposed.</exception>
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
    /// Acquires the cached texture of one glyph for the current frame, or enqueues its generation.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="retention">
    /// The frame's retention set. When the glyph's texture is cached, the pipeline adds the texture's
    /// frame hold to this set before returning, so eviction cannot strand the recorder.
    /// </param>
    /// <returns>
    /// The cached texture when the glyph is ready, or <see langword="null"/> when it is pending, empty,
    /// failed, absent, or the pipeline is disposed. A miss enqueues one generation request and never
    /// blocks.
    /// </returns>
    /// <remarks>
    /// Single owner: only the tick thread calls this method, after <see cref="BeginFrame"/>. Pass the
    /// retention set of the frame being recorded; a set that was already released ignores the hold, so
    /// a stale set cannot resurrect a texture. The added frame hold is idempotent per distinct texture
    /// for the lifetime of the set, so acquiring the same glyph twice in one frame does not double-add
    /// it. The recorder owns and releases the retention set; the pipeline never releases the hold. A
    /// stopped generation service rejects the request, the glyph settles as failed, and one warning per
    /// rejection reason is logged for the pipeline.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="retention"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
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
    /// Peeks at the cached texture of one glyph without recording a use or adding a frame hold.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="texture">The cached texture, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a texture is cached for the glyph.</returns>
    /// <remarks>
    /// This is a diagnostic and test peek. It does not add a frame hold, so a recorder must use
    /// <see cref="Acquire"/> instead. After disposal it returns <see langword="false"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
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
    /// Gets whether one glyph finished generation. An empty glyph is ready even though it has no
    /// texture, because nothing will ever be drawn for it.
    /// </summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns><see langword="true"/> for a ready empty or textured glyph; otherwise <see langword="false"/>.</returns>
    /// <remarks>After disposal it returns <see langword="false"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
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
    /// Inspect <see cref="IsGlyphReady"/> after the task completes to distinguish ready from failed.
    /// </returns>
    /// <remarks>
    /// Test hook. Call <see cref="Acquire"/> first when the glyph is not generated yet; an absent glyph
    /// has no pending request, so its task is already complete. An evicted glyph returns to absent, so
    /// its task completes and a later <see cref="Acquire"/> enqueues a fresh generation. After disposal
    /// it returns a completed task.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
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
    /// Inserts one generated texture. The generation service calls it on the font thread.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <param name="texture">
    /// The uploaded texture. The pipeline takes ownership of its one owner reference.
    /// </param>
    /// <remarks>
    /// The cache is hard-capped: the method evicts the least-recently-used textures in one batch until
    /// the new entry fits, regardless of the frame that last used them. At capacity it copies and sorts
    /// at most <c>budget + 1</c> cached entries under the lock; the cost is proportional to the cache
    /// size, not to the face's glyph count. A redundant or late texture for an already terminal or
    /// disposed pipeline is disposed instead of leaked. Evicted textures are disposed outside the cache
    /// lock, and the glyph waiter is settled even when a disposal throws.
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
    /// Marks one generated glyph ready without a texture. The generation service calls it on the font
    /// thread for an empty glyph such as a space.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
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
    /// Marks one pending glyph failed for the font's lifetime. The generation service calls it for a
    /// generation or upload failure, for a stop cancellation, and for a rejected request; the caller
    /// owns the diagnostic, so this method itself never logs. Later draws do not retry the glyph.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    internal void MarkGlyphFailed(int glyphId) => CompleteGlyph(glyphId, GlyphState.Failed);

    /// <summary>
    /// Gets whether the metrics path already logged a font-data failure for one glyph. The generation
    /// service suppresses its own warning for that glyph, so one font-data failure is reported once and
    /// a later generation failure of the same terminal glyph is not logged again.
    /// </summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <returns><see langword="true"/> when a metrics fallback warning was already logged.</returns>
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
    /// Idempotent. Unregisters the pipeline from the generation service, disposes every cached texture
    /// exactly once, and settles every pending waiter even when a texture disposal throws. The service
    /// must be stopped first.
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
