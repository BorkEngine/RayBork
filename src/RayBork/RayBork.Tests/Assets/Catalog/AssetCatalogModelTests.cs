using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

public sealed class AssetCatalogModelTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LoadModelReturnsTheSameInstanceAndResolvesEveryPart() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Model first = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        Model second = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);

        Assert.Same(first, second);
        Assert.Equal("/models/castle", first.Path);
        ModelPart part = Assert.Single(first.Parts);
        Assert.Equal("body", part.Name);
        Assert.Equal("/meshes/quad", part.Mesh.Path);
        Assert.Equal("/shaders/basic", part.Shader.Path);
        Assert.Equal(new BoundingBox(new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f)), first.Bounds);
        Assert.Equal(new Transform(new Vector3(1f, 2f, 3f)), part.LocalTransform);

        first.Dispose();
        Assert.False(first.IsDisposed);

        second.Dispose();
        Assert.True(first.IsDisposed);
        Assert.True(part.Mesh.IsDisposed);
        Assert.True(part.Shader.IsDisposed);
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
    public async Task PartWithoutAShaderPathFallsBackToTheBuiltInMeshShader() {
        PackBuilder builder = CatalogTestData.CreateStandardBuilder();
        builder.Add("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor()));
        builder.Add(
            "models/castle",
            AssetEntryType.Model,
            AssetSerialization.Serialize(CreateDescriptor(shaderPath: null)));
        using CatalogTestHarness harness = new(userPacks: [builder.Build()]);

        Model model = await harness.Catalog.Load<Model>(
            "/models/castle",
            TestContext.Current.CancellationToken);

        try {
            Assert.Equal("/shaders/mesh", model.Parts[0].Shader.Path);
        } finally {
            model.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LegacyDefaultShaderPathResolvesToTheBuiltInMeshShader() {
        PackBuilder builder = CatalogTestData.CreateStandardBuilder();
        builder.Add("shaders/mesh", AssetEntryType.Shader, AssetSerialization.Serialize(CatalogTestData.CreateShaderDescriptor()));
        builder.Add(
            "models/castle",
            AssetEntryType.Model,
            AssetSerialization.Serialize(CreateDescriptor(shaderPath: "shaders/default")));
        using CatalogTestHarness harness = new(userPacks: [builder.Build()]);

        Model model = await harness.Catalog.Load<Model>(
            "/models/castle",
            TestContext.Current.CancellationToken);

        try {
            Assert.Equal("/shaders/mesh", model.Parts[0].Shader.Path);
        } finally {
            model.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposingOneOwnerKeepsTheSharedModelAndItsPartsUsable() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Model first = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        Model second = await harness.Catalog.Load<Model>("/models/castle", cancellationToken);
        Assert.Same(first, second);

        first.Dispose();

        Assert.False(second.IsDisposed);
        Assert.False(second.Parts[0].Mesh.IsDisposed);
        Assert.False(second.Parts[0].Shader.IsDisposed);
        Assert.Empty(harness.Worker.DestroyCalls);

        second.Dispose();

        AssetCatalogLoadTests.WaitUntil(
            () => harness.Worker.DestroyCalls.Count == 2,
            "The last owner releases the distinct part references.");
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
    public async Task ModelMetadataAndDisposeFollowTheEngineAssetContract() {
        using CatalogTestHarness harness = new(userPacks: [BuildModelPack(CreateDescriptor())]);
        Model model = await harness.Catalog.Load<Model>(
            "/models/castle",
            TestContext.Current.CancellationToken);
        model.Dispose();

        Assert.Equal("/models/castle", model.Path);
        Assert.True(model.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = model.Parts);
        Assert.Throws<ObjectDisposedException>(() => _ = model.Bounds);
        Assert.Throws<ObjectDisposedException>(model.Dispose);
    }

    public static TheoryData<string> InvalidDescriptors => new() {
        "empty-parts",
        "null-parts",
        "empty-name",
        "non-canonical-mesh",
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
        string? shaderPath = CatalogTestData.SHADER_PATH) {
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
        string? shaderPath = CatalogTestData.SHADER_PATH)
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
