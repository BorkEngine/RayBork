namespace RayBork.Internal;

/// <summary>
/// Creates, recreates, and destroys the GPU objects behind an engine asset.
/// </summary>
/// <remarks>
/// <para>
/// Every member runs on the render thread. The M3 Vulkan worker is the only implementation; unit
/// tests substitute a fake that records the calls. The interface never exposes a backend or native
/// type: an implementation stores its handles inside <see cref="GpuAssetResource"/> subclasses.
/// </para>
/// <para>
/// <see cref="Upload"/> and <see cref="Restore"/> may throw; the bridge converts a failure into an
/// <see cref="AssetLoadException"/> with the <c>RB1010</c> diagnostic code and keeps the render loop
/// running. <see cref="Destroy"/> must not throw; the bridge treats destruction as best effort so a
/// teardown cannot stall.
/// </para>
/// </remarks>
internal interface IAssetGpuWorker {
    /// <summary>
    /// Creates the GPU objects for one upload.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <returns>The created resource. It must not be <c>null</c>.</returns>
    GpuAssetResource Upload(AssetGpuUpload upload);

    /// <summary>
    /// Recreates the GPU objects of a live resource from its retained upload after a device recovery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The implementation may update <paramref name="previous"/>'s handles and return it, or create a
    /// distinct resource whose handles <paramref name="previous"/> adopts. The bridge keeps
    /// <paramref name="previous"/> as the live entry, so a worker that carries more than the primary
    /// handle must either update <paramref name="previous"/> or override
    /// <see cref="GpuAssetResource.AdoptRestored"/>.
    /// </para>
    /// <para>
    /// The M3 render loop restores when the backend's recovery counter advances, which also happens
    /// for presentation-only recovery. The implementation must therefore replace the previous GPU
    /// objects without leaking them whether or not the device was recreated.
    /// </para>
    /// </remarks>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="previous">The live resource whose handles are replaced.</param>
    /// <returns>The resource that owns the recreated GPU objects. It must not be <c>null</c>.</returns>
    GpuAssetResource Restore(AssetGpuUpload upload, GpuAssetResource previous);

    /// <summary>
    /// Destroys the GPU objects of a resource. The bridge calls it exactly once, on the render thread.
    /// </summary>
    /// <param name="resource">The resource to destroy.</param>
    void Destroy(GpuAssetResource resource);
}
