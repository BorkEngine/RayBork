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
/// <see cref="BeginUpload"/> starts an upload without blocking: it may create the destination object
/// and submit a copy, but it must not wait for the GPU. <see cref="TryComplete"/> is a non-blocking
/// poll that returns <see langword="false"/> while the copy is pending and <see langword="true"/>
/// exactly once the operation finished, with either the resource or the failure. The bridge never
/// blocks the render loop on a fence; only <see cref="Restore"/> and <see cref="Cancel"/> wait, and
/// both run on the render thread when a device recovery, a cancellation, or shutdown makes the wait
/// acceptable.
/// </para>
/// <para>
/// <see cref="BeginUpload"/>, <see cref="Restore"/>, and <see cref="TryComplete"/> may report a
/// failure; the bridge converts it into an <see cref="AssetLoadException"/> with the <c>RB1010</c>
/// diagnostic code and keeps the render loop running. <see cref="Destroy"/> must not throw; the bridge
/// treats destruction as best effort so a teardown cannot stall.
/// </para>
/// </remarks>
internal interface IAssetGpuWorker {
    /// <summary>
    /// Gets whether no submitted frame can still reference a GPU asset resource.
    /// </summary>
    /// <remarks>
    /// The bridge defers destruction requests while this is <see langword="false"/>, so a texture or
    /// mesh destroyed after the tick that drew it stays alive until the GPU finished every frame that
    /// could reference it. The Vulkan worker polls every frame fence without blocking. Render-thread
    /// only.
    /// </remarks>
    bool AreFrameResourcesIdle { get; }

    /// <summary>
    /// Blocks until no submitted frame can still reference a GPU asset resource.
    /// </summary>
    /// <remarks>
    /// The render loop calls it once before it closes the bridge and destroys the live resources. It
    /// is not a per-frame operation; a device that already stopped cannot throw from it, because
    /// shutdown must still release what it can. Render-thread only.
    /// </remarks>
    void WaitForFrameResourcesIdle();

    /// <summary>
    /// Starts the GPU work for one upload without waiting for it to finish.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <returns>
    /// The in-flight operation. Its resource becomes available through <see cref="TryComplete"/>; the
    /// operation must be passed to <see cref="TryComplete"/> or <see cref="Cancel"/> exactly once.
    /// </returns>
    GpuAssetUploadOperation BeginUpload(AssetGpuUpload upload);

    /// <summary>
    /// Polls an in-flight upload without blocking.
    /// </summary>
    /// <param name="operation">The operation returned by <see cref="BeginUpload"/>.</param>
    /// <param name="resource">
    /// The created resource once the operation finished successfully; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="error">
    /// The failure once the operation finished with an error; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation finished, with either <paramref name="resource"/> or
    /// <paramref name="error"/> set; <see langword="false"/> while the GPU copy is still pending.
    /// </returns>
    bool TryComplete(
        GpuAssetUploadOperation operation,
        out GpuAssetResource? resource,
        out Exception? error);

    /// <summary>
    /// Abandons an in-flight upload and releases everything it created.
    /// </summary>
    /// <remarks>
    /// The bridge calls it on the render thread for a canceled request and when it closes. The call is
    /// idempotent, destroys partial and dependent GPU objects plus the staging allocation, and may wait
    /// for the upload fence because cancellation and shutdown are rare.
    /// </remarks>
    /// <param name="operation">The operation to cancel.</param>
    void Cancel(GpuAssetUploadOperation operation);

    /// <summary>
    /// Recreates the GPU objects of a live resource from its retained upload after a device recovery.
    /// </summary>
    /// <remarks>
    /// The implementation may update <paramref name="previous"/>'s handles and return it, or create a
    /// distinct resource whose handles <paramref name="previous"/> adopts. The bridge keeps
    /// <paramref name="previous"/> as the live entry, so a worker that carries more than the primary
    /// handle must either update <paramref name="previous"/> or override
    /// <see cref="GpuAssetResource.AdoptRestored"/>. The bridge calls this only after the backend
    /// reports a new logical-device generation, so the previous GPU objects belong to a destroyed
    /// device. A recovery is rare, so the call may wait synchronously for the recreated upload.
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
