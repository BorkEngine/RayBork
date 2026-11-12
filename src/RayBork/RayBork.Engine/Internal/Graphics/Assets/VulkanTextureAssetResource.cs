using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan objects behind one uploaded texture: the device-local image and memory, the image view,
/// and the sampler created from the texture's filter, wrap, and anisotropy.
/// </summary>
/// <remarks>
/// Created on the render thread by <see cref="VulkanAssetWorker.BeginUpload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/> after a device recovery. The image is the primary handle.
/// The view, image, and memory are destroyed together by <see cref="VulkanAssetWorker.Destroy"/>
/// after the last frame that references the texture has completed. The sampler handle is owned by the
/// per-device <see cref="VulkanSamplerCache"/> and is never destroyed here. The bindless slot is
/// reserved when the upload completes, written into every frame descriptor set, and released when the
/// resource is destroyed. A packed texture is described by its KTX2 descriptor; a procedural texture
/// is described by its <see cref="ProceduralTextureGpuUpload"/>. The public texture metadata is the
/// descriptor's; the copies here are the validated values the upload used.
/// </remarks>
internal sealed class VulkanTextureAssetResource : VulkanAssetResource {
    /// <summary>
    /// Initializes a texture resource.
    /// </summary>
    /// <param name="upload">The retained texture upload description.</param>
    /// <param name="image">The sampled image.</param>
    /// <param name="view">The image view.</param>
    /// <param name="sampler">The sampler created from the descriptor state.</param>
    /// <param name="memory">The device memory bound to the image.</param>
    /// <param name="format">The Vulkan format of the image and view.</param>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="mipLevels">The number of mip levels.</param>
    /// <param name="deviceGeneration">The logical-device generation that owns the handles.</param>
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
        int deviceGeneration)
        : base(upload, unchecked((long)image.Handle), deviceGeneration) {
        Image = image;
        View = view;
        Sampler = sampler;
        Memory = memory;
        Format = format;
        Width = width;
        Height = height;
        MipLevels = mipLevels;
    }

    /// <summary>
    /// Gets the sampled image.
    /// </summary>
    internal Image Image { get; private set; }

    /// <summary>
    /// Gets the image view over every mip level.
    /// </summary>
    internal ImageView View { get; private set; }

    /// <summary>
    /// Gets the sampler for the descriptor filter, wrap, and anisotropy. The per-device sampler cache
    /// owns it; destroying this resource must not destroy the sampler.
    /// </summary>
    internal Sampler Sampler { get; private set; }

    /// <summary>
    /// Gets the bindless table slot the upload reserved, or <c>0</c> when the upload has not completed
    /// yet. Slot 0 is the renderer's white fallback, so zero also means "no slot".
    /// </summary>
    internal uint BindlessSlot { get; private set; }

    /// <summary>
    /// Stores the bindless slot reserved for the completed upload.
    /// </summary>
    /// <param name="slot">The reserved slot; it must be non-zero.</param>
    internal void AssignBindlessSlot(uint slot) {
        ArgumentOutOfRangeException.ThrowIfZero(slot);
        BindlessSlot = slot;
    }

    /// <summary>
    /// Releases the bindless slot to the current table and clears it.
    /// </summary>
    /// <remarks>
    /// The worker calls it only after frame idleness has retired every frame that could reference the
    /// texture, so a later allocation may reuse the slot immediately.
    /// </remarks>
    /// <param name="table">The current bindless texture table.</param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <c>null</c>.</exception>
    internal void ReleaseBindlessSlot(VulkanBindlessTextureTable table) {
        ArgumentNullException.ThrowIfNull(table);
        if (BindlessSlot == 0) {
            return;
        }

        table.Release(BindlessSlot);
        BindlessSlot = 0;
    }

    /// <summary>
    /// Gets the device memory bound to the image.
    /// </summary>
    internal DeviceMemory Memory { get; private set; }

    /// <summary>
    /// Gets the Vulkan format of the image and view.
    /// </summary>
    internal Format Format { get; private set; }

    /// <summary>
    /// Gets the base-level width in pixels.
    /// </summary>
    internal uint Width { get; private set; }

    /// <summary>
    /// Gets the base-level height in pixels.
    /// </summary>
    internal uint Height { get; private set; }

    /// <summary>
    /// Gets the number of mip levels.
    /// </summary>
    internal int MipLevels { get; private set; }

    /// <summary>
    /// Adopts the handles of a texture recreated after a device recovery.
    /// </summary>
    /// <param name="restored">The recreated texture resource.</param>
    internal override void AdoptRestored(GpuAssetResource restored) {
        if (ReferenceEquals(this, restored)) {
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
