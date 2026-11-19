using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The runtime asset catalog that mounts packs, resolves visible entries, and loads assets on one
/// loader thread.
/// </summary>
/// <remarks>
/// Mounting is atomic and later packs shadow earlier ones on the same canonical path.
/// Engine assets are cached by canonical path and type, coalesced, and reference-counted; other
/// values are caller-owned and never cached.
/// </remarks>
internal sealed class AssetCatalog : IAssetCatalog, IProceduralAssetProvider
{
    /// <summary>The loader thread name.</summary>
    internal const string LOADER_THREAD_NAME = "RayBork.Asset";

    /// <summary>The logger category for catalog diagnostics.</summary>
    internal const string LOGGER_CATEGORY = "RayBork.Asset";

    /// <summary>The queued/in-flight request bound, a backstop against a stopped loader.</summary>
    internal const int MAX_PENDING_LOADS = 2048;

    private const string BUILT_IN_DISPLAY_PATH = "<built-in>";

    private readonly ILogger _logger;
    private readonly ILogger _fontLogger;
    private readonly GpuAssetBridge _bridge;
    private readonly ProceduralAssetService _proceduralAssets;
    private readonly GlyphGenerationService _glyphService;
    private readonly ProceduralGlyphTextureFactory _glyphTextures;
    private readonly ReadOnlyMemory<byte> _builtInPack;
    private readonly string[] _userPackPaths;
    private readonly AssetTypeRegistry _registry;
    private readonly Lock _gate = new();
    private readonly Queue<AssetLoaderWorkItem> _queue = new();
    private readonly HashSet<AssetLoaderWorkItem> _pending = [];
    private readonly Dictionary<AssetKey, EngineCacheEntry> _engineCache = [];
    private readonly AutoResetEvent _workAvailable = new(false);
    private readonly CancellationTokenSource _stopSource = new();

    private Dictionary<string, VisibleAsset> _visible = new(StringComparer.Ordinal);
    private PackArchive[] _packs = [];
    private Thread? _loaderThread;
    private bool _started;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>Creates a catalog without mounting a pack.</summary>
    /// <remarks>
    /// Empty built-in bytes add no entry; relative user pack paths resolve against
    /// <see cref="AppContext.BaseDirectory"/> at start.
    /// </remarks>
    internal AssetCatalog(
        ILoggerFactory loggerFactory,
        GpuAssetBridge bridge,
        ReadOnlyMemory<byte> builtInPack,
        IReadOnlyList<string> userPackPaths,
        IReadOnlyList<AssetTypeRegistration> assetTypes
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(userPackPaths);
        ArgumentNullException.ThrowIfNull(assetTypes);

        _logger = loggerFactory.CreateLogger(LOGGER_CATEGORY);
        _fontLogger = loggerFactory.CreateLogger(GlyphGenerationService.LOGGER_CATEGORY);
        _bridge = bridge;
        _proceduralAssets = new ProceduralAssetService(bridge);
        _glyphService = new GlyphGenerationService(loggerFactory);
        _glyphTextures = new ProceduralGlyphTextureFactory(_proceduralAssets);
        _builtInPack = builtInPack;
        _userPackPaths = [.. userPackPaths];
        _registry = new AssetTypeRegistry([.. BuiltInAssetLoaders.CreateDefault(), .. assetTypes]);
    }

