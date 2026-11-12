using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The bindless combined-image-sampler table that backs the fixed <c>RayBorkTextures[2048]</c> array
/// in every geometry shader.
/// </summary>
/// <remarks>
/// <para>
/// Slot 0 always holds a renderer-created 1x1 white texture, so a <see langword="null"/> texture
/// binding and the default model draw sample white without a shader branch. Texture uploads reserve
/// the lowest free slot after their GPU image and view exist, write the descriptor into every frame
/// descriptor set, and hand the slot to the texture resource. The slot is released when the texture
/// resource is destroyed, which already waits for frame idleness, so a reused slot is never
/// referenced by an in-flight frame.
/// </para>
/// <para>
/// The table is created once per backend and re-initialized after a device recreation: <see
/// cref="Reset"/> releases the white texture and every slot before the old device is destroyed, and
/// <see cref="Initialize"/> creates the new white texture after the new device, descriptor state, and
/// frame resources exist. The GPU asset bridge re-uploads every live texture after the recreation, so
/// each one reserves a fresh slot before frames resume. Every member is render-thread only.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanBindlessTextureTable : IDisposable {
    /// <summary>The slot that always holds the white fallback texture.</summary>
    internal const uint WHITE_SLOT = 0;

    private const uint WHITE_TEXTURE_SIZE = 1;
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    private readonly VulkanBindlessSlotAllocator _allocator;
    private readonly ILogger _logger;

    private Vk? _vulkan;
    private Device _device;
    private VulkanDescriptorState? _descriptors;
    private VulkanFrameResources? _frameResources;
    private Image _whiteImage;
    private DeviceMemory _whiteMemory;
    private ImageView _whiteView;
    private Sampler _whiteSampler;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Initializes the table for a maximum capacity. No GPU object exists until <see cref="Initialize"/>.
    /// </summary>
    /// <param name="capacity">The maximum number of slots, including the white slot.</param>
    /// <param name="logger">The renderer logger for white-texture diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is zero.</exception>
    internal VulkanBindlessTextureTable(uint capacity, ILogger logger) {
        ArgumentNullException.ThrowIfNull(logger);

        _allocator = new VulkanBindlessSlotAllocator(capacity);
        _logger = logger;
    }

    /// <summary>Gets the total number of slots, including the white slot.</summary>
    internal uint Capacity => _allocator.Capacity;

    /// <summary>Gets the number of live slots, including the white slot.</summary>
    internal int LiveCount => _allocator.LiveCount;

    /// <summary>Gets the white fallback image, for diagnostics and tests.</summary>
    internal Image WhiteImage => _whiteImage;

    /// <summary>Gets the white fallback image view, for diagnostics and tests.</summary>
    internal ImageView WhiteView => _whiteView;

    /// <summary>
    /// Creates the white fallback texture and writes slot 0 into every frame descriptor set.
    /// </summary>
    /// <param name="device">The current logical device.</param>
    /// <param name="descriptors">The shared descriptor state.</param>
    /// <param name="frameResources">The frame resources that own the descriptor sets.</param>
    /// <exception cref="InvalidOperationException">The table is already initialized or disposed.</exception>
    /// <exception cref="GraphicsException">
    /// The white texture could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal void Initialize(
        VulkanDevice device,
        VulkanDescriptorState descriptors,
        VulkanFrameResources frameResources) {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(frameResources);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) {
            throw new InvalidOperationException("The bindless texture table is already initialized.");
        }

        _vulkan = device.Vulkan;
        _device = device.Device;
        _descriptors = descriptors;
        _frameResources = frameResources;
        CreateWhiteTexture(device);
        _allocator.ReserveWhiteSlot();
        _descriptors.WriteBindlessTexture(
            frameResources.DescriptorSets,
            WHITE_SLOT,
            _whiteView,
            _whiteSampler);
        _initialized = true;
    }

    /// <summary>
    /// Tries to reserve a texture slot. The caller writes the image descriptor and then stores the
    /// slot on the texture resource.
    /// </summary>
    /// <param name="slot">The reserved slot, or <c>0</c> when the table is full.</param>
    /// <returns><see langword="true"/> when a slot was reserved.</returns>
    /// <exception cref="InvalidOperationException">The table is not initialized.</exception>
    internal bool TryAllocate(out uint slot) {
        EnsureInitialized();
        return _allocator.TryAllocate(out slot);
    }

    /// <summary>
    /// Writes one texture's image view and sampler into a reserved slot in every frame set.
    /// </summary>
    /// <param name="slot">The reserved slot.</param>
    /// <param name="view">The texture image view.</param>
    /// <param name="sampler">The sampler for the texture's state.</param>
    /// <exception cref="InvalidOperationException">The table is not initialized.</exception>
    internal void WriteTexture(uint slot, ImageView view, Sampler sampler) {
        EnsureInitialized();
        _descriptors!.WriteBindlessTexture(_frameResources!.DescriptorSets, slot, view, sampler);
    }

    /// <summary>
    /// Releases one texture slot. The texture resource calls it after frame idleness has retired every
    /// frame that could reference the slot.
    /// </summary>
    /// <param name="slot">The slot to release.</param>
    internal void Release(uint slot) => _allocator.Release(slot);

    /// <summary>
    /// Releases every slot and destroys the white texture. The caller has waited for the device to
    /// idle and calls it before destroying the descriptor state and the logical device.
    /// </summary>
    internal void Reset() {
        _allocator.Reset();
        DestroyWhiteTexture();
        _initialized = false;
        _descriptors = null;
        _frameResources = null;
    }

    /// <summary>
    /// Releases every slot and destroys the white texture. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        Reset();
        _vulkan = null;
    }

    private void EnsureInitialized() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) {
            throw new InvalidOperationException("The bindless texture table is not initialized.");
        }
    }

    private void CreateWhiteTexture(VulkanDevice device) {
        Vk vulkan = device.Vulkan;
        Device deviceHandle = device.Device;
        Image image = default;
        DeviceMemory memory = default;
        ImageView view = default;
        Sampler sampler = default;
        CommandPool commandPool = default;
        Fence fence = default;
        try {
            ImageCreateInfo imageInfo = new() {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                Extent = new Extent3D {
                    Width = WHITE_TEXTURE_SIZE,
                    Height = WHITE_TEXTURE_SIZE,
                    Depth = 1,
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            ThrowIfFailed(vulkan.CreateImage(deviceHandle, &imageInfo, null, out image), "vkCreateImage");
            MemoryRequirements requirements = vulkan.GetImageMemoryRequirements(deviceHandle, image);
            PhysicalDeviceMemoryProperties memoryProperties =
                vulkan.GetPhysicalDeviceMemoryProperties(device.PhysicalDevice);
            MemoryAllocateInfo allocateInfo = new() {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = FindDeviceLocalMemoryType(requirements.MemoryTypeBits, memoryProperties),
            };
            ThrowIfFailed(vulkan.AllocateMemory(deviceHandle, &allocateInfo, null, out memory), "vkAllocateMemory");
            ThrowIfFailed(vulkan.BindImageMemory(deviceHandle, image, memory, 0), "vkBindImageMemory");
            ImageViewCreateInfo viewInfo = new() {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Unorm,
                Components = new ComponentMapping {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange = new ImageSubresourceRange {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            ThrowIfFailed(vulkan.CreateImageView(deviceHandle, &viewInfo, null, out view), "vkCreateImageView");

            SamplerCreateInfo samplerInfo = new() {
                SType = StructureType.SamplerCreateInfo,
                MagFilter = Filter.Linear,
                MinFilter = Filter.Linear,
                MipmapMode = SamplerMipmapMode.Nearest,
                AddressModeU = SamplerAddressMode.ClampToEdge,
                AddressModeV = SamplerAddressMode.ClampToEdge,
                AddressModeW = SamplerAddressMode.ClampToEdge,
                MipLodBias = 0f,
                AnisotropyEnable = false,
                MaxAnisotropy = 1f,
                CompareEnable = false,
                CompareOp = CompareOp.Always,
                MinLod = 0f,
                MaxLod = 0f,
                BorderColor = BorderColor.FloatOpaqueWhite,
                UnnormalizedCoordinates = false,
            };
            ThrowIfFailed(vulkan.CreateSampler(deviceHandle, &samplerInfo, null, out sampler), "vkCreateSampler");

            CommandPoolCreateInfo poolInfo = new() {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.TransientBit,
                QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
            };
            ThrowIfFailed(vulkan.CreateCommandPool(deviceHandle, &poolInfo, null, out commandPool), "vkCreateCommandPool");
            CommandBufferAllocateInfo allocateCommandInfo = new() {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = commandPool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            ThrowIfFailed(
                vulkan.AllocateCommandBuffers(deviceHandle, &allocateCommandInfo, out CommandBuffer commandBuffer),
                "vkAllocateCommandBuffers");
            CommandBufferBeginInfo beginInfo = new() {
                SType = StructureType.CommandBufferBeginInfo,
                Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            };
            ThrowIfFailed(vulkan.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
            RecordWhiteTextureClear(vulkan, commandBuffer, image);
            ThrowIfFailed(vulkan.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

            FenceCreateInfo fenceInfo = new() { SType = StructureType.FenceCreateInfo };
            ThrowIfFailed(vulkan.CreateFence(deviceHandle, &fenceInfo, null, out fence), "vkCreateFence");
            SubmitInfo submitInfo = new() {
                SType = StructureType.SubmitInfo,
                CommandBufferCount = 1,
                PCommandBuffers = &commandBuffer,
            };
            ThrowIfFailed(vulkan.QueueSubmit(device.GraphicsQueue, 1, &submitInfo, fence), "vkQueueSubmit");
            ThrowIfFailed(vulkan.WaitForFences(deviceHandle, 1, &fence, true, WAIT_TIMEOUT), "vkWaitForFences");

            _whiteImage = image;
            _whiteMemory = memory;
            _whiteView = view;
            _whiteSampler = sampler;
            image = default;
            memory = default;
            view = default;
            sampler = default;
        } finally {
            if (fence.Handle != 0) {
                vulkan.DestroyFence(deviceHandle, fence, null);
            }

            if (commandPool.Handle != 0) {
                vulkan.DestroyCommandPool(deviceHandle, commandPool, null);
            }

            if (sampler.Handle != 0) {
                vulkan.DestroySampler(deviceHandle, sampler, null);
            }

            if (view.Handle != 0) {
                vulkan.DestroyImageView(deviceHandle, view, null);
            }

            if (image.Handle != 0) {
                vulkan.DestroyImage(deviceHandle, image, null);
            }

            if (memory.Handle != 0) {
                vulkan.FreeMemory(deviceHandle, memory, null);
            }
        }
    }

    private static void RecordWhiteTextureClear(Vk vulkan, CommandBuffer commandBuffer, Image image) {
        ImageSubresourceRange range = new() {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        ImageMemoryBarrier toTransfer = new() {
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
            &toTransfer);
        ClearColorValue white = new() {
            Float32_0 = 1f,
            Float32_1 = 1f,
            Float32_2 = 1f,
            Float32_3 = 1f,
        };
        vulkan.CmdClearColorImage(
            commandBuffer,
            image,
            ImageLayout.TransferDstOptimal,
            &white,
            1,
            &range);

        ImageMemoryBarrier toShaderRead = toTransfer;
        toShaderRead.SrcAccessMask = AccessFlags.TransferWriteBit;
        toShaderRead.DstAccessMask = AccessFlags.ShaderReadBit;
        toShaderRead.OldLayout = ImageLayout.TransferDstOptimal;
        toShaderRead.NewLayout = ImageLayout.ShaderReadOnlyOptimal;
        vulkan.CmdPipelineBarrier(
            commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.FragmentShaderBit,
            0,
            0,
            null,
            0,
            null,
            1,
            &toShaderRead);
    }

    private void DestroyWhiteTexture() {
        if (_vulkan is null) {
            return;
        }

        Vk vulkan = _vulkan;
        if (_whiteSampler.Handle != 0) {
            DestroyBestEffort("sampler", () => vulkan.DestroySampler(_device, _whiteSampler, null));
            _whiteSampler = default;
        }

        if (_whiteView.Handle != 0) {
            DestroyBestEffort("image view", () => vulkan.DestroyImageView(_device, _whiteView, null));
            _whiteView = default;
        }

        if (_whiteImage.Handle != 0) {
            DestroyBestEffort("image", () => vulkan.DestroyImage(_device, _whiteImage, null));
            _whiteImage = default;
        }

        if (_whiteMemory.Handle != 0) {
            DestroyBestEffort("memory", () => vulkan.FreeMemory(_device, _whiteMemory, null));
            _whiteMemory = default;
        }
    }

    private void DestroyBestEffort(string description, Action destroy) {
        try {
            destroy();
        } catch (Exception exception) {
            // The table can be reset while a failed device recovery is already unwinding. Logging is
            // the only safe action; the object belongs to the device that is being torn down.
            _logger.LogWarning(exception, "The Vulkan bindless white texture {Description} could not be destroyed.", description);
        }
    }

    private static uint FindDeviceLocalMemoryType(
        uint typeBits,
        PhysicalDeviceMemoryProperties memoryProperties) {
        const MemoryPropertyFlags required = MemoryPropertyFlags.DeviceLocalBit;
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++) {
            bool typeSupported = (typeBits & (1u << (int)index)) != 0;
            bool propertiesMatch =
                (memoryProperties.MemoryTypes[(int)index].PropertyFlags & required) == required;
            if (typeSupported && propertiesMatch) {
                return index;
            }
        }

        throw new GraphicsException(
            $"No device-local Vulkan memory type is available for the bindless white texture "
                + $"(type bits 0x{typeBits:X8}).",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static void ThrowIfFailed(Result result, string operation) {
        if (result == Result.Success) {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed while creating the bindless white texture: {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }
}
