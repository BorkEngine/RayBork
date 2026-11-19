using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The bindless view of a render target's resolved color or depth image.</summary>
/// <remarks>
/// The target owns the image and view; this resource stores the sampled handles and bindless slot,
/// holding the previous handles across a device recreation. Each color or depth view has its own
/// instance and never uses the GPU asset bridge.
/// </remarks>
internal sealed class RenderTextureViewResource : ITextureBindingResource
{
    /// <summary>Initializes the view resource for a sampled render target image.</summary>
    internal RenderTextureViewResource(ImageView view, Sampler sampler)
    {
        View = view;
        Sampler = sampler;
    }

    /// <summary>Gets or sets the sampled image view.</summary>
    internal ImageView View { get; set; }

    /// <summary>Gets or sets the sampler written beside the view.</summary>
    internal Sampler Sampler { get; set; }

    /// <summary>Gets or sets the bindless table slot, or <c>0</c> when none is reserved.</summary>
    internal uint BindlessSlot { get; set; }

    /// <inheritdoc/>
    uint ITextureBindingResource.BindlessSlot => BindlessSlot;
}
