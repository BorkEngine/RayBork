using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

public sealed class GpuAssetBridgeTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UploadCompletesOnlyAfterProcessPending() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        TextureGpuUpload upload = CreateTextureUpload("/textures/test");

        Task<GpuAssetResource> task = bridge.UploadAsync(upload, CancellationToken.None);

        Assert.True(bridge.HasWorker);
        Assert.False(task.IsCompleted);
        Assert.True(bridge.HasPendingWork);
        Assert.Equal(1, bridge.PendingUploadCount);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.LiveResourceCount);

        bridge.ProcessPending(worker);

        GpuAssetResource resource = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Same(upload, resource.Upload);
        Assert.Equal(AssetGpuUploadKind.Texture, resource.Kind);
        Assert.NotEqual(0, resource.Handle);
        Assert.False(resource.IsDestroyed);
        Assert.Equal(1, worker.UploadCallCount);
        Assert.Same(upload, worker.UploadCalls[0]);
        Assert.Equal(1, bridge.LiveResourceCount);
        Assert.Equal(0, bridge.PendingUploadCount);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DeferredUploadStaysInFlightAcrossPollsAndCompletesOnALaterCall() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        TextureGpuUpload upload = CreateTextureUpload("/textures/test");
        Task<GpuAssetResource> task = bridge.UploadAsync(upload, CancellationToken.None);

        bridge.ProcessPending(worker);
        FakeGpuAssetUploadOperation operation = worker.WaitForUpload(_timeout);

        Assert.False(task.IsCompleted);
        Assert.True(bridge.HasPendingWork);
        Assert.Equal(0, bridge.PendingUploadCount);
        Assert.Equal(1, bridge.InFlightUploadCount);
        Assert.Equal(upload.EstimatedBytes, bridge.InFlightUploadBytes);

        bridge.ProcessPending(worker);
        Assert.False(task.IsCompleted);
        Assert.Equal(1, bridge.InFlightUploadCount);
        Assert.Equal(1, worker.UploadCallCount);

        operation.Complete();
        Assert.False(task.IsCompleted);

        bridge.ProcessPending(worker);

        GpuAssetResource resource = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Same(upload, resource.Upload);
        Assert.Equal(1, bridge.LiveResourceCount);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.InFlightUploadBytes);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DeferredUploadsRespectTheInFlightBound() {
        GpuAssetBridge bridge = new(maxInFlightUploads: 2, maxInFlightUploadBytes: 1_000_000);
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        Task<GpuAssetResource>[] tasks = [
            bridge.UploadAsync(CreateTextureUpload("/textures/a"), CancellationToken.None),
            bridge.UploadAsync(CreateTextureUpload("/textures/b"), CancellationToken.None),
            bridge.UploadAsync(CreateTextureUpload("/textures/c"), CancellationToken.None),
            bridge.UploadAsync(CreateTextureUpload("/textures/d"), CancellationToken.None),
        ];

        bridge.ProcessPending(worker);

        Assert.Equal(2, worker.UploadCallCount);
        Assert.Equal(2, bridge.InFlightUploadCount);
        Assert.Equal(2, bridge.PendingUploadCount);
        Assert.All(tasks, task => Assert.False(task.IsCompleted));

        worker.WaitForUpload(0, _timeout).Complete();
        bridge.ProcessPending(worker);

        Assert.Equal(3, worker.UploadCallCount);
        Assert.Equal(2, bridge.InFlightUploadCount);
        Assert.Equal(1, bridge.PendingUploadCount);

        worker.WaitForUpload(1, _timeout).Complete();
        worker.WaitForUpload(2, _timeout).Complete();
        bridge.ProcessPending(worker);

        Assert.Equal(4, worker.UploadCallCount);
        Assert.Equal(1, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.PendingUploadCount);

        worker.WaitForUpload(3, _timeout).Complete();
        bridge.ProcessPending(worker);
        await Task.WhenAll(tasks).WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Equal(4, bridge.LiveResourceCount);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.PendingUploadCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task InFlightUploadByteBudgetLimitsStartedUploads() {
        GpuAssetBridge bridge = new(maxInFlightUploads: 8, maxInFlightUploadBytes: 10);
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> first = bridge.UploadAsync(
            CreateTextureUpload("/textures/a", byteCount: 6),
            CancellationToken.None);
        Task<GpuAssetResource> second = bridge.UploadAsync(
            CreateTextureUpload("/textures/b", byteCount: 6),
            CancellationToken.None);

        bridge.ProcessPending(worker);

        Assert.Equal(1, worker.UploadCallCount);
        Assert.Equal(1, bridge.InFlightUploadCount);
        Assert.Equal(1, bridge.PendingUploadCount);
        Assert.Equal(6, bridge.InFlightUploadBytes);

        // The second upload starts only after the first copy finished and freed its budget.
        worker.WaitForUpload(0, _timeout).Complete();
        bridge.ProcessPending(worker);

        Assert.Equal(2, worker.UploadCallCount);
        Assert.Equal(1, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.PendingUploadCount);

        worker.WaitForUpload(1, _timeout).Complete();
        bridge.ProcessPending(worker);

        await Task.WhenAll(first, second).WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.InFlightUploadBytes);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UploadWithoutAWorkerFailsWithGraphicsException() {
        GpuAssetBridge bridge = new();

        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);

        GraphicsException failure = await Assert.ThrowsAsync<GraphicsException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED, failure.DiagnosticCode);
        Assert.Equal(0, bridge.PendingUploadCount);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AlreadyCanceledUploadIsNotQueued() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(0, bridge.PendingUploadCount);
        Assert.Equal(0, worker.UploadCallCount);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledUploadQueuedBeforeProcessingCreatesNoResource() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        using CancellationTokenSource cancellation = new();
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));

        bridge.ProcessPending(worker);

        Assert.Equal(0, worker.UploadCallCount);
        Assert.Equal(0, bridge.LiveResourceCount);
        Assert.Empty(worker.CancelCalls);
        Assert.Empty(worker.DestroyCalls);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledInFlightUploadIsCanceledAndLeavesNoLiveResource() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        using CancellationTokenSource cancellation = new();
        Task<GpuAssetResource> task = bridge.UploadAsync(
            CreateTextureUpload("/textures/test"),
            cancellation.Token);
        bridge.ProcessPending(worker);
        FakeGpuAssetUploadOperation operation = worker.WaitForUpload(_timeout);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));

        Assert.Equal(1, bridge.InFlightUploadCount);

        bridge.ProcessPending(worker);

        GpuAssetUploadOperation canceled = Assert.Single(worker.CancelCalls);
        Assert.Same(operation, canceled);
        Assert.True(operation.IsCanceled);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.LiveResourceCount);
        Assert.Empty(worker.DestroyCalls);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FailedUploadCompletesWithAssetLoadExceptionAndKeepsTheLoopRunning() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        InvalidOperationException cause = new("upload failed");
        worker.UploadHandler = upload => upload.Path == "textures/broken"
            ? throw cause
            : new FakeGpuAssetResource(upload, worker.NextHandle());
        Task<GpuAssetResource> broken = bridge.UploadAsync(CreateTextureUpload("/textures/broken"), CancellationToken.None);
        Task<GpuAssetResource> healthy = bridge.UploadAsync(CreateTextureUpload("/textures/healthy"), CancellationToken.None);

        bridge.ProcessPending(worker);

        AssetLoadException failure = await Assert.ThrowsAsync<AssetLoadException>(
            () => broken.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED, failure.DiagnosticCode);
        Assert.Same(cause, failure.InnerException);
        GpuAssetResource resource = await healthy.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal("textures/healthy", resource.Upload.Path);
        Assert.Equal(1, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DeferredUploadFailureCompletesWithAssetLoadExceptionOnALaterPoll() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        InvalidOperationException cause = new("copy failed");
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        bridge.ProcessPending(worker);
        FakeGpuAssetUploadOperation operation = worker.WaitForUpload(_timeout);

        operation.Fail(cause);
        bridge.ProcessPending(worker);

        AssetLoadException failure = await Assert.ThrowsAsync<AssetLoadException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED, failure.DiagnosticCode);
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.LiveResourceCount);
        Assert.False(bridge.HasPendingWork);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DestroyDestroysAResourceExactlyOnceOnTheRenderThreadPath() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateMeshUpload("/models/wall"), CancellationToken.None);
        bridge.ProcessPending(worker);
        GpuAssetResource resource = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        bridge.Destroy(resource);
        bridge.Destroy(resource);

        Assert.Equal(1, bridge.PendingDestroyCount);
        Assert.Equal(0, bridge.LiveResourceCount);

        bridge.ProcessPending(worker);

        Assert.Single(worker.DestroyCalls);
        Assert.Same(resource, worker.DestroyCalls[0]);
        Assert.True(resource.IsDestroyed);
        Assert.Equal(0, bridge.PendingDestroyCount);

        bridge.ProcessPending(worker);
        Assert.Single(worker.DestroyCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DestroyOfAnUnknownResourceIsIgnored() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);

        bridge.Destroy(new FakeGpuAssetResource(CreateTextureUpload("/textures/test"), 7));
        bridge.ProcessPending(worker);

        Assert.Empty(worker.DestroyCalls);
        Assert.Equal(0, bridge.PendingDestroyCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseCompletesPendingUploadsWithTheFailureAndDestroysLiveResources() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> liveTask = bridge.UploadAsync(CreateMeshUpload("/models/wall"), CancellationToken.None);
        bridge.ProcessPending(worker);
        GpuAssetResource live = await liveTask.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Task<GpuAssetResource> pending = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        GraphicsException failure = new("render loop stopped", DiagnosticCodes.GRAPHICS_UNRECOVERABLE, null);

        bridge.Close(failure);

        GraphicsException observed = await Assert.ThrowsAsync<GraphicsException>(
            () => pending.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Same(failure, observed);
        GpuAssetResource destroyed = Assert.Single(worker.DestroyCalls);
        Assert.Same(live, destroyed);
        Assert.True(bridge.IsClosed);
        Assert.Equal(0, bridge.LiveResourceCount);
        Assert.Equal(0, bridge.PendingUploadCount);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.False(bridge.HasPendingWork);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => bridge.UploadAsync(CreateTextureUpload("/textures/late"), CancellationToken.None)
                .WaitAsync(_timeout, TestContext.Current.CancellationToken));

        bridge.Destroy(live);
        bridge.Close(null);
        Assert.Single(worker.DestroyCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseCancelsInFlightUploadsWithoutHanging() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        bridge.ProcessPending(worker);
        FakeGpuAssetUploadOperation operation = worker.WaitForUpload(_timeout);

        bridge.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => task.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Same(operation, Assert.Single(worker.CancelCalls));
        Assert.True(operation.IsCanceled);
        Assert.Equal(0, bridge.InFlightUploadCount);
        Assert.Equal(0, bridge.LiveResourceCount);
        Assert.False(bridge.HasPendingWork);

        bridge.Close(null);
        Assert.Single(worker.CancelCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseWithoutFailureCompletesPendingUploadsWithObjectDisposedException() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> pending = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);

        bridge.Close(null);

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pending.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Empty(worker.DestroyCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task RestoreRecreatesEveryLiveResourceFromItsRetainedUpload() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        TextureGpuUpload textureUpload = CreateTextureUpload("/textures/test");
        MeshGpuUpload meshUpload = CreateMeshUpload("/models/wall");
        Task<GpuAssetResource> textureTask = bridge.UploadAsync(textureUpload, CancellationToken.None);
        Task<GpuAssetResource> meshTask = bridge.UploadAsync(meshUpload, CancellationToken.None);
        bridge.ProcessPending(worker);
        GpuAssetResource texture = await textureTask.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        GpuAssetResource mesh = await meshTask.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        long textureHandle = texture.Handle;
        long meshHandle = mesh.Handle;

        bridge.Restore(worker);

        Assert.Equal(2, worker.RestoreCallCount);
        Assert.Contains(worker.RestoreCalls, upload => ReferenceEquals(upload, textureUpload));
        Assert.Contains(worker.RestoreCalls, upload => ReferenceEquals(upload, meshUpload));
        Assert.NotEqual(textureHandle, texture.Handle);
        Assert.NotEqual(meshHandle, mesh.Handle);
        Assert.Same(textureUpload, texture.Upload);
        Assert.Same(meshUpload, mesh.Upload);
        Assert.Equal(2, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task RestoreAdoptsTheHandlesOfAFreshResource() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        bridge.ProcessPending(worker);
        GpuAssetResource live = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        worker.RestoreHandler = (upload, _) => new FakeGpuAssetResource(upload, 9001);

        bridge.Restore(worker);

        Assert.Equal(9001, live.Handle);
        Assert.False(live.IsDestroyed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FailedRestoreThrowsAssetLoadExceptionWithRb1010() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        bridge.ProcessPending(worker);
        await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        worker.RestoreException = new InvalidOperationException("recreate failed");

        AssetLoadException failure = Assert.Throws<AssetLoadException>(() => bridge.Restore(worker));

        Assert.Equal(DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED, failure.DiagnosticCode);
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(1, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MultipleQueuedUploadsCompleteIndependently() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource>[] tasks = [
            bridge.UploadAsync(CreateTextureUpload("/textures/a"), CancellationToken.None),
            bridge.UploadAsync(CreateMeshUpload("/models/b"), CancellationToken.None),
            bridge.UploadAsync(CreateShaderUpload("/shaders/c"), CancellationToken.None),
        ];

        bridge.ProcessPending(worker);

        GpuAssetResource[] resources = await Task.WhenAll(tasks).WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(3, worker.UploadCallCount);
        Assert.Equal(3, resources.Select(resource => resource.Handle).Distinct().Count());
        Assert.Equal(3, bridge.LiveResourceCount);
        Assert.Equal(
            new[] { AssetGpuUploadKind.Texture, AssetGpuUploadKind.Mesh, AssetGpuUploadKind.Shader },
            resources.Select(resource => resource.Kind));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UploadQueueIsBounded() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource>[] tasks = new Task<GpuAssetResource>[GpuAssetBridge.MAX_PENDING_UPLOADS];
        for (int i = 0; i < tasks.Length; i++) {
            tasks[i] = bridge.UploadAsync(CreateTextureUpload($"/textures/queued{i}"), CancellationToken.None);
        }

        Task<GpuAssetResource> overflow = bridge.UploadAsync(CreateTextureUpload("/textures/overflow"), CancellationToken.None);

        AssetLoadException failure = await Assert.ThrowsAsync<AssetLoadException>(
            () => overflow.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED, failure.DiagnosticCode);
        Assert.Equal(GpuAssetBridge.MAX_PENDING_UPLOADS, bridge.PendingUploadCount);

        bridge.ProcessPending(worker);
        GpuAssetResource[] resources = await Task.WhenAll(tasks).WaitAsync(_timeout, TestContext.Current.CancellationToken);
        Assert.Equal(GpuAssetBridge.MAX_PENDING_UPLOADS, resources.Length);
        Assert.Equal(GpuAssetBridge.MAX_PENDING_UPLOADS, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationAfterCompletionLeavesTheResourceLive() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        using CancellationTokenSource cancellation = new();
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), cancellation.Token);
        bridge.ProcessPending(worker);
        GpuAssetResource resource = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);

        cancellation.Cancel();
        bridge.ProcessPending(worker);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Same(resource, await task);
        Assert.Empty(worker.DestroyCalls);
        Assert.Empty(worker.CancelCalls);
        Assert.Equal(1, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CloseDestroysResourcesThatWereQueuedForDestruction() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new();
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);
        bridge.ProcessPending(worker);
        GpuAssetResource resource = await task.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        bridge.Destroy(resource);

        bridge.Close(null);

        GpuAssetResource destroyed = Assert.Single(worker.DestroyCalls);
        Assert.Same(resource, destroyed);
        Assert.Equal(0, bridge.PendingDestroyCount);
        Assert.Equal(0, bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AttachWorkerTwiceThrows() {
        GpuAssetBridge bridge = new();
        bridge.AttachWorker(new FakeAssetGpuWorker());

        Assert.Throws<InvalidOperationException>(() => bridge.AttachWorker(new FakeAssetGpuWorker()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HasPendingWorkTracksQueuedAndInFlightUploads() {
        GpuAssetBridge bridge = new();
        FakeAssetGpuWorker worker = new() { DeferUploads = true };
        bridge.AttachWorker(worker);
        Task<GpuAssetResource> task = bridge.UploadAsync(CreateTextureUpload("/textures/test"), CancellationToken.None);

        Assert.True(bridge.HasPendingWork);

        bridge.ProcessPending(worker);
        FakeGpuAssetUploadOperation operation = worker.WaitForUpload(_timeout);
        Assert.True(bridge.HasPendingWork);

        operation.Complete();
        Assert.True(bridge.HasPendingWork);

        bridge.ProcessPending(worker);
        Assert.False(bridge.HasPendingWork);
        Assert.True(task.IsCompletedSuccessfully);
    }

    private static TextureGpuUpload CreateTextureUpload(string path, int byteCount = 4) => new(
        path,
        new TextureAssetDescriptor {
            Payload = "payloads/ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Srgb,
            IsSrgb = true,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Repeat,
            MaxAnisotropy = 1f,
        },
        new byte[byteCount]);

    private static MeshGpuUpload CreateMeshUpload(string path) => new(
        path,
        new MeshAssetDescriptor {
            Payload = "payloads/mesh",
            VertexCount = 3,
            IndexCount = 3,
            Bounds = new float[6],
            Topology = PrimitiveTopology.TriangleList,
        },
        new byte[] { 5, 6, 7, 8 });

    private static ShaderGpuUpload CreateShaderUpload(string path) => new(
        path,
        new ShaderAssetDescriptor {
            VertexPayload = "payloads/vert",
            FragmentPayload = "payloads/frag",
            VertexEntry = "main_vertex",
            FragmentEntry = "main_fragment",
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = [],
            Textures = [],
        },
        new byte[] { 9, 10, 11, 12 },
        new byte[] { 13, 14, 15, 16 });
}
