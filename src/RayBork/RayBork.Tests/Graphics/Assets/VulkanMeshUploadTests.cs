using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// GPU tests for mesh uploads through the GPU asset bridge and the Vulkan worker.
/// </summary>
[Collection("SDL")]
public sealed class VulkanMeshUploadTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UploadCreatesVertexAndIndexBuffers() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);

        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Mesh());

        VulkanMeshAssetResource mesh = Assert.IsType<VulkanMeshAssetResource>(resource);
        Assert.Equal(AssetGpuUploadKind.Mesh, mesh.Kind);
        Assert.Equal(3ul * 88, mesh.VertexBufferSize);
        Assert.Equal(3ul * sizeof(uint), mesh.IndexBufferSize);
        Assert.NotEqual(0ul, mesh.VertexBuffer.Handle);
        Assert.NotEqual(0ul, mesh.IndexBuffer.Handle);
        Assert.NotEqual(0ul, mesh.Memory.Handle);
        Assert.Equal(unchecked((long)mesh.VertexBuffer.Handle), mesh.Handle);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);

        harness.Destroy(resource);

        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void PayloadLengthMismatchFailsTheUploadWithRB1008() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        MeshGpuUpload broken = AssetUploadTestData.Mesh(lengthDelta: -1);

        Task<GpuAssetResource> task = harness.Bridge.UploadAsync(broken, CancellationToken.None);
        harness.Bridge.ProcessPending(harness.Worker);

        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => task.GetAwaiter().GetResult());
        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Contains("require", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }
}
