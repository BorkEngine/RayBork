namespace RayBork.Internal;

/// <summary>
/// The shared state of one Vulkan-created asset resource: the logical-device generation that owns its
/// native handles and the one-shot release gate.
/// </summary>
/// <remarks>
/// <para>
/// A device recreation destroys every handle created before it, so a resource records the
/// <see cref="Vulkan12Backend.DeviceGeneration"/> it was created on. <see
/// cref="VulkanAssetWorker.Destroy"/> skips a resource whose generation is older than the current one:
/// its device already released the objects, and touching the stale handles on the new device would be
/// invalid. A successful <see cref="IAssetGpuWorker.Restore"/> replaces the handles and adopts the
/// recreated resource's generation.
/// </para>
/// <para>
/// Every member is render-thread affine. The release gate makes <see
/// cref="VulkanAssetWorker.Destroy"/> idempotent even if a caller invokes it twice.
/// </para>
/// </remarks>
internal abstract class VulkanAssetResource : GpuAssetResource {
    private int _released;

    /// <summary>
    /// Initializes a Vulkan asset resource.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="handle">The primary native handle, or <c>0</c> before creation completes.</param>
    /// <param name="deviceGeneration">The logical-device generation that owns the handles.</param>
    private protected VulkanAssetResource(AssetGpuUpload upload, long handle, int deviceGeneration)
        : base(upload, handle) {
        DeviceGeneration = deviceGeneration;
    }

    /// <summary>
    /// Gets the logical-device generation that owns the native handles.
    /// </summary>
    internal int DeviceGeneration { get; private set; }

    /// <summary>
    /// Marks the resource released and reports whether this call won the single transition.
    /// </summary>
    /// <returns><see langword="true"/> for the first call; <see langword="false"/> afterwards.</returns>
    internal bool TryRelease() => Interlocked.Exchange(ref _released, 1) == 0;

    /// <summary>
    /// Re-arms the release gate for the handles of a recreated resource.
    /// </summary>
    /// <remarks>
    /// A device recreation destroys the previous handles before the device is destroyed and then
    /// adopts the recreated handles into the same live instance. Without re-arming, the later
    /// destruction of the recreated handles would be skipped and they would leak.
    /// </remarks>
    private protected void ResetRelease() => Volatile.Write(ref _released, 0);

    /// <summary>
    /// Adopts the generation of a resource recreated after a device recovery.
    /// </summary>
    /// <param name="restored">The recreated resource.</param>
    private protected void AdoptGeneration(VulkanAssetResource restored) => DeviceGeneration = restored.DeviceGeneration;

    /// <summary>
    /// Casts a restored resource to the expected concrete resource type.
    /// </summary>
    /// <typeparam name="T">The concrete resource type.</typeparam>
    /// <param name="restored">The resource returned by <see cref="IAssetGpuWorker.Restore"/>.</param>
    /// <returns>The typed restored resource.</returns>
    /// <exception cref="InvalidOperationException">
    /// The restored resource is a different concrete type.
    /// </exception>
    private protected T CastRestored<T>(GpuAssetResource restored) where T : VulkanAssetResource {
        if (restored is not T typed) {
            throw new InvalidOperationException(
                $"Cannot adopt a {restored.GetType().Name} resource into a {GetType().Name} resource.");
        }

        return typed;
    }
}
