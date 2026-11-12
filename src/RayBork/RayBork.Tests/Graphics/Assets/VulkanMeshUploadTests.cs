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
        Assert.Equal(0, harness.Worker.PendingOperationCount);
        Assert.Equal(0, harness.Worker.PendingStagingBufferCount);

        harness.Destroy(resource);

        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BeginUploadIsPendingUntilTheMeshCopyCompletes() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        MeshGpuUpload upload = AssetUploadTestData.Mesh();

        VulkanAssetUploadOperation operation = Assert.IsType<VulkanAssetUploadOperation>(
            harness.Worker.BeginUpload(upload));

        Assert.False(operation.IsFinished);
        Assert.Equal(1, harness.Worker.PendingOperationCount);
        Assert.Equal(1, harness.Worker.PendingStagingBufferCount);

        GpuAssetResource? resource = null;
        Exception? error = null;
        bool completed = SpinWait.SpinUntil(
            () => harness.Worker.TryComplete(operation, out resource, out error),
            AssetGpuUploadHarness.Timeout);

        Assert.True(completed, "The mesh upload did not complete.");
        Assert.Null(error);
        VulkanMeshAssetResource mesh = Assert.IsType<VulkanMeshAssetResource>(resource);
        Assert.NotEqual(0ul, mesh.VertexBuffer.Handle);
        Assert.NotEqual(0ul, mesh.IndexBuffer.Handle);
        Assert.Equal(0, harness.Worker.PendingOperationCount);
        Assert.Equal(0, harness.Worker.PendingStagingBufferCount);

        harness.Worker.Destroy(mesh);

        Assert.Equal(0, harness.Worker.PendingOperationCount);
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
