using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for camera routing into a render texture: the target viewport clamping, the
/// <see cref="Camera.Target"/> value, and the disposed-target rejection.
/// </summary>
public sealed class RenderTextureCameraTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ACameraTargetsTheRenderTextureAndClampsToTheTargetSize() {
        using RenderTextureService service = new();
        using FakeRendererBackend renderer = new();
        RenderTexture target = await CreateTargetAsync(service, renderer, new RenderTextureDescription(64, 32));
        try {
            using CameraTestHost host = new(1280, 720);
            host.BeginFrame();
            using (Camera fullViewport = host.Service.CreateCamera(
                Transform.Identity,
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null,
                target)) {
                Assert.Same(target, fullViewport.Target);
                Assert.Equal(new Rect(0f, 0f, 64f, 32f), fullViewport.Viewport);
            }

            using (Camera clamped = host.Service.CreateCamera(
                Transform.Identity,
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                new Rect(-10f, -10f, 1280f, 720f),
                target)) {
                Assert.Equal(new Rect(0f, 0f, 64f, 32f), clamped.Viewport);
            }

            using (Camera subset = host.Service.CreateCamera(
                Transform.Identity,
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                new Rect(8f, 4f, 20f, 10f),
                target)) {
                Assert.Equal(new Rect(8f, 4f, 20f, 10f), subset.Viewport);
            }

            // The packet retained the target for the recorded frame.
            Assert.True(host.Packet.Retention.Count > 0);
            host.Packet.Retention.Release();
        } finally {
            target.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ADisposedTargetIsRejectedWhenACameraIsCreated() {
        using RenderTextureService service = new();
        using FakeRendererBackend renderer = new();
        RenderTexture target = await CreateTargetAsync(service, renderer, new RenderTextureDescription(16, 16));
        target.Dispose();

        using CameraTestHost host = new(64, 64);
        host.BeginFrame();
        Assert.Throws<ObjectDisposedException>(() => host.Service.CreateCamera(
            Transform.Identity,
            CameraProjection.Perspective,
            60f,
            0f,
            ClearMode.ColorAndDepth,
            null,
            0.1f,
            100f,
            null,
            target));
    }

    private static Task<RenderTexture> CreateTargetAsync(
        RenderTextureService service,
        FakeRendererBackend renderer,
        RenderTextureDescription description) {
        Task<RenderTexture> request = service.CreateAsync(description, CancellationToken.None);
        renderer.ProcessRenderTextureRequests(service);
        return request;
    }
}
