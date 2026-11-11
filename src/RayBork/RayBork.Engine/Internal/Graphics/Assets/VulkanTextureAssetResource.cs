using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan objects behind one uploaded packed texture: the device-local image and memory, the
/// image view, and the sampler created from the baked descriptor.
/// </summary>
/// <remarks>
/// Created on the render thread by <see cref="VulkanAssetWorker.Upload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/> after a device recovery. The image is the primary handle.
/// The view, sampler, image, and memory are destroyed together by <see
/// cref="VulkanAssetWorker.Destroy"/> after the last frame that references the texture has completed.
/// The public texture metadata is the descriptor's; the copies here are the validated payload values
/// the upload used.
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
        TextureGpuUpload upload,
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
    /// Gets the sampler created from the descriptor filter, wrap, and anisotropy.
    /// </summary>
    internal Sampler Sampler { get; private set; }

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
