using System.Numerics;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// GPU tests for procedural mesh and texture uploads through the GPU asset bridge and the Vulkan
/// worker. The texture cases use the raw procedural level upload; the mesh cases use the fixed
/// 88-byte layout builder data.
/// </summary>
[Collection("SDL")]
public sealed class VulkanProceduralUploadTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ProceduralRgbaTextureUploadCreatesAMipMappedImage() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ProceduralTextureGpuUpload upload = CreateTextureUpload(
            TextureFormat.Rgba8Srgb,
            width: 4,
            height: 4,
            TextureFilter.Linear,
            TextureWrap.Clamp);

        GpuAssetResource resource = harness.Upload(upload);

        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(resource);
        Assert.Equal(AssetGpuUploadKind.ProceduralTexture, texture.Kind);
        Assert.Equal(Format.R8G8B8A8Srgb, texture.Format);
        Assert.Equal(4u, texture.Width);
        Assert.Equal(4u, texture.Height);
        Assert.Equal(3, texture.MipLevels);
        Assert.NotEqual(0ul, texture.Image.Handle);
        Assert.NotEqual(0ul, texture.View.Handle);
        Assert.NotEqual(0ul, texture.Sampler.Handle);
        Assert.NotEqual(0ul, texture.Memory.Handle);
        Assert.NotEqual(0, texture.Handle);
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
    public void ProceduralR8TextureUploadUsesTheLinearFormat() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ProceduralTextureGpuUpload upload = CreateTextureUpload(
            TextureFormat.R8Unorm,
            width: 2,
            height: 2,
            TextureFilter.Nearest,
            TextureWrap.Repeat);

        GpuAssetResource resource = harness.Upload(upload);

        VulkanTextureAssetResource texture = Assert.IsType<VulkanTextureAssetResource>(resource);
        Assert.Equal(Format.R8Unorm, texture.Format);
        Assert.Equal(2, texture.MipLevels);
        Assert.NotEqual(0ul, texture.Image.Handle);
        Assert.NotEqual(0ul, texture.Sampler.Handle);

        harness.Destroy(resource);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ProceduralMeshBuildUploadsBuilderDataThroughTheBridge() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ProceduralAssetService service = new(harness.Bridge);
        MeshBuilder builder = service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero),
            new Vertex(new Vector3(1f, 0f, 0f)),
            new Vertex(new Vector3(1f, 1f, 0f)),
            new Vertex(new Vector3(0f, 1f, 0f)),
        ]);
        builder.SetIndices([0, 1, 2, 0, 2, 3]);
        builder.ComputeNormals();

        Mesh mesh = harness.WaitFor(builder.BuildAsync(TestContext.Current.CancellationToken));

        try {
            VulkanMeshAssetResource resource = Assert.IsType<VulkanMeshAssetResource>(mesh.Lifetime.Resource);
            Assert.Equal(AssetGpuUploadKind.Mesh, resource.Kind);
            Assert.Equal(4ul * 88, resource.VertexBufferSize);
            Assert.Equal(6ul * sizeof(uint), resource.IndexBufferSize);
            Assert.NotEqual(0ul, resource.VertexBuffer.Handle);
            Assert.NotEqual(0ul, resource.IndexBuffer.Handle);
            Assert.NotEqual(0ul, resource.Memory.Handle);
            Assert.Equal(1, harness.Bridge.LiveResourceCount);
            Assert.Equal(0, harness.Worker.PendingOperationCount);
            Assert.Equal(0, harness.Worker.PendingStagingBufferCount);
            Assert.Equal(4, mesh.Vertices.Length);
            Assert.Equal(6, mesh.Indices.Length);
        } finally {
            mesh.Dispose();
        }

        Assert.True(
            SpinWait.SpinUntil(
                () => {
                    harness.Pump();
                    return harness.Bridge.LiveResourceCount == 0;
                },
                AssetGpuUploadHarness.Timeout),
            "The procedural mesh resource was not destroyed.");
        service.Stop();
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ProceduralTextureBuildGeneratesAndUploadsTheMipChain() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ProceduralAssetService service = new(harness.Bridge);
        TextureBuilder builder = service.CreateTextureBuilder(4, 4);
        builder.SetPixels(CreateOpaquePixels(4, 4));

        Texture texture = harness.WaitFor(builder.BuildAsync(TestContext.Current.CancellationToken));

        try {
            VulkanTextureAssetResource resource = Assert.IsType<VulkanTextureAssetResource>(texture.Lifetime.Resource);
            Assert.Equal(AssetGpuUploadKind.ProceduralTexture, resource.Kind);
            Assert.Equal(Format.R8G8B8A8Srgb, resource.Format);
            Assert.Equal(3, resource.MipLevels);
            Assert.NotEqual(0ul, resource.Image.Handle);
            Assert.NotEqual(0ul, resource.View.Handle);
            Assert.NotEqual(0ul, resource.Sampler.Handle);
            Assert.Equal(1, harness.Bridge.LiveResourceCount);
            Assert.Equal(3, texture.MipLevels);
        } finally {
            texture.Dispose();
        }

        Assert.True(
            SpinWait.SpinUntil(
                () => {
                    harness.Pump();
                    return harness.Bridge.LiveResourceCount == 0;
                },
                AssetGpuUploadHarness.Timeout),
            "The procedural texture resource was not destroyed.");
        service.Stop();
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ProceduralTextureBuildWithoutMipsUploadsOnlyTheBaseLevel() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using AssetGpuUploadHarness harness = new(host.Backend);
        ProceduralAssetService service = new(harness.Bridge);
        TextureBuilder builder = service.CreateTextureBuilder(4, 4, generateMips: false);
        builder.SetPixels(CreateOpaquePixels(4, 4));

        Texture texture = harness.WaitFor(builder.BuildAsync(TestContext.Current.CancellationToken));

        try {
            VulkanTextureAssetResource resource = Assert.IsType<VulkanTextureAssetResource>(texture.Lifetime.Resource);
            Assert.Equal(AssetGpuUploadKind.ProceduralTexture, resource.Kind);
            Assert.Equal(1, resource.MipLevels);
            Assert.NotEqual(0ul, resource.Image.Handle);
            Assert.NotEqual(0ul, resource.View.Handle);
            Assert.NotEqual(0ul, resource.Sampler.Handle);
            Assert.Equal(1, texture.MipLevels);
        } finally {
            texture.Dispose();
        }

        Assert.True(
            SpinWait.SpinUntil(
                () => {
                    harness.Pump();
                    return harness.Bridge.LiveResourceCount == 0;
                },
                AssetGpuUploadHarness.Timeout),
            "The procedural texture resource was not destroyed.");
        service.Stop();
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static ProceduralTextureGpuUpload CreateTextureUpload(
        TextureFormat format,
        int width,
        int height,
        TextureFilter filter,
        TextureWrap wrap) {
        int bytesPerPixel = format == TextureFormat.R8Unorm ? 1 : 4;
        int levelCount = LevelCount(width, height);
        int[] levelLengths = new int[levelCount];
        int totalLength = 0;
        for (int level = 0; level < levelCount; level++) {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            levelLengths[level] = levelWidth * levelHeight * bytesPerPixel;
            totalLength += levelLengths[level];
        }

        byte[] pixels = new byte[totalLength];
        for (int index = 0; index < pixels.Length; index++) {
            pixels[index] = (byte)(index * 13);
        }

        return new ProceduralTextureGpuUpload(
            "_procedural/texture/test",
            width,
            height,
            format,
            filter,
            wrap,
            1f,
            pixels,
            levelLengths);
    }

    private static byte[] CreateOpaquePixels(int width, int height) {
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4) {
            pixels[index] = (byte)(index & 0xFF);
            pixels[index + 1] = (byte)((index * 3) & 0xFF);
            pixels[index + 2] = (byte)((index * 7) & 0xFF);
            pixels[index + 3] = 255;
        }

        return pixels;
    }

    private static int LevelCount(int width, int height) {
        int levels = 1;
        int levelWidth = width;
        int levelHeight = height;
        while (levelWidth > 1 || levelHeight > 1) {
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
            levels++;
        }

        return levels;
    }
}
