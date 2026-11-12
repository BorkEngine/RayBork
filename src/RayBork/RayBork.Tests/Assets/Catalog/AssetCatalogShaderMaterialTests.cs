using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the public <see cref="Shader"/> material contract through the real catalog: every
/// <see cref="IAssetCatalog.Load{T}"/> call returns a new independent material over one cached internal
/// program.
/// </summary>
public sealed class AssetCatalogShaderMaterialTests {
    private const string SHADER_PATH = "/shaders/basic";
    private const string TEXTURE_PATH = "/textures/albedo";
    private const string TINT_UNIFORM = "uTint";
    private const string TEXTURE_BINDING = "mainTexture";
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EveryLoadReturnsADistinctMaterialOverOneCachedProgram() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Shader first = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Shader second = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);

        try {
            Assert.NotSame(first, second);
            Assert.Same(first.Program, second.Program);
            Assert.Equal(SHADER_PATH, first.Path);
            Assert.Equal(SHADER_PATH, second.Path);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.IsType<ShaderGpuUpload>(harness.Worker.UploadCalls[0]);
            Assert.False(first.IsDisposed);
            Assert.False(second.IsDisposed);
        } finally {
            first.Dispose();
            second.Dispose();
        }

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The one shared program is destroyed when the last material reference is released.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposingOneMaterialLeavesTheSiblingMaterialUsable() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Shader disposedMaterial = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Shader survivingMaterial = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);

        disposedMaterial.Dispose();

        Assert.True(disposedMaterial.IsDisposed);
        Assert.False(survivingMaterial.IsDisposed);
        Assert.Empty(harness.Worker.DestroyCalls);
        OnTickThread(() => survivingMaterial.SetUniform(TINT_UNIFORM, new Vector4(0.5f, 0.25f, 0.125f, 1f)));
        Assert.Equal(new Vector4(0.5f, 0.25f, 0.125f, 1f), ReadTint(survivingMaterial));

        survivingMaterial.Dispose();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The last material release destroys the shared program exactly once.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PerMaterialUniformCursorsAreIndependent() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Shader first = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Shader second = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);

        try {
            OnTickThread(() => first.SetUniform(TINT_UNIFORM, new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));

            Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), ReadTint(first));
            Assert.Equal(new Vector4(1f, 1f, 1f, 1f), ReadTint(second));
            Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), ReadTint(first));
        } finally {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ResetUniformsRestoresTheProgramBakeDefaultsForOneMaterialOnly() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Shader first = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Shader second = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);

        try {
            OnTickThread(() => {
                first.SetUniform(TINT_UNIFORM, new Vector4(0.4f, 0.3f, 0.2f, 0.1f));
                second.SetUniform(TINT_UNIFORM, new Vector4(0.9f, 0.8f, 0.7f, 0.6f));

                first.ResetUniforms();

                Assert.Equal(new Vector4(1f, 1f, 1f, 1f), ReadTint(first));
                Assert.Equal(new Vector4(0.9f, 0.8f, 0.7f, 0.6f), ReadTint(second));
            });
        } finally {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TextureBindingsAreOwnedPerMaterialAndPerCursor() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Shader first = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Shader second = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Texture texture = await harness.Catalog.Load<Texture>(TEXTURE_PATH, cancellationToken);

        try {
            OnTickThread(() => {
                first.SetUniform(TEXTURE_BINDING, texture);
                Assert.Same(texture, first.TextureValues[0]);
                Assert.Null(second.TextureValues[0]);
            });

            texture.Dispose();
            Assert.False(texture.IsDisposed);

            first.Dispose();
            Assert.True(texture.IsDisposed);
            AssetCatalogLoadTests.WaitUntil(
                () => harness.Worker.DestroyCalls.Count == 1,
                "Disposing the material releases its texture owner reference.");
            Assert.Null(second.TextureValues[0]);
        } finally {
            if (!first.IsDisposed) {
                first.Dispose();
            }

            if (!second.IsDisposed) {
                second.Dispose();
            }

            if (!texture.IsDisposed) {
                texture.Dispose();
            }
        }

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "The program is destroyed after the last material reference is released.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ConcurrentLoadsCoalesceOneProgramUploadAndReturnOneMaterialPerCaller() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        harness.Worker.DeferUploads = true;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<Shader> firstLoad = harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        FakeGpuAssetUploadOperation upload = harness.Worker.WaitForUpload(_timeout);
        Task<Shader> secondLoad = harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        Assert.False(firstLoad.IsCompleted);

        upload.Complete();

        Shader first = await firstLoad.WaitAsync(_timeout, cancellationToken);
        Shader second = await secondLoad.WaitAsync(_timeout, cancellationToken);

        try {
            Assert.NotSame(first, second);
            Assert.Same(first.Program, second.Program);
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.False(first.IsDisposed);
            Assert.False(second.IsDisposed);
        } finally {
            first.Dispose();
            second.Dispose();
        }

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "Two materials over one coalesced program load destroy the program once.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ACanceledWaitDoesNotAcquireAMaterialAndTheRetryReturnsANewOne() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        harness.Worker.DeferUploads = true;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource cancellation = new();

        Task<Shader> canceled = harness.Catalog.Load<Shader>(SHADER_PATH, cancellation.Token);
        FakeGpuAssetUploadOperation firstUpload = harness.Worker.WaitForUpload(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(firstUpload.IsCanceled);

        firstUpload.Complete();
        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "A load whose only waiter canceled destroys the uploaded program.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        Task<Shader> retryTask = harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        FakeGpuAssetUploadOperation retryUpload = harness.Worker.WaitForUpload(1, _timeout);
        retryUpload.Complete();
        Shader retry = await retryTask.WaitAsync(_timeout, cancellationToken);

        try {
            Assert.Equal(2, harness.Worker.UploadCallCount);
            Assert.Equal(SHADER_PATH, retry.Path);
            Assert.False(retry.IsDisposed);
        } finally {
            retry.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnAlreadyCanceledCallerDoesNotGetAMaterialFromTheProgramCache() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Shader owner = await harness.Catalog.Load<Shader>(SHADER_PATH, cancellationToken);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        try {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => harness.Catalog.Load<Shader>(SHADER_PATH, cancellation.Token));
            Assert.Equal(1, harness.Worker.UploadCallCount);
            Assert.False(owner.IsDisposed);
            Assert.Empty(harness.Worker.DestroyCalls);

            // The canceled caller released its program reference, so the live material still owns the
            // program and its cursor stays usable.
            OnTickThread(() => owner.SetUniform(TINT_UNIFORM, new Vector4(0.2f, 0.4f, 0.6f, 1f)));
            Assert.Equal(new Vector4(0.2f, 0.4f, 0.6f, 1f), ReadTint(owner));
        } finally {
            owner.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadShaderOnAMeshEntryThrowsAssetTypeMismatch() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetTypeMismatchException thrown = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Shader>("/meshes/quad", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadShaderAfterTheCatalogStoppedThrowsObjectDisposed() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);
        harness.Catalog.Stop();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => harness.Catalog.Load<Shader>(SHADER_PATH, TestContext.Current.CancellationToken));
    }

    private static Vector4 ReadTint(Shader shader) {
        ShaderAbiUniform uniform = shader.Abi.Uniforms.First(candidate => candidate.Name == TINT_UNIFORM);
        ReadOnlySpan<byte> value = shader.InstanceData.Slice(
            uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE,
            uniform.Size);
        return new Vector4(
            BinaryPrimitives.ReadSingleLittleEndian(value[..4]),
            BinaryPrimitives.ReadSingleLittleEndian(value.Slice(4, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(value.Slice(8, 4)),
            BinaryPrimitives.ReadSingleLittleEndian(value.Slice(12, 4)));
    }

    private static void OnTickThread(Action action) {
        TickThread.Attach();
        try {
            action();
        } finally {
            TickThread.Detach();
        }
    }
}
