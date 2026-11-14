using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the geometry pipeline cache key: the polygon mode is part of the key, so a wireframe
/// draw can never reuse the filled pipeline over the same shader, render state, and topology.
/// </summary>
public sealed class GeometryPipelineKeyTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PipelineKeySeparatesTheFilledAndWireframePolygonModes() {
        RenderState state = RenderState.Opaque;
        VulkanGeometryPipelineCache.PipelineKey filled = new(
            null!,
            state,
            PrimitiveTopology.TriangleList,
            Wireframe: false);
        VulkanGeometryPipelineCache.PipelineKey wireframe = new(
            null!,
            state,
            PrimitiveTopology.TriangleList,
            Wireframe: true);

        Assert.NotEqual(filled, wireframe);
        Assert.Equal(
            filled,
            new VulkanGeometryPipelineCache.PipelineKey(
                null!,
                state,
                PrimitiveTopology.TriangleList,
                Wireframe: false));
        Assert.Equal(
            wireframe,
            new VulkanGeometryPipelineCache.PipelineKey(
                null!,
                state,
                PrimitiveTopology.TriangleList,
                Wireframe: true));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PipelineKeyKeepsTheRenderStateAndTopologyParts() {
        VulkanGeometryPipelineCache.PipelineKey baseline = new(
            null!,
            RenderState.Opaque,
            PrimitiveTopology.TriangleList,
            Wireframe: false);

        Assert.NotEqual(
            baseline,
            new VulkanGeometryPipelineCache.PipelineKey(
                null!,
                RenderState.Additive,
                PrimitiveTopology.TriangleList,
                Wireframe: false));
        Assert.NotEqual(
            baseline,
            new VulkanGeometryPipelineCache.PipelineKey(
                null!,
                RenderState.Opaque,
                PrimitiveTopology.LineList,
                Wireframe: false));
    }
}
