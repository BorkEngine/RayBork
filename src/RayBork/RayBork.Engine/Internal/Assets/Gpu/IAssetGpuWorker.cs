namespace RayBork.Internal;

/// <summary>
/// Creates, recreates, and destroys the GPU objects behind an engine asset.
/// </summary>
/// <remarks>
/// Every member runs on the render thread and must not expose a backend or native type.
/// Failures become an <see cref="AssetLoadException"/> with <c>RB1010</c>; <see cref="Destroy"/>
/// must not throw.
/// </remarks>
internal interface IAssetGpuWorker
{
    /// <summary>Gets whether no submitted frame can still reference a GPU asset resource.</summary>
    /// <remarks>
    /// Destruction requests are deferred while this is <see langword="false"/>.
    /// </remarks>
    bool AreFrameResourcesIdle { get; }

    /// <summary>Blocks until no submitted frame can still reference a GPU asset resource.</summary>
    /// <remarks>
    /// Runs once before shutdown and must not throw. Render-thread only.
    /// </remarks>
    void WaitForFrameResourcesIdle();

    /// <summary>
    /// Starts the GPU work for one upload without waiting; the operation must reach
    /// <see cref="TryComplete"/> or <see cref="Cancel"/> exactly once.
    /// </summary>
    GpuAssetUploadOperation BeginUpload(AssetGpuUpload upload);

    /// <summary>Polls an in-flight upload without blocking.</summary>
    /// <returns><see langword="true"/> once finished, with the resource or error set.</returns>
    bool TryComplete(
        GpuAssetUploadOperation operation,
        out GpuAssetResource? resource,
        out Exception? error
    );

    /// <summary>Abandons an in-flight upload and releases its objects; idempotent.</summary>
    void Cancel(GpuAssetUploadOperation operation);

    /// <summary>Recreates a live resource's GPU objects after a device recovery.</summary>
    /// <remarks>
    /// The live instance is kept, so a worker owning several handles must update it or override
    /// <see cref="GpuAssetResource.AdoptRestored"/>. The call may wait for the recreated upload.
    /// </remarks>
    GpuAssetResource Restore(AssetGpuUpload upload, GpuAssetResource previous);

    /// <summary>Destroys the GPU objects of a resource exactly once on the render thread.</summary>
    void Destroy(GpuAssetResource resource);
}
