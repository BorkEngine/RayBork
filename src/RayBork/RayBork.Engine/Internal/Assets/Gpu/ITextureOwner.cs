namespace RayBork.Internal;

/// <summary>
/// The lifetime behind one public <see cref="Texture"/> wrapper.
/// </summary>
/// <remarks>
/// A loaded or procedural texture is owned by an <see cref="EngineAsset"/>; a render target view is
/// owned by the <see cref="RenderTexture"/> with an empty <see cref="Path"/>. Owner references are
/// tick-thread calls, and frame holds share the same retention window.
/// </remarks>
internal interface ITextureOwner : IAssetRetentionTarget
{
    /// <summary>Gets the public path, or empty for a render target view.</summary>
    string Path { get; }

    /// <summary>Gets whether the texture is disposed; readable after disposal.</summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets the engine-asset lifetime, or <see langword="null"/> for a render target view.
    /// </summary>
    EngineAsset? EngineAsset { get; }

    /// <summary>
    /// Gets the bindless binding resource, or <see langword="null"/> before the GPU resource exists.
    /// </summary>
    ITextureBindingResource? BindingResource { get; }

    /// <summary>Adds one owner reference, keeping the GPU resource alive.</summary>
    /// <exception cref="ObjectDisposedException">The texture is already disposed.</exception>
    void AddOwnerReference();

    /// <summary>Releases one owner reference.</summary>
    /// <exception cref="ObjectDisposedException">The owner count is already zero.</exception>
    void ReleaseOwnerReference();
}
