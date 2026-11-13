using System.Numerics;
using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The app's runtime asset catalog: it mounts the built-in pack and the configured user packs,
/// resolves the visible entry for a path, and loads engine assets and custom data on one dedicated
/// loader thread.
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
/// cache. Custom data is deserialized on the loader and returned as a fresh caller-owned instance;
/// custom data and <see cref="LoadData"/> payloads are never cached. <see cref="Shader"/> is a
/// material, not an engine asset: <c>Load&lt;Shader&gt;</c> loads or reuses the internal
/// <see cref="ShaderProgram"/> and returns a new material over it for every call. <see cref="Model"/>
/// is a caller-owned composition, not an engine asset: <c>Load&lt;Model&gt;</c> resolves the shared
/// cached part meshes and programs and returns a new model wrapper with its own materials per call.
/// </para>
/// <para>
/// The loader thread only reads and decompresses pack payloads, deserializes custom data, and starts
/// GPU uploads; it never calls SDL, Vulkan, or game code and never waits for a GPU copy. An engine
/// load is deferred: while its upload is in flight the loader keeps serving the next queued request,
/// so custom data and <see cref="LoadData"/> do not wait behind a GPU upload. <see cref="Stop"/>
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

    /// <summary>
    /// The canonical shader path the runtime uses for a model part whose descriptor has no shader path.
    /// The path is baked into the built-in pack as the default mesh draw shader.
    /// </summary>
    private const string DEFAULT_MODEL_SHADER_PATH = "shaders/mesh";

    /// <summary>
    /// The shader path older model packs baked when a part had no authored shader. It resolves to
    /// <see cref="DEFAULT_MODEL_SHADER_PATH"/> because the built-in pack no longer publishes it.
    /// </summary>
    private const string LEGACY_MODEL_SHADER_PATH = "shaders/default";

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
    /// <param name="assetTypes">The user asset type registrations; the built-in engine formats are added by the catalog.</param>
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
    public Task<T> Load<T>(string path, CancellationToken cancellationToken = default) {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical)) {
            lock (_gate) {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
        }

        Type assetType = typeof(T);
        if (assetType == typeof(Shader)) {
            return LoadShaderMaterialAsync<T>(canonical, cancellationToken);
        }

        if (assetType == typeof(Model)) {
            return LoadModelAsync<T>(canonical, cancellationToken);
        }

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

        return LoadCustomAsync(canonical, loaderRegistration, cancellationToken);
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
    /// Loads the internal shader program through the engine cache and returns a new material over it.
    /// </summary>
    /// <remarks>
    /// Every successful call returns a distinct caller-owned <see cref="Shader"/>. The program load is
    /// coalesced and cached by the canonical path and the <see cref="ShaderProgram"/> type, so materials
    /// over the same path share one GPU program and batch together. The load's own program reference is
    /// released as soon as the material acquired its own, whether the material was created or the
    /// caller canceled first.
    /// </remarks>
    private async Task<T> LoadShaderMaterialAsync<T>(string canonical, CancellationToken cancellationToken) {
        ShaderProgram program = await LoadEngineAsync<ShaderProgram>(
            canonical,
            GetEngineRegistration<ShaderProgram>(),
            cancellationToken).ConfigureAwait(false);
        try {
            // The caller owns the returned material, never the load's program reference. An
            // already-canceled caller must not acquire a material even when the program was cached.
            cancellationToken.ThrowIfCancellationRequested();
            return (T)(object)program.CreateMaterial();
        } finally {
            ReleaseAcquiredReference(program.Lifetime);
        }
    }

    /// <summary>
    /// Gets the engine registration for a built-in asset type.
    /// </summary>
    /// <remarks>
    /// The catalog always registers the built-in formats before the user registrations, so a missing
    /// or loader-backed registration means the registry was corrupted; the check exists so a future
    /// registration change fails loudly instead of loading through the wrong path.
    /// </remarks>
    /// <typeparam name="T">The built-in engine asset type.</typeparam>
    /// <returns>The matching engine registration.</returns>
    /// <exception cref="InvalidOperationException">The type is not registered as an engine format.</exception>
    private EngineAssetTypeRegistration<T> GetEngineRegistration<T>() {
        AssetTypeRegistration registration = _registry.Get(typeof(T));
        if (registration is not EngineAssetTypeRegistration<T> engineRegistration) {
            throw new InvalidOperationException(
                $"The asset type '{typeof(T).FullName}' is registered with a loader for a different "
                    + "asset type.");
        }

        return engineRegistration;
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

    /// <summary>
    /// Loads a new model wrapper over the shared cached part meshes and programs.
    /// </summary>
    /// <remarks>
    /// A model is not an engine asset: it has no cache entry, no shared identity, and no lifetime of
    /// its own. Every successful call resolves the packed descriptor and returns a new
    /// <see cref="Model"/> that owns one material per part and one mesh reference per distinct part
    /// mesh. The part loads themselves still use the shared engine cache, so repeated model loads share
    /// the underlying meshes and programs without sharing the model or its materials.
    /// </remarks>
    private Task<T> LoadModelAsync<T>(string canonical, CancellationToken cancellationToken) {
        AssetLoaderWorkItem workItem;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<T>(AssetLoadErrors.CreateNotFound(canonical));
            }

            if (visible.Entry.Type != AssetEntryType.Model) {
                return Task.FromException<T>(
                    AssetLoadErrors.CreateTypeMismatch(canonical, AssetEntryType.Model, visible.Entry.Type));
            }

            workItem = new AssetLoaderWorkItem(
                stopToken => LoadModelObjectAsync(visible, stopToken, cancellationToken),
                CompleteWorkItem,
                mapCancellationToStopped: false);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        // The work item observes the caller token through its linked token, so cancellation releases
        // every partly acquired part reference and material before the caller sees the exception. The
        // wait itself must not cancel independently: that could abandon an already constructed model.
        return AwaitModelWorkAsync<T>(workItem);
    }

    /// <summary>
    /// Builds one model on the loader thread by resolving every part's mesh and material through the
    /// public <see cref="Load{T}"/> path.
    /// </summary>
    /// <remarks>
    /// The method returns an incomplete task while the part loads run. <see cref="AssetLoaderWorkItem"/>
    /// does not block the loader loop on an incomplete task: the loader continues serving the part
    /// requests while the model task awaits them, so calling the public load path here cannot deadlock.
    /// A failed or canceled load releases every part mesh reference and material it already acquired,
    /// and no model cache entry exists to clean up, so a retry starts a fresh load. The caller token is
    /// linked to the catalog stop token for the duration of the load.
    /// </remarks>
    private async Task<object> LoadModelObjectAsync(
        VisibleAsset descriptorEntry,
        CancellationToken stopToken,
        CancellationToken cancellationToken) {
        string canonical = descriptorEntry.Entry.Path;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopToken,
            cancellationToken);
        List<Mesh> meshes = [];
        List<Shader> materials = [];
        try {
            linked.Token.ThrowIfCancellationRequested();

            ModelAssetDescriptor descriptor;
            try {
                byte[] descriptorPayload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
                descriptor = AssetSerialization.Deserialize<ModelAssetDescriptor>(descriptorPayload);
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the model descriptor could not be decoded: {exception.Message}");
            }

            ValidateModelDescriptor(descriptor, canonical);

            ModelPart[] parts = new ModelPart[descriptor.Parts.Length];
            Dictionary<string, Mesh> meshesByPath = new(StringComparer.Ordinal);
            for (int index = 0; index < descriptor.Parts.Length; index++) {
                linked.Token.ThrowIfCancellationRequested();
                ModelPartAsset part = descriptor.Parts[index];
                string meshPath = AssetPath.Normalize(part.MeshPath);
                if (!meshesByPath.TryGetValue(meshPath, out Mesh? mesh)) {
                    mesh = await Load<Mesh>(meshPath, linked.Token).ConfigureAwait(false);
                    meshesByPath.Add(meshPath, mesh);
                    meshes.Add(mesh);
                }

                linked.Token.ThrowIfCancellationRequested();
                // Every part gets its own material even when two parts share a program path. The
                // program is cached and reference-counted, so the parts still share one GPU program.
                string shaderPath = ResolveModelPartShaderPath(part.ShaderPath);
                Shader material = await Load<Shader>(shaderPath, linked.Token).ConfigureAwait(false);
                materials.Add(material);

                parts[index] = new ModelPart(
                    part.Name,
                    mesh,
                    material,
                    renderState: null,
                    localTransform: CreateLocalTransform(part.LocalTransform));
            }

            linked.Token.ThrowIfCancellationRequested();
            Model model = new(parts);
            if (linked.Token.IsCancellationRequested) {
                // The catalog stopped or the caller canceled while the model was being constructed.
                // The model already owns its part references, so dispose it before reporting the
                // cancellation instead of abandoning a fully constructed model.
                model.Dispose();
                linked.Token.ThrowIfCancellationRequested();
            }

            // The model constructor acquired its own mesh references. This load's references are the
            // transfer mechanism and are released now, so the model remains the only owner.
            ReleaseMeshReferences(meshes);
            materials.Clear();
            return model;
        } catch (Exception exception) {
            ReleaseMeshReferences(meshes);
            DisposeMaterials(materials);
            if (stopToken.IsCancellationRequested) {
                throw AssetLoadErrors.CreateStoppedException();
            }

            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) {
                throw;
            }

            throw AssetLoadErrors.WrapLoadFailure(canonical, "could not be loaded", exception);
        }
    }

    private static Transform CreateLocalTransform(float[] local) => new(
        new Vector3(local[0], local[1], local[2]),
        new Quaternion(local[3], local[4], local[5], local[6]),
        new Vector3(local[7], local[8], local[9]));

    private static void ReleaseMeshReferences(List<Mesh> meshes) {
        foreach (Mesh mesh in meshes) {
            try {
                mesh.Lifetime.ReleaseOwnerReference();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the part after the load started.
            }
        }

        meshes.Clear();
    }

    private static void DisposeMaterials(List<Shader> materials) {
        foreach (Shader material in materials) {
            try {
                material.Dispose();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the program or material after the load started.
            }
        }

        materials.Clear();
    }

    private static async Task<T> AwaitModelWorkAsync<T>(AssetLoaderWorkItem workItem) {
        object result = await workItem.Completion.ConfigureAwait(false);
        return (T)result;
    }

    private static string ResolveModelPartShaderPath(string? shaderPath) {
        if (shaderPath is null) {
            return DEFAULT_MODEL_SHADER_PATH;
        }

        string normalized = AssetPath.Normalize(shaderPath);
        // Packs baked before the mesh shader replaced the old default still name `shaders/default`.
        // They did not commit that program, so resolve the legacy path to the same built-in mesh
        // shader instead of failing the model load with AssetNotFoundException.
        return string.Equals(normalized, LEGACY_MODEL_SHADER_PATH, StringComparison.Ordinal)
            ? DEFAULT_MODEL_SHADER_PATH
            : normalized;
    }

    private static void ValidateModelDescriptor(ModelAssetDescriptor descriptor, string canonical) {
        if (descriptor.Parts is null || descriptor.Parts.Length == 0) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the model descriptor must carry at least one part");
        }

        ValidateBounds(descriptor.Bounds, canonical, "the model");
        foreach (ModelPartAsset? part in descriptor.Parts) {
            if (part is null) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "the model descriptor contains a null part");
            }

            if (string.IsNullOrWhiteSpace(part.Name)) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "a model part has an empty name");
            }

            if (!AssetPath.TryValidateCanonical(part.MeshPath, out string? meshReason)) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the mesh path '{part.MeshPath}' of model part '{part.Name}' is not canonical: "
                        + meshReason);
            }

            if (part.ShaderPath is not null
                && !AssetPath.TryValidateCanonical(part.ShaderPath, out string? shaderReason)) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the shader path '{part.ShaderPath}' of model part '{part.Name}' is not canonical: "
                        + shaderReason);
            }

            if (part.LocalTransform is null || part.LocalTransform.Length != 10) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"model part '{part.Name}' must carry exactly ten local-transform floats "
                        + "(position XYZ, rotation XYZW, and scale XYZ)");
            }

            foreach (float component in part.LocalTransform) {
                if (!float.IsFinite(component)) {
                    throw AssetLoadErrors.CreateLoadFailure(
                        canonical,
                        $"model part '{part.Name}' has a non-finite local-transform component");
                }
            }

            ValidateBounds(part.Bounds, canonical, $"model part '{part.Name}'");
        }
    }

    private static void ValidateBounds(float[]? bounds, string canonical, string owner) {
        if (bounds is null || bounds.Length != 6) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"{owner} must carry exactly six bounds floats (minimum XYZ, maximum XYZ)");
        }

        for (int index = 0; index < bounds.Length; index++) {
            if (!float.IsFinite(bounds[index])) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, $"{owner} has a non-finite bounds component");
            }
        }

        if (bounds[0] > bounds[3] || bounds[1] > bounds[4] || bounds[2] > bounds[5]) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, $"{owner} bounds minimum must not exceed the maximum");
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
        CancellationToken cancellationToken) {
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

            workItem = new AssetLoaderWorkItem(
                stopToken => LoadCustomObjectAsync(
                    visible,
                    registration.Loader,
                    stopToken,
                    cancellationToken),
                CompleteWorkItem,
                mapCancellationToStopped: false);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<T>(AssetLoadErrors.CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<T>(workItem, cancellationToken);
    }

    /// <summary>
    /// Loads one custom data asset on the loader thread through its registered loader.
    /// </summary>
    /// <remarks>
    /// The caller token is linked to the catalog stop token for the duration of the load. Stop
    /// cancellation is reported as <see cref="ObjectDisposedException"/>, while caller cancellation
    /// stays an <see cref="OperationCanceledException"/> so the work item completes as canceled
    /// instead of faulted.
    /// </remarks>
    private static async Task<object> LoadCustomObjectAsync<T>(
        VisibleAsset descriptorEntry,
        IAssetLoader<T> loader,
        CancellationToken stopToken,
        CancellationToken cancellationToken) {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            stopToken,
            cancellationToken);
        try {
            byte[] payload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
            AssetLoadContext context = new(descriptorEntry.Entry.Path, linked.Token, payload);
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
                    // A model load observes its caller token through a linked token. It reports
                    // OperationCanceledException after releasing every partly acquired part.
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
