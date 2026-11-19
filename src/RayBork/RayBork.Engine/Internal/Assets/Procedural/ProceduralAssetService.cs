using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>The app-lifetime service behind the procedural mesh and texture builders.</summary>
/// <remarks>
/// The service queues GPU uploads and deferred destruction on the <see cref="GpuAssetBridge"/> but never
/// caches an asset or resolves a path. Every member is thread-safe; the bridge performs the
/// render-thread handoff.
/// </remarks>
internal sealed class ProceduralAssetService
{
    private readonly GpuAssetBridge _bridge;
    private int _stopped;

    /// <summary>Initializes the service over the app's GPU asset bridge.</summary>
    internal ProceduralAssetService(GpuAssetBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        _bridge = bridge;
    }

    /// <summary>Gets whether the app already stopped the service.</summary>
    internal bool IsStopped => Volatile.Read(ref _stopped) != 0;

    /// <summary>Creates a builder for one procedural mesh.</summary>
    internal MeshBuilder CreateMeshBuilder(
        PrimitiveTopology topology = PrimitiveTopology.TriangleList
    )
    {
        ThrowIfStopped();
        return new MeshBuilder(this, topology);
    }

    /// <summary>Creates a builder for one procedural texture.</summary>
    internal TextureBuilder CreateTextureBuilder(
        int width,
        int height,
        TextureFormat format = TextureFormat.Rgba8Srgb,
        bool generateMips = true,
        SamplerState? sampler = null
    )
    {
        ThrowIfStopped();
        return new TextureBuilder(this, width, height, format, generateMips, sampler);
    }

    /// <summary>Queues an upload on the bridge and returns its completion task.</summary>
    internal Task<GpuAssetResource> UploadAsync(
        AssetGpuUpload upload,
        CancellationToken cancellationToken
    )
    {
        ThrowIfStopped();
        return _bridge.UploadAsync(upload, cancellationToken);
    }

    /// <summary>Queues destruction of a resource; a failure is swallowed.</summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A procedural resource release must not throw: the only throwing path is a full destroy queue while the render thread is not draining it, and Destroy re-adds the resource to the live set so Close still destroys it. No caller could handle the failure."
    )]
    internal void QueueDestruction(GpuAssetResource resource)
    {
        try
        {
            _bridge.Destroy(resource);
        }
        catch (Exception)
        {
            // A full destroy queue with the render thread not draining it is the only throwing path;
            // Destroy puts the resource back in the live set, so Close still destroys it, and there is
            // no caller that could report the failure.
        }
    }

    /// <summary>Stops the service during app shutdown; idempotent.</summary>
    internal void Stop() => Interlocked.Exchange(ref _stopped, 1);

    /// <summary>Throws when the app has stopped.</summary>
    internal void ThrowIfStopped()
    {
        if (IsStopped)
        {
            throw new ObjectDisposedException(
                nameof(ProceduralAssetService),
                "The app has stopped; procedural assets cannot be created or built after shutdown."
            );
        }
    }
}
