using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// GPU tests for shader uploads through the GPU asset bridge and the Vulkan worker.
/// </summary>
[Collection("SDL")]
public sealed class VulkanShaderUploadTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UploadCreatesBothShaderModules() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);

        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Shader());

        VulkanShaderAssetResource shader = Assert.IsType<VulkanShaderAssetResource>(resource);
        Assert.Equal(AssetGpuUploadKind.Shader, shader.Kind);
        Assert.NotEqual(0ul, shader.VertexModule.Handle);
        Assert.NotEqual(0ul, shader.FragmentModule.Handle);
        Assert.NotEqual(shader.VertexModule.Handle, shader.FragmentModule.Handle);
        Assert.Equal(unchecked((long)shader.VertexModule.Handle), shader.Handle);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);
        Assert.Equal(0, harness.Worker.PendingOperationCount);

        harness.Destroy(resource);

        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BeginUploadCompletesImmediatelyForShaderModules() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);

        VulkanAssetUploadOperation operation = Assert.IsType<VulkanAssetUploadOperation>(
            harness.Worker.BeginUpload(AssetUploadTestData.Shader()));

        Assert.False(operation.IsFinished);

        // A shader upload records no GPU copy, so the first poll returns the modules without waiting.
        bool completed = harness.Worker.TryComplete(operation, out GpuAssetResource? resource, out Exception? error);

        Assert.True(completed);
        Assert.Null(error);
        VulkanShaderAssetResource shader = Assert.IsType<VulkanShaderAssetResource>(resource);
        Assert.NotEqual(0ul, shader.VertexModule.Handle);
        Assert.NotEqual(0ul, shader.FragmentModule.Handle);
        Assert.Equal(0, harness.Worker.PendingOperationCount);

        harness.Worker.Destroy(shader);

        Assert.Equal(0, harness.Worker.PendingOperationCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ModuleThatIsNotWordAlignedFailsTheUploadWithRB1008() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ShaderGpuUpload broken = AssetUploadTestData.Shader(vertexSpirv: new byte[] { 1, 2, 3 });

        Task<GpuAssetResource> task = harness.Bridge.UploadAsync(broken, CancellationToken.None);
        harness.Bridge.ProcessPending(harness.Worker);

        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => task.GetAwaiter().GetResult());
        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Contains("32-bit words", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }
}
