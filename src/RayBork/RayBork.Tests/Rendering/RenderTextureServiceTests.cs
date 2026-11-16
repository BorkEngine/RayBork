using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the render texture request handoff, cancellation, lifetime, and shutdown behavior. The
/// fake renderer completes the requests without a GPU, exactly as the render loop services them.
/// </summary>
public sealed class RenderTextureServiceTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CreateCompletesOnlyAfterTheRenderThreadProcessesTheRequest() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();

        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(64, 32, TextureFormat.Rgba16Float, Depth: true, SampleCount: 4),
            CancellationToken.None);
        Assert.False(request.IsCompleted);

        host.ProcessRenderTextureRequests(service);
        Assert.True(request.IsCompletedSuccessfully);
        using RenderTexture target = await request;

        Assert.Equal(64, target.Width);
        Assert.Equal(32, target.Height);
        Assert.Equal(4, target.SampleCount);
        Assert.True(target.IsHdr);
        Assert.False(target.IsDisposed);
        Assert.Equal(string.Empty, target.ColorTexture.Path);
        Assert.False(target.ColorTexture.IsProcedural);
        Assert.Equal(1, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationBeforeProcessingCompletesTheRequestAsCanceled() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        using CancellationTokenSource cancellation = new();

        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(8, 8),
            cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        host.ProcessRenderTextureRequests(service);
        Assert.Equal(0, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseFailsPendingRequestsAndRejectsLaterRequests() {
        using RenderTextureService service = new();

        Task<RenderTexture> pending = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        service.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None));
        Assert.True(service.IsStopped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFrameHoldKeepsTheTargetUntilTheFrameReleasesIt() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> request = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;

        target.Lifetime.AddFrameHold();
        target.Dispose();
        host.ProcessRenderTextureRequests(service);
        Assert.Equal(1, host.LiveRenderTextureCount);

        target.Lifetime.ReleaseFrameHold();
        host.ProcessRenderTextureRequests(service);
        Assert.Equal(0, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TheRenderTextureOwnsTheColorViewAndDisposeIsIdempotent() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> request = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(target.ColorTexture.Dispose);
        Assert.Contains("RenderTexture", exception.Message, StringComparison.Ordinal);
        Assert.False(target.ColorTexture.IsDisposed);

        // A material that bound the view can release its reference after the render texture was
        // disposed; retirement makes the view unusable without trapping the delayed internal release.
        target.ColorTexture.AddOwnerReference();
        target.Dispose();
        target.Dispose();
        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);
        target.ColorTexture.ReleaseOwnerReference();
        Assert.Equal(string.Empty, target.ColorTexture.Path);
        Assert.Throws<ObjectDisposedException>(() => _ = target.ColorTexture.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADescriptionIsValidatedBeforeTheRequestIsQueued() {
        using RenderTextureService service = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = service.CreateAsync(new RenderTextureDescription(0, 8), CancellationToken.None); });
        Assert.Throws<ArgumentException>(
            () => {
                _ = service.CreateAsync(
                    new RenderTextureDescription(8, 8, TextureFormat.Bc7Srgb),
                    CancellationToken.None);
            });
        Assert.False(service.HasPendingWork);
    }
}
