namespace RayBork.Internal;

/// <summary>
/// One decoded engine asset: the GPU upload to run and the factory for the public asset over the
/// resulting lifetime.
/// </summary>
/// <remarks>
/// The catalog uploads <see cref="Upload"/> on the loader thread and then calls <see cref="Create"/>
/// with the committed <see cref="EngineAsset"/>. The factory runs exactly once per successful load,
/// after the GPU copy completed.
/// </remarks>
/// <typeparam name="T">The engine asset type the factory creates.</typeparam>
internal sealed class DecodedAsset<T> {
    /// <summary>
    /// Initializes a decoded asset from its upload and asset factory.
    /// </summary>
    /// <param name="upload">The GPU upload the catalog runs for this asset.</param>
    /// <param name="create">The factory that creates the public asset over the uploaded lifetime.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal DecodedAsset(AssetGpuUpload upload, Func<EngineAsset, T> create) {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(create);

        Upload = upload;
        Create = create;
    }

    /// <summary>
    /// Gets the GPU upload the catalog runs for this asset.
    /// </summary>
    internal AssetGpuUpload Upload { get; }

    /// <summary>
    /// Gets the factory that creates the public asset over the uploaded lifetime.
    /// </summary>
    internal Func<EngineAsset, T> Create { get; }
}
