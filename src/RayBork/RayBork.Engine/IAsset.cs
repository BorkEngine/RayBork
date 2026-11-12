namespace RayBork;

/// <summary>
/// A loaded, shared engine asset such as a texture, mesh, or shader.
/// </summary>
/// <remarks>
/// <para>
/// Engine assets are reference-counted. Each successful <see cref="IAssetCatalog.Load{T}"/> call
/// returns the same object and owns one reference; <see cref="IDisposable.Dispose"/> releases one
/// reference. Once the count reaches zero the object becomes disposed and its GPU resources are
/// destroyed after any frame that still references it completes.
/// </para>
/// <para>
/// Reading a member of a disposed asset throws <see cref="ObjectDisposedException"/>, except for
/// <see cref="IsDisposed"/> and <see cref="Path"/>. Custom data loaded with
/// <see cref="IAssetCatalog.Load{T}"/> is not an <see cref="IAsset"/> and is caller-owned.
/// </para>
/// </remarks>
public interface IAsset : IDisposable {
    /// <summary>
    /// Gets the canonical asset path, beginning with <c>/</c>.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets whether every reference has been released and the asset is disposed.
    /// </summary>
    bool IsDisposed { get; }
}
