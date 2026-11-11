using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// GPU tests for packed texture uploads through the GPU asset bridge and the Vulkan worker.
/// </summary>
[Collection("SDL")]
public sealed class VulkanTextureUploadTests {
    [Theory]
    [InlineData(TextureFormat.Rgba8Unorm, Format.R8G8B8A8Unorm)]
    [InlineData(TextureFormat.Rgba8Srgb, Format.R8G8B8A8Srgb)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UploadCreatesImageWithTheDescriptorFormat(TextureFormat format, Format expectedFormat) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);

        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Texture(format));

        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(resource);
        Assert.Equal(AssetGpuUploadKind.Texture, texture.Kind);
        Assert.Equal(expectedFormat, texture.Format);
        Assert.Equal(4u, texture.Width);
        Assert.Equal(4u, texture.Height);
        Assert.Equal(3, texture.MipLevels);
        Assert.NotEqual(0ul, texture.Image.Handle);
        Assert.NotEqual(0ul, texture.View.Handle);
        Assert.NotEqual(0ul, texture.Sampler.Handle);
        Assert.NotEqual(0ul, texture.Memory.Handle);
        Assert.NotEqual(0, texture.Handle);
        Assert.Equal(1, harness.Bridge.LiveResourceCount);

        harness.Destroy(resource);
        harness.Worker.Destroy(resource);

        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void Bc7UploadCreatesTheBlockCompressedImageWhenSupported() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        if (!host.Device.Capabilities.TextureCompressionBc) {
            Assert.Skip("The device does not support textureCompressionBC.");
        }

        using AssetGpuUploadHarness harness = new(host.Backend);

        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Texture(TextureFormat.Bc7Srgb));

        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(resource);
        Assert.Equal(Format.BC7SrgbBlock, texture.Format);
        Assert.Equal(3, texture.MipLevels);
        Assert.NotEqual(0ul, texture.Image.Handle);
        harness.Destroy(resource);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UploadWithAnisotropicSamplerFallsBackToLinearCleanly() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);

        GpuAssetResource resource = harness.Upload(AssetUploadTestData.Texture(
            filter: TextureFilter.Anisotropic,
            maxAnisotropy: 16f));

        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(resource);
        Assert.NotEqual(0ul, texture.Sampler.Handle);
        harness.Destroy(resource);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void MalformedKtx2FailsTheUploadWithRB1008() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        TextureGpuUpload valid = AssetUploadTestData.Texture();
        byte[] truncated = valid.Ktx2Data.ToArray()[..^8];
        TextureGpuUpload broken = new(valid.Path, valid.Texture, truncated);

        Task<GpuAssetResource> task = harness.Bridge.UploadAsync(broken, CancellationToken.None);
        harness.Bridge.ProcessPending(harness.Worker);

        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => task.GetAwaiter().GetResult());
        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void RenderTargetTextureFormatFailsTheUploadWithRB1008() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        TextureGpuUpload upload = new(
            "textures/render-target",
            new TextureAssetDescriptor {
                Payload = "textures/render-target-ktx2",
                Width = 4,
                Height = 4,
                MipLevels = 3,
                Format = TextureFormat.Rgba16Float,
                IsSrgb = false,
                Filter = TextureFilter.Linear,
                Wrap = TextureWrap.Repeat,
                MaxAnisotropy = 1f,
            },
            Ktx2TestWriter.Write(TextureFormat.Rgba8Unorm, 4, 4));

        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => harness.Worker.Upload(upload));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, failure.DiagnosticCode);
        Assert.Contains("render-target", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }
}
