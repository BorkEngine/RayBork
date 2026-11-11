using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

public sealed class VulkanDepthStateTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstantsDescribeTheReverseZConvention() {
        Assert.Equal(0f, VulkanDepthState.CLEAR_DEPTH);
        Assert.Equal(CompareOp.GreaterOrEqual, VulkanDepthState.COMPARE_OPERATION);
        Assert.True(VulkanDepthState.DEPTH_TEST_ENABLE);
        Assert.True(VulkanDepthState.DEPTH_WRITE_ENABLE);
        Assert.Equal(0f, VulkanDepthState.MIN_DEPTH);
        Assert.Equal(1f, VulkanDepthState.MAX_DEPTH);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PipelineStateEnablesReverseZDepthAndDisablesStencil() {
        PipelineDepthStencilStateCreateInfo state = VulkanDepthState.CreatePipelineState();

        Assert.Equal(StructureType.PipelineDepthStencilStateCreateInfo, state.SType);
        Assert.NotEqual(0u, state.DepthTestEnable.Value);
        Assert.NotEqual(0u, state.DepthWriteEnable.Value);
        Assert.Equal(CompareOp.GreaterOrEqual, state.DepthCompareOp);
        Assert.Equal(0u, state.DepthBoundsTestEnable.Value);
        Assert.Equal(0u, state.StencilTestEnable.Value);
        Assert.Equal(0f, state.MinDepthBounds);
        Assert.Equal(1f, state.MaxDepthBounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PipelineStateLeavesTheStencilStatesAtTheirDefaults() {
        PipelineDepthStencilStateCreateInfo state = VulkanDepthState.CreatePipelineState();

        AssertStencilDisabled(state.Front);
        AssertStencilDisabled(state.Back);
    }

    private static void AssertStencilDisabled(StencilOpState stencil) {
        Assert.Equal(default, stencil.FailOp);
        Assert.Equal(default, stencil.PassOp);
        Assert.Equal(default, stencil.DepthFailOp);
        Assert.Equal(default, stencil.CompareOp);
        Assert.Equal(0u, stencil.CompareMask);
        Assert.Equal(0u, stencil.WriteMask);
        Assert.Equal(0u, stencil.Reference);
    }
}
