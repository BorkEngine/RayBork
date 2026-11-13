using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the M4 model loader: every <see cref="IAssetCatalog.Load{T}"/> call returns a new
/// <see cref="Model"/> wrapper with its own materials over the shared cached part meshes and programs.
/// </summary>
public sealed class AssetCatalogModelTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadModelReturnsANewWrapperWithItsOwnMaterials() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Model first = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        Model second = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);

        try {
            Assert.NotSame(first, second);

            ModelPart firstPart = Assert.Single(first.Parts);
            ModelPart secondPart = Assert.Single(second.Parts);
            Assert.Equal("body", firstPart.Name);
            Assert.Equal("/meshes/quad", firstPart.Mesh.Path);
            Assert.Equal("/shaders/basic", firstPart.Shader.Path);
            Assert.Same(firstPart.Mesh, secondPart.Mesh);
            // Distinct materials over one cached program: two model loads never share a material.
            Assert.NotSame(firstPart.Shader, secondPart.Shader);
            Assert.Same(firstPart.Shader.Program, secondPart.Shader.Program);

            // Bounds are computed from the mesh bounds transformed by the part-local transform, and
            // the model bounds are the union of the part bounds.
            Assert.Equal(
                new BoundingBox(new Vector3(0f, 0f, 0f), new Vector3(2f, 4f, 6f)),
                firstPart.Bounds);
            Assert.Equal(firstPart.Bounds, first.Bounds);
            Assert.Equal(new Transform(new Vector3(1f, 2f, 3f)), firstPart.LocalTransform);

            // One mesh upload and one program upload are shared by both loads.
            Assert.Equal(2, harness.Worker.UploadCallCount);
            Assert.False(first.IsDisposed);
            Assert.False(second.IsDisposed);
        } finally {
            first.Dispose();
            second.Dispose();
        }

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "Disposing both models releases the shared mesh and the shared program exactly once each.");
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ModelDisposeReleasesEveryDistinctPartAssetOnce() {
        using CatalogTestHarness harness = new(
            userPacks: [BuildModelPack(CreateDescriptor(partCount: 2, shareParts: true))]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Model model = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        ModelPart first = model.Parts[0];
        ModelPart second = model.Parts[1];

        Assert.Same(first.Mesh, second.Mesh);
        // Both parts name the same program path, but each part owns its own material over the one
        // cached program.
        Assert.NotSame(first.Shader, second.Shader);
        Assert.Same(first.Shader.Program, second.Shader.Program);
        Assert.Equal(first.Shader.Path, second.Shader.Path);

        model.Dispose();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "The shared mesh and the one shared program are released exactly once each.");
        Assert.True(first.Mesh.IsDisposed);
        Assert.True(first.Shader.IsDisposed);
        Assert.True(second.Shader.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TwoModelsSharingPartsEachReleaseTheirOwnReference() {
        PackBuilder builder = CatalogTestData.CreateStandardBuilder();
        builder.Add("models/castle", AssetEntryType.Model, AssetSerialization.Serialize(CreateDescriptor()));
        builder.Add("models/tower", AssetEntryType.Model, AssetSerialization.Serialize(CreateDescriptor()));
        using CatalogTestHarness harness = new(userPacks: [builder.Build()]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Model castle = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        Model tower = await harness.Catalog.Load<Model>("/models/tower", cancellationToken);
        Mesh sharedMesh = castle.Parts[0].Mesh;
        Shader castleMaterial = castle.Parts[0].Shader;
        Shader towerMaterial = tower.Parts[0].Shader;
        Assert.Same(sharedMesh, tower.Parts[0].Mesh);
        Assert.NotSame(castleMaterial, towerMaterial);
        Assert.Same(castleMaterial.Program, towerMaterial.Program);
        Assert.NotSame(castle, tower);

        castle.Dispose();
        Assert.False(sharedMesh.IsDisposed);
        // The castle's own material is disposed with the model. The tower's material shares the same
        // program but not the castle's material reference, so it stays usable.
        Assert.True(castleMaterial.IsDisposed);
        Assert.False(towerMaterial.IsDisposed);
        Assert.Empty(harness.Worker.DestroyCalls);

        tower.Dispose();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "The last model reference destroys the shared mesh and the shared program.");
        Assert.True(sharedMesh.IsDisposed);
        Assert.True(towerMaterial.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PartWithAnEmptyShaderPathFailsTheLoadWithoutAPartUpload() {
        using CatalogTestHarness harness = new(
            userPacks: [BuildModelPack(CreateDescriptor(shaderPath: ""))]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Model>(
                "/models/castle",
                TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Contains("has no shader path", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AFailedModelLoadReleasesThePartReferencesItAlreadyAcquired() {
        using CatalogTestHarness harness = new(
            userPacks: [BuildModelPack(CreateDescriptor(shaderPath: "shaders/missing"))]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        AssetNotFoundException thrown = await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.Load<Model>("/models/castle", cancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_NOT_FOUND, thrown.DiagnosticCode);

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 1,
            "The model load must release the mesh reference it acquired before the shader failed.");

        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/quad", cancellationToken);
        try {
            Assert.False(mesh.IsDisposed);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AnAlreadyCanceledModelLoadAcquiresNoPart() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Catalog.Load<Model>("/models/castle", cancellation.Token));

        Assert.Equal(0, harness.Worker.UploadCallCount);
        Assert.Empty(harness.Worker.DestroyCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CancellationAfterPartAcquisitionReleasesTheAcquiredParts() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        harness.Worker.DeferUploads = true;
        using CancellationTokenSource cancellation = new();

        Task<Model> load = harness.Catalog.Load<Model>("/models/castle", cancellation.Token);

        // The mesh upload starts first. Complete it so the model load acquires the mesh reference and
        // moves on to the part material's program upload.
        FakeGpuAssetUploadOperation meshUpload = harness.Worker.WaitForUpload(0, _timeout);
        meshUpload.Complete();
        FakeGpuAssetUploadOperation shaderUpload = harness.Worker.WaitForUpload(1, _timeout);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);

        // The canceled load released the mesh reference. The abandoned program upload destroys its
        // resource when it completes, because its only waiter canceled.
        shaderUpload.Complete();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "Cancellation must release the acquired mesh and the material's program reference.");
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        harness.Worker.DeferUploads = false;
        Model retry = await harness.Catalog.Load<Model>(
            "/models/castle",
            TestContext.Current.CancellationToken);
        try {
            Assert.Single(retry.Parts);
        } finally {
            retry.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadModelOnAMeshEntryThrowsAssetTypeMismatch() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetTypeMismatchException thrown = await Assert.ThrowsAsync<AssetTypeMismatchException>(
            () => harness.Catalog.Load<Model>("/meshes/quad", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_TYPE_MISMATCH, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AMissingModelPathThrowsAssetNotFound() {
        using CatalogTestHarness harness = new(userPacks: [CatalogTestData.BuildStandardPack()]);

        AssetNotFoundException thrown = await Assert.ThrowsAsync<AssetNotFoundException>(
            () => harness.Catalog.Load<Model>("/models/absent", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_NOT_FOUND, thrown.DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadModelAfterTheCatalogStoppedThrowsObjectDisposed() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        harness.Catalog.Stop();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => harness.Catalog.Load<Model>("/models/castle", TestContext.Current.CancellationToken));
    }

    public static TheoryData<string> InvalidDescriptors => new() {
        "empty-parts",
        "null-parts",
        "empty-name",
        "non-canonical-mesh",
        "empty-shader",
        "non-canonical-shader",
        "short-transform",
        "non-finite-transform",
        "short-bounds",
        "non-finite-bounds",
        "inverted-bounds",
        "invalid-model-bounds",
    };

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [MemberData(nameof(InvalidDescriptors))]
    public async Task InvalidModelDescriptorsFailAsAssetLoadWithoutAPartUpload(string variant) {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateInvalidDescriptor(variant))]);

        AssetLoadException thrown = await Assert.ThrowsAsync<AssetLoadException>(
            () => harness.Catalog.Load<Model>("/models/castle", TestContext.Current.CancellationToken));

        Assert.Equal(DiagnosticCodes.ASSET_LOAD_FAILED, thrown.DiagnosticCode);
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    private static ModelAssetDescriptor CreateDescriptor(
        int partCount = 1,
        bool shareParts = true,
        string shaderPath = CatalogTestData.SHADER_PATH) {
        ModelPartAsset[] parts = new ModelPartAsset[partCount];
        for (int index = 0; index < partCount; index++) {
            parts[index] = CreatePart(
                name: shareParts ? "body" : $"body-{index}",
                meshPath: shareParts ? CatalogTestData.MESH_PATH : $"meshes/quad-{index}",
                shaderPath: shaderPath);
        }

        return new ModelAssetDescriptor {
            Parts = parts,
            Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
        };
    }

    private static ModelPartAsset CreatePart(
        string name = "body",
        string meshPath = CatalogTestData.MESH_PATH,
        string shaderPath = CatalogTestData.SHADER_PATH)
        => new() {
            Name = name,
            MeshPath = meshPath,
            ShaderPath = shaderPath,
            LocalTransform = [1f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f, 1f],
            Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
        };

    private static ModelAssetDescriptor CreateInvalidDescriptor(string variant) {
        ModelPartAsset valid = CreatePart();
        return variant switch {
            "empty-parts" => new ModelAssetDescriptor { Parts = [], Bounds = [-1f, -2f, -3f, 1f, 2f, 3f] },
            "null-parts" => new ModelAssetDescriptor { Parts = null!, Bounds = [-1f, -2f, -3f, 1f, 2f, 3f] },
            "empty-name" => new ModelAssetDescriptor {
                Parts = [CreatePart(name: " ")],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "non-canonical-mesh" => new ModelAssetDescriptor {
                Parts = [CreatePart(meshPath: "/meshes/quad")],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "empty-shader" => new ModelAssetDescriptor {
                Parts = [CreatePart(shaderPath: "")],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "non-canonical-shader" => new ModelAssetDescriptor {
                Parts = [CreatePart(shaderPath: "/shaders/basic")],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "short-transform" => new ModelAssetDescriptor {
                Parts = [
                    new ModelPartAsset {
                        Name = valid.Name,
                        MeshPath = valid.MeshPath,
                        ShaderPath = valid.ShaderPath,
                        LocalTransform = [0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f],
                        Bounds = valid.Bounds,
                    },
                ],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "non-finite-transform" => new ModelAssetDescriptor {
                Parts = [
                    new ModelPartAsset {
                        Name = valid.Name,
                        MeshPath = valid.MeshPath,
                        ShaderPath = valid.ShaderPath,
                        LocalTransform = [float.NaN, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f],
                        Bounds = valid.Bounds,
                    },
                ],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "short-bounds" => new ModelAssetDescriptor {
                Parts = [
                    new ModelPartAsset {
                        Name = valid.Name,
                        MeshPath = valid.MeshPath,
                        ShaderPath = valid.ShaderPath,
                        LocalTransform = valid.LocalTransform,
                        Bounds = [-1f, -2f, -3f, 1f, 2f],
                    },
                ],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "non-finite-bounds" => new ModelAssetDescriptor {
                Parts = [
                    new ModelPartAsset {
                        Name = valid.Name,
                        MeshPath = valid.MeshPath,
                        ShaderPath = valid.ShaderPath,
                        LocalTransform = valid.LocalTransform,
                        Bounds = [-1f, float.NaN, -3f, 1f, 2f, 3f],
                    },
                ],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "inverted-bounds" => new ModelAssetDescriptor {
                Parts = [
                    new ModelPartAsset {
                        Name = valid.Name,
                        MeshPath = valid.MeshPath,
                        ShaderPath = valid.ShaderPath,
                        LocalTransform = valid.LocalTransform,
                        Bounds = [1f, -2f, -3f, -1f, 2f, 3f],
                    },
                ],
                Bounds = [-1f, -2f, -3f, 1f, 2f, 3f],
            },
            "invalid-model-bounds" => new ModelAssetDescriptor {
                Parts = [valid],
                Bounds = [1f, -2f, -3f, -1f, 2f, 3f],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown descriptor variant."),
        };
    }

    private static byte[] BuildModelPack(ModelAssetDescriptor descriptor) {
        PackBuilder builder = CatalogTestData.CreateStandardBuilder();
        builder.Add("models/castle", AssetEntryType.Model, AssetSerialization.Serialize(descriptor));
        return builder.Build();
    }
}
