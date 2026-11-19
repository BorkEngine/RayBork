namespace RayBork;

/// <summary>A loaded, shared engine asset such as a texture, mesh, or shader.</summary>
/// <remarks>
/// Engine assets are reference-counted: each successful load owns one reference and
/// <see cref="IDisposable.Dispose"/> releases one. Reading a disposed asset throws
/// <see cref="ObjectDisposedException"/>, except <see cref="IsDisposed"/> and <see cref="Path"/>.
/// </remarks>
public interface IAsset : IDisposable
{
    /// <summary>Gets the canonical asset path, beginning with <c>/</c>.</summary>
    string Path { get; }

    /// <summary>Gets whether every reference was released and the asset is disposed.</summary>
    bool IsDisposed { get; }
}
