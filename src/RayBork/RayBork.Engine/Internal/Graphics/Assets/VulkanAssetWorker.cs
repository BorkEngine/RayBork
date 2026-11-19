using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan <see cref="IAssetGpuWorker"/> that creates, recreates, and destroys the GPU objects
/// behind textures, meshes, and shaders.
/// </summary>
/// <remarks>
/// Every member runs on the render thread and resolves the current <see cref="VulkanDevice"/>, so a
/// resource from an older logical-device generation is never destroyed. Only <see cref="Restore"/>
/// and <see cref="Cancel"/> wait for a fence, and a bindless slot is released after its last frame.
/// </remarks>
internal sealed unsafe class VulkanAssetWorker : IAssetGpuWorker
{
    private readonly Func<VulkanDevice> _deviceAccessor;
    private readonly Func<int> _deviceGenerationAccessor;
    private readonly Func<VulkanBindlessTextureTable> _bindlessTextureTableAccessor;
    private readonly Func<bool> _frameResourcesIdleAccessor;
    private readonly Action _waitForFrameResourcesIdle;
    private readonly VulkanSamplerCache _samplers;
    private readonly ILogger _logger;
    private readonly HashSet<VulkanAssetResource> _resources = [];
    private readonly HashSet<VulkanAssetUploadOperation> _pending = [];

    /// <summary>Initializes the worker for one backend.</summary>
    internal VulkanAssetWorker(
        Func<VulkanDevice> deviceAccessor,
        Func<int> deviceGenerationAccessor,
        Func<VulkanBindlessTextureTable> bindlessTextureTableAccessor,
        Func<bool> frameResourcesIdleAccessor,
        Action waitForFrameResourcesIdle,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(deviceAccessor);
        ArgumentNullException.ThrowIfNull(deviceGenerationAccessor);
        ArgumentNullException.ThrowIfNull(bindlessTextureTableAccessor);
        ArgumentNullException.ThrowIfNull(frameResourcesIdleAccessor);
        ArgumentNullException.ThrowIfNull(waitForFrameResourcesIdle);
        ArgumentNullException.ThrowIfNull(logger);

        _deviceAccessor = deviceAccessor;
        _deviceGenerationAccessor = deviceGenerationAccessor;
        _bindlessTextureTableAccessor = bindlessTextureTableAccessor;
        _frameResourcesIdleAccessor = frameResourcesIdleAccessor;
        _waitForFrameResourcesIdle = waitForFrameResourcesIdle;
        _logger = logger;
        _samplers = new VulkanSamplerCache(deviceAccessor, logger);
    }

    /// <inheritdoc />
    public bool AreFrameResourcesIdle => _frameResourcesIdleAccessor();

    /// <inheritdoc />
    public void WaitForFrameResourcesIdle() => _waitForFrameResourcesIdle();

    /// <summary>Gets the number of started uploads that have not finished or been canceled.</summary>
    internal int PendingOperationCount => _pending.Count;

    /// <summary>Gets the number of pending uploads that still own a staging buffer.</summary>
    internal int PendingStagingBufferCount =>
        _pending.Count(operation => operation.StagingBuffer.Handle != 0);

    /// <summary>Gets the number of cached samplers; matching state shares one entry.</summary>
    internal int SamplerCount => _samplers.Count;

