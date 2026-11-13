using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The app's runtime asset catalog: it mounts the built-in pack and the configured user packs,
/// resolves the visible entry for a path, and loads engine assets and registered values on one
/// dedicated loader thread.
/// </summary>
/// <remarks>
/// <para>
/// Mounting is atomic and happens in <see cref="Start"/>: the built-in pack is opened first, then the
/// user packs in call order, and the visible stack is published only after every pack validated.
/// Later packs shadow earlier packs for the same canonical path. A failed mount disposes the packs it
/// opened, starts no loader thread, and throws <see cref="AssetPackException"/>.
/// </para>
/// <para>
/// <see cref="Load{T}"/>, <see cref="LoadData"/>, and <see cref="Exists"/> are thread-safe. Engine
/// assets are cached by <c>(canonical path, engine asset type)</c>, coalesced while the first load is
/// in flight, and reference-counted. The shared load uses the catalog stop token, never a caller
/// token, so one canceled waiter leaves the other waiters and the created resource intact. When every
/// waiter cancels before acquiring a reference the created asset is destroyed and removed from the
/// cache. Every other registered type is loaded by an <see cref="IAssetLoader{T}"/> and returned as a
/// fresh caller-owned value; custom values and <see cref="LoadData"/> payloads are never cached.
/// <see cref="Shader"/> and <see cref="Model"/> are registered loaders, not catalog special cases:
/// <c>Load&lt;Shader&gt;</c> resolves the cached internal <see cref="ShaderProgram"/> and returns a
/// new material over it for every call, and <c>Load&lt;Model&gt;</c> resolves the shared cached part
/// meshes and programs and returns a new model wrapper with its own materials per call.
/// </para>
/// <para>
/// The loader thread only reads and decompresses pack payloads, runs the registered loaders, and starts
/// GPU uploads; it never calls SDL, Vulkan, or game code and never waits for a GPU copy. An engine load
/// is deferred: while its upload is in flight the loader keeps serving the next queued request, so
/// custom data and <see cref="LoadData"/> do not wait behind a GPU upload. A loader that needs another
/// asset requests it through its <see cref="AssetLoadContext"/>; the nested request is queued on the
/// same loader thread, so the loader must return an incomplete task instead of blocking. <see cref="Stop"/>
/// cancels and joins the loader, fails the pending loads with <see cref="ObjectDisposedException"/>,
/// forces every cached asset to dispose, and releases the pack mappings. It runs before the frame ring
/// completes so the render thread can still service the queued GPU destructions.
/// </para>
/// <para>
/// The catalog also owns the app's <see cref="ProceduralAssetService"/>. Procedural assets are never
/// cached or resolved by this catalog; the service only borrows the same GPU upload bridge. The
/// reserved <c>_procedural</c> path prefix is reported as missing instead of resolving a pack entry.
/// </para>
/// </remarks>
internal sealed class AssetCatalog : IAssetCatalog, IProceduralAssetProvider {
    /// <summary>The loader thread name.</summary>
    internal const string LOADER_THREAD_NAME = "RayBork.Asset";

    /// <summary>The logger category for catalog diagnostics.</summary>
    internal const string LOGGER_CATEGORY = "RayBork.Asset";

    /// <summary>
    /// The maximum number of queued or in-flight loader requests. The bound is a backstop against a
    /// stopped loader, not a normal concurrency limit.
    /// </summary>
    internal const int MAX_PENDING_LOADS = 2048;

    private const string BUILT_IN_DISPLAY_PATH = "<built-in>";

    private readonly ILogger _logger;
    private readonly GpuAssetBridge _bridge;
    private readonly ProceduralAssetService _proceduralAssets;
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

