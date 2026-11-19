namespace RayBork.Internal;

/// <summary>
/// The shared state of one Vulkan asset resource: the device generation that owns its handles and
/// the one-shot release gate.
/// </summary>
/// <remarks>
/// A device recreation destroys every earlier handle, so <see cref="VulkanAssetWorker.Destroy"/>
/// skips a resource from an older generation. Every member is render-thread affine, and the release
/// gate makes destruction idempotent.
/// </remarks>
internal abstract class VulkanAssetResource : GpuAssetResource
{
    private int _released;

    /// <summary>Initializes a Vulkan asset resource.</summary>
    private protected VulkanAssetResource(AssetGpuUpload upload, long handle, int deviceGeneration)
        : base(upload, handle)
    {
        DeviceGeneration = deviceGeneration;
    }

    /// <summary>Gets the logical-device generation that owns the native handles.</summary>
    internal int DeviceGeneration { get; private set; }

    /// <summary>Marks the resource released; <see langword="true"/> only for the first call.</summary>
    internal bool TryRelease() => Interlocked.Exchange(ref _released, 1) == 0;

    /// <summary>Re-arms the release gate for the handles of a recreated resource.</summary>
    /// <remarks>
    /// Without it, destroying the adopted handles would be skipped and they would leak.
    /// </remarks>
    private protected void ResetRelease() => Volatile.Write(ref _released, 0);

    /// <summary>Adopts the generation of a recreated resource.</summary>
    private protected void AdoptGeneration(VulkanAssetResource restored) =>
        DeviceGeneration = restored.DeviceGeneration;

    /// <summary>Casts a restored resource to the expected concrete type.</summary>
    /// <exception cref="InvalidOperationException">
    /// The restored resource is a different concrete type.
    /// </exception>
    private protected T CastRestored<T>(GpuAssetResource restored)
        where T : VulkanAssetResource
    {
        if (restored is not T typed)
        {
            throw new InvalidOperationException(
                $"Cannot adopt a {restored.GetType().Name} resource into a {GetType().Name} resource."
            );
        }

        return typed;
    }
}
