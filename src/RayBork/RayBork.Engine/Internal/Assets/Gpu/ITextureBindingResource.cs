namespace RayBork.Internal;

/// <summary>
/// A GPU object that can be written into the bindless texture table and sampled by a geometry shader.
/// </summary>
/// <remarks>
/// A packed or procedural texture is represented by <see cref="VulkanTextureAssetResource"/>; a render
/// target's color view is represented by <see cref="RenderTextureViewResource"/>. The renderer resolves a
/// public <see cref="Texture"/> to this interface, so both kinds of texture reach the bindless slot the
/// same way. Every member is render-thread affine.
/// </remarks>
internal interface ITextureBindingResource {
    /// <summary>
    /// Gets the bindless table slot, or <c>0</c> when the resource has not completed its slot
    /// reservation yet.
    /// </summary>
    uint BindlessSlot { get; }
}