    /// <summary>Gets the loader thread, or <see langword="null"/> until started.</summary>
    internal Thread? LoaderThread
    {
        get
        {
            lock (_gate)
            {
                return _loaderThread;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>The service uses the app's upload bridge but never the catalog cache.</remarks>
    ProceduralAssetService IProceduralAssetProvider.ProceduralAssets => _proceduralAssets;

    /// <summary>Gets the app-lifetime glyph generation service.</summary>
    internal GlyphGenerationService GlyphService => _glyphService;

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        string canonical = AssetPath.Normalize(path);
        lock (_gate)
        {
            ThrowIfUnavailableLocked();
            return !ProceduralAssetIds.IsReserved(canonical) && _visible.ContainsKey(canonical);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Path and type validation throw synchronously; loading runs on the loader thread.
    /// </remarks>
    public Task<T> Load<T>(string path, CancellationToken cancellationToken = default) =>
        LoadCore<T>(path, chain: null, cancellationToken);

    /// <summary>
    /// Resolves the registration for <typeparamref name="T"/> and starts the load.
    /// </summary>
    /// <remarks>A nested load carries the chain that requested it.</remarks>
    internal Task<T> LoadCore<T>(string path, LoadChain? chain, CancellationToken cancellationToken)
    {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical))
        {
            lock (_gate)
            {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
        }

        Type assetType = typeof(T);
        AssetTypeRegistration registration;
        lock (_gate)
        {
            ThrowIfUnavailableLocked();
            registration = _registry.Get(assetType);
        }

        if (registration is EngineAssetTypeRegistration<T> engineRegistration)
        {
            return LoadEngineAsync(canonical, engineRegistration, cancellationToken);
        }

        if (registration is not LoaderAssetTypeRegistration<T> loaderRegistration)
        {
            throw new InvalidOperationException(
                $"The asset type '{assetType.FullName}' is registered with a loader for a different "
                    + "asset type."
            );
        }

        return LoadCustomAsync(canonical, loaderRegistration, chain, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default)
    {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical))
        {
            lock (_gate)
            {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<byte[]>(AssetLoadErrors.CreateNotFound(canonical));
        }

        return LoadDataAsync(canonical, cancellationToken);
    }

    /// <summary>
    /// Mounts every pack, publishes the visible stack, and starts the loader thread.
    /// </summary>
    /// <remarks>A failed mount leaves no pack mounted and starts no thread.</remarks>
    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("The asset catalog is already started.");
            }

            _started = true;
        }

        List<PackArchive> mounted = [];
        bool glyphServiceStarted = false;
        try
        {
            if (_builtInPack.Length > 0)
            {
                mounted.Add(OpenPack(_builtInPack, BUILT_IN_DISPLAY_PATH));
            }

            string baseDirectory = AppContext.BaseDirectory;
            foreach (string configuredPath in _userPackPaths)
            {
                string fullPath;
                try
                {
                    fullPath = ResolvePackPath(baseDirectory, configuredPath);
                }
                catch (Exception exception)
                    when (exception
                            is ArgumentException
                                or NotSupportedException
                                or PathTooLongException
                    )
                {
                    throw new AssetPackException(
                        $"The asset pack path '{configuredPath}' is invalid: {exception.Message}",
                        DiagnosticCodes.ASSET_PACK_READ_FAILED,
                        exception
                    );
                }

                mounted.Add(OpenPack(fullPath, fullPath));
            }

            Dictionary<string, VisibleAsset> visible = BuildVisibleStack(mounted);
            lock (_gate)
            {
                if (_disposed)
                {
                    throw AssetLoadErrors.CreateStoppedException();
                }

                _packs = [.. mounted];
                _visible = visible;
                _glyphService.Start();
                glyphServiceStarted = true;
                Thread loader = new(LoaderLoop) { IsBackground = true, Name = LOADER_THREAD_NAME };
                _loaderThread = loader;
                loader.Start();
            }
        }
        catch
        {
            if (glyphServiceStarted)
            {
                _glyphService.Stop();
            }

            foreach (PackArchive archive in mounted)
            {
                archive.Dispose();
            }

            lock (_gate)
            {
                _started = false;
                _loaderThread = null;
            }

            throw;
        }
    }

    /// <summary>
    /// Stops the loader and glyph service, fails pending loads, releases cached assets and packs.
    /// Idempotent.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called on the loader thread.</exception>
    internal void Stop()
    {
        Thread? loader;
        PackArchive[] packs;
        EngineAsset[] assets;
        AssetLoaderWorkItem[] pending;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (ReferenceEquals(Thread.CurrentThread, _loaderThread))
            {
                throw new InvalidOperationException(
                    "The asset catalog cannot be stopped from its own loader thread."
                );
            }

            _disposed = true;
            _stopRequested = true;
            loader = _loaderThread;
            packs = _packs;
            _packs = [];
            pending = [.. _pending];
            _pending.Clear();
            _queue.Clear();
            assets =
            [
                .. _engineCache
                    .Values.Where(entry => entry.Lifetime is not null)
                    .Select(entry => entry.Lifetime!),
            ];
            _engineCache.Clear();
            _visible = new Dictionary<string, VisibleAsset>(StringComparer.Ordinal);
        }

        _stopSource.Cancel();
        _workAvailable.Set();

        // The tick thread has returned, so no draw can request a glyph any more. Stop the font thread
        // before the procedural service and before the cached assets are force-disposed: the render
        // thread still services the bridge, so an in-flight glyph upload completes or cancels.
        _glyphService.Stop();

