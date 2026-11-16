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

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResizeCompletesOnlyAfterTheRenderThreadProcessesTheRequest() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(
            new RenderTextureDescription(64, 64),
            CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task request = service.ResizeAsync(target.Lifetime, 96, 48);
        Assert.False(request.IsCompleted);
        Assert.True(service.HasPendingWork);
        Assert.True(service.Signal.WaitOne(0));

        host.ProcessRenderTextureRequests(service);
        await request;
        Assert.Equal(96, target.Width);
        Assert.Equal(48, target.Height);
        Assert.Equal(96, target.ColorTexture.Width);
        Assert.Equal(48, target.ColorTexture.Height);
        Assert.Equal(96, target.DepthTexture!.Width);
        Assert.Equal(48, target.DepthTexture!.Height);
        Assert.False(service.HasPendingWork);
        Assert.Equal(1, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PublicResizeAfterStopReportsObjectDisposedWithoutTouchingTheTarget() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        service.Stop();

        Assert.False(target.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => target.Resize(96, 48));
        Assert.Equal(64, target.Width);
        Assert.Equal(64, target.Height);
        Assert.Equal(64, target.ColorTexture.Width);
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResizeOfAnUnchangedSizeIsANoOpEvenWhileTheTargetIsHeld() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task unchanged = service.ResizeAsync(target.Lifetime, 64, 64);
        Assert.True(unchanged.IsCompletedSuccessfully);
        Assert.False(service.HasPendingWork);

        target.Lifetime.AddFrameHold();
        try {
            Task held = service.ResizeAsync(target.Lifetime, 64, 64);
            Assert.True(held.IsCompletedSuccessfully);
        } finally {
            target.Lifetime.ReleaseFrameHold();
        }

        Assert.Equal(64, target.Width);
        Assert.Equal(64, target.Height);
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResizeRejectsInvalidDimensionsBeforeItQueuesTheRequest() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.ResizeAsync(target.Lifetime, 0, 8); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = service.ResizeAsync(target.Lifetime, 8, -1); });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = service.ResizeAsync(target.Lifetime, RenderTextureValidation.MAX_DIMENSION + 1, 8); });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { _ = service.ResizeAsync(target.Lifetime, 8, RenderTextureValidation.MAX_DIMENSION + 1); });
        Assert.False(service.HasPendingWork);
        Assert.Equal(64, target.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResizeOfADisposedTargetThrowsObjectDisposedException() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        RenderTexture target = await create;
        target.Dispose();
        host.ProcessRenderTextureRequests(service);

        Assert.Throws<ObjectDisposedException>(() => { _ = service.ResizeAsync(target.Lifetime, 96, 48); });
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResizeOfATargetInUseThrowsInvalidOperationExceptionBeforeQueueing() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        target.Lifetime.AddFrameHold();
        try {
            Assert.Throws<InvalidOperationException>(() => { _ = service.ResizeAsync(target.Lifetime, 96, 48); });
        } finally {
            target.Lifetime.ReleaseFrameHold();
        }

        Assert.False(service.HasPendingWork);
        Assert.Equal(64, target.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFrameHoldThatAppearsAfterQueueingFailsTheResizeOnTheRenderThread() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task request = service.ResizeAsync(target.Lifetime, 96, 48);
        target.Lifetime.AddFrameHold();
        try {
            host.ProcessRenderTextureRequests(service);
            await Assert.ThrowsAsync<InvalidOperationException>(() => request);
        } finally {
            target.Lifetime.ReleaseFrameHold();
        }

        Assert.Equal(64, target.Width);
        Assert.Equal(64, target.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseFailsPendingResizesAndRejectsLaterResizes() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task pending = service.ResizeAsync(target.Lifetime, 96, 48);
        service.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ResizeAsync(target.Lifetime, 128, 64));
        Assert.False(service.HasPendingWork);
        Assert.True(service.IsStopped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopFailsPendingResizesAndRejectsLaterResizes() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(64, 64), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task pending = service.ResizeAsync(target.Lifetime, 96, 48);
        service.Stop();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ResizeAsync(target.Lifetime, 128, 64));
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReadbackCompletesOnlyAfterTheRenderThreadProcessesTheRequest() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(4, 2), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task<Color[,]> colorsRequest = service.ReadColorsAsync(target.Lifetime, CancellationToken.None);
        Task<byte[]> pixelsRequest = service.ReadPixelsAsync(target.Lifetime, CancellationToken.None);
        Assert.False(colorsRequest.IsCompleted);
        Assert.False(pixelsRequest.IsCompleted);
        Assert.True(service.HasPendingWork);
        Assert.True(service.Signal.WaitOne(0));

        host.ProcessRenderTextureRequests(service);

        Color[,] colors = await colorsRequest;
        byte[] pixels = await pixelsRequest;
        Assert.Equal(2, colors.GetLength(0));
        Assert.Equal(4, colors.GetLength(1));
        Assert.Equal(4 * 2 * 4, pixels.Length);
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationBeforeProcessingCompletesReadbacksAsCanceled() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        using CancellationTokenSource cancellation = new();

        Task<Color[,]> colors = service.ReadColorsAsync(target.Lifetime, cancellation.Token);
        Task<byte[]> pixels = service.ReadPixelsAsync(target.Lifetime, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => colors);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pixels);
        host.ProcessRenderTextureRequests(service);
        Assert.False(service.HasPendingWork);
        Assert.Equal(1, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task APreCanceledReadbackTokenDoesNotQueueWork() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Task<Color[,]> request = service.ReadColorsAsync(target.Lifetime, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFailedReadbackKeepsTheTargetAndItsViewsUnchanged() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(
            new RenderTextureDescription(32, 16, TextureFormat.Rgba8Srgb, Depth: true, SampleCount: 1),
            CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        RenderTextureDescription before = target.Lifetime.Description;
        Texture colorView = target.ColorTexture;
        Texture depthView = Assert.IsType<Texture>(target.DepthTexture);

        host.ReadbackException = new GraphicsException(
            "The fake readback failed.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
        Task<byte[]> request = service.ReadPixelsAsync(target.Lifetime, CancellationToken.None);
        host.ProcessRenderTextureRequests(service);

        await Assert.ThrowsAsync<GraphicsException>(() => request);
        Assert.True(host.IsRenderTargetLive(target.TargetId));
        Assert.Equal(before, target.Lifetime.Description);
        Assert.Same(colorView, target.ColorTexture);
        Assert.Same(depthView, target.DepthTexture);
        Assert.Equal(32, colorView.Width);
        Assert.Equal(16, depthView.Height);
        Assert.Equal(1, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReadbackDoesNotMutateTheTargetDescriptionOrViews() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(
            new RenderTextureDescription(8, 4, TextureFormat.Rgba8Unorm, Depth: true, SampleCount: 2),
            CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        RenderTextureDescription before = target.Lifetime.Description;
        int sampleCount = target.SampleCount;
        long targetId = target.TargetId;
        int liveCount = host.LiveRenderTextureCount;

        Task<Color[,]> colors = service.ReadColorsAsync(target.Lifetime, CancellationToken.None);
        Task<byte[]> pixels = service.ReadPixelsAsync(target.Lifetime, CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        await colors;
        await pixels;

        Assert.Equal(before, target.Lifetime.Description);
        Assert.Equal(targetId, target.TargetId);
        Assert.Equal(sampleCount, target.SampleCount);
        Assert.Equal(8, target.Width);
        Assert.Equal(4, target.Height);
        Assert.Equal(liveCount, host.LiveRenderTextureCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReadbackOfADisposedTargetFailsWithObjectDisposed() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        RenderTexture target = await create;
        target.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => { _ = target.ReadColorsAsync(TestContext.Current.CancellationToken); });
        Assert.Throws<ObjectDisposedException>(
            () => { _ = target.ReadPixelsAsync(TestContext.Current.CancellationToken); });
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ReadColorsAsync(target.Lifetime, CancellationToken.None));
        host.ProcessRenderTextureRequests(service);
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnUnknownTargetIdFailsTheReadbackClearly() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        RenderTextureTargetLifetime orphan = new(
            424242,
            new RenderTextureDescription(8, 8),
            1,
            static _ => { });

        Task<Color[,]> request = service.ReadColorsAsync(orphan, CancellationToken.None);
        host.ProcessRenderTextureRequests(service);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => request);
        Assert.Contains("424242", exception.Message, StringComparison.Ordinal);
        Assert.Contains("no longer alive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationAfterTheRequestIsDequeuedCompletesTheTaskAsCanceled() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;
        using CancellationTokenSource cancellation = new();

        Task<byte[]> request = service.ReadPixelsAsync(target.Lifetime, cancellation.Token);
        RenderTextureService.ReadbackRequest[] drained = service.DrainReadbacks();
        Assert.Single(drained);
        await cancellation.CancelAsync();

        // The render thread would still finish its copy and restore the layout, but the canceled task is
        // already completed and the result is discarded.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        drained[0].DisposeRegistration();
        Assert.True(host.IsRenderTargetLive(target.TargetId));
        Assert.Equal(8, target.Width);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseFailsPendingReadbacksAndRejectsLaterReadbacks() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task<byte[]> pending = service.ReadPixelsAsync(target.Lifetime, CancellationToken.None);
        service.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ReadColorsAsync(target.Lifetime, CancellationToken.None));
        Assert.False(service.HasPendingWork);
        Assert.True(service.IsStopped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopFailsPendingReadbacksAndRejectsLaterReadbacks() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(8, 8), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task<Color[,]> pending = service.ReadColorsAsync(target.Lifetime, CancellationToken.None);
        service.Stop();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ReadPixelsAsync(target.Lifetime, CancellationToken.None));
        Assert.False(service.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task QueuedCreatesResizesAndDestroysAreServicedInOneRenderThreadRound() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> resizedRequest = service.CreateAsync(
            new RenderTextureDescription(16, 16),
            CancellationToken.None);
        Task<RenderTexture> destroyedRequest = service.CreateAsync(
            new RenderTextureDescription(8, 8),
            CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture resized = await resizedRequest;
        RenderTexture destroyed = await destroyedRequest;

        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(32, 32), CancellationToken.None);
        Task resize = service.ResizeAsync(resized.Lifetime, 48, 24);
        destroyed.Dispose();
        host.ProcessRenderTextureRequests(service);

        using RenderTexture created = await create;
        await resize;
        Assert.Equal(48, resized.Width);
        Assert.Equal(24, resized.Height);
        Assert.True(host.IsRenderTargetLive(resized.TargetId));
        Assert.True(host.IsRenderTargetLive(created.TargetId));
        Assert.False(host.IsRenderTargetLive(destroyed.TargetId));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReadbackInTheSameRoundAsACreateResizeAndDestroyIsServiced() {
        using RenderTextureService service = new();
        using FakeRendererBackend host = new();
        Task<RenderTexture> create = service.CreateAsync(new RenderTextureDescription(16, 16), CancellationToken.None);
        host.ProcessRenderTextureRequests(service);
        using RenderTexture target = await create;

        Task resize = service.ResizeAsync(target.Lifetime, 32, 8);
        Task<Color[,]> readback = service.ReadColorsAsync(target.Lifetime, CancellationToken.None);
        host.ProcessRenderTextureRequests(service);

        await resize;
        Color[,] colors = await readback;
        Assert.Equal(8, colors.GetLength(0));
        Assert.Equal(32, colors.GetLength(1));
        Assert.True(host.IsRenderTargetLive(target.TargetId));
    }
}