    /// <inheritdoc />
    public GpuAssetUploadOperation BeginUpload(AssetGpuUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        VulkanAssetUploadOperation operation = upload.Kind switch
        {
            AssetGpuUploadKind.Texture => BeginTexture((TextureGpuUpload)upload),
            AssetGpuUploadKind.ProceduralTexture => BeginProceduralTexture(
                (ProceduralTextureGpuUpload)upload
            ),
            AssetGpuUploadKind.Mesh => BeginMesh((MeshGpuUpload)upload),
            AssetGpuUploadKind.Shader => BeginShader((ShaderGpuUpload)upload),
            _ => throw VulkanAssetErrors.UploadFailed(
                upload.Path,
                $"the upload kind {upload.Kind} is not supported."
            ),
        };

        _pending.Add(operation);
        return operation;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failing completion is returned to the bridge as a failed upload instead of escaping the render thread."
    )]
    public bool TryComplete(
        GpuAssetUploadOperation operation,
        out GpuAssetResource? resource,
        out Exception? error
    )
    {
        VulkanAssetUploadOperation upload = RequireUploadOperation(operation);
        if (!upload.IsFinished && !upload.IsCanceled)
        {
            try
            {
                if (
                    upload.HasCopy
                    && !VulkanAssetUploadCommands.TryComplete(
                        upload.Device,
                        upload.Submission,
                        upload.Upload.Path
                    )
                )
                {
                    resource = null;
                    error = null;
                    return false;
                }

                CompleteUpload(upload);
            }
            catch (Exception exception)
            {
                FailUpload(upload, exception);
            }
        }

        resource = upload.Resource;
        error = upload.Error;
        return true;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Cancellation runs during shutdown after the device may already be gone; the failure is logged and the loop continues."
    )]
    public void Cancel(GpuAssetUploadOperation operation)
    {
        VulkanAssetUploadOperation upload = RequireUploadOperation(operation);
        if (!upload.TryMarkCanceled())
        {
            return;
        }

        _pending.Remove(upload);
        try
        {
            if (!upload.IsFinished)
            {
                if (upload.HasCopy)
                {
                    // The copy may still be writing the staging buffer or the destination image. Wait
                    // for it before anything is freed; cancellation and shutdown are rare.
                    VulkanAssetUploadCommands.Wait(
                        upload.Device,
                        upload.Submission,
                        upload.Upload.Path
                    );
                }

                upload.MarkFailed(
                    VulkanAssetErrors.UploadFailed(
                        upload.Upload.Path,
                        "the GPU upload was canceled before it completed."
                    )
                );
            }

            DestroyUploadObjects(upload);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The GPU asset upload of '{Path}' could not be canceled cleanly.",
                AssetPath.ToPublic(upload.Upload.Path)
            );
            DestroyUploadObjects(upload);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The previous instance adopts the recreated handles and is returned, so the bridge keeps one live
    /// entry and public asset identity is unchanged.
    /// </remarks>
    public GpuAssetResource Restore(AssetGpuUpload upload, GpuAssetResource previous)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(previous);
        if (upload.Kind != previous.Kind)
        {
            throw new InvalidOperationException(
                $"Cannot restore a {previous.Kind} resource from a {upload.Kind} upload."
            );
        }

        if (previous.IsDestroyed)
        {
            throw new InvalidOperationException(
                $"The GPU asset resource for '{AssetPath.ToPublic(upload.Path)}' is queued for destruction and cannot be restored."
            );
        }

        GpuAssetResource restored = UploadAndWait(upload);
        try
        {
            previous.AdoptRestored(restored);
        }
        catch
        {
            Destroy(restored);
            throw;
        }

        // UploadAndWait registered the temporary instance. The live entry the bridge keeps is the
        // previous instance, so move the registry entry to it; otherwise a later destruction would
        // free the adopted handles twice.
        if (restored is VulkanAssetResource restoredResource)
        {
            _resources.Remove(restoredResource);
        }

        if (previous is VulkanAssetResource previousResource)
        {
            _resources.Add(previousResource);
        }

        return previous;
    }

    /// <summary>Starts an upload and waits for its fence; only <see cref="Restore"/> uses it.</summary>
    private GpuAssetResource UploadAndWait(AssetGpuUpload upload)
    {
        VulkanAssetUploadOperation operation = RequireUploadOperation(BeginUpload(upload));
        try
        {
            if (operation.HasCopy)
            {
                VulkanAssetUploadCommands.Wait(operation.Device, operation.Submission, upload.Path);
            }

            if (!TryComplete(operation, out GpuAssetResource? resource, out Exception? error))
            {
                throw VulkanAssetErrors.UploadFailed(
                    upload.Path,
                    "the upload did not finish after its fence signaled."
                );
            }

            if (error is not null)
            {
                throw error;
            }

            return resource
                ?? throw VulkanAssetErrors.UploadFailed(
                    upload.Path,
                    "the upload finished without a resource."
                );
        }
        catch
        {
            Cancel(operation);
            throw;
        }
    }

    /// <summary>Adopts a finished upload: frees transients, reserves a bindless slot.</summary>
    private void CompleteUpload(VulkanAssetUploadOperation upload)
    {
        VulkanAssetResource resource = upload.Resource
            ?? throw VulkanAssetErrors.UploadFailed(
                upload.Upload.Path,
                "the upload finished without a resource."
            );

        DestroyUploadTransients(upload);
        if (resource is VulkanTextureAssetResource texture)
        {
            AdoptBindlessSlot(texture);
        }

        _resources.Add(resource);
        _pending.Remove(upload);
        upload.MarkFinished();
    }

    /// <summary>Reserves a bindless slot and writes the texture into every frame set.</summary>
    /// <exception cref="AssetLoadException">
    /// The table is full or the descriptor write failed (<c>RB1010</c>).
    /// </exception>
    private void AdoptBindlessSlot(VulkanTextureAssetResource texture)
    {
        VulkanBindlessTextureTable table = _bindlessTextureTableAccessor();
        if (!table.TryAllocate(out uint slot))
        {
            throw VulkanAssetErrors.UploadFailed(
                texture.Upload.Path,
                $"the bindless texture table is full ({table.Capacity} slots, including the reserved "
                    + "white slot); dispose live textures or increase the device's update-after-bind "
                    + "sampled-image limits"
            );
        }

        try
        {
            table.WriteTexture(slot, texture.View, texture.Sampler);
        }
        catch
        {
            table.Release(slot);
            throw;
        }

        texture.AssignBindlessSlot(slot);
    }

    /// <summary>Frees a failed upload and records the failure for the bridge to observe.</summary>
    private void FailUpload(VulkanAssetUploadOperation upload, Exception exception)
    {
        _pending.Remove(upload);
        DestroyUploadObjects(upload);
        upload.MarkFailed(exception);
    }

    /// <summary>Frees the staging buffer, command pool, and fence after the copy finished.</summary>
    private void DestroyUploadTransients(VulkanAssetUploadOperation upload)
    {
        DestroyStagingBuffer(
            upload.Device,
            new StagingAllocation(upload.StagingBuffer, upload.StagingMemory)
        );
        VulkanAssetUploadCommands.Destroy(upload.Device, _logger, upload.Submission);
        upload.ClearTransients();
    }

    /// <summary>Frees everything a canceled or failed upload owns, including partials.</summary>
    private void DestroyUploadObjects(VulkanAssetUploadOperation upload)
    {
        DestroyUploadTransients(upload);
        if (upload.Resource is VulkanAssetResource resource)
        {
            _resources.Remove(resource);
            DestroyResource(resource);
            upload.ClearResource();
        }
    }

    private static VulkanAssetUploadOperation RequireUploadOperation(
        GpuAssetUploadOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(operation);

        return operation as VulkanAssetUploadOperation
            ?? throw new InvalidOperationException(
                $"The Vulkan asset worker cannot handle a {operation.GetType().Name} operation."
            );
    }

    /// <inheritdoc />
    public void Destroy(GpuAssetResource resource)
    {
        if (resource is null)
        {
            return;
        }

        if (resource is not VulkanAssetResource vulkanResource)
        {
            _logger.LogWarning(
                "The Vulkan asset worker was asked to destroy the unknown resource type {ResourceType}.",
                resource.GetType().Name
            );
            return;
        }

        _resources.Remove(vulkanResource);
        DestroyResource(vulkanResource);
    }

    /// <summary>Cancels uploads, destroys tracked resources, and releases samplers.</summary>
    /// <remarks>
    /// Render-thread only, before the backend destroys the logical device; it never throws, and
    /// <see cref="Restore"/> recreates the resources from the retained uploads.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Shutdown destroys every resource best-effort so one failure cannot leave the rest alive."
    )]
    internal void DestroyAllResources()
    {
        VulkanAssetUploadOperation[] pending = [.. _pending];
        foreach (VulkanAssetUploadOperation upload in pending)
        {
            try
            {
                Cancel(upload);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "The pending GPU asset upload of '{Path}' could not be canceled before the device was destroyed.",
                    AssetPath.ToPublic(upload.Upload.Path)
                );
            }
        }

        _pending.Clear();
        VulkanAssetResource[] resources = [.. _resources];
        _resources.Clear();
        foreach (VulkanAssetResource resource in resources)
        {
            DestroyResource(resource);
        }

        _samplers.DestroyAll();
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Resource destruction is best-effort during upload failure and shutdown; each failure is logged."
    )]
    private void DestroyResource(VulkanAssetResource resource)
    {
        try
        {
            int currentGeneration = _deviceGenerationAccessor();
            if (resource.DeviceGeneration != currentGeneration)
            {
                // The resource's device was destroyed. Its handles were released with the device, and
                // touching them on the current device would be invalid.
                _logger.LogDebug(
                    "Skipping the destruction of the GPU asset resource for '{Path}': it belongs to device generation {ResourceGeneration}, the current generation is {CurrentGeneration}.",
                    AssetPath.ToPublic(resource.Upload.Path),
                    resource.DeviceGeneration,
                    currentGeneration
                );
                return;
            }

            switch (resource)
            {
                case VulkanTextureAssetResource texture:
                    DestroyTexture(texture);
                    break;
                case VulkanMeshAssetResource mesh:
                    DestroyMesh(mesh);
                    break;
                case VulkanShaderAssetResource shader:
                    DestroyShader(shader);
                    break;
                default:
                    _logger.LogWarning(
                        "The Vulkan asset worker was asked to destroy the unknown resource type {ResourceType}.",
                        resource.GetType().Name
                    );
                    break;
            }
        }
        catch (Exception exception)
        {
            // Destruction is best effort: the app is tearing down or the device is already gone, and
            // there is no caller that could handle a failure.
            _logger.LogError(
                exception,
                "The GPU asset resource for '{Path}' could not be destroyed.",
                AssetPath.ToPublic(resource.Upload.Path)
            );
        }
    }

    private VulkanAssetUploadOperation BeginTexture(TextureGpuUpload upload)
    {
        VulkanDevice device = _deviceAccessor();
        Ktx2Texture texture = Ktx2Reader.Read(upload.Ktx2Data.Span, upload.Texture);
        TextureLevelRange[] levels = new TextureLevelRange[texture.MipLevels];
        for (int level = 0; level < texture.MipLevels; level++)
        {
            Ktx2Level levelData = texture.Levels[level];
            levels[level] = new TextureLevelRange(levelData.ByteOffset, levelData.ByteLength);
        }

        return BeginTextureUpload(
            device,
            upload,
            texture.Format,
            texture.Width,
            texture.Height,
            levels,
            upload.Ktx2Data,
            upload.Texture.Filter,
            upload.Texture.Wrap,
            upload.Texture.MaxAnisotropy,
            upload.Texture.Payload
        );
    }

    private VulkanAssetUploadOperation BeginProceduralTexture(ProceduralTextureGpuUpload upload)
    {
        if (
            !Enum.IsDefined(upload.Format)
            || upload.Format
                is not (
                    TextureFormat.Rgba8Unorm
                    or TextureFormat.Rgba8Srgb
                    or TextureFormat.R8Unorm
                )
        )
        {
            throw VulkanAssetErrors.DecodeFailed(
                upload.Path,
                $"the procedural texture format {upload.Format} is not an uncompressed procedural format"
            );
        }

        if (!Enum.IsDefined(upload.Filter) || !Enum.IsDefined(upload.Wrap))
        {
            throw VulkanAssetErrors.DecodeFailed(
                upload.Path,
                "the procedural texture sampler state is not valid"
            );
        }

        if (!float.IsFinite(upload.MaxAnisotropy) || upload.MaxAnisotropy < 1f)
        {
            throw VulkanAssetErrors.DecodeFailed(
                upload.Path,
                "the procedural texture maximum anisotropy must be finite and at least 1"
            );
        }

        uint fullMipCount = VulkanTextureFormat.FullMipCount(
            (uint)upload.Width,
            (uint)upload.Height
        );
        if (upload.MipLevels > checked((int)fullMipCount))
        {
            throw VulkanAssetErrors.DecodeFailed(
                upload.Path,
                $"the procedural texture has {upload.MipLevels} mip level(s); "
                    + $"{upload.Width}x{upload.Height} supports at most {fullMipCount}"
            );
        }

        TextureLevelRange[] levels = new TextureLevelRange[upload.MipLevels];
        ReadOnlySpan<int> levelLengths = upload.LevelByteLengths.Span;
        ulong offset = 0;
        for (int level = 0; level < levels.Length; level++)
        {
            int expectedLength = VulkanTextureFormat.GetLevelByteLength(
                upload.Format,
                VulkanTextureFormat.LevelExtent((uint)upload.Width, level),
                VulkanTextureFormat.LevelExtent((uint)upload.Height, level)
            );
            int length = levelLengths[level];
            if (length != expectedLength)
            {
                throw VulkanAssetErrors.DecodeFailed(
                    upload.Path,
                    $"mip level {level} of the {upload.Width}x{upload.Height} {upload.Format} procedural "
                        + $"texture is {length} byte(s); the level requires {expectedLength}"
                );
            }

            levels[level] = new TextureLevelRange(offset, (ulong)length);
            offset += (ulong)length;
        }

        if (offset != (ulong)upload.PixelData.Length)
        {
            throw VulkanAssetErrors.DecodeFailed(
                upload.Path,
                $"the procedural mip levels describe {offset} byte(s), but the payload is "
                    + $"{upload.PixelData.Length} byte(s)"
            );
        }

        VulkanDevice device = _deviceAccessor();
        return BeginTextureUpload(
            device,
            upload,
            upload.Format,
            (uint)upload.Width,
            (uint)upload.Height,
            levels,
            upload.PixelData,
            upload.Filter,
            upload.Wrap,
            upload.MaxAnisotropy,
            upload.Path
        );
    }

    private VulkanAssetUploadOperation BeginTextureUpload(
        VulkanDevice device,
        AssetGpuUpload upload,
        TextureFormat textureFormat,
        uint width,
        uint height,
        TextureLevelRange[] levels,
        ReadOnlyMemory<byte> levelData,
        TextureFilter filter,
        TextureWrap wrap,
        float maxAnisotropy,
        string payloadPath
    )
    {
        int mipLevels = levels.Length;
        Format format = VulkanTextureFormat.ToVkFormat(textureFormat);
        ValidateTextureSupport(device, upload, textureFormat, format);

        int deviceGeneration = _deviceGenerationAccessor();
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        PhysicalDeviceMemoryProperties memoryProperties = vulkan.GetPhysicalDeviceMemoryProperties(
            device.PhysicalDevice
        );
        Image image = default;
        DeviceMemory memory = default;
        ImageView view = default;
        StagingAllocation staging = default;
        VulkanAssetUploadCommands.UploadSubmission submission = default;
        VulkanTextureAssetResource? resource = null;
        try
        {
            image = CreateImage(device, upload.Path, width, height, mipLevels, format);
            MemoryRequirements requirements = vulkan.GetImageMemoryRequirements(
                deviceHandle,
                image
            );
            memory = VulkanAssetMemory.Allocate(
                device,
                memoryProperties,
                requirements,
                MemoryPropertyFlags.DeviceLocalBit,
                upload.Path,
                $"{format} texture image"
            );
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BindImageMemory(deviceHandle, image, memory, 0),
                upload.Path,
                "vkBindImageMemory"
            );

            ImageViewCreateInfo viewInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = format,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = (uint)mipLevels,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateImageView(deviceHandle, &viewInfo, null, out view),
                upload.Path,
                "vkCreateImageView"
            );

            Sampler sampler = _samplers.GetOrCreate(
                filter,
                wrap,
                maxAnisotropy,
                upload.Path,
                payloadPath
            );
            resource = new VulkanTextureAssetResource(
                upload,
                image,
                view,
                sampler,
                memory,
                format,
                width,
                height,
                mipLevels,
                deviceGeneration
            );

            staging = CreateStagingBuffer(
                device,
                memoryProperties,
                levelData.Span,
                upload.Path,
                "texture"
            );
            submission = VulkanAssetUploadCommands.RecordAndSubmit(
                device,
                _logger,
                upload.Path,
                (recordVulkan, commandBuffer) =>
                    RecordTextureUpload(
                        recordVulkan,
                        commandBuffer,
                        staging.Buffer,
                        image,
                        width,
                        height,
                        levels
                    )
            );
            return new VulkanAssetUploadOperation(
                upload,
                device,
                submission,
                staging.Buffer,
                staging.Memory,
                resource
            );
        }
        catch
        {
            DestroyFailedUpload(device, upload, submission, staging, resource);
            if (resource is null)
            {
                DestroyImageView(vulkan, deviceHandle, view);
                DestroyImage(vulkan, deviceHandle, image);
                DestroyMemory(vulkan, deviceHandle, memory);
            }

            throw;
        }
    }

    private VulkanAssetUploadOperation BeginMesh(MeshGpuUpload upload)
    {
        MeshAssetDescriptor descriptor = upload.Mesh;
        if (descriptor.VertexCount == 0 || descriptor.IndexCount == 0)
        {
            throw VulkanAssetErrors.DecodeFailed(
                descriptor.Payload,
                "the mesh descriptor declares no vertices or no indices"
            );
        }

        long vertexBytes = (long)descriptor.VertexCount * MeshAssetDescriptor.VERTEX_STRIDE;
        long indexBytes = (long)descriptor.IndexCount * sizeof(uint);
        long expectedBytes = vertexBytes + indexBytes;
        if (upload.VertexAndIndexData.Length != expectedBytes)
        {
            throw VulkanAssetErrors.DecodeFailed(
                descriptor.Payload,
                $"the mesh payload is {upload.VertexAndIndexData.Length} byte(s); {descriptor.VertexCount} vertices at {MeshAssetDescriptor.VERTEX_STRIDE} bytes and {descriptor.IndexCount} 32-bit indices require {expectedBytes}"
            );
        }

        ulong vertexSize = (ulong)vertexBytes;
        ulong indexSize = (ulong)indexBytes;
        VulkanDevice device = _deviceAccessor();
        int deviceGeneration = _deviceGenerationAccessor();
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        PhysicalDeviceMemoryProperties memoryProperties = vulkan.GetPhysicalDeviceMemoryProperties(
            device.PhysicalDevice
        );
        Buffer vertexBuffer = default;
        Buffer indexBuffer = default;
        DeviceMemory memory = default;
        StagingAllocation staging = default;
        VulkanAssetUploadCommands.UploadSubmission submission = default;
        VulkanMeshAssetResource? resource = null;
        try
        {
            vertexBuffer = CreateBuffer(
                device,
                vertexSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                upload.Path,
                "vertex buffer"
            );
            indexBuffer = CreateBuffer(
                device,
                indexSize,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                upload.Path,
                "index buffer"
            );

            MemoryRequirements vertexRequirements = vulkan.GetBufferMemoryRequirements(
                deviceHandle,
                vertexBuffer
            );
            MemoryRequirements indexRequirements = vulkan.GetBufferMemoryRequirements(
                deviceHandle,
                indexBuffer
            );
            ulong indexOffset = Align(vertexRequirements.Size, indexRequirements.Alignment);
            ulong allocationSize = indexOffset + indexRequirements.Size;
            uint memoryTypeBits =
                vertexRequirements.MemoryTypeBits & indexRequirements.MemoryTypeBits;
            memory = VulkanAssetMemory.Allocate(
                device,
                memoryProperties,
                allocationSize,
                memoryTypeBits,
                MemoryPropertyFlags.DeviceLocalBit,
                upload.Path,
                "mesh vertex and index buffers"
            );
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BindBufferMemory(deviceHandle, vertexBuffer, memory, 0),
                upload.Path,
                "vkBindBufferMemory for the vertex buffer"
            );
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BindBufferMemory(deviceHandle, indexBuffer, memory, indexOffset),
                upload.Path,
                "vkBindBufferMemory for the index buffer"
            );

            resource = new VulkanMeshAssetResource(
                upload,
                vertexBuffer,
                indexBuffer,
                memory,
                vertexSize,
                indexSize,
                deviceGeneration
            );
            staging = CreateStagingBuffer(
                device,
                memoryProperties,
                upload.VertexAndIndexData.Span,
                upload.Path,
                "mesh"
            );
            submission = VulkanAssetUploadCommands.RecordAndSubmit(
                device,
                _logger,
                upload.Path,
                (recordVulkan, commandBuffer) =>
                    RecordMeshUpload(
                        recordVulkan,
                        commandBuffer,
                        staging.Buffer,
                        vertexBuffer,
                        indexBuffer,
                        vertexSize,
                        indexSize
                    )
            );
            return new VulkanAssetUploadOperation(
                upload,
                device,
                submission,
                staging.Buffer,
                staging.Memory,
                resource
            );
        }
        catch
        {
            DestroyFailedUpload(device, upload, submission, staging, resource);
            if (resource is null)
            {
                DestroyBuffer(vulkan, deviceHandle, indexBuffer);
                DestroyBuffer(vulkan, deviceHandle, vertexBuffer);
                DestroyMemory(vulkan, deviceHandle, memory);
            }

            throw;
        }
    }

    private VulkanAssetUploadOperation BeginShader(ShaderGpuUpload upload)
    {
        ValidateShaderDescriptor(upload.Shader);
        VulkanDevice device = _deviceAccessor();
        int deviceGeneration = _deviceGenerationAccessor();
        ShaderModule vertexModule = CreateShaderModule(
            device,
            upload.VertexSpirv,
            upload.Shader.VertexPayload,
            upload.Path,
            "vertex"
        );
        ShaderModule fragmentModule = default;
        try
        {
            fragmentModule = CreateShaderModule(
                device,
                upload.FragmentSpirv,
                upload.Shader.FragmentPayload,
                upload.Path,
                "fragment"
            );
            VulkanShaderAssetResource resource = new(
                upload,
                vertexModule,
                fragmentModule,
                deviceGeneration
            );
            return new VulkanAssetUploadOperation(
                upload,
                device,
                default,
                default,
                default,
                resource
            );
        }
        catch
        {
            DestroyShaderModule(device.Vulkan, device.Device, fragmentModule);
            DestroyShaderModule(device.Vulkan, device.Device, vertexModule);
            throw;
        }
    }

    /// <summary>Releases the submission, staging, and partials of a failed start.</summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Cleanup after a failed upload may itself fail; the original upload failure is what the caller needs."
    )]
    private void DestroyFailedUpload(
        VulkanDevice device,
        AssetGpuUpload upload,
        VulkanAssetUploadCommands.UploadSubmission submission,
        StagingAllocation staging,
        VulkanAssetResource? resource
    )
    {
        if (submission.Fence.Handle != 0)
        {
            try
            {
                VulkanAssetUploadCommands.Wait(device, submission, upload.Path);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "The failed GPU asset upload of '{Path}' could not be waited out before cleanup.",
                    AssetPath.ToPublic(upload.Path)
                );
            }
        }

        VulkanAssetUploadCommands.Destroy(device, _logger, submission);
        DestroyStagingBuffer(device, staging);
        if (resource is not null)
        {
            DestroyResource(resource);
        }
    }

    private static void ValidateTextureSupport(
        VulkanDevice device,
        AssetGpuUpload upload,
        TextureFormat textureFormat,
        Format format
    )
    {
        if (
            VulkanTextureFormat.IsBlockCompressed(textureFormat)
            && !device.Capabilities.TextureCompressionBc
        )
        {
            throw VulkanAssetErrors.UploadFailed(
                upload.Path,
                $"the texture is {textureFormat} but the device does not support textureCompressionBC."
            );
        }

        FormatProperties formatProperties = device.Vulkan.GetPhysicalDeviceFormatProperties(
            device.PhysicalDevice,
            format
        );
        FormatFeatureFlags requiredFeatures =
            FormatFeatureFlags.SampledImageBit | FormatFeatureFlags.TransferDstBit;
        if ((formatProperties.OptimalTilingFeatures & requiredFeatures) != requiredFeatures)
        {
            throw VulkanAssetErrors.UploadFailed(
                upload.Path,
                $"the device does not support sampling and transfer-destination use of {format} with optimal tiling (supported features 0x{(uint)formatProperties.OptimalTilingFeatures:X8})."
            );
        }
    }

    private static Image CreateImage(
        VulkanDevice device,
        string canonicalPath,
        uint width,
        uint height,
        int mipLevels,
        Format format
    )
    {
        PhysicalDeviceProperties properties = device.Vulkan.GetPhysicalDeviceProperties(
            device.PhysicalDevice
        );
        if (
            width > properties.Limits.MaxImageDimension2D
            || height > properties.Limits.MaxImageDimension2D
        )
        {
            throw VulkanAssetErrors.UploadFailed(
                canonicalPath,
                $"the {width}x{height} texture exceeds the device's maximum 2D image dimension {properties.Limits.MaxImageDimension2D}."
            );
        }

        ImageCreateInfo createInfo = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D
            {
                Width = width,
                Height = height,
                Depth = 1,
            },
            MipLevels = (uint)mipLevels,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Result result = device.Vulkan.CreateImage(
            device.Device,
            &createInfo,
            null,
            out Image image
        );
        VulkanAssetErrors.ThrowIfFailed(
            result,
            canonicalPath,
            $"vkCreateImage for the {format} texture"
        );
        if (image.Handle == 0)
        {
            throw VulkanAssetErrors.UploadFailed(
                canonicalPath,
                "vkCreateImage returned a zero image handle."
            );
        }

        return image;
    }

    private static Buffer CreateBuffer(
        VulkanDevice device,
        ulong size,
        BufferUsageFlags usage,
        string canonicalPath,
        string description
    )
    {
        BufferCreateInfo createInfo = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
        };
        Result result = device.Vulkan.CreateBuffer(
            device.Device,
            &createInfo,
            null,
            out Buffer buffer
        );
        VulkanAssetErrors.ThrowIfFailed(
            result,
            canonicalPath,
            $"vkCreateBuffer for the {description}"
        );
        if (buffer.Handle == 0)
        {
            throw VulkanAssetErrors.UploadFailed(
                canonicalPath,
                $"vkCreateBuffer returned a zero handle for the {description}."
            );
        }

        return buffer;
    }

    private static void RecordTextureUpload(
        Vk vulkan,
        CommandBuffer commandBuffer,
        Buffer stagingBuffer,
        Image image,
        uint width,
        uint height,
        ReadOnlySpan<TextureLevelRange> levels
    )
    {
        ImageSubresourceRange range = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = (uint)levels.Length,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        ImageMemoryBarrier toTransfer = new()
        {
            SType = StructureType.ImageMemoryBarrier,
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.TransferWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
        };
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TopOfPipeBit,
            PipelineStageFlags.TransferBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &toTransfer
        );

        for (int level = 0; level < levels.Length; level++)
        {
            TextureLevelRange levelData = levels[level];
            BufferImageCopy copy = new()
            {
                BufferOffset = levelData.ByteOffset,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = (uint)level,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
                ImageOffset = new Offset3D
                {
                    X = 0,
                    Y = 0,
                    Z = 0,
                },
                ImageExtent = new Extent3D
                {
                    Width = (uint)VulkanTextureFormat.LevelExtent(width, level),
                    Height = (uint)VulkanTextureFormat.LevelExtent(height, level),
                    Depth = 1,
                },
            };
            vulkan.CmdCopyBufferToImage(
                commandBuffer,
                stagingBuffer,
                image,
                ImageLayout.TransferDstOptimal,
                1,
                &copy
            );
        }

        ImageMemoryBarrier toShaderRead = toTransfer;
        toShaderRead.SrcAccessMask = AccessFlags.TransferWriteBit;
        toShaderRead.DstAccessMask = AccessFlags.ShaderReadBit;
        toShaderRead.OldLayout = ImageLayout.TransferDstOptimal;
        toShaderRead.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.AllCommandsBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &toShaderRead
        );
    }

    private static void RecordMeshUpload(
        Vk vulkan,
        CommandBuffer commandBuffer,
        Buffer stagingBuffer,
        Buffer vertexBuffer,
        Buffer indexBuffer,
        ulong vertexSize,
        ulong indexSize
    )
    {
        BufferCopy vertexCopy = new()
        {
            SrcOffset = 0,
            DstOffset = 0,
            Size = vertexSize,
        };
        vulkan.CmdCopyBuffer(commandBuffer, stagingBuffer, vertexBuffer, 1, &vertexCopy);
        BufferCopy indexCopy = new()
        {
            SrcOffset = vertexSize,
            DstOffset = 0,
            Size = indexSize,
        };
        vulkan.CmdCopyBuffer(commandBuffer, stagingBuffer, indexBuffer, 1, &indexCopy);

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
        };
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.VertexInputBit,
            0,
            1,
            &barrier,
            0,
            null,
            0,
            null
        );
    }

    private StagingAllocation CreateStagingBuffer(
        VulkanDevice device,
        PhysicalDeviceMemoryProperties memoryProperties,
        ReadOnlySpan<byte> data,
        string canonicalPath,
        string description
    )
    {
        if (data.IsEmpty)
        {
            throw VulkanAssetErrors.UploadFailed(
                canonicalPath,
                $"the {description} payload is empty."
            );
        }

        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        Buffer buffer = default;
        DeviceMemory memory = default;
        void* mapped = null;
        try
        {
            BufferCreateInfo createInfo = new()
            {
                SType = StructureType.BufferCreateInfo,
                Size = (ulong)data.Length,
                Usage = BufferUsageFlags.TransferSrcBit,
                SharingMode = SharingMode.Exclusive,
            };
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.CreateBuffer(deviceHandle, &createInfo, null, out buffer),
                canonicalPath,
                "vkCreateBuffer for the staging buffer"
            );

            MemoryRequirements requirements = vulkan.GetBufferMemoryRequirements(
                deviceHandle,
                buffer
            );
            memory = VulkanAssetMemory.Allocate(
                device,
                memoryProperties,
                requirements,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                canonicalPath,
                "staging buffer"
            );
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.BindBufferMemory(deviceHandle, buffer, memory, 0),
                canonicalPath,
                "vkBindBufferMemory for the staging buffer"
            );
            VulkanAssetErrors.ThrowIfFailed(
                vulkan.MapMemory(
                    deviceHandle,
                    memory,
                    0,
                    (ulong)data.Length,
                    MemoryMapFlags.None,
                    &mapped
                ),
                canonicalPath,
                "vkMapMemory for the staging buffer"
            );
            data.CopyTo(new Span<byte>(mapped, data.Length));
            return new StagingAllocation(buffer, memory);
        }
        catch
        {
            if (mapped is not null)
            {
                vulkan.UnmapMemory(deviceHandle, memory);
            }

            DestroyBuffer(vulkan, deviceHandle, buffer);
            DestroyMemory(vulkan, deviceHandle, memory);
            throw;
        }
    }

    private ShaderModule CreateShaderModule(
        VulkanDevice device,
        ReadOnlyMemory<byte> spirv,
        string payloadPath,
        string canonicalPath,
        string stage
    )
    {
        if (spirv.IsEmpty)
        {
            throw VulkanAssetErrors.DecodeFailed(
                payloadPath,
                $"the {stage} SPIR-V module is empty"
            );
        }

        if (spirv.Length % sizeof(uint) != 0)
        {
            throw VulkanAssetErrors.DecodeFailed(
                payloadPath,
                $"the {stage} SPIR-V module is {spirv.Length} byte(s), which is not a whole number of 32-bit words"
            );
        }

        // Copy into a uint[] so the code pointer is 4-byte aligned; the payload may be a slice of a
        // larger pack buffer.
        uint[] code = MemoryMarshal.Cast<byte, uint>(spirv.Span).ToArray();
        fixed (uint* codePointer = code)
        {
            ShaderModuleCreateInfo createInfo = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)(code.Length * sizeof(uint)),
                PCode = codePointer,
            };
            Result result = device.Vulkan.CreateShaderModule(
                device.Device,
                &createInfo,
                null,
                out ShaderModule module
            );
            VulkanAssetErrors.ThrowIfFailed(
                result,
                canonicalPath,
                $"vkCreateShaderModule for the {stage} stage"
            );
            if (module.Handle == 0)
            {
                throw VulkanAssetErrors.UploadFailed(
                    canonicalPath,
                    $"vkCreateShaderModule returned a zero {stage} module handle."
                );
            }

            return module;
        }
    }

    private static void ValidateShaderDescriptor(ShaderAssetDescriptor descriptor)
    {
        const ShaderStageMask requiredStages = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if ((descriptor.Stages & requiredStages) != requiredStages)
        {
            throw VulkanAssetErrors.DecodeFailed(
                descriptor.VertexPayload,
                $"the shader descriptor declares stages '{descriptor.Stages}', but a vertex and fragment module are required"
            );
        }

        if (string.IsNullOrWhiteSpace(descriptor.VertexEntry))
        {
            throw VulkanAssetErrors.DecodeFailed(
                descriptor.VertexPayload,
                "the shader descriptor has no vertex entry point name"
            );
        }

        if (string.IsNullOrWhiteSpace(descriptor.FragmentEntry))
        {
            throw VulkanAssetErrors.DecodeFailed(
                descriptor.FragmentPayload,
                "the shader descriptor has no fragment entry point name"
            );
        }
    }

    private void DestroyTexture(VulkanTextureAssetResource texture)
    {
        if (!texture.TryRelease())
        {
            return;
        }

        VulkanDevice device = _deviceAccessor();
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        DestroyImageView(vulkan, deviceHandle, texture.View);
        DestroyImage(vulkan, deviceHandle, texture.Image);
        DestroyMemory(vulkan, deviceHandle, texture.Memory);
        // The sampler belongs to the per-device cache, not to this resource. The slot is released only
        // after the bridge has confirmed that no submitted frame can reference the texture.
        texture.ReleaseBindlessSlot(_bindlessTextureTableAccessor());
    }

    private void DestroyMesh(VulkanMeshAssetResource mesh)
    {
        if (!mesh.TryRelease())
        {
            return;
        }

        VulkanDevice device = _deviceAccessor();
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        DestroyBuffer(vulkan, deviceHandle, mesh.IndexBuffer);
        DestroyBuffer(vulkan, deviceHandle, mesh.VertexBuffer);
        DestroyMemory(vulkan, deviceHandle, mesh.Memory);
    }

    private void DestroyShader(VulkanShaderAssetResource shader)
    {
        if (!shader.TryRelease())
        {
            return;
        }

        VulkanDevice device = _deviceAccessor();
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        DestroyShaderModule(vulkan, deviceHandle, shader.FragmentModule);
        DestroyShaderModule(vulkan, deviceHandle, shader.VertexModule);
    }

    private void DestroyStagingBuffer(VulkanDevice device, StagingAllocation staging)
    {
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        DestroyBuffer(vulkan, deviceHandle, staging.Buffer);
        DestroyMemory(vulkan, deviceHandle, staging.Memory);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort teardown: the device may already be lost when the image view is destroyed."
    )]
    private void DestroyImageView(Vk vulkan, Device device, ImageView view)
    {
        if (view.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.DestroyImageView(device, view, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The Vulkan asset image view could not be destroyed.");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort teardown: the device may already be lost when the image is destroyed."
    )]
    private void DestroyImage(Vk vulkan, Device device, Image image)
    {
        if (image.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.DestroyImage(device, image, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The Vulkan asset image could not be destroyed.");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort teardown: the device may already be lost when the buffer is destroyed."
    )]
    private void DestroyBuffer(Vk vulkan, Device device, Buffer buffer)
    {
        if (buffer.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.DestroyBuffer(device, buffer, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The Vulkan asset buffer could not be destroyed.");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort teardown: the device may already be lost when the allocation is freed."
    )]
    private void DestroyMemory(Vk vulkan, Device device, DeviceMemory memory)
    {
        if (memory.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.FreeMemory(device, memory, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The Vulkan asset memory could not be freed.");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort teardown: the device may already be lost when the shader module is destroyed."
    )]
    private void DestroyShaderModule(Vk vulkan, Device device, ShaderModule module)
    {
        if (module.Handle == 0)
        {
            return;
        }

        try
        {
            vulkan.DestroyShaderModule(device, module, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The Vulkan asset shader module could not be destroyed.");
        }
    }

    private static ulong Align(ulong value, ulong alignment) =>
        ((value + alignment - 1) / alignment) * alignment;

    /// <summary>The staging buffer and memory of one upload, freed after its fence signals.</summary>
    private readonly record struct StagingAllocation(Buffer Buffer, DeviceMemory Memory);

    /// <summary>One mip level's byte range; KTX2 offsets include container padding.</summary>
    private readonly record struct TextureLevelRange(ulong ByteOffset, ulong ByteLength);
}
