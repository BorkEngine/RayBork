using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The engine's reverse-Z depth convention: the single pipeline and render-pass depth state every
/// engine pass uses.
/// </summary>
/// <remarks>
/// <para>
/// Projection maps the near plane to <c>1.0</c> and infinity to <c>0.0</c>, so the depth buffer is
/// cleared to zero and fragments pass the test when their depth is greater than or equal to the
/// stored value. Mixing in an OpenGL-style <c>[-1, 1]</c> projection or a less-than compare makes the
/// depth test wrong.
/// </para>
/// <para>
/// The constants are pure data. <see cref="CreatePipelineState"/> builds the matching pipeline state
/// with stencil and depth-bounds testing disabled.
/// </para>
/// </remarks>
internal static class VulkanDepthState {
    /// <summary>
    /// The depth value the scene target is cleared to, which is the reverse-Z far plane.
    /// </summary>
    internal const float CLEAR_DEPTH = 0f;

    /// <summary>
    /// The depth comparison used by every depth-tested pipeline.
    /// </summary>
    internal const CompareOp COMPARE_OPERATION = CompareOp.GreaterOrEqual;

    /// <summary>
    /// Whether depth testing is enabled for opaque geometry.
    /// </summary>
    internal const bool DEPTH_TEST_ENABLE = true;

    /// <summary>
    /// Whether opaque geometry writes depth.
    /// </summary>
    internal const bool DEPTH_WRITE_ENABLE = true;

    /// <summary>
    /// The lower bound of the Vulkan depth range, which is the reverse-Z far plane.
    /// </summary>
    internal const float MIN_DEPTH = 0f;

    /// <summary>
    /// The upper bound of the Vulkan depth range, which is the reverse-Z near plane.
    /// </summary>
    internal const float MAX_DEPTH = 1f;

    /// <summary>
    /// Creates the pipeline depth-stencil state for a depth-tested opaque pipeline.
    /// </summary>
    /// <returns>
    /// The state with reverse-Z testing and writes enabled, and with stencil and depth-bounds testing
    /// disabled.
    /// </returns>
    internal static PipelineDepthStencilStateCreateInfo CreatePipelineState() => new() {
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
