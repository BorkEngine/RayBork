namespace RayBork.Internal;

/// <summary>
/// One decoded engine asset: its GPU upload, public-asset factory, and CPU-only teardown.
/// </summary>
/// <remarks>
/// The catalog runs <see cref="Upload"/> and then <see cref="Create"/> exactly once per successful
/// load. A CPU-only asset has no upload and passes <see cref="Release"/> when its public state needs a
/// teardown.
/// </remarks>
internal sealed class DecodedAsset<T>
{
    /// <summary>Initializes a decoded asset from its upload, asset factory, and optional teardown.</summary>
    internal DecodedAsset(
        AssetGpuUpload? upload,
        Func<EngineAsset, T> create,
        Action? release = null
    )
    {
        ArgumentNullException.ThrowIfNull(create);

        Upload = upload;
        Create = create;
        Release = release;
    }

    /// <summary>Gets the GPU upload, or <see langword="null"/> for a CPU-only asset.</summary>
    internal AssetGpuUpload? Upload { get; }

    /// <summary>Gets the factory that creates the public asset over the committed lifetime.</summary>
    internal Func<EngineAsset, T> Create { get; }

    /// <summary>Gets the CPU-only teardown that runs once when the lifetime is destroyed, or null.</summary>
    internal Action? Release { get; }
}
