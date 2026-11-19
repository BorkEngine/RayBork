using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The engine's reverse-Z depth convention: the pipeline and render-pass depth state every pass uses.
/// </summary>
/// <remarks>
/// Projection maps near to <c>1.0</c> and infinity to <c>0.0</c>, so the buffer clears to zero and
/// fragments pass when their depth is greater than or equal to the stored value. Mixing in an
/// OpenGL-style <c>[-1, 1]</c> projection or a less-than compare breaks the test.
/// </remarks>
internal static class VulkanDepthState
{
    /// <summary>The reverse-Z far-plane depth the scene target clears to.</summary>
    internal const float CLEAR_DEPTH = 0f;

    /// <summary>The depth comparison every depth-tested pipeline uses.</summary>
    internal const CompareOp COMPARE_OPERATION = CompareOp.GreaterOrEqual;

    /// <summary>Whether opaque geometry tests depth.</summary>
    internal const bool DEPTH_TEST_ENABLE = true;

    /// <summary>Whether opaque geometry writes depth.</summary>
    internal const bool DEPTH_WRITE_ENABLE = true;

    /// <summary>The lower depth-range bound, the reverse-Z far plane.</summary>
    internal const float MIN_DEPTH = 0f;

    /// <summary>The upper depth-range bound, the reverse-Z near plane.</summary>
    internal const float MAX_DEPTH = 1f;

    /// <summary>Creates the depth-stencil state for a depth-tested opaque pipeline.</summary>
    /// <returns>
    /// Reverse-Z testing and writes enabled, with stencil and depth-bounds testing disabled.
    /// </returns>
    internal static PipelineDepthStencilStateCreateInfo CreatePipelineState() =>
        new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = DEPTH_TEST_ENABLE,
            DepthWriteEnable = DEPTH_WRITE_ENABLE,
            DepthCompareOp = COMPARE_OPERATION,
            DepthBoundsTestEnable = false,
            StencilTestEnable = false,
            Front = default,
            Back = default,
            MinDepthBounds = MIN_DEPTH,
            MaxDepthBounds = MAX_DEPTH,
        };
}
