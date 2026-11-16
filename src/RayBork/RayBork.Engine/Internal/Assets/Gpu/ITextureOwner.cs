namespace RayBork.Internal;

/// <summary>
/// The lifetime behind one public <see cref="Texture"/> wrapper.
/// </summary>
/// <remarks>
/// <para>
/// A loaded or procedural texture is owned by an <see cref="EngineAsset"/>; a render target's color view
/// is owned by the <see cref="RenderTexture"/> and has a synthetic empty <see cref="Path"/>. Both forms
/// share the same owner-reference and frame-hold contract, so a shader or a recorded draw retains either
/// kind of texture identically.
/// </para>
/// <para>
/// Owner references are tick-thread calls. Frame holds are added while a frame records and released by
/// the frame ring after the render thread released the slot, so both kinds of target are protected for
/// the same window.
/// </para>
/// </remarks>
internal interface ITextureOwner : IAssetRetentionTarget {
    /// <summary>
    /// Gets the public path, including its leading slash, or the empty string for a render target view.
    /// It stays readable after disposal.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Gets whether the texture is disposed. It stays readable after disposal.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets the engine-asset lifetime, or <see langword="null"/> for a render target view.
    /// </summary>
    EngineAsset? EngineAsset { get; }

    /// <summary>
    /// Gets the bindless binding resource, or <see langword="null"/> when the GPU resource does not
    /// exist yet.
    /// </summary>
    ITextureBindingResource? BindingResource { get; }

    /// <summary>
    /// Adds one owner reference, keeping the GPU resource alive.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The texture is already disposed.</exception>
    void AddOwnerReference();

    /// <summary>
    /// Releases one owner reference.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The owner count is already zero.</exception>
    void ReleaseOwnerReference();
}
