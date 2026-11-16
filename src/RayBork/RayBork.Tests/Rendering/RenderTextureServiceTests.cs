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
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        Assert.Equal(TextureFormat.Depth32Float, depthTexture.Format);
        Assert.False(depthTexture.IsSrgb);
        Assert.Equal(TextureFilter.Nearest, depthTexture.Sampler.Filter);
        Assert.Equal(TextureWrap.Clamp, depthTexture.Sampler.Wrap);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Equal(1, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DepthFalseCreatesNoDepthView() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(8, 8, TextureFormat.Rgba16Float, Depth: false),
            CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await request;

        Assert.Null(target.DepthTexture);
        Assert.NotNull(target.ColorTexture);
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
    public async Task TheRenderTextureOwnsBothViewsAndDisposeIsIdempotent() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> request = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(target.ColorTexture.Dispose);
        Assert.Contains("RenderTexture", exception.Message, StringComparison.Ordinal);
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        InvalidOperationException depthException = Assert.Throws<InvalidOperationException>(depthTexture.Dispose);
        Assert.Contains("RenderTexture", depthException.Message, StringComparison.Ordinal);
        Assert.False(target.ColorTexture.IsDisposed);
        Assert.False(depthTexture.IsDisposed);

        // A material that bound the view can release its reference after the render texture was
        // disposed; retirement makes the view unusable without trapping the delayed internal release.
        target.ColorTexture.AddOwnerReference();
        depthTexture.AddOwnerReference();
        target.Dispose();
        target.Dispose();
        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);
        Assert.True(depthTexture.IsDisposed);
        target.ColorTexture.ReleaseOwnerReference();
        depthTexture.ReleaseOwnerReference();
        Assert.Equal(string.Empty, target.ColorTexture.Path);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Throws<ObjectDisposedException>(() => _ = target.ColorTexture.Width);
        Assert.Throws<ObjectDisposedException>(() => _ = depthTexture.Width);
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

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ASampleCountChangeFromRecoveryIsVisibleOnTheRenderTexture() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> request = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await request;
        int original = target.SampleCount;
        try {
            // The backend updates the lifetime when a recreated device selects a different fallback.
            target.Lifetime.SetSampleCount(1);
            Assert.Equal(1, target.SampleCount);
        } finally {
            target.Lifetime.SetSampleCount(original);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AStoppedServiceReportsObjectDisposedBeforeDescriptionValidation() {
        using RenderTextureService service = new();
        service.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.CreateAsync(new RenderTextureDescription(0, 0), CancellationToken.None));
    }
}
