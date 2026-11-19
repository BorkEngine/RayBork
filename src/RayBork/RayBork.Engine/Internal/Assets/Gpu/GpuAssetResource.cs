namespace RayBork.Internal;

/// <summary>
/// The render-thread-owned GPU objects behind one uploaded engine asset.
/// </summary>
/// <remarks>
/// The resource retains its <see cref="Upload"/> so a device recovery can recreate it. A derived
/// worker keeps <see cref="Handle"/> equal to its primary native handle and overrides
/// <see cref="AdoptRestored"/> when it owns more than one handle.
/// </remarks>
internal abstract class GpuAssetResource
{
    private long _handle;
    private int _destroyed;

    /// <summary>Initializes a resource for one upload and its primary native handle.</summary>
    private protected GpuAssetResource(AssetGpuUpload upload, long handle)
    {
        ArgumentNullException.ThrowIfNull(upload);

        Upload = upload;
        _handle = handle;
    }

    /// <summary>Gets the retained upload description used for device recovery.</summary>
    internal AssetGpuUpload Upload { get; }

    /// <summary>Gets the kind of GPU resource.</summary>
    internal AssetGpuUploadKind Kind => Upload.Kind;

    /// <summary>Gets the primary native handle, or <c>0</c> before it is published.</summary>
    internal long Handle => Volatile.Read(ref _handle);

    /// <summary>Gets whether the bridge already marked the resource for destruction.</summary>
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

    /// <summary>Publishes the primary native handle of a recreated GPU object atomically.</summary>
    internal void PublishHandle(long handle) => Volatile.Write(ref _handle, handle);

    /// <summary>Adopts the handles of a resource recreated after a device recovery.</summary>
    /// <remarks>
    /// The default publishes only <see cref="Handle"/>; a worker owning more handles overrides this.
    /// </remarks>
    internal virtual void AdoptRestored(GpuAssetResource restored)
    {
        ArgumentNullException.ThrowIfNull(restored);
        if (restored.Kind != Kind)
        {
            throw new InvalidOperationException(
                $"Cannot adopt a restored {restored.Kind} resource into a live {Kind} resource."
            );
        }

        PublishHandle(restored.Handle);
    }

    /// <summary>Marks the resource destroyed and reports whether this call won.</summary>
    /// <returns><see langword="true"/> for the first call.</returns>
    internal bool TryMarkDestroyed() => Interlocked.Exchange(ref _destroyed, 1) == 0;
}
