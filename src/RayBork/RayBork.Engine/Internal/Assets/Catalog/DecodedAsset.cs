namespace RayBork.Internal;

/// <summary>
/// One decoded engine asset: the optional GPU upload to run, the factory for the public asset over the
/// resulting lifetime, and the optional CPU-only teardown for an asset with no GPU resource.
/// </summary>
/// <remarks>
/// <para>
/// The catalog uploads <see cref="Upload"/> on the loader thread and then calls <see cref="Create"/>
/// with the committed <see cref="EngineAsset"/>. The factory runs exactly once per successful load,
/// after the GPU copy completed, or immediately when <see cref="Upload"/> is <see langword="null"/>.
/// </para>
/// <para>
/// A CPU-only engine asset, such as <see cref="Font"/>, passes a <see langword="null"/> upload: it
/// owns no GPU resource of its own because its glyph textures are separate procedural textures created
/// on demand. Such an asset passes <see cref="Release"/> to dispose the CPU state exactly once when
/// the last owner reference and frame hold are gone; an asset with a GPU resource leaves it
/// <see langword="null"/> and relies on the destruction of the uploaded resource.
/// </para>
/// </remarks>
/// <typeparam name="T">The engine asset type the factory creates.</typeparam>
internal sealed class DecodedAsset<T>
{
    /// <summary>
    /// Initializes a decoded asset from its upload, asset factory, and optional CPU-only teardown.
    /// </summary>
    /// <param name="upload">The GPU upload the catalog runs, or <see langword="null"/> for a CPU-only asset.</param>
    /// <param name="create">The factory that creates the public asset over the committed lifetime.</param>
    /// <param name="release">
    /// The CPU-only teardown the catalog runs exactly once when the lifetime is destroyed, or
    /// <see langword="null"/> when the asset has no CPU state beyond the public object.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is <c>null</c>.</exception>
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

    /// <summary>
    /// Gets the GPU upload the catalog runs for this asset, or <see langword="null"/> for a CPU-only
    /// asset. The catalog creates the lifetime without an upload in that case.
    /// </summary>
    internal AssetGpuUpload? Upload { get; }

    /// <summary>
    /// Gets the factory that creates the public asset over the committed lifetime.
    /// </summary>
    internal Func<EngineAsset, T> Create { get; }

    /// <summary>
    /// Gets the CPU-only teardown that runs exactly once when the lifetime is destroyed, or
    /// <see langword="null"/> when the asset has none.
    /// </summary>
    internal Action? Release { get; }
}
