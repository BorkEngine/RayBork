using System.Buffers.Binary;
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
/// custom data and <see cref="LoadData"/> payloads are never cached.
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

    private readonly ILogger _logger;
    private readonly GpuAssetBridge _bridge;
    private readonly ProceduralAssetService _proceduralAssets;
    private readonly ReadOnlyMemory<byte> _builtInPack;
    private readonly string[] _userPackPaths;
    private readonly Dictionary<Type, AssetTypeRegistration> _customTypes;
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
    /// <param name="assetTypes">The custom data type registrations.</param>
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
        _customTypes = new Dictionary<Type, AssetTypeRegistration>(assetTypes.Count);
        foreach (AssetTypeRegistration registration in assetTypes) {
            if (!_customTypes.TryAdd(registration.AssetType, registration)) {
                throw new InvalidOperationException(
                    $"The asset type '{registration.AssetType.FullName}' is registered more than once.");
            }
        }
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

            return Task.FromException<T>(CreateNotFound(canonical));
        }

        Type assetType = typeof(T);
        if (assetType == typeof(Mesh) || assetType == typeof(Texture) || assetType == typeof(Shader)) {
            return LoadEngineAsync<T>(canonical, assetType, cancellationToken);
        }

        AssetTypeRegistration? registration;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            _customTypes.TryGetValue(assetType, out registration);
        }

        if (registration is not null) {
            return LoadCustomAsync<T>(canonical, registration, cancellationToken);
        }

        throw new NotSupportedException(
            $"The asset type '{assetType.FullName}' is not a built-in engine asset (Mesh, Texture, or "
                + "Shader) and is not a registered custom data type. Register custom types with "
                + "WithAssetType<T>() before Build.");
    }

    /// <inheritdoc/>
    public Task<byte[]> LoadData(string path, CancellationToken cancellationToken = default) {
        string canonical = AssetPath.Normalize(path);
        if (ProceduralAssetIds.IsReserved(canonical)) {
            lock (_gate) {
                ThrowIfUnavailableLocked();
            }

            return Task.FromException<byte[]>(CreateNotFound(canonical));
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
                    throw CreateStoppedException();
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

        ObjectDisposedException stopped = CreateStoppedException();
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

    private Task<T> LoadEngineAsync<T>(string canonical, Type assetType, CancellationToken cancellationToken) {
        EngineCacheEntry entry;
        AssetLoaderWorkItem? workItem = null;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<T>(CreateNotFound(canonical));
            }

            AssetKey key = new(canonical, assetType);
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
                    stopToken => LoadEngineObjectAsync(entry, visibleStack, visible, assetType, stopToken),
                    CompleteWorkItem);
                entry.Completion = workItem.Completion;
                if (!TryEnqueueLocked(workItem)) {
                    _engineCache.Remove(key);
                    return Task.FromException<T>(CreateLoaderQueueFull(canonical));
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

        throw CreateStoppedException();
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
    private async Task<object> LoadEngineObjectAsync(
        EngineCacheEntry entry,
        Dictionary<string, VisibleAsset> visibleStack,
        VisibleAsset descriptorEntry,
        Type assetType,
        CancellationToken stopToken) {
        GpuAssetResource? resource = null;
        try {
            AssetEntryType expected = ExpectedEntryType(assetType);
            if (descriptorEntry.Entry.Type != expected) {
                throw CreateTypeMismatch(descriptorEntry.Entry.Path, expected, descriptorEntry.Entry.Type);
            }

            DecodedAsset decoded = DecodeAsset(
                assetType,
                descriptorEntry.Entry.Path,
                descriptorEntry,
                visibleStack);
            resource = await _bridge.UploadAsync(decoded.Upload, stopToken).ConfigureAwait(false);
            stopToken.ThrowIfCancellationRequested();

            EngineAsset lifetime = new(
                AssetPath.ToPublic(descriptorEntry.Entry.Path),
                resource,
                QueueDestruction,
                () => OnOwnersReleased(entry));
            object asset = decoded.Create(lifetime);
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

            throw WrapLoadFailure(descriptorEntry.Entry.Path, "could not be loaded", exception);
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
        AssetTypeRegistration registration,
        CancellationToken cancellationToken) {
        AssetLoaderWorkItem workItem;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<T>(CreateNotFound(canonical));
            }

            workItem = new AssetLoaderWorkItem(
                _ => Task.FromResult<object>(DeserializeCustom(visible, registration)),
                CompleteWorkItem);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<T>(CreateLoaderQueueFull(canonical));
            }
        }

        return AwaitWorkAsync<T>(workItem, cancellationToken);
    }

    private object DeserializeCustom(VisibleAsset entry, AssetTypeRegistration registration) {
        if (entry.Entry.Type != AssetEntryType.Data) {
            throw CreateTypeMismatch(entry.Entry.Path, AssetEntryType.Data, entry.Entry.Type);
        }

        try {
            byte[] payload = entry.Archive.ReadPayload(entry.Entry);
            return registration.Deserialize(payload);
        } catch (Exception exception) {
            throw WrapLoadFailure(entry.Entry.Path, "custom data could not be deserialized", exception);
        }
    }

    private Task<byte[]> LoadDataAsync(string canonical, CancellationToken cancellationToken) {
        AssetLoaderWorkItem workItem;
        lock (_gate) {
            ThrowIfUnavailableLocked();
            if (!_visible.TryGetValue(canonical, out VisibleAsset visible)) {
                return Task.FromException<byte[]>(CreateNotFound(canonical));
            }

            workItem = new AssetLoaderWorkItem(
                _ => Task.FromResult<object>(visible.Archive.ReadPayload(visible.Entry)),
                CompleteWorkItem);
            if (!TryEnqueueLocked(workItem)) {
                return Task.FromException<byte[]>(CreateLoaderQueueFull(canonical));
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

    private static DecodedAsset DecodeAsset(
        Type assetType,
        string canonical,
        VisibleAsset descriptorEntry,
        Dictionary<string, VisibleAsset> visibleStack) {
        byte[] descriptorPayload = descriptorEntry.Archive.ReadPayload(descriptorEntry.Entry);
        if (assetType == typeof(Mesh)) {
            MeshAssetDescriptor descriptor = AssetSerialization.Deserialize<MeshAssetDescriptor>(descriptorPayload);
            byte[] payload = ReadRawPayload(visibleStack, descriptor.Payload, canonical, "mesh");
            ValidateMesh(descriptor, payload, canonical);
            Vertex[] vertices = MeshPayloadCodec.DecodeVertices(
                payload,
                checked((int)descriptor.VertexCount));
            uint[] indices = MeshPayloadCodec.DecodeIndices(
                payload,
                checked((int)descriptor.VertexCount),
                checked((int)descriptor.IndexCount));
            MeshAssetDescriptor captured = descriptor;
            return new DecodedAsset(
                new MeshGpuUpload(canonical, descriptor, payload),
                lifetime => new Mesh(lifetime, captured, vertices, indices, isProcedural: false));
        }

        if (assetType == typeof(Texture)) {
            TextureAssetDescriptor descriptor = AssetSerialization.Deserialize<TextureAssetDescriptor>(descriptorPayload);
            byte[] payload = ReadRawPayload(visibleStack, descriptor.Payload, canonical, "KTX2");
            ValidateTexture(descriptor, payload, canonical);
            TextureAssetDescriptor captured = descriptor;
            return new DecodedAsset(
                new TextureGpuUpload(canonical, descriptor, payload),
                lifetime => new Texture(lifetime, captured));
        }

        if (assetType == typeof(Shader)) {
            ShaderAssetDescriptor descriptor = AssetSerialization.Deserialize<ShaderAssetDescriptor>(descriptorPayload);
            byte[] vertexPayload = ReadRawPayload(
                visibleStack,
                descriptor.VertexPayload,
                canonical,
                "vertex SPIR-V");
            byte[] fragmentPayload = ReadRawPayload(
                visibleStack,
                descriptor.FragmentPayload,
                canonical,
                "fragment SPIR-V");
            ValidateShader(descriptor, vertexPayload, fragmentPayload, canonical);
            ShaderAssetDescriptor captured = descriptor;
            return new DecodedAsset(
                new ShaderGpuUpload(canonical, descriptor, vertexPayload, fragmentPayload),
                lifetime => new Shader(lifetime, captured));
        }

        throw new NotSupportedException(
            $"The asset type '{assetType.FullName}' is not a built-in engine asset.");
    }

    private static byte[] ReadRawPayload(
        Dictionary<string, VisibleAsset> visibleStack,
        string payloadPath,
        string assetPath,
        string payloadKind) {
        if (!AssetPath.TryValidateCanonical(payloadPath, out string? reason)) {
            throw CreateLoadFailure(
                assetPath,
                $"the {payloadKind} payload path '{payloadPath}' is not canonical: {reason}");
        }

        if (!visibleStack.TryGetValue(payloadPath, out VisibleAsset payload)) {
            throw CreateLoadFailure(
                assetPath,
                $"the {payloadKind} payload '{payloadPath}' is not in the visible pack stack");
        }

        if (payload.Entry.Type != AssetEntryType.Raw) {
            throw CreateLoadFailure(
                assetPath,
                $"the {payloadKind} payload '{payloadPath}' is a {payload.Entry.Type} entry, not raw");
        }

        return payload.Archive.ReadPayload(payload.Entry);
    }

    private static void ValidateMesh(MeshAssetDescriptor descriptor, byte[] payload, string canonical) {
        if (descriptor.Bounds is null || descriptor.Bounds.Length != 6) {
            throw CreateLoadFailure(
                canonical,
                "the mesh descriptor must carry six bounds floats (minimum XYZ, maximum XYZ)");
        }

        for (int i = 0; i < descriptor.Bounds.Length; i++) {
            if (!float.IsFinite(descriptor.Bounds[i])) {
                throw CreateLoadFailure(canonical, "the mesh descriptor bounds must be finite");
            }
        }

        if (descriptor.Bounds[0] > descriptor.Bounds[3]
            || descriptor.Bounds[1] > descriptor.Bounds[4]
            || descriptor.Bounds[2] > descriptor.Bounds[5]) {
            throw CreateLoadFailure(
                canonical,
                "the mesh descriptor bounds minimum must not exceed the maximum");
        }

        if (descriptor.VertexCount == 0 || descriptor.IndexCount == 0) {
            throw CreateLoadFailure(
                canonical,
                "the mesh descriptor must declare at least one vertex and one index");
        }

        if (!Enum.IsDefined(descriptor.Topology)) {
            throw CreateLoadFailure(
                canonical,
                $"the mesh descriptor topology {(int)descriptor.Topology} is not a defined value");
        }

        long expectedLength = (long)descriptor.VertexCount * MeshAssetDescriptor.VERTEX_STRIDE
            + (long)descriptor.IndexCount * sizeof(uint);
        if (payload.Length != expectedLength) {
            throw CreateLoadFailure(
                canonical,
                $"the mesh payload is {payload.Length} bytes; the descriptor requires exactly "
                    + $"{expectedLength} bytes for {descriptor.VertexCount} vertices and "
                    + $"{descriptor.IndexCount} indices");
        }

        // The CPU indices are exposed on the loaded mesh, so an out-of-range index is a corrupt payload
        // even though the GPU buffers would accept the raw bytes.
        ReadOnlySpan<byte> payloadSpan = payload;
        int indexCount = checked((int)descriptor.IndexCount);
        int indexOffset = checked((int)descriptor.VertexCount) * MeshAssetDescriptor.VERTEX_STRIDE;
        for (int index = 0; index < indexCount; index++) {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                payloadSpan[(indexOffset + (index * sizeof(uint)))..]);
            if (value >= descriptor.VertexCount) {
                throw CreateLoadFailure(
                    canonical,
                    $"the mesh index {value} at position {index} is outside the "
                        + $"{descriptor.VertexCount} vertices");
            }
        }
    }

    private static void ValidateTexture(
        TextureAssetDescriptor descriptor,
        byte[] payload,
        string canonical) {
        if (descriptor.Width == 0 || descriptor.Height == 0) {
            throw CreateLoadFailure(canonical, "the texture descriptor must declare a non-zero size");
        }

        if (descriptor.MipLevels == 0) {
            throw CreateLoadFailure(canonical, "the texture descriptor must declare at least one mip level");
        }

        uint maxMipLevels = (uint)BitOperations.Log2(Math.Max(descriptor.Width, descriptor.Height)) + 1u;
        if (descriptor.MipLevels > maxMipLevels) {
            throw CreateLoadFailure(
                canonical,
                $"the texture descriptor declares {descriptor.MipLevels} mip levels; "
                    + $"{descriptor.Width}x{descriptor.Height} supports at most {maxMipLevels}");
        }

        if (!Enum.IsDefined(descriptor.Format)
            || descriptor.Format is TextureFormat.Rgba16Float or TextureFormat.Depth32Float) {
            throw CreateLoadFailure(
                canonical,
                $"the texture descriptor format {(int)descriptor.Format} is not a packed texture format");
        }

        bool srgbFormat = descriptor.Format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb;
        if (descriptor.IsSrgb != srgbFormat) {
            throw CreateLoadFailure(
                canonical,
                $"the texture descriptor sRGB flag {descriptor.IsSrgb} does not match format "
                    + $"{descriptor.Format}");
        }

        if (!Enum.IsDefined(descriptor.Filter) || !Enum.IsDefined(descriptor.Wrap)) {
            throw CreateLoadFailure(canonical, "the texture descriptor sampler state is not valid");
        }

        if (!float.IsFinite(descriptor.MaxAnisotropy) || descriptor.MaxAnisotropy < 1f) {
            throw CreateLoadFailure(
                canonical,
                "the texture descriptor maximum anisotropy must be finite and at least 1");
        }

        if (payload.Length == 0) {
            throw CreateLoadFailure(canonical, "the texture KTX2 payload is empty");
        }
    }

    private static void ValidateShader(
        ShaderAssetDescriptor descriptor,
        byte[] vertexPayload,
        byte[] fragmentPayload,
        string canonical) {
        const ShaderStageMask requiredStages = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if (descriptor.Stages != requiredStages) {
            throw CreateLoadFailure(
                canonical,
                $"the shader descriptor stages 0x{(int)descriptor.Stages:x} do not describe a complete "
                    + "vertex+fragment program");
        }

        if (string.IsNullOrWhiteSpace(descriptor.VertexEntry)
            || string.IsNullOrWhiteSpace(descriptor.FragmentEntry)) {
            throw CreateLoadFailure(canonical, "the shader descriptor must name both entry points");
        }

        if (!IsSpirvModule(vertexPayload) || !IsSpirvModule(fragmentPayload)) {
            throw CreateLoadFailure(
                canonical,
                "the shader SPIR-V payloads must be non-empty, 4-byte aligned modules");
        }

        if (descriptor.Uniforms is null || descriptor.Textures is null) {
            throw CreateLoadFailure(canonical, "the shader descriptor metadata arrays must not be null");
        }

        HashSet<string> textureNames = new(StringComparer.Ordinal);
        foreach (string? texture in descriptor.Textures) {
            if (string.IsNullOrWhiteSpace(texture)) {
                throw CreateLoadFailure(canonical, "the shader descriptor has an empty texture name");
            }

            if (!textureNames.Add(texture)) {
                throw CreateLoadFailure(
                    canonical,
                    $"the shader descriptor repeats the texture name '{texture}'");
            }
        }

        foreach (ShaderUniformAsset? uniform in descriptor.Uniforms) {
            if (uniform is null
                || string.IsNullOrWhiteSpace(uniform.Name)
                || !Enum.IsDefined(uniform.Type)
                || uniform.Default is null) {
                throw CreateLoadFailure(canonical, "the shader descriptor has invalid uniform metadata");
            }
        }
    }

    private static bool IsSpirvModule(byte[] payload)
        => payload.Length >= 20 && payload.Length % sizeof(uint) == 0;

    private static AssetEntryType ExpectedEntryType(Type assetType)
        => assetType == typeof(Mesh)
            ? AssetEntryType.Mesh
            : assetType == typeof(Texture)
                ? AssetEntryType.Texture
                : AssetEntryType.Shader;

    private static AssetNotFoundException CreateNotFound(string canonical) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' is not in any mounted pack.",
        DiagnosticCodes.ASSET_NOT_FOUND,
        null);

    private static AssetTypeMismatchException CreateTypeMismatch(
        string canonical,
        AssetEntryType expected,
        AssetEntryType actual) => new(
            $"The asset '{AssetPath.ToPublic(canonical)}' was baked as {actual}, but it was requested as "
                + $"{expected}.",
            DiagnosticCodes.ASSET_TYPE_MISMATCH,
            null);

    private static AssetLoadException CreateLoadFailure(string canonical, string reason) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' could not be loaded: {reason}.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    private static AssetLoadException CreateLoaderQueueFull(string canonical) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' could not be queued: the asset loader has "
            + $"{MAX_PENDING_LOADS} pending requests.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    private static Exception WrapLoadFailure(string canonical, string what, Exception exception) {
        if (exception is AssetPackException
            or AssetTypeMismatchException
            or AssetNotFoundException) {
            return exception;
        }

        if (exception is AssetLoadException loadFailure
            && (string.Equals(
                    loadFailure.DiagnosticCode,
                    DiagnosticCodes.ASSET_LOAD_FAILED,
                    StringComparison.Ordinal)
                || string.Equals(
                    loadFailure.DiagnosticCode,
                    DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                    StringComparison.Ordinal))) {
            return loadFailure;
        }

        return new AssetLoadException(
            $"The asset '{AssetPath.ToPublic(canonical)}' {what}: {exception.Message}",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            exception);
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

    private static ObjectDisposedException CreateStoppedException() => new(
        nameof(AssetCatalog),
        "The asset catalog is stopped.");

    private readonly record struct AssetKey(string Path, Type AssetType);

    private readonly record struct VisibleAsset(PackArchive Archive, PackEntry Entry);

    private readonly record struct DecodedAsset(
        AssetGpuUpload Upload,
        Func<EngineAsset, object> Create);

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
        private readonly TaskCompletionSource<object> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal AssetLoaderWorkItem(
            Func<CancellationToken, Task<object>> work,
            Action<AssetLoaderWorkItem> onCompleted) {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(onCompleted);

            _work = work;
            _onCompleted = onCompleted;
        }

        internal Task<object> Completion => _completion.Task;

        internal void Execute(CancellationToken stopToken) {
            Task<object> work;
            try {
                work = _work(stopToken);
            } catch (OperationCanceledException) when (stopToken.IsCancellationRequested) {
                _completion.TrySetException(CreateStoppedException());
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
                // The loader work only observes the catalog stop token, whose cancellation is reported
                // as ObjectDisposedException at the public caller.
                _completion.TrySetException(CreateStoppedException());
                return;
            }

            Exception exception = work.Exception?.InnerException
                ?? (Exception?)work.Exception
                ?? new InvalidOperationException("The asset loader work item failed.");
            _completion.TrySetException(exception);
        }
    }
}
