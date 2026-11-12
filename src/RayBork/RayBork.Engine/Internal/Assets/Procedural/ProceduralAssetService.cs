namespace RayBork.Internal;

/// <summary>
/// The app-lifetime service behind <see cref="MeshBuilder"/> and <see cref="TextureBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// The service owns only the GPU upload handoff. It queues a procedural asset's upload on the
/// <see cref="GpuAssetBridge"/> and queues the resource destruction after the last owner releases it,
/// exactly like the pack catalog does for a loaded asset, but it never caches an asset and never
/// resolves a path.
/// </para>
/// <para>
/// The catalog owns one instance for the app lifetime and stops it during shutdown. A factory or build
/// call after that throws <see cref="ObjectDisposedException"/>; a build that is already waiting on an
/// upload fails when the bridge closes on the render thread. Every member is thread-safe: builders may
/// be created and built on any thread, and the bridge performs the render-thread handoff.
/// </para>
/// </remarks>
internal sealed class ProceduralAssetService {
    private readonly GpuAssetBridge _bridge;
    private int _stopped;

    /// <summary>
    /// Initializes the service over the app's GPU asset bridge.
    /// </summary>
    /// <param name="bridge">The bridge serviced by the render loop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="bridge"/> is <c>null</c>.</exception>
    internal ProceduralAssetService(GpuAssetBridge bridge) {
        ArgumentNullException.ThrowIfNull(bridge);

        _bridge = bridge;
    }

    /// <summary>
    /// Gets whether the app already stopped the service.
    /// </summary>
    internal bool IsStopped => Volatile.Read(ref _stopped) != 0;

    /// <summary>
    /// Creates a builder for one procedural mesh.
    /// </summary>
    /// <param name="topology">The primitive topology of the mesh.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    internal MeshBuilder CreateMeshBuilder(PrimitiveTopology topology = PrimitiveTopology.TriangleList) {
        ThrowIfStopped();
        return new MeshBuilder(this, topology);
    }

    /// <summary>
    /// Creates a builder for one procedural texture.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="format">The uncompressed storage format.</param>
    /// <param name="generateMips">Whether a build generates the full mip chain when none exists.</param>
    /// <param name="sampler">The sampler state, or the default linear repeat sampler when omitted.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    internal TextureBuilder CreateTextureBuilder(
        int width,
        int height,
        TextureFormat format = TextureFormat.Rgba8Srgb,
        bool generateMips = true,
        SamplerState? sampler = null) {
        ThrowIfStopped();
        return new TextureBuilder(this, width, height, format, generateMips, sampler);
    }

    /// <summary>
    /// Queues an upload on the bridge and returns its completion task.
    /// </summary>
    /// <param name="upload">The upload description built by a builder.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The created GPU resource.</returns>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    internal Task<GpuAssetResource> UploadAsync(AssetGpuUpload upload, CancellationToken cancellationToken) {
        ThrowIfStopped();
        return _bridge.UploadAsync(upload, cancellationToken);
    }

    /// <summary>
    /// Queues the destruction of a resource the service created. A failure is swallowed because the
    /// render thread may already be gone; the bridge destroy call is itself best effort.
    /// </summary>
    /// <param name="resource">The resource to destroy.</param>
    internal void QueueDestruction(GpuAssetResource resource) {
        try {
            _bridge.Destroy(resource);
        } catch (Exception) {
            // Destructions run on the render thread. A closed bridge already destroyed every live
            // resource, and there is no caller that could handle a failure here.
        }
    }

    /// <summary>
    /// Stops the service during app shutdown. Idempotent.
    /// </summary>
    internal void Stop() => Interlocked.Exchange(ref _stopped, 1);

    /// <summary>
    /// Throws when the app has stopped.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    internal void ThrowIfStopped() {
        if (IsStopped) {
            throw new ObjectDisposedException(
                nameof(ProceduralAssetService),
                "The app has stopped; procedural assets cannot be created or built after shutdown.");
        }
    }
}
