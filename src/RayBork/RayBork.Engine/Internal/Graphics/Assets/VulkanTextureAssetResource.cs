using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The Vulkan image, view, and sampler behind one uploaded texture.</summary>
/// <remarks>
/// Created by <see cref="VulkanAssetWorker.BeginUpload"/> on the render thread and recreated by
/// <see cref="IAssetGpuWorker.Restore"/> after a device recovery. The view, image, and memory are
/// destroyed together; the sampler belongs to <see cref="VulkanSamplerCache"/> and the slot to the
/// frame table.
/// </remarks>
internal sealed class VulkanTextureAssetResource : VulkanAssetResource, ITextureBindingResource
{
    /// <summary>Initializes a texture resource.</summary>
    internal VulkanTextureAssetResource(
        AssetGpuUpload upload,
        Image image,
        ImageView view,
        Sampler sampler,
        DeviceMemory memory,
        Format format,
        uint width,
        uint height,
        int mipLevels,
        int deviceGeneration
    )
        : base(upload, unchecked((long)image.Handle), deviceGeneration)
    {
        Image = image;
        View = view;
        Sampler = sampler;
        Memory = memory;
        Format = format;
        Width = width;
        Height = height;
        MipLevels = mipLevels;
    }

    /// <summary>Gets the sampled image.</summary>
    internal Image Image { get; private set; }

    /// <summary>Gets the image view over every mip level.</summary>
    internal ImageView View { get; private set; }

    /// <summary>
    /// Gets the sampler for the filter, wrap, and anisotropy; the sampler cache owns it.
    /// </summary>
    internal Sampler Sampler { get; private set; }

    /// <summary>Gets the bindless slot, or <c>0</c> while pending; 0 is the white fallback.</summary>
    internal uint BindlessSlot { get; private set; }

    /// <inheritdoc/>
    uint ITextureBindingResource.BindlessSlot => BindlessSlot;

    /// <summary>Stores the non-zero slot reserved for the completed upload.</summary>
    internal void AssignBindlessSlot(uint slot)
    {
        ArgumentOutOfRangeException.ThrowIfZero(slot);
        BindlessSlot = slot;
    }

    /// <summary>Releases the bindless slot to the current table and clears it.</summary>
    /// <remarks>
    /// Runs only after frame idleness retired every frame that could reference the texture.
    /// </remarks>
    internal void ReleaseBindlessSlot(VulkanBindlessTextureTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (BindlessSlot == 0)
        {
            return;
        }

        table.Release(BindlessSlot);
        BindlessSlot = 0;
    }

    /// <summary>Gets the device memory bound to the image.</summary>
    internal DeviceMemory Memory { get; private set; }

    /// <summary>Gets the Vulkan format of the image and view.</summary>
    internal Format Format { get; private set; }

    /// <summary>Gets the base-level width in pixels.</summary>
    internal uint Width { get; private set; }

    /// <summary>Gets the base-level height in pixels.</summary>
    internal uint Height { get; private set; }

    /// <summary>Gets the number of mip levels.</summary>
    internal int MipLevels { get; private set; }

    /// <summary>Adopts the handles of a texture recreated after a device recovery.</summary>
    internal override void AdoptRestored(GpuAssetResource restored)
    {
        if (ReferenceEquals(this, restored))
        {
            return;
        }

        VulkanTextureAssetResource texture = CastRestored<VulkanTextureAssetResource>(restored);
        Image = texture.Image;
        View = texture.View;
        Sampler = texture.Sampler;
        BindlessSlot = texture.BindlessSlot;
        Memory = texture.Memory;
        Format = texture.Format;
        Width = texture.Width;
        Height = texture.Height;
        MipLevels = texture.MipLevels;
        AdoptGeneration(texture);
        ResetRelease();
        PublishHandle(texture.Handle);
    }
}
