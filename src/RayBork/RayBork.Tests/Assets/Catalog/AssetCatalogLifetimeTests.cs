using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetCatalogLifetimeTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposeKeepsPathAndIsDisposedReadableAndRejectsMetadata() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken);

        mesh.Dispose();

        Assert.True(mesh.IsDisposed);
        Assert.Equal("/meshes/quad", mesh.Path);
        Assert.Throws<ObjectDisposedException>(() => mesh.VertexCount);
        Assert.Throws<ObjectDisposedException>(() => mesh.IndexCount);
        Assert.Throws<ObjectDisposedException>(() => mesh.Bounds);
        Assert.Throws<ObjectDisposedException>(() => mesh.Topology);
        Assert.Throws<ObjectDisposedException>(() => mesh.Dispose());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposingTheSameSharedObjectTwiceReleasesBothOwnersAsDocumented() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Mesh first = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        Mesh second = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);

        first.Dispose();

        Assert.False(first.IsDisposed);
        Assert.Equal(3, second.VertexCount);

        // The engine cannot distinguish a second owner's release from a double Dispose of the same
        // object, so this releases the remaining reference and disposes the shared asset.
        first.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => first.Dispose());
        Assert.Throws<ObjectDisposedException>(() => second.Dispose());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FrameHoldDefersGpuDestructionUntilTheLastHoldIsReleased() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken);
        EngineAsset lifetime = mesh.Lifetime;

        lifetime.AddFrameHold();
        mesh.Dispose();

        Assert.True(mesh.IsDisposed);
        Assert.Empty(harness.Worker.DestroyCalls);

        lifetime.ReleaseFrameHold();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The released frame hold must queue exactly one GPU destruction.");
        Assert.True(harness.Worker.DestroyCalls[0].IsDestroyed);
        Assert.Throws<InvalidOperationException>(() => lifetime.ReleaseFrameHold());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FrameHoldCannotBeAddedToADisposedAsset() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken);
        EngineAsset lifetime = mesh.Lifetime;

        mesh.Dispose();

        Assert.Throws<ObjectDisposedException>(() => lifetime.AddFrameHold());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentLoadsCoalesceToOneUploadAndEachAcquireAReference() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        using ManualResetEventSlim uploadStarted = new(false);
        using ManualResetEventSlim releaseUpload = new(false);
        harness.Worker.UploadHandler = upload => {
            uploadStarted.Set();
            Assert.True(
                releaseUpload.Wait(_timeout),
                "The test never released the coalesced upload.");
            return new FakeGpuAssetResource(upload, harness.Worker.NextHandle());
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<Mesh> firstLoad = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        Assert.True(uploadStarted.Wait(_timeout, cancellationToken));
        Task<Mesh> secondLoad = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        releaseUpload.Set();

        Mesh first = await firstLoad.WaitAsync(_timeout, cancellationToken);
        Mesh second = await secondLoad.WaitAsync(_timeout, cancellationToken);

        Assert.Same(first, second);
        Assert.Equal(1, harness.Worker.UploadCallCount);

        first.Dispose();
        Assert.False(second.IsDisposed);
        second.Dispose();
        Assert.True(first.IsDisposed);

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The last released reference must queue one GPU destruction.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledWaiterAcquiresNoReferenceAndDoesNotBreakTheOtherWaiter() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        using ManualResetEventSlim uploadStarted = new(false);
        using ManualResetEventSlim releaseUpload = new(false);
        harness.Worker.UploadHandler = upload => {
            uploadStarted.Set();
            Assert.True(releaseUpload.Wait(_timeout), "The test never released the upload.");
            return new FakeGpuAssetResource(upload, harness.Worker.NextHandle());
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = new();

        Task<Mesh> canceled = harness.Catalog.Load<Mesh>("/meshes/quad", cancellation.Token);
        Assert.True(uploadStarted.Wait(_timeout, cancellationToken));
        Task<Mesh> survivor = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);

        releaseUpload.Set();
        Mesh mesh = await survivor.WaitAsync(_timeout, cancellationToken);

        Assert.Equal(1, harness.Worker.UploadCallCount);
        mesh.Dispose();
        Assert.True(mesh.IsDisposed);
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The canceled waiter must not have acquired an extra reference.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AllCanceledWaitersDestroyTheUnusedResourceAndLeaveNoCacheEntry() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        using ManualResetEventSlim uploadStarted = new(false);
        using ManualResetEventSlim releaseUpload = new(false);
        harness.Worker.UploadHandler = upload => {
            uploadStarted.Set();
            Assert.True(releaseUpload.Wait(_timeout), "The test never released the upload.");
            return new FakeGpuAssetResource(upload, harness.Worker.NextHandle());
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = new();

        Task<Mesh> canceled = harness.Catalog.Load<Mesh>("/meshes/quad", cancellation.Token);
        Assert.True(uploadStarted.Wait(_timeout, cancellationToken));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        releaseUpload.Set();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "A load whose waiters all canceled must destroy the created resource.");

        Mesh retry = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);

        try {
            Assert.Equal(2, harness.Worker.UploadCallCount);
            Assert.Equal(3, retry.VertexCount);
        } finally {
            retry.Dispose();
        }

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "The retry must be destroyed when its only owner disposes.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FailedUploadIsNotCachedAndARetrySucceeds() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        AssetLoadException uploadFailure = new(
            "The fake worker refused the upload.",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            null);
        harness.Worker.UploadException = uploadFailure;

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken));

        Assert.Same(uploadFailure, thrown);
        harness.Worker.UploadException = null;

        Mesh retry = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);

        try {
            Assert.Equal(2, harness.Worker.UploadCallCount);
            Assert.False(retry.IsDisposed);
        } finally {
            retry.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CorruptMeshPayloadFailsAsAssetLoadAndCreatesNoResource() {
        byte[] pack = CatalogTestData.BuildPack(
            (CatalogTestData.MESH_PATH, AssetEntryType.Mesh, AssetSerialization.Serialize(CatalogTestData.CreateMeshDescriptor())),
            (CatalogTestData.MESH_PAYLOAD_PATH, AssetEntryType.Raw, new byte[16]));
        using CatalogTestHarness harness = new(userPacks: [pack]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Mesh>("/meshes/quad", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
        Assert.Empty(harness.Worker.DestroyCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CustomDataShapeFailureIsAssetLoad() {
        byte[] pack = CatalogTestData.BuildPack(
            ("data/broken", AssetEntryType.Data, [0xC1]));
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [pack],
            assetTypes: [CatalogTestData.SettingsRegistration]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<CatalogTestSettings>("/data/broken", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AlreadyCanceledCustomDataCallerStillLeavesLaterCallsWorking() {
        using CatalogTestHarness harness = new(
            builtInPack: null,
            userPacks: [CatalogTestData.BuildStandardPack()],
            assetTypes: [CatalogTestData.SettingsRegistration]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Catalog.Load<CatalogTestSettings>("/data/settings", cancellation.Token));

        CatalogTestSettings settings = await harness.Catalog.Load<CatalogTestSettings>(
            "/data/settings",
            cancellationToken);
        Assert.Equal("catalog", settings.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task OneOwnerDisposeDoesNotBreakAnotherCoalescedWaiter() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        using ManualResetEventSlim uploadStarted = new(false);
        using ManualResetEventSlim releaseUpload = new(false);
        harness.Worker.UploadHandler = upload => {
            uploadStarted.Set();
            Assert.True(releaseUpload.Wait(_timeout), "The test never released the upload.");
            return new FakeGpuAssetResource(upload, harness.Worker.NextHandle());
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<Mesh> firstLoad = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        Assert.True(uploadStarted.Wait(_timeout, cancellationToken));
        Task<Mesh> secondLoad = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        releaseUpload.Set();

        Mesh first = await firstLoad.WaitAsync(_timeout, cancellationToken);
        first.Dispose();
        Mesh second = await secondLoad.WaitAsync(_timeout, cancellationToken);

        Assert.Same(first, second);
        Assert.False(second.IsDisposed);
        Assert.Equal(3, second.VertexCount);
        Assert.Equal(1, harness.Worker.UploadCallCount);

        second.Dispose();
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The surviving waiter's reference must keep the asset alive until it disposes.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ReloadAfterTheLastDisposeCreatesANewObjectAndUpload() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Mesh first = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        first.Dispose();

        Mesh second = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);

        try {
            Assert.NotSame(first, second);
            Assert.False(second.IsDisposed);
            Assert.Equal(2, harness.Worker.UploadCallCount);
        } finally {
            second.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentCacheHitAndLastDisposeNeverReturnADisposedAsset() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        for (int iteration = 0; iteration < 50; iteration++) {
            Mesh owner = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
            long destroysBefore = harness.Worker.DestroyCalls.Count;
            Mesh? loaded = null;
            Exception? loadFailure = null;
            Exception? disposeFailure = null;
            using Barrier barrier = new(participantCount: 2);

            Thread loaderThread = new(() => {
                try {
                    barrier.SignalAndWait(_timeout, cancellationToken);
                    loaded = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                } catch (Exception exception) {
                    loadFailure = exception;
                }
            }) {
                IsBackground = true,
                Name = "RayBork.Tests.AssetLoadRace",
            };
            Thread disposerThread = new(() => {
                try {
                    barrier.SignalAndWait(_timeout, cancellationToken);
                    owner.Dispose();
                } catch (Exception exception) {
                    disposeFailure = exception;
                }
            }) {
                IsBackground = true,
                Name = "RayBork.Tests.AssetDisposeRace",
            };

            loaderThread.Start();
            disposerThread.Start();
            loaderThread.Join();
            disposerThread.Join();

            Assert.Null(disposeFailure);
            int expectedDestroys;
            if (loadFailure is not null) {
                Assert.IsType<ObjectDisposedException>(loadFailure);
                Assert.True(owner.IsDisposed);
                expectedDestroys = 1;
            } else {
                Assert.NotNull(loaded);
                Assert.False(loaded!.IsDisposed);

                if (ReferenceEquals(owner, loaded)) {
                    // The cache hit won the retain race; both references belong to the same asset.
                    loaded.Dispose();
                    expectedDestroys = 1;
                } else {
                    // The dispose won and removed the cache entry before the lookup, so the load
                    // started a fresh asset. Both the old and the new resource are destroyed once.
                    Assert.True(owner.IsDisposed);
                    loaded.Dispose();
                    expectedDestroys = 2;
                }

                Assert.True(loaded.IsDisposed);
            }

            AssetCatalogLoadTests.WaitUntil(
                () => harness.Worker.DestroyCalls.Count == destroysBefore + expectedDestroys,
                "The race must destroy every resource exactly once.");
            Assert.Equal(0, harness.Bridge.LiveResourceCount);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task StopFailsPendingLoadsAndStopsTheLoaderThread() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        using ManualResetEventSlim uploadStarted = new(false);
        using ManualResetEventSlim releaseUpload = new(false);
        harness.Worker.UploadHandler = upload => {
            uploadStarted.Set();
            Assert.True(releaseUpload.Wait(_timeout), "The test never released the upload.");
            return new FakeGpuAssetResource(upload, harness.Worker.NextHandle());
        };
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Assert.Equal("RayBork.Asset", harness.Catalog.LoaderThread!.Name);
        Assert.True(harness.Catalog.LoaderThread.IsAlive);

        Task<Mesh> pending = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        Assert.True(uploadStarted.Wait(_timeout, cancellationToken));

        harness.Catalog.Stop();
        releaseUpload.Set();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.False(harness.Catalog.LoaderThread.IsAlive);
        Assert.Throws<ObjectDisposedException>(() => harness.Catalog.Exists("/meshes/quad"));
        Assert.Throws<ObjectDisposedException>(
            () => {
                _ = harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
            });
        Assert.Throws<ObjectDisposedException>(
            () => {
                _ = harness.Catalog.LoadData("/meshes/quad", cancellationToken);
            });

        harness.Catalog.Stop();
    }
}
