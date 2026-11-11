using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// GPU tests for the device-recovery resource recreation path. They use the backend's device-loss
/// fault injection, which recreates the logical device deterministically, and the same worker instance
/// the render loop would keep.
/// </summary>
[Collection("SDL")]
public sealed class VulkanAssetRestoreTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void RestoreRecreatesEveryLiveResourceOnTheNewDevice() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(
            harness.Upload(AssetUploadTestData.Texture()));
        VulkanMeshAssetResource mesh = Assert.IsType<VulkanMeshAssetResource>(
            harness.Upload(AssetUploadTestData.Mesh()));
        VulkanShaderAssetResource shader = Assert.IsType<VulkanShaderAssetResource>(
            harness.Upload(AssetUploadTestData.Shader()));
        ulong textureImage = texture.Image.Handle;
        ulong textureView = texture.View.Handle;
        ulong textureSampler = texture.Sampler.Handle;
        ulong vertexBuffer = mesh.VertexBuffer.Handle;
        ulong indexBuffer = mesh.IndexBuffer.Handle;
        ulong vertexModule = shader.VertexModule.Handle;
        ulong fragmentModule = shader.FragmentModule.Handle;
        int generation = host.Backend.DeviceGeneration;

        Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));
        Assert.Equal(generation + 1, host.Backend.DeviceGeneration);

        // The harness keeps the worker created before the recovery; it must resolve the recreated
        // device on its own.
        harness.Restore();

        Assert.Equal(generation + 1, texture.DeviceGeneration);
        Assert.Equal(generation + 1, mesh.DeviceGeneration);
        Assert.Equal(generation + 1, shader.DeviceGeneration);
        Assert.NotEqual(textureImage, texture.Image.Handle);
        Assert.NotEqual(textureView, texture.View.Handle);
        Assert.NotEqual(textureSampler, texture.Sampler.Handle);
        Assert.NotEqual(vertexBuffer, mesh.VertexBuffer.Handle);
        Assert.NotEqual(indexBuffer, mesh.IndexBuffer.Handle);
        Assert.NotEqual(vertexModule, shader.VertexModule.Handle);
        Assert.NotEqual(fragmentModule, shader.FragmentModule.Handle);
        Assert.NotEqual(0ul, texture.Image.Handle);
        Assert.NotEqual(0ul, mesh.VertexBuffer.Handle);
        Assert.NotEqual(0ul, shader.FragmentModule.Handle);
        Assert.Equal(3, harness.Bridge.LiveResourceCount);

        harness.Destroy(texture);
        harness.Destroy(mesh);
        harness.Destroy(shader);

        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DestroySkipsAResourceThatBelongsToADestroyedDevice() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Texture());

        Assert.True(host.Backend.Recover(RenderRecoveryReason.DeviceLost));

        // The resource was never restored, so its handles belong to the device that was just
        // destroyed. Destroying it must not touch the recreated device with stale handles.
        harness.Destroy(resource);

        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }
}
