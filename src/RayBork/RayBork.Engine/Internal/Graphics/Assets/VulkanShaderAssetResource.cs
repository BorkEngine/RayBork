using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The Vulkan vertex and fragment shader modules behind one uploaded shader.</summary>
/// <remarks>
/// Created by <see cref="VulkanAssetWorker.BeginUpload"/> and recreated by
/// <see cref="IAssetGpuWorker.Restore"/>; entry points stay in the retained descriptor and no
/// pipeline is created here. Both modules are destroyed together.
/// </remarks>
internal sealed class VulkanShaderAssetResource : VulkanAssetResource
{
    /// <summary>Initializes a shader resource.</summary>
    internal VulkanShaderAssetResource(
        ShaderGpuUpload upload,
        ShaderModule vertexModule,
        ShaderModule fragmentModule,
        int deviceGeneration
    )
        : base(upload, unchecked((long)vertexModule.Handle), deviceGeneration)
    {
        VertexModule = vertexModule;
        FragmentModule = fragmentModule;
    }

    /// <summary>Gets the vertex-stage shader module.</summary>
    internal ShaderModule VertexModule { get; private set; }

    /// <summary>Gets the fragment-stage shader module.</summary>
    internal ShaderModule FragmentModule { get; private set; }

    /// <summary>Adopts the handles of a shader recreated after a device recovery.</summary>
    internal override void AdoptRestored(GpuAssetResource restored)
    {
        if (ReferenceEquals(this, restored))
        {
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
