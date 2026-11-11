namespace RayBork.Internal;

/// <summary>
/// The render-thread-owned GPU objects behind one uploaded engine asset.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="IAssetGpuWorker"/> creates a resource on the render thread; from then on every
/// member is render-thread affine except <see cref="Handle"/> and <see cref="IsDestroyed"/>, which a
/// caller may read after a destruction request. The base retains the <see cref="Upload"/> that
/// created the resource so a device recovery can recreate it.
/// </para>
/// <para>
/// A Vulkan worker derives from this class and stores the image, view, sampler, buffer, or
/// shader-module handles it owns, keeps <see cref="Handle"/> equal to the primary native handle, and
/// overrides <see cref="AdoptRestored"/> to copy every handle when a restoration returns a distinct
/// resource instance. A resource is destroyed exactly once by the bridge; <see
/// cref="IAssetGpuWorker.Destroy"/> runs on the render thread after the destruction request is
/// drained.
/// </para>
/// </remarks>
internal abstract class GpuAssetResource {
    private long _handle;
    private int _destroyed;

    /// <summary>
    /// Initializes a resource for one upload and its primary native handle.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="handle">The primary native handle, or <c>0</c> when the resource has none yet.</param>
    /// <exception cref="ArgumentNullException"><paramref name="upload"/> is <c>null</c>.</exception>
    private protected GpuAssetResource(AssetGpuUpload upload, long handle) {
        ArgumentNullException.ThrowIfNull(upload);

        Upload = upload;
        _handle = handle;
    }

    /// <summary>
    /// Gets the upload description the resource was created from. It is retained for device recovery.
    /// </summary>
    internal AssetGpuUpload Upload { get; }

    /// <summary>
    /// Gets the kind of GPU resource.
    /// </summary>
    internal AssetGpuUploadKind Kind => Upload.Kind;

    /// <summary>
    /// Gets the primary native handle as an opaque token, or <c>0</c> before it is published. The
    /// bridge reads it after a restoration; it is not a public identifier.
    /// </summary>
    internal long Handle => Volatile.Read(ref _handle);

    /// <summary>
    /// Gets whether the bridge already marked the resource for destruction.
    /// </summary>
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

    /// <summary>
    /// Publishes the primary native handle of a recreated GPU object. Called on the render thread
    /// after a successful device recovery; the write is atomic so a reader sees either the old or the
    /// new handle, never a torn value.
    /// </summary>
    /// <param name="handle">The new primary native handle.</param>
    internal void PublishHandle(long handle) => Volatile.Write(ref _handle, handle);

    /// <summary>
    /// Adopts the handles of a resource that was recreated after a device recovery.
    /// </summary>
    /// <remarks>
    /// The bridge calls this on the live resource and keeps that instance as the live entry, so a
    /// public asset keeps its identity across a recovery. The default implementation publishes only
    /// <see cref="Handle"/>; a worker that owns more than one native handle overrides it and copies
    /// all of them.
    /// </remarks>
    /// <param name="restored">The resource returned by <see cref="IAssetGpuWorker.Restore"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="restored"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="restored"/> has a different <see cref="Kind"/>.
    /// </exception>
    internal virtual void AdoptRestored(GpuAssetResource restored) {
        ArgumentNullException.ThrowIfNull(restored);
        if (restored.Kind != Kind) {
            throw new InvalidOperationException(
                $"Cannot adopt a restored {restored.Kind} resource into a live {Kind} resource.");
        }

        PublishHandle(restored.Handle);
    }

    /// <summary>
    /// Marks the resource destroyed and reports whether this call won the single transition.
    /// </summary>
    /// <returns><see langword="true"/> for the first call; <see langword="false"/> afterwards.</returns>
    internal bool TryMarkDestroyed() => Interlocked.Exchange(ref _destroyed, 1) == 0;
}
