using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace RayBork.Internal;

/// <summary>
/// The shared descriptor state of the M4 geometry path: one set layout, one update-after-bind
/// descriptor pool, and the single pipeline layout every geometry pipeline uses.
/// </summary>
/// <remarks>
/// <para>
/// Set 0 binding 0 is the frame uniform block, binding 1 is the bindless combined-image-sampler
/// array, and binding 2 is the per-instance storage buffer. The array binding is partially bound and
/// update-after-bind, so the renderer can write a newly allocated slot while frames are in flight;
/// the uniform and storage-buffer bindings are ordinary descriptors. The set layout and pool carry
/// the update-after-bind flags the array binding requires. No push constants are declared.
/// </para>
/// <para>
/// The state is created on the render thread with the logical device and destroyed before the device.
/// The frame descriptor sets are allocated by <see cref="VulkanFrameResources"/>; the bindless table
/// writes image descriptors into those sets. Every member is render-thread only and the call is not
/// thread-safe.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanDescriptorState : IDisposable {
    /// <summary>The descriptor set index of the geometry path.</summary>
    internal const uint GEOMETRY_SET = 0;

    /// <summary>The binding index of the frame uniform block.</summary>
    internal const uint FRAME_BINDING = 0;

    /// <summary>The binding index of the bindless texture array.</summary>
    internal const uint TEXTURE_BINDING = 1;

    /// <summary>The binding index of the instance storage buffer.</summary>
    internal const uint INSTANCE_BINDING = 2;

    private readonly Vk _vulkan;
    private readonly Device _device;
    private readonly ResourceState _resources;

    private VulkanDescriptorState(Vk vulkan, Device device, ResourceState resources) {
        _vulkan = vulkan;
        _device = device;
        _resources = resources;
    }

    /// <summary>
    /// Gets the descriptor set layout shared by every geometry pipeline.
    /// </summary>
    internal DescriptorSetLayout SetLayout => _resources.SetLayout;

    /// <summary>
    /// Gets the pipeline layout with the single geometry set layout and no push constants.
    /// </summary>
    internal PipelineLayout PipelineLayout => _resources.PipelineLayout;

    /// <summary>
    /// Creates the set layout, descriptor pool, and pipeline layout.
    /// </summary>
    /// <param name="device">The device that owns the state.</param>
    /// <param name="bindlessCapacity">The bindless texture array capacity.</param>
    /// <param name="maxSets">The number of frame descriptor sets to pool.</param>
    /// <returns>The created descriptor state.</returns>
    /// <exception cref="GraphicsException">
    /// A Vulkan object could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanDescriptorState Create(VulkanDevice device, uint bindlessCapacity, uint maxSets) {
        ArgumentNullException.ThrowIfNull(device);
        if (bindlessCapacity == 0) {
            throw new ArgumentOutOfRangeException(
                nameof(bindlessCapacity),
                bindlessCapacity,
                "The bindless texture capacity must be positive.");
        }

        if (maxSets == 0) {
            throw new ArgumentOutOfRangeException(nameof(maxSets), maxSets, "At least one descriptor set is required.");
        }

        Vk vulkan = device.Vulkan;
        DescriptorSetLayout setLayout = CreateSetLayout(vulkan, device.Device, bindlessCapacity);
        DescriptorPool pool = default;
        PipelineLayout pipelineLayout = default;
        try {
            pool = CreatePool(vulkan, device.Device, bindlessCapacity, maxSets);
            pipelineLayout = CreatePipelineLayout(vulkan, device.Device, setLayout);
            return new VulkanDescriptorState(
                vulkan,
                device.Device,
                new ResourceState(setLayout, pool, pipelineLayout, bindlessCapacity));
        } catch {
            DestroyPipelineLayout(vulkan, device.Device, pipelineLayout);
            DestroyPool(vulkan, device.Device, pool);
            DestroySetLayout(vulkan, device.Device, setLayout);
            throw;
        }
    }

    /// <summary>
    /// Allocates one descriptor set from the shared pool.
    /// </summary>
    /// <returns>The allocated set, already bound to <see cref="SetLayout"/>.</returns>
    /// <exception cref="GraphicsException">
    /// The pool could not allocate the set. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal DescriptorSet AllocateSet() {
        DescriptorSetLayout setLayout = SetLayout;
        DescriptorSetAllocateInfo allocateInfo = new() {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _resources.Pool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        Result result = _vulkan.AllocateDescriptorSets(_device, &allocateInfo, out DescriptorSet descriptorSet);
        if (result != Result.Success || descriptorSet.Handle == 0) {
            throw new GraphicsException(
                $"The Vulkan geometry descriptor set could not be allocated: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return descriptorSet;
    }

    /// <summary>
    /// Writes the frame uniform-buffer descriptor of one frame set.
    /// </summary>
    /// <param name="set">The frame set to update.</param>
    /// <param name="buffer">The host-visible frame uniform buffer.</param>
    /// <param name="blockSize">The byte size of the frame block.</param>
    internal void WriteFrameUniformBuffer(DescriptorSet set, Buffer buffer, ulong blockSize) {
        DescriptorBufferInfo bufferInfo = new() {
            Buffer = buffer,
            Offset = 0,
            Range = blockSize,
        };
        WriteDescriptorSet write = new() {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = FRAME_BINDING,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &bufferInfo,
        };
        _vulkan.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    /// <summary>
    /// Writes the per-instance storage-buffer descriptor of one frame set.
    /// </summary>
    /// <param name="set">The frame set to update.</param>
    /// <param name="buffer">The host-visible instance buffer.</param>
    /// <param name="size">The buffer size in bytes.</param>
    internal void WriteInstanceStorageBuffer(DescriptorSet set, Buffer buffer, ulong size) {
        DescriptorBufferInfo bufferInfo = new() {
            Buffer = buffer,
            Offset = 0,
            Range = size,
        };
        WriteDescriptorSet write = new() {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = INSTANCE_BINDING,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo,
        };
        _vulkan.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    /// <summary>
    /// Writes one bindless texture slot into every frame descriptor set.
    /// </summary>
    /// <remarks>
    /// A newly allocated slot is never referenced by an in-flight frame, so the update-after-bind
    /// array binding makes this write safe while earlier frames are still executing. The caller must
    /// not rewrite a slot that an in-flight frame can reference.
    /// </remarks>
    /// <param name="sets">The frame descriptor sets.</param>
    /// <param name="slot">The bindless slot to write.</param>
    /// <param name="view">The texture image view.</param>
    /// <param name="sampler">The sampler for the texture's state.</param>
    internal void WriteBindlessTexture(DescriptorSet[] sets, uint slot, ImageView view, Sampler sampler) {
        ArgumentNullException.ThrowIfNull(sets);
        if (sets.Length == 0) {
            return;
        }

        if (slot >= _resources.Capacity) {
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"The bindless slot must be below the table capacity {_resources.Capacity}.");
        }

        DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[sets.Length];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[sets.Length];
        for (int index = 0; index < sets.Length; index++) {
            imageInfos[index] = new DescriptorImageInfo {
                Sampler = sampler,
                ImageView = view,
                ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            };
            writes[index] = new WriteDescriptorSet {
                SType = StructureType.WriteDescriptorSet,
                DstSet = sets[index],
                DstBinding = TEXTURE_BINDING,
                DstArrayElement = slot,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.CombinedImageSampler,
                PImageInfo = &imageInfos[index],
            };
        }

        _vulkan.UpdateDescriptorSets(_device, (uint)sets.Length, writes, 0, null);
    }

    /// <summary>
    /// Destroys the pipeline layout, descriptor pool, and set layout. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_resources.Disposed) {
            return;
        }

        DestroyPipelineLayout(_vulkan, _device, _resources.PipelineLayout);
        DestroyPool(_vulkan, _device, _resources.Pool);
        DestroySetLayout(_vulkan, _device, _resources.SetLayout);
        _resources.Disposed = true;
    }

    private static DescriptorSetLayout CreateSetLayout(Vk vulkan, Device device, uint bindlessCapacity) {
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[3];
        bindings[FRAME_BINDING] = new DescriptorSetLayoutBinding {
            Binding = FRAME_BINDING,
            DescriptorType = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };
        bindings[TEXTURE_BINDING] = new DescriptorSetLayoutBinding {
            Binding = TEXTURE_BINDING,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = bindlessCapacity,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };
        bindings[INSTANCE_BINDING] = new DescriptorSetLayoutBinding {
            Binding = INSTANCE_BINDING,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };
        DescriptorBindingFlags* bindingFlags = stackalloc DescriptorBindingFlags[3];
        bindingFlags[FRAME_BINDING] = DescriptorBindingFlags.None;
        bindingFlags[TEXTURE_BINDING] =
            DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit;
        bindingFlags[INSTANCE_BINDING] = DescriptorBindingFlags.None;
        DescriptorSetLayoutBindingFlagsCreateInfo flagsInfo = new() {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 3,
            PBindingFlags = bindingFlags,
        };
        DescriptorSetLayoutCreateInfo createInfo = new() {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            PNext = &flagsInfo,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 3,
            PBindings = bindings,
        };
        Result result = vulkan.CreateDescriptorSetLayout(device, &createInfo, null, out DescriptorSetLayout setLayout);
        if (result != Result.Success || setLayout.Handle == 0) {
            throw new GraphicsException(
                $"The Vulkan geometry descriptor set layout could not be created: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return setLayout;
    }

    private static DescriptorPool CreatePool(Vk vulkan, Device device, uint bindlessCapacity, uint maxSets) {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3];
        poolSizes[0] = new DescriptorPoolSize {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = maxSets,
        };
        poolSizes[1] = new DescriptorPoolSize {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = checked(bindlessCapacity * maxSets),
        };
        poolSizes[2] = new DescriptorPoolSize {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = maxSets,
        };
        DescriptorPoolCreateInfo createInfo = new() {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = maxSets,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes,
        };
        Result result = vulkan.CreateDescriptorPool(device, &createInfo, null, out DescriptorPool pool);
        if (result != Result.Success || pool.Handle == 0) {
            throw new GraphicsException(
                $"The Vulkan geometry descriptor pool could not be created: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return pool;
    }

    private static PipelineLayout CreatePipelineLayout(Vk vulkan, Device device, DescriptorSetLayout setLayout) {
        PipelineLayoutCreateInfo createInfo = new() {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
            PushConstantRangeCount = 0,
            PPushConstantRanges = null,
        };
        Result result = vulkan.CreatePipelineLayout(device, &createInfo, null, out PipelineLayout pipelineLayout);
        if (result != Result.Success || pipelineLayout.Handle == 0) {
            throw new GraphicsException(
                $"The Vulkan geometry pipeline layout could not be created: {result}.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return pipelineLayout;
    }

    private static void DestroyPipelineLayout(Vk vulkan, Device device, PipelineLayout pipelineLayout) {
        if (pipelineLayout.Handle != 0) {
            vulkan.DestroyPipelineLayout(device, pipelineLayout, null);
        }
    }

    private static void DestroyPool(Vk vulkan, Device device, DescriptorPool pool) {
        if (pool.Handle != 0) {
            vulkan.DestroyDescriptorPool(device, pool, null);
        }
    }

    private static void DestroySetLayout(Vk vulkan, Device device, DescriptorSetLayout setLayout) {
        if (setLayout.Handle != 0) {
            vulkan.DestroyDescriptorSetLayout(device, setLayout, null);
        }
    }

    private sealed class ResourceState {
        internal ResourceState(
            DescriptorSetLayout setLayout,
            DescriptorPool pool,
            PipelineLayout pipelineLayout,
            uint capacity) {
            SetLayout = setLayout;
            Pool = pool;
            PipelineLayout = pipelineLayout;
            Capacity = capacity;
        }

        internal DescriptorSetLayout SetLayout { get; }

        internal DescriptorPool Pool { get; }

        internal PipelineLayout PipelineLayout { get; }

        internal uint Capacity { get; }

        internal bool Disposed { get; set; }
    }
}
