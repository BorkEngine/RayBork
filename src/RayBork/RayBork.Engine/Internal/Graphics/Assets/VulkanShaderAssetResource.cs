using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan objects behind one uploaded shader: the vertex-stage and fragment-stage shader modules.
/// </summary>
/// <remarks>
/// Created on the render thread by <see cref="VulkanAssetWorker.Upload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/> after a device recovery. The vertex module is the primary
/// handle. The entry point names, stages, and default render state stay in the retained descriptor;
/// a shader module does not carry an entry point, so pipeline creation in M4 pairs the modules with
/// the descriptor's entry points. No pipeline is created here. The modules are destroyed together by
/// <see cref="VulkanAssetWorker.Destroy"/>.
/// </remarks>
internal sealed class VulkanShaderAssetResource : VulkanAssetResource {
    /// <summary>
    /// Initializes a shader resource.
    /// </summary>
    /// <param name="upload">The retained shader upload description.</param>
    /// <param name="vertexModule">The vertex-stage shader module.</param>
    /// <param name="fragmentModule">The fragment-stage shader module.</param>
    /// <param name="deviceGeneration">The logical-device generation that owns the handles.</param>
    internal VulkanShaderAssetResource(
        ShaderGpuUpload upload,
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        int deviceGeneration)
        : base(upload, unchecked((long)vertexModule.Handle), deviceGeneration) {
        VertexModule = vertexModule;
        FragmentModule = fragmentModule;
    }

    /// <summary>
    /// Gets the vertex-stage shader module.
    /// </summary>
    internal ShaderModule VertexModule { get; private set; }

    /// <summary>
    /// Gets the fragment-stage shader module.
    /// </summary>
    internal ShaderModule FragmentModule { get; private set; }

    /// <summary>
    /// Adopts the handles of a shader recreated after a device recovery.
    /// </summary>
    /// <param name="restored">The recreated shader resource.</param>
    internal override void AdoptRestored(GpuAssetResource restored) {
        if (ReferenceEquals(this, restored)) {
            return;
        }

        VulkanShaderAssetResource shader = CastRestored<VulkanShaderAssetResource>(restored);
        VertexModule = shader.VertexModule;
        FragmentModule = shader.FragmentModule;
        AdoptGeneration(shader);
        ResetRelease();
        PublishHandle(shader.Handle);
    }
}
