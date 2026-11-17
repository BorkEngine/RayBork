using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The GPU view of one render target's single-sample resolved color or depth image: the image view and
/// sampler written into a bindless slot, or the previous view's handles before a device recreation.
/// </summary>
/// <remarks>
/// The target owns the image and its image view; this resource only remembers the handles the bindless
/// table samples and the slot it reserved. It is created on the render thread when the target is created
/// and replaced when a device recreation recreates the target. It is not an
/// <see cref="GpuAssetResource"/>: a render target is not a pack asset and never goes through the GPU
/// asset bridge or its upload queue. A color view and a depth view each own their own instance and
/// bindless slot on the same target.
/// </remarks>
internal sealed class RenderTextureViewResource : ITextureBindingResource
{
    /// <summary>
    /// Initializes the view resource for a sampled render target image.
    /// </summary>
    /// <param name="view">The single-sample resolved image view.</param>
    /// <param name="sampler">The target's sampler for that view.</param>
    internal RenderTextureViewResource(ImageView view, Sampler sampler)
    {
        View = view;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the sampled image view.</summary>
    internal ImageView View { get; set; }

    /// <summary>Gets or sets the sampler written beside the view.</summary>
    internal Sampler Sampler { get; set; }

    /// <summary>Gets or sets the bindless table slot, or <c>0</c> when no slot is reserved.</summary>
    internal uint BindlessSlot { get; set; }

    /// <inheritdoc/>
    uint ITextureBindingResource.BindlessSlot => BindlessSlot;
}