    /// <summary>
    /// Initializes a catalog without mounting any pack.
    /// </summary>
    /// <param name="loggerFactory">The app logger factory.</param>
    /// <param name="bridge">The GPU asset bridge serviced by the render loop.</param>
    /// <param name="builtInPack">The built-in pack bytes; empty means the built-in pack has no entries.</param>
    /// <param name="userPackPaths">
    /// The user pack paths in mount order. Relative paths resolve against
    /// <see cref="AppContext.BaseDirectory"/> when the catalog starts.
    /// </param>
    /// <param name="assetTypes">The user asset type registrations; the built-in registrations are added by the catalog.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal AssetCatalog(
        ILoggerFactory loggerFactory,
        GpuAssetBridge bridge,
        ReadOnlyMemory<byte> builtInPack,
        IReadOnlyList<string> userPackPaths,
        IReadOnlyList<AssetTypeRegistration> assetTypes) {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(userPackPaths);
        ArgumentNullException.ThrowIfNull(assetTypes);

        _logger = loggerFactory.CreateLogger(LOGGER_CATEGORY);
        _bridge = bridge;
        _proceduralAssets = new ProceduralAssetService(bridge);
        _builtInPack = builtInPack;
        _userPackPaths = [.. userPackPaths];
        _registry = new AssetTypeRegistry([.. BuiltInAssetLoaders.CreateDefault(), .. assetTypes]);
    }

    /// <summary>
    /// Gets the loader thread, or <see langword="null"/> before <see cref="Start"/> and after it failed.
    /// </summary>
    internal Thread? LoaderThread {
        get {
            lock (_gate) {
                return _loaderThread;
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The service shares the app's GPU upload bridge but never enters this catalog's cache, so a
    /// procedural asset cannot shadow or be shadowed by a pack path.
    /// </remarks>
    ProceduralAssetService IProceduralAssetProvider.ProceduralAssets => _proceduralAssets;

    /// <inheritdoc/>
    public bool Exists(string path) {
        string canonical = AssetPath.Normalize(path);
        lock (_gate) {
            ThrowIfUnavailableLocked();
            return !ProceduralAssetIds.IsReserved(canonical) && _visible.ContainsKey(canonical);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Path and type validation happen synchronously; pack lookup, descriptor decoding, and the GPU
    /// upload complete asynchronously on the loader thread.
    /// </remarks>
    public Task<T> Load<T>(string path, CancellationToken cancellationToken = default)
        => LoadCore<T>(path, cancellationToken, chain: null);

    /// <summary>
    /// Normalizes the path, resolves the registered strategy for <typeparamref name="T"/>, and starts
    /// the load. A nested load carries the chain that requested it.
    /// </summary>
    /// <typeparam name="T">The requested asset type.</typeparam>
    /// <param name="path">The caller-supplied path.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <param name="chain">
    /// The chain of loads that requested this load, or <see langword="null"/> for a top-level load.
    /// </param>
    /// <returns>The load task.</returns>
    internal Task<T> LoadCore<T>(string path, CancellationToken cancellationToken, LoadChain? chain) {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical)) {
            lock (_gate) {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
        }

        Type assetType = typeof(T);
        AssetTypeRegistration registration;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            registration = _registry.Get(assetType);
        }

        if (registration is EngineAssetTypeRegistration<T> engineRegistration) {
            return LoadEngineAsync(canonical, engineRegistration, cancellationToken);
        }

        if (registration is not LoaderAssetTypeRegistration<T> loaderRegistration) {
            throw new InvalidOperationException(
                $"The asset type '{assetType.FullName}' is registered with a loader for a different "
                    + "asset type.");
        }

        return LoadCustomAsync(canonical, loaderRegistration, cancellationToken, chain);
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default) {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical)) {
            lock (_gate) {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<byte[]>(AssetLoadErrors.CreateNotFound(canonical));
        }

        return LoadDataAsync(canonical, cancellationToken);
    }

    /// <summary>
    /// Mounts the built-in pack and every configured user pack, publishes the visible stack, and
    /// starts the loader thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The catalog was already started.</exception>
    /// <exception cref="ObjectDisposedException">The catalog is disposed.</exception>
    /// <exception cref="AssetPackException">
    /// A pack is missing, unreadable, truncated, has an unsupported format, or has an invalid index.
    /// The catalog leaves no pack mounted and starts no loader thread.
    /// </exception>
    internal void Start() {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) {
                throw new InvalidOperationException("The asset catalog is already started.");
            }

            _started = true;
        }

        List<PackArchive> mounted = [];
        try {
            if (_builtInPack.Length > 0) {
                mounted.Add(OpenPack(_builtInPack, BUILT_IN_DISPLAY_PATH));
            }

            string baseDirectory = AppContext.BaseDirectory;
            foreach (string configuredPath in _userPackPaths) {
                string fullPath;
                try {
                    fullPath = ResolvePackPath(baseDirectory, configuredPath);
                } catch (Exception exception) when (exception is ArgumentException
                    or NotSupportedException or PathTooLongException) {
                    throw new AssetPackException(
                        $"The asset pack path '{configuredPath}' is invalid: {exception.Message}",
                        DiagnosticCodes.ASSET_PACK_READ_FAILED,
                        exception);
                }

                mounted.Add(OpenPack(fullPath, fullPath));
            }

            Dictionary<string, VisibleAsset> visible = BuildVisibleStack(mounted);
            lock (_gate) {
                if (_disposed) {
                    throw AssetLoadErrors.CreateStoppedException();
                }

                _packs = [.. mounted];
                _visible = visible;
                Thread loader = new(LoaderLoop) {
                    IsBackground = true,
                    Name = LOADER_THREAD_NAME,
                };
                _loaderThread = loader;
                loader.Start();
            }
        } catch {
            foreach (PackArchive archive in mounted) {
                archive.Dispose();
            }

            lock (_gate) {
                _started = false;
                _loaderThread = null;
            }

            throw;
        }
    }

    /// <summary>
    /// Stops the loader, fails every pending load, releases every cached engine asset, and disposes the
    /// mounted packs. Idempotent. Runs before the frame ring completes so the render thread still
    /// services the queued GPU destructions.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called on the loader thread.</exception>
    internal void Stop() {
        Thread? loader;
        PackArchive[] packs;
        EngineAsset[] assets;
        AssetLoaderWorkItem[] pending;
        lock (_gate) {
            if (_disposed) {
                return;
            }

            if (ReferenceEquals(Thread.CurrentThread, _loaderThread)) {
                throw new InvalidOperationException(
                    "The asset catalog cannot be stopped from its own loader thread.");
            }

            _disposed = true;
            _stopRequested = true;
            loader = _loaderThread;
            packs = _packs;
            _packs = [];
            pending = [.. _pending];
            _pending.Clear();
            _queue.Clear();
            assets = [.. _engineCache.Values
                .Where(entry => entry.Lifetime is not null)
                .Select(entry => entry.Lifetime!)];
            _engineCache.Clear();
            _visible = new Dictionary<string, VisibleAsset>(StringComparer.Ordinal);
        }

        _stopSource.Cancel();
        _workAvailable.Set();

        // The tick thread has returned, so no game callback can create or build a procedural asset any
        // more. A build already waiting on an upload fails when the render thread closes the bridge.
        _proceduralAssets.Stop();

        ObjectDisposedException stopped = AssetLoadErrors.CreateStoppedException();
        foreach (AssetLoaderWorkItem item in pending) {
            item.Fail(stopped);
        }

        loader?.Join();

        foreach (EngineAsset asset in assets) {
            asset.ForceDispose();
        }

        foreach (PackArchive archive in packs) {
            archive.Dispose();
        }

        _stopSource.Dispose();
        _workAvailable.Dispose();
    }

    private static Dictionary<string, VisibleAsset> BuildVisibleStack(List<PackArchive> mounted) {
        Dictionary<string, VisibleAsset> visible = new(StringComparer.Ordinal);
        foreach (PackArchive archive in mounted) {
            foreach (PackEntry entry in archive.Entries) {
                visible[entry.Path] = new VisibleAsset(archive, entry);
            }
        }

        return visible;
    }

    private static string ResolvePackPath(string baseDirectory, string configuredPath)
        => Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(baseDirectory, configuredPath);

    private static PackArchive OpenPack(ReadOnlyMemory<byte> pack, string displayPath) {
        try {
            return PackArchive.Open(pack, displayPath);
        } catch (AssetPackException) {
            throw;
        } catch (Exception exception) {
            throw new AssetPackException(
                $"The asset pack '{displayPath}' could not be mounted.",
                DiagnosticCodes.ASSET_PACK_READ_FAILED,
                exception);
        }
    }

    private static PackArchive OpenPack(string fullPath, string displayPath) {
        try {
            return PackArchive.Open(fullPath);
        } catch (AssetPackException) {
            throw;
        } catch (Exception exception) {
            throw new AssetPackException(
                $"The asset pack '{displayPath}' could not be mounted.",
                DiagnosticCodes.ASSET_PACK_READ_FAILED,
                exception);
        }
    }

    private void LoaderLoop() {
        while (true) {
            _workAvailable.WaitOne();
            while (true) {
                AssetLoaderWorkItem item;
                lock (_gate) {
                    if (_stopRequested) {
                        return;
                    }

                    if (_queue.Count == 0) {
                        break;
                    }

                    item = _queue.Dequeue();
                }

                item.Execute(_stopSource.Token);
            }
        }
    }

    private void CompleteWorkItem(AssetLoaderWorkItem item) {
        lock (_gate) {
            _pending.Remove(item);
        }
    }

    private Task<T> LoadEngineAsync<T>(
        string canonical,
        EngineAssetTypeRegistration<T> registration,
        CancellationToken cancellationToken) {
        EngineCacheEntry entry;
        AssetLoaderWorkItem? workItem = null;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
            }

            AssetKey key = new(canonical, registration.AssetType);
            if (_engineCache.TryGetValue(key, out EngineCacheEntry? cached)) {
                if (cached.Asset is not null) {
                    try {
                        cached.Lifetime!.AddOwnerReference();
                        return Task.FromResult((T)cached.Asset);
                    } catch (ObjectDisposedException) {
                        // The last owner disposed between this lookup and the retain. Drop the stale
                        // entry so the next load starts a fresh load instead of returning a disposed
                        // asset, and report the concurrent dispose to this caller.
                        cached.Disposed = true;
                        if (_engineCache.TryGetValue(key, out EngineCacheEntry? current)
                            && ReferenceEquals(current, cached)) {
                            _engineCache.Remove(key);
                        }

                        throw;
                    }
                }

                entry = cached;
                entry.Waiters++;
            } else {
                entry = new EngineCacheEntry(key);
                entry.Waiters = 1;
                _engineCache.Add(key, entry);
                Dictionary<string, VisibleAsset> visibleStack = _visible;
                workItem = new AssetLoaderWorkItem(
                    stopToken => LoadEngineObjectAsync(entry, visibleStack, visible, registration, stopToken),
                    CompleteWorkItem);
                entry.Completion = workItem.Completion;
                if (!TryEnqueueLocked(workItem)) {
                    _engineCache.Remove(key);
                    return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
                }
            }
        }

        return JoinEngineLoadAsync<T>(entry, cancellationToken);
    }

    private async Task<T> JoinEngineLoadAsync<T>(
        EngineCacheEntry entry,
        CancellationToken cancellationToken) {
        object result;
        try {
            result = await entry.Completion!.WaitAsync(cancellationToken).ConfigureAwait(false);
        } catch {
            ReleaseCanceledWaiter(entry);
            throw;
        }

        EngineAsset? release = null;
        lock (_gate) {
            entry.Waiters--;
            if (!_stopRequested && !entry.Disposed && entry.Asset is not null) {
                // The loader acquired one owner reference for every waiter registered at commit time.
                // Returning consumes this waiter's reference.
                return (T)result;
            }

            if (entry.ReferencesAcquired) {
                release = entry.Lifetime;
            }
        }

        if (release is not null) {
            ReleaseAcquiredReference(release);
        }

        throw AssetLoadErrors.CreateStoppedException();
    }

    private void ReleaseCanceledWaiter(EngineCacheEntry entry) {
        EngineAsset? release = null;
        lock (_gate) {
            entry.Waiters--;
            if (entry.ReferencesAcquired) {
                release = entry.Lifetime;
            }
        }

        if (release is not null) {
            ReleaseAcquiredReference(release);
        }
    }

    private static void ReleaseAcquiredReference(EngineAsset lifetime) {
        try {
            lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // Shutdown force-disposed the asset after commit; the reference is already gone.
        }
    }

    /// <summary>
    /// Decodes one engine asset on the loader thread, starts its GPU upload, and constructs the public
    /// asset when the upload completes.
    /// </summary>
    /// <remarks>
    /// The method returns an incomplete task while the GPU copy runs, so the loader can serve the next
    /// queued request. The upload uses the catalog stop token, never a caller token, so one canceled
    /// waiter never cancels the shared load. A stop or a failure removes the pending cache entry and
    /// destroys the just-created resource before the task faults.
    /// </remarks>
    private async Task<object> LoadEngineObjectAsync<T>(
        EngineCacheEntry entry,
        Dictionary<string, VisibleAsset> visibleStack,
        VisibleAsset descriptorEntry,
        EngineAssetTypeRegistration<T> registration,
        CancellationToken stopToken) {
        GpuAssetResource? resource = null;
        try {
            if (descriptorEntry.Entry.Type != registration.EntryType) {
                throw AssetLoadErrors.CreateTypeMismatch(
                    descriptorEntry.Entry.Path,
                    registration.EntryType,
                    descriptorEntry.Entry.Type);
            }

            byte[] descriptorPayload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
            AssetFormatContext context = new(descriptorEntry.Entry.Path, descriptorPayload, visibleStack);
            DecodedAsset<T> decoded = registration.Format.Decode(context);
            resource = await _bridge.UploadAsync(decoded.Upload, stopToken).ConfigureAwait(false);
            stopToken.ThrowIfCancellationRequested();

            EngineAsset lifetime = new(
                AssetPath.ToPublic(descriptorEntry.Entry.Path),
                resource,
                QueueDestruction,
                () => OnOwnersReleased(entry));
            object asset = decoded.Create(lifetime)!;
            CommitEngineAsset(entry, lifetime, asset);
            return asset;
        } catch (Exception exception) {
            if (resource is not null) {
                QueueDestruction(resource);
            }

            RemoveEngineEntry(entry);
            if (exception is OperationCanceledException && stopToken.IsCancellationRequested) {
                throw;
            }

            throw AssetLoadErrors.WrapLoadFailure(descriptorEntry.Entry.Path, "could not be loaded", exception);
        }
    }

    private void CommitEngineAsset(EngineCacheEntry entry, EngineAsset lifetime, object asset) {
        bool disposeUnused = false;
        lock (_gate) {
            if (_stopRequested) {
                disposeUnused = true;
            } else {
                entry.Asset = asset;
                entry.Lifetime = lifetime;
                int references = entry.Waiters;
                for (int i = 0; i < references; i++) {
                    lifetime.AddOwnerReference();
                }

                entry.ReferencesAcquired = references > 0;
                if (references == 0) {
                    entry.Disposed = true;
                    _engineCache.Remove(entry.Key);
                    disposeUnused = true;
                }
            }
        }

        if (disposeUnused) {
            lifetime.ForceDispose();
        }
    }

    private void RemoveEngineEntry(EngineCacheEntry entry) {
        lock (_gate) {
            entry.Disposed = true;
            if (_engineCache.TryGetValue(entry.Key, out EngineCacheEntry? cached)
                && ReferenceEquals(cached, entry)) {
                _engineCache.Remove(entry.Key);
            }
        }
    }

    private void OnOwnersReleased(EngineCacheEntry entry) {
        lock (_gate) {
            if (entry.Disposed) {
                return;
            }

            entry.Disposed = true;
            if (_engineCache.TryGetValue(entry.Key, out EngineCacheEntry? cached)
                && ReferenceEquals(cached, entry)) {
                _engineCache.Remove(entry.Key);
            }
        }
    }

    private Task<T> LoadCustomAsync<T>(
        string canonical,
        LoaderAssetTypeRegistration<T> registration,
        CancellationToken cancellationToken,
        LoadChain? chain) {
        AssetLoaderWorkItem workItem;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
            }

            if (visible.Entry.Type != registration.EntryType) {
                return Task.FromException<T>(
                    AssetLoadErrors.CreateTypeMismatch(
                        canonical,
                        registration.EntryType,
                        visible.Entry.Type));
            }

            if (chain is not null && chain.Contains(typeof(T), canonical)) {
                return Task.FromException<T>(AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    "the loader requested the same asset type and path it is currently loading"));
            }

            workItem = new AssetLoaderWorkItem(
                stopToken => LoadCustomObjectAsync(
                    visible,
                    registration.Loader,
                    stopToken,
                    cancellationToken,
                    chain),
                CompleteWorkItem,
                mapCancellationToStopped: false);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<T>(workItem, cancellationToken);
    }

    /// <summary>
    /// Loads one registered value asset on the loader thread through its loader.
    /// </summary>
    /// <remarks>
    /// The caller token is linked to the catalog stop token for the duration of the load. Stop
    /// cancellation is reported as <see cref="ObjectDisposedException"/>, while caller cancellation
    /// stays an <see cref="OperationCanceledException"/> so the work item completes as canceled
    /// instead of faulted.
    /// </remarks>
    private async Task<object> LoadCustomObjectAsync<T>(
        VisibleAsset descriptorEntry,
        IAssetLoader<T> loader,
        CancellationToken stopToken,
        CancellationToken cancellationToken,
        LoadChain? chain) {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopToken,
            cancellationToken);
        try {
            byte[] payload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
            AssetLoadContext context = new(
                this,
                descriptorEntry.Entry.Path,
                linked.Token,
                payload,
                new LoadChain(typeof(T), descriptorEntry.Entry.Path, chain));
            T value = await loader.LoadAsync(context).ConfigureAwait(false);
            return value!;
        } catch (Exception exception) {
            if (stopToken.IsCancellationRequested) {
                throw AssetLoadErrors.CreateStoppedException();
            }

            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) {
                throw;
            }

            throw AssetLoadErrors.WrapLoadFailure(
                descriptorEntry.Entry.Path,
                "custom data could not be deserialized",
                exception);
        }
    }

    private Task<byte[]> LoadDataAsync(string canonical, CancellationToken cancellationToken) {
        AssetLoaderWorkItem workItem;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<byte[]>(AssetLoadErrors.CreateNotFound(canonical));
            }

            workItem = new AssetLoaderWorkItem(
                _ => Task.FromResult<object>(visible.Archive.ReadPayload(visible.Entry)),
                CompleteWorkItem);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<byte[]>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<byte[]>(workItem, cancellationToken);
    }

    private static async Task<T> AwaitWorkAsync<T>(
        AssetLoaderWorkItem workItem,
        CancellationToken cancellationToken) {
        object result = await workItem.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        return (T)result;
    }

    private bool TryEnqueueLocked(AssetLoaderWorkItem workItem) {
        if (_pending.Count >= MAX_PENDING_LOADS) {
            return false;
        }

        _pending.Add(workItem);
        _queue.Enqueue(workItem);
        _workAvailable.Set();
        return true;
    }

    private void QueueDestruction(GpuAssetResource resource) {
        try {
            _bridge.Destroy(resource);
        } catch (Exception exception) {
            _logger.LogWarning(
                exception,
                "The asset catalog could not queue GPU destruction for one resource.");
        }
    }

    private void ThrowIfUnavailableLocked() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started) {
            throw new InvalidOperationException(
                "The asset catalog is not started; RayBorkApp starts it before calling the game.");
        }
    }

    private readonly record struct AssetKey(string Path, Type AssetType);

    /// <summary>
    /// One cached engine asset, its pending coalesced load, and its owner-reference bookkeeping. A
    /// successful commit acquires one owner reference per waiter registered at that moment, so a waiter
    /// that disposes immediately cannot invalidate the object another waiter is about to receive.
    /// </summary>
    private sealed class EngineCacheEntry {
        internal EngineCacheEntry(AssetKey key) => Key = key;

        internal AssetKey Key { get; }

        internal int Waiters;

        internal object? Asset;

        internal EngineAsset? Lifetime;

        internal Task<object>? Completion;

        internal bool ReferencesAcquired;

        internal bool Disposed;
    }

    /// <summary>
    /// One queued loader request. The completion task never runs user code inline on the loader thread.
    /// </summary>
    /// <remarks>
    /// The work returns a task so a GPU upload can stay pending while the loader dequeues the next
    /// request. A completed work task completes the item inline; an incomplete one stays in the
    /// catalog's pending set until its continuation completes the item and removes it. That keeps the
    /// queue bound and <see cref="Stop"/> correct for deferred loads while the loader thread continues.
    /// </remarks>
    private sealed class AssetLoaderWorkItem {
        private readonly Func<CancellationToken, Task<object>> _work;
        private readonly Action<AssetLoaderWorkItem> _onCompleted;
        private readonly bool _mapCancellationToStopped;
        private readonly TaskCompletionSource<object> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal AssetLoaderWorkItem(
            Func<CancellationToken, Task<object>> work,
            Action<AssetLoaderWorkItem> onCompleted,
            bool mapCancellationToStopped = true) {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(onCompleted);

            _work = work;
            _onCompleted = onCompleted;
            _mapCancellationToStopped = mapCancellationToStopped;
        }

        internal Task<object> Completion => _completion.Task;

        internal void Execute(CancellationToken stopToken) {
            Task<object> work;
            try {
                work = _work(stopToken);
            } catch (OperationCanceledException) when (stopToken.IsCancellationRequested) {
                _completion.TrySetException(AssetLoadErrors.CreateStoppedException());
                _onCompleted(this);
                return;
            } catch (Exception exception) {
                _completion.TrySetException(exception);
                _onCompleted(this);
                return;
            }

            if (work.IsCompleted) {
                CompleteFrom(work);
                _onCompleted(this);
                return;
            }

            work.ContinueWith(
                static (completed, state) => {
                    AssetLoaderWorkItem item = (AssetLoaderWorkItem)state!;
                    item.CompleteFrom(completed);
                    item._onCompleted(item);
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        internal void Fail(Exception exception) => _completion.TrySetException(exception);

        private void CompleteFrom(Task<object> work) {
            if (work.IsCompletedSuccessfully) {
                _completion.TrySetResult(work.Result);
                return;
            }

            if (work.IsCanceled) {
                if (_mapCancellationToStopped) {
                    // Shared engine-asset work only observes the catalog stop token, whose
                    // cancellation is reported as ObjectDisposedException at the public caller.
                    _completion.TrySetException(AssetLoadErrors.CreateStoppedException());
                } else {
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