        // The tick thread has returned, so no game callback can create or build a procedural asset any
        // more. A build already waiting on an upload fails when the render thread closes the bridge.
        _proceduralAssets.Stop();

        ObjectDisposedException stopped = AssetLoadErrors.CreateStoppedException();
        foreach (AssetLoaderWorkItem item in pending)
        {
            item.Fail(stopped);
        }

        loader?.Join();

        foreach (EngineAsset asset in assets)
        {
            asset.ForceDispose();
        }

        foreach (PackArchive archive in packs)
        {
            archive.Dispose();
        }

        _stopSource.Dispose();
        _workAvailable.Dispose();
    }

    private static Dictionary<string, VisibleAsset> BuildVisibleStack(List<PackArchive> mounted)
    {
        Dictionary<string, VisibleAsset> visible = new(StringComparer.Ordinal);
        foreach (PackArchive archive in mounted)
        {
            foreach (PackEntry entry in archive.Entries)
            {
                visible[entry.Path] = new VisibleAsset(archive, entry);
            }
        }

        return visible;
    }

    private static string ResolvePackPath(string baseDirectory, string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath);

    private static PackArchive OpenPack(ReadOnlyMemory<byte> pack, string displayPath)
    {
        try
        {
            return PackArchive.Open(pack, displayPath);
        }
        catch (AssetPackException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AssetPackException(
                $"The asset pack '{displayPath}' could not be mounted.",
                DiagnosticCodes.ASSET_PACK_READ_FAILED,
                exception
            );
        }
    }

    private static PackArchive OpenPack(string fullPath, string displayPath)
    {
        try
        {
            return PackArchive.Open(fullPath);
        }
        catch (AssetPackException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AssetPackException(
                $"The asset pack '{displayPath}' could not be mounted.",
                DiagnosticCodes.ASSET_PACK_READ_FAILED,
                exception
            );
        }
    }

    private void LoaderLoop()
    {
        while (true)
        {
            _workAvailable.WaitOne();
            while (true)
            {
                AssetLoaderWorkItem item;
                lock (_gate)
                {
                    if (_stopRequested)
                    {
                        return;
                    }

                    if (_queue.Count == 0)
                    {
                        break;
                    }

                    item = _queue.Dequeue();
                }

                item.Execute(_stopSource.Token);
            }
        }
    }

    private void CompleteWorkItem(AssetLoaderWorkItem item)
    {
        lock (_gate)
        {
            _pending.Remove(item);
        }
    }

    private Task<T> LoadEngineAsync<T>(
        string canonical,
        EngineAssetTypeRegistration<T> registration,
        CancellationToken cancellationToken
    )
    {
        EngineCacheEntry entry;
        AssetLoaderWorkItem? workItem = null;
        lock (_gate)
        {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible))
            {
                return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
            }

            AssetKey key = new(canonical, registration.AssetType);
            if (_engineCache.TryGetValue(key, out EngineCacheEntry? cached))
            {
                if (cached.Asset is not null)
                {
                    try
                    {
                        cached.Lifetime!.AddOwnerReference();
                        return Task.FromResult((T)cached.Asset);
                    }
                    catch (ObjectDisposedException)
                    {
                        // The last owner disposed between this lookup and the retain. Drop the stale
                        // entry so the next load starts a fresh load instead of returning a disposed
                        // asset, and report the concurrent dispose to this caller.
                        cached.Disposed = true;
                        if (
                            _engineCache.TryGetValue(key, out EngineCacheEntry? current)
                            && ReferenceEquals(current, cached)
                        )
                        {
                            _engineCache.Remove(key);
                        }

                        throw;
                    }
                }

                entry = cached;
                entry.Waiters++;
            }
            else
            {
                entry = new EngineCacheEntry(key);
                entry.Waiters = 1;
                _engineCache.Add(key, entry);
                Dictionary<string, VisibleAsset> visibleStack = _visible;
                workItem = new AssetLoaderWorkItem(
                    stopToken =>
                        LoadEngineObjectAsync(
                            entry,
                            visibleStack,
                            visible,
                            registration,
                            stopToken
                        ),
                    CompleteWorkItem
                );
                entry.Completion = workItem.Completion;
                if (!TryEnqueueLocked(workItem))
                {
                    _engineCache.Remove(key);
                    return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
                }
            }
        }

        return JoinEngineLoadAsync<T>(entry, cancellationToken);
    }

    private async Task<T> JoinEngineLoadAsync<T>(
        EngineCacheEntry entry,
        CancellationToken cancellationToken
    )
    {
        object result;
        try
        {
            result = await entry.Completion!.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseCanceledWaiter(entry);
            throw;
        }

        EngineAsset? release = null;
        lock (_gate)
        {
            entry.Waiters--;
            if (!_stopRequested && !entry.Disposed && entry.Asset is not null)
            {
                // The loader acquired one owner reference for every waiter registered at commit time.
                // Returning consumes this waiter's reference.
                return (T)result;
            }

            if (entry.ReferencesAcquired)
            {
                release = entry.Lifetime;
            }
        }

        if (release is not null)
        {
            ReleaseAcquiredReference(release);
        }

        throw AssetLoadErrors.CreateStoppedException();
    }

    private void ReleaseCanceledWaiter(EngineCacheEntry entry)
    {
        EngineAsset? release = null;
        lock (_gate)
        {
            entry.Waiters--;
            if (entry.ReferencesAcquired)
            {
                release = entry.Lifetime;
            }
        }

        if (release is not null)
        {
            ReleaseAcquiredReference(release);
        }
    }

    private static void ReleaseAcquiredReference(EngineAsset lifetime)
    {
        try
        {
            lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown force-disposed the asset after commit; the reference is already gone.
        }
    }

    /// <summary>
    /// Decodes one engine asset, starts its upload, and constructs the public asset.
    /// </summary>
    /// <remarks>
    /// Returns an incomplete task while a GPU copy runs, so the loader serves the next request. It
    /// uses the catalog stop token; a failure removes the entry and releases decoded state.
    /// </remarks>
    private async Task<object> LoadEngineObjectAsync<T>(
        EngineCacheEntry entry,
        Dictionary<string, VisibleAsset> visibleStack,
        VisibleAsset descriptorEntry,
        EngineAssetTypeRegistration<T> registration,
        CancellationToken stopToken
    )
    {
        GpuAssetResource? resource = null;
        EngineAsset? lifetime = null;
        Action? release = null;
        try
        {
            if (descriptorEntry.Entry.Type != registration.EntryType)
            {
                throw AssetLoadErrors.CreateTypeMismatch(
                    descriptorEntry.Entry.Path,
                    registration.EntryType,
                    descriptorEntry.Entry.Type
                );
            }

            byte[] descriptorPayload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
            AssetFormatContext context = new(
                descriptorEntry.Entry.Path,
                descriptorPayload,
                visibleStack,
                _glyphService,
                _glyphTextures,
                _fontLogger
            );
            DecodedAsset<T> decoded = registration.Format.Decode(context);
            release = decoded.Release;
            if (decoded.Upload is not null)
            {
                resource = await _bridge
                    .UploadAsync(decoded.Upload, stopToken)
                    .ConfigureAwait(false);
            }

            stopToken.ThrowIfCancellationRequested();

            lifetime = new EngineAsset(
                AssetPath.ToPublic(descriptorEntry.Entry.Path),
                resource,
                QueueDestruction,
                () => OnOwnersReleased(entry),
                decoded.Release
            );
            object asset = decoded.Create(lifetime)!;
            CommitEngineAsset(entry, lifetime, asset);
            return asset;
        }
        catch (Exception exception)
        {
            if (lifetime is not null)
            {
                // The lifetime exists, so its release and any GPU destruction run exactly once here.
                lifetime.ForceDispose();
            }
            else
            {
                // The decode may have created CPU state that never got a lifetime, for example when the
                // catalog stopped between the decode and the commit.
                release?.Invoke();
                if (resource is not null)
                {
                    QueueDestruction(resource);
                }
            }

            RemoveEngineEntry(entry);
            if (exception is OperationCanceledException && stopToken.IsCancellationRequested)
            {
                throw;
            }

            throw AssetLoadErrors.WrapLoadFailure(
                descriptorEntry.Entry.Path,
                "could not be loaded",
                exception
            );
        }
    }

    private void CommitEngineAsset(EngineCacheEntry entry, EngineAsset lifetime, object asset)
    {
        bool disposeUnused = false;
        lock (_gate)
        {
            if (_stopRequested)
            {
                disposeUnused = true;
            }
            else
            {
                entry.Asset = asset;
                entry.Lifetime = lifetime;
                int references = entry.Waiters;
                for (int i = 0; i < references; i++)
                {
                    lifetime.AddOwnerReference();
                }

                entry.ReferencesAcquired = references > 0;
                if (references == 0)
                {
                    entry.Disposed = true;
                    _engineCache.Remove(entry.Key);
                    disposeUnused = true;
                }
            }
        }

        if (disposeUnused)
        {
            lifetime.ForceDispose();
        }
    }

    private void RemoveEngineEntry(EngineCacheEntry entry)
    {
        lock (_gate)
        {
            entry.Disposed = true;
            if (
                _engineCache.TryGetValue(entry.Key, out EngineCacheEntry? cached)
                && ReferenceEquals(cached, entry)
            )
            {
                _engineCache.Remove(entry.Key);
            }
        }
    }

    private void OnOwnersReleased(EngineCacheEntry entry)
    {
        lock (_gate)
        {
            if (entry.Disposed)
            {
                return;
            }

            entry.Disposed = true;
            if (
                _engineCache.TryGetValue(entry.Key, out EngineCacheEntry? cached)
                && ReferenceEquals(cached, entry)
            )
            {
                _engineCache.Remove(entry.Key);
            }
        }
    }

    private Task<T> LoadCustomAsync<T>(
        string canonical,
        LoaderAssetTypeRegistration<T> registration,
        LoadChain? chain,
        CancellationToken cancellationToken
    )
    {
        AssetLoaderWorkItem workItem;
        lock (_gate)
        {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible))
            {
                return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
            }

            if (visible.Entry.Type != registration.EntryType)
            {
                return Task.FromException<T>(
                    AssetLoadErrors.CreateTypeMismatch(
                        canonical,
                        registration.EntryType,
                        visible.Entry.Type
                    )
                );
            }

            if (chain is not null && chain.Contains(typeof(T), canonical))
            {
                return Task.FromException<T>(
                    AssetLoadErrors.CreateLoadFailure(
                        canonical,
                        "the loader requested the same asset type and path it is currently loading"
                    )
                );
            }

            workItem = new AssetLoaderWorkItem(
                stopToken =>
                    LoadCustomObjectAsync(
                        visible,
                        registration.Loader,
                        registration.FailureDescription,
                        chain,
                        stopToken,
                        cancellationToken
                    ),
                CompleteWorkItem,
                mapCancellationToStopped: false
            );
            if (!TryEnqueueLocked(workItem))
            {
                return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<T>(workItem, cancellationToken);
    }

    /// <summary>Loads one registered value on the loader thread.</summary>
    /// <remarks>
    /// Stop cancellation surfaces as <see cref="ObjectDisposedException"/>; caller cancellation
    /// stays an <see cref="OperationCanceledException"/>.
    /// </remarks>
    private async Task<object> LoadCustomObjectAsync<T>(
        VisibleAsset descriptorEntry,
        IAssetLoader<T> loader,
        string failureDescription,
        LoadChain? chain,
        CancellationToken stopToken,
        CancellationToken cancellationToken
    )
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopToken,
            cancellationToken
        );
        try
        {
            byte[] payload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
            AssetLoadContext context = new(
                this,
                descriptorEntry.Entry.Path,
                payload,
                new LoadChain(typeof(T), descriptorEntry.Entry.Path, chain),
                linked.Token
            );
            T value = await loader.LoadAsync(context).ConfigureAwait(false);
            if (value is null)
            {
                throw AssetLoadErrors.CreateLoadFailure(
                    descriptorEntry.Entry.Path,
                    "the registered loader returned no value"
                );
            }

            return value;
        }
        catch (Exception exception)
        {
            if (stopToken.IsCancellationRequested)
            {
                throw AssetLoadErrors.CreateStoppedException();
            }

            if (
                exception is OperationCanceledException
                && cancellationToken.IsCancellationRequested
            )
            {
                throw;
            }

            throw AssetLoadErrors.WrapLoadFailure(
                descriptorEntry.Entry.Path,
                failureDescription,
                exception
            );
        }
    }

    private Task<byte[]> LoadDataAsync(string canonical, CancellationToken cancellationToken)
    {
        AssetLoaderWorkItem workItem;
        lock (_gate)
        {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible))
            {
                return Task.FromException<byte[]>(AssetLoadErrors.CreateNotFound(canonical));
            }

            workItem = new AssetLoaderWorkItem(
                _ => Task.FromResult<object>(visible.Archive.ReadPayload(visible.Entry)),
                CompleteWorkItem
            );
            if (!TryEnqueueLocked(workItem))
            {
                return Task.FromException<byte[]>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<byte[]>(workItem, cancellationToken);
    }

    private static async Task<T> AwaitWorkAsync<T>(
        AssetLoaderWorkItem workItem,
        CancellationToken cancellationToken
    )
    {
        object result = await workItem
            .Completion.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return (T)result;
    }

    private bool TryEnqueueLocked(AssetLoaderWorkItem workItem)
    {
        if (_pending.Count >= MAX_PENDING_LOADS)
        {
            return false;
        }

        _pending.Add(workItem);
        _queue.Enqueue(workItem);
        _workAvailable.Set();
        return true;
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Queuing GPU destruction happens while an asset is being released, so it must not throw out of the release path; the failure is logged."
    )]
    private void QueueDestruction(GpuAssetResource resource)
    {
        try
        {
            _bridge.Destroy(resource);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The asset catalog could not queue GPU destruction for one resource."
            );
        }
    }

    private void ThrowIfUnavailableLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException(
                "The asset catalog is not started; RayBorkApp starts it before calling the game."
            );
        }
    }

    private readonly record struct AssetKey(string Path, Type AssetType);

    /// <summary>One cached engine asset, its coalesced load, and its owner references.</summary>
    private sealed class EngineCacheEntry
    {
        internal EngineCacheEntry(AssetKey key) => Key = key;

        internal AssetKey Key { get; }

        internal int Waiters;

        internal object? Asset;

        internal EngineAsset? Lifetime;

        internal Task<object>? Completion;

        internal bool ReferencesAcquired;

        internal bool Disposed;
    }

    /// <summary>One queued loader request; its completion never runs user code inline.</summary>
    /// <remarks>
    /// The work returns a task so a GPU upload can stay pending while the loader dequeues the next
    /// request; an incomplete item stays pending until its continuation removes it.
    /// </remarks>
    private sealed class AssetLoaderWorkItem
    {
        private readonly Func<CancellationToken, Task<object>> _work;
        private readonly Action<AssetLoaderWorkItem> _onCompleted;
        private readonly bool _mapCancellationToStopped;
        private readonly TaskCompletionSource<object> _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        internal AssetLoaderWorkItem(
            Func<CancellationToken, Task<object>> work,
            Action<AssetLoaderWorkItem> onCompleted,
            bool mapCancellationToStopped = true
        )
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(onCompleted);

            _work = work;
            _onCompleted = onCompleted;
            _mapCancellationToStopped = mapCancellationToStopped;
        }

        internal Task<object> Completion => _completion.Task;

        [SuppressMessage(
            "Design",
            "CA1031",
            Justification = "A loader is user code; any failure completes the work item with that exception instead of killing the loader thread."
        )]
        internal void Execute(CancellationToken stopToken)
        {
            Task<object> work;
            try
            {
                work = _work(stopToken);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                _completion.TrySetException(AssetLoadErrors.CreateStoppedException());
                _onCompleted(this);
                return;
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
                _onCompleted(this);
                return;
            }

            if (work.IsCompleted)
            {
                CompleteFrom(work);
                _onCompleted(this);
                return;
            }

            work.ContinueWith(
                static (completed, state) =>
                {
                    AssetLoaderWorkItem item = (AssetLoaderWorkItem)state!;
                    item.CompleteFrom(completed);
                    item._onCompleted(item);
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        internal void Fail(Exception exception) => _completion.TrySetException(exception);

        private void CompleteFrom(Task<object> work)
        {
            if (work.IsCompletedSuccessfully)
            {
                _completion.TrySetResult(work.Result);
                return;
            }

            if (work.IsCanceled)
            {
                if (_mapCancellationToStopped)
                {
                    // Shared engine-asset work only observes the catalog stop token, whose
                    // cancellation is reported as ObjectDisposedException at the public caller.
                    _completion.TrySetException(AssetLoadErrors.CreateStoppedException());
                }
                else
                {
                    // A value load observes its caller token through a linked token. It reports
                    // OperationCanceledException after releasing every partly acquired asset.
                    _completion.TrySetCanceled();
                }

                return;
            }

            Exception exception = work.Exception?.InnerException
                ?? (Exception?)work.Exception
                ?? new InvalidOperationException("The asset loader work item failed.");
            _completion.TrySetException(exception);
        }
    }
}
