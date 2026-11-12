using System.Numerics;
using RayBork.Bake;
using RayBork.Bake.Incremental;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake.Models;

/// <summary>Tests for the built-in model processor and its glTF/OBJ readers.</summary>
public sealed class ModelProcessorTests {
    private const float TOLERANCE = 1e-4f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ObjGroupsBecomePartsWithTriangulatedFacesAndComputedNormals() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/shapes.obj", ModelTestData.TWO_GROUP_OBJ);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/shapes");
        Assert.Equal(2, model.Parts.Length);
        Assert.Equal(new[] { "first", "second" }, model.Parts.Select(part => part.Name));
        Assert.Equal("models/shapes/first", model.Parts[0].MeshPath);
        Assert.Equal("models/shapes/second", model.Parts[1].MeshPath);
        Assert.Equal("shaders/default", model.Parts[0].ShaderPath);
        AssertBounds([-3f, 0f, 0f, 0f, 1f, 0f], model.Bounds);

        MeshAssetDescriptor first = ModelTestData.ReadMesh(archive, "models/shapes/first");
        Assert.Equal(PrimitiveTopology.TriangleList, first.Topology);
        Assert.Equal(3u, first.VertexCount);
        Assert.Equal(3u, first.IndexCount);
        byte[] firstPayload = ModelTestData.ReadPayload(archive, "models/shapes/first-mesh");
        Assert.Equal((3 * 88) + (3 * 4), firstPayload.Length);
        AssertIndices([0u, 1u, 2u], firstPayload, 3);
        AssertBounds([-1f, 0f, 0f, 0f, 1f, 0f], first.Bounds);
        AssertVector(new Vector3(0f, 0f, -1f), ModelTestData.NormalAt(firstPayload, 0));

        MeshAssetDescriptor second = ModelTestData.ReadMesh(archive, "models/shapes/second");
        Assert.Equal(6u, second.VertexCount);
        Assert.Equal(6u, second.IndexCount);
        byte[] secondPayload = ModelTestData.ReadPayload(archive, "models/shapes/second-mesh");
        Assert.Equal((6 * 88) + (6 * 4), secondPayload.Length);
        AssertIndices([0u, 1u, 2u, 3u, 4u, 5u], secondPayload, 6);
        AssertBounds([-3f, 0f, 0f, -2f, 1f, 0f], second.Bounds);

        AssertVector(Vector2.Zero, ModelTestData.UvAt(firstPayload, 0));
        AssertVector(Vector4.One, ModelTestData.ColorAt(firstPayload, 0));
        AssertVector(Vector4.Zero, ModelTestData.TangentAt(firstPayload, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScaleAndFrontCorrectionApplyBeforeBoundsAndPayloads() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/correction.obj", ModelTestData.SINGLE_TRIANGLE_OBJ);
        workspace.WriteAsset("models/correction.toml", """
            [model]
            scale = 2.0
            front = "-x"
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/correction");
        ModelPartAsset part = Assert.Single(model.Parts);
        Assert.Equal("correction", part.Name);
        AssertBounds([-2f, 0f, 0f, 0f, 2f, 2f], part.Bounds);
        AssertBounds([-2f, 0f, 0f, 0f, 2f, 2f], model.Bounds);
        AssertIdentityTransform(part.LocalTransform);

        MeshAssetDescriptor mesh = ModelTestData.ReadMesh(archive, part.MeshPath);
        AssertBounds([-2f, 0f, 0f, 0f, 2f, 2f], mesh.Bounds);
        byte[] payload = ModelTestData.ReadPayload(archive, mesh.Payload);
        AssertVector(new Vector3(0f, 0f, 2f), ModelTestData.PositionAt(payload, 0));
        AssertVector(new Vector3(0f, 2f, 0f), ModelTestData.PositionAt(payload, 1));
        AssertVector(new Vector3(-2f, 0f, 0f), ModelTestData.PositionAt(payload, 2));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData("-z", 0f, 0f, 0f, 1f, 1f, 1f)]
    [InlineData("+z", -1f, 0f, -1f, 0f, 1f, 0f)]
    [InlineData("+x", 0f, 0f, -1f, 1f, 1f, 0f)]
    [InlineData("-x", -1f, 0f, 0f, 0f, 1f, 1f)]
    [InlineData("+y", 0f, 0f, -1f, 1f, 1f, 0f)]
    [InlineData("-y", 0f, -1f, 0f, 1f, 0f, 1f)]
    public void FrontAxisCorrectionMapsTheAuthoredForwardAxisToNegativeZ(
        string front,
        float minimumX,
        float minimumY,
        float minimumZ,
        float maximumX,
        float maximumY,
        float maximumZ) {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/axes.obj", """
            v 1 0 0
            v 0 1 0
            v 0 0 1
            f 1 2 3
            """);
        workspace.WriteAsset("models/axes.toml", $"""
            [model]
            front = "{front}"
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/axes");
        AssertBounds([minimumX, minimumY, minimumZ, maximumX, maximumY, maximumZ], model.Bounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicatePartNamesGetDeterministicNumericSuffixes() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/dupes.obj", """
            o cube
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            o cube
            v 2 0 0
            v 3 0 0
            v 2 1 0
            f 4 5 6
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/dupes");
        Assert.Equal(new[] { "cube", "cube-2" }, model.Parts.Select(part => part.Name));
        Assert.Equal("models/dupes/cube", model.Parts[0].MeshPath);
        Assert.Equal("models/dupes/cube-2", model.Parts[1].MeshPath);
        Assert.Equal("models/dupes/cube-2-mesh", ModelTestData.ReadMesh(archive, model.Parts[1].MeshPath).Payload);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void APartSlugCannotCollideWithAnotherPartPayload() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/collision.obj", """
            o foo
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            o foo-mesh
            v 2 0 0
            v 3 0 0
            v 2 1 0
            f 4 5 6
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/collision");
        Assert.Equal(new[] { "foo", "foo-mesh-2" }, model.Parts.Select(part => part.Name));
        Assert.Equal("models/collision/foo-mesh-2", model.Parts[1].MeshPath);
        Assert.Equal(
            "models/collision/foo-mesh",
            ModelTestData.ReadMesh(archive, model.Parts[0].MeshPath).Payload);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ObjWithoutFacesIsAnError() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/empty.obj", """
            v 0 0 0
            v 1 0 0
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4009", result.Error);
        Assert.Contains("no faces", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ObjNegativeIndicesAndIgnoredMaterialLibraryAreSupported() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/negative.obj", """
            mtllib materials.mtl
            o negative
            vt 0 0
            vt 1 0
            vt 1 1
            v 0 0 0
            v 1 0 0
            v 1 1 0
            f -3/-3 -2/-2 -1/-1
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        Assert.Contains("material library", result.Error);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/negative");
        ModelPartAsset part = Assert.Single(model.Parts);
        Assert.Equal("negative", part.Name);
        byte[] payload = ModelTestData.ReadPayload(
            archive,
            ModelTestData.ReadMesh(archive, part.MeshPath).Payload);
        AssertVector(new Vector2(0f, 1f), ModelTestData.UvAt(payload, 0));
        AssertVector(new Vector2(1f, 1f), ModelTestData.UvAt(payload, 1));
        AssertVector(new Vector2(1f, 0f), ModelTestData.UvAt(payload, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ObjQuadFlipsVToTheTopLeftUvOrigin() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/uvquad.obj", """
            o quad
            v 0 0 0
            v 0 1 0
            v 1 1 0
            v 1 0 0
            vt 0 0
            vt 0 1
            vt 1 1
            vt 1 0
            vn 0 0 1
            f 1/1/1 2/2/1 3/3/1 4/4/1
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/uvquad");
        ModelPartAsset part = Assert.Single(model.Parts);
        MeshAssetDescriptor mesh = ModelTestData.ReadMesh(archive, part.MeshPath);
        Assert.Equal(4u, mesh.VertexCount);
        byte[] payload = ModelTestData.ReadPayload(archive, mesh.Payload);
        AssertIndices([0u, 1u, 2u, 0u, 2u, 3u], payload, 4);
        AssertVector(new Vector2(0f, 1f), ModelTestData.UvAt(payload, 0));
        AssertVector(new Vector2(0f, 0f), ModelTestData.UvAt(payload, 1));
        AssertVector(new Vector2(1f, 0f), ModelTestData.UvAt(payload, 2));
        AssertVector(new Vector2(1f, 1f), ModelTestData.UvAt(payload, 3));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GltfTextureCoordinatesAreUsedAsAuthored() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/uvtri.gltf", ModelTestData.UvTriangleGltf());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/uvtri");
        ModelPartAsset part = Assert.Single(model.Parts);
        byte[] payload = ModelTestData.ReadPayload(
            archive,
            ModelTestData.ReadMesh(archive, part.MeshPath).Payload);
        AssertVector(new Vector2(0.25f, 0.75f), ModelTestData.UvAt(payload, 0));
        AssertVector(new Vector2(0.5f, 0.5f), ModelTestData.UvAt(payload, 1));
        AssertVector(new Vector2(0.75f, 0.25f), ModelTestData.UvAt(payload, 2));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData("[model]\nscale = 0\n", "RB4009")]
    [InlineData("[model]\nscale = \"big\"\n", "RB4002")]
    [InlineData("[model]\nfront = \"up\"\n", "RB4009")]
    [InlineData("[model]\nbogus = 1\n", "RB4002")]
    [InlineData("[model]\nshader = \"/shaders/castle\"\n", "RB4009")]
    public void InvalidSidecarValuesAreReported(string sidecar, string code) {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/prop.obj", ModelTestData.SINGLE_TRIANGLE_OBJ);
        workspace.WriteAsset("models/prop.toml", sidecar);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(code, result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PartWithoutASidecarShaderDefaultsToTheBuiltInShader() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/plain.obj", ModelTestData.TWO_GROUP_OBJ);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/plain");
        Assert.All(model.Parts, part => Assert.Equal("shaders/default", part.ShaderPath));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ShaderPathIsStoredOnEveryPartAndMayReferenceAnotherPack() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/armed.obj", ModelTestData.TWO_GROUP_OBJ);
        workspace.WriteAsset("models/armed.toml", """
            [model]
            shader = "shaders/castle"
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/armed");
        Assert.All(model.Parts, part => Assert.Equal("shaders/castle", part.ShaderPath));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MixedCaseSourceAndPartNamesMapToCanonicalOutputPaths() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("Models/Prop.OBJ", """
            o Wall Panel
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/prop");
        ModelPartAsset part = Assert.Single(model.Parts);
        Assert.Equal("wall-panel", part.Name);
        Assert.Equal("models/prop/wall-panel", part.MeshPath);
        Assert.Equal("models/prop/wall-panel-mesh", ModelTestData.ReadMesh(archive, part.MeshPath).Payload);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmbeddedGltfBufferBakesOnePartWithItsNodeTransform() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/tri.gltf", ModelTestData.EmbeddedTriangleGltf());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/tri");
        ModelPartAsset part = Assert.Single(model.Parts);
        Assert.Equal("trimesh", part.Name);
        Assert.Equal("models/tri/trimesh", part.MeshPath);
        Assert.Equal("shaders/default", part.ShaderPath);
        AssertTransform([1f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f, 1f], part.LocalTransform);
        AssertBounds([0f, 2f, 3f, 1f, 3f, 3f], part.Bounds);
        AssertBounds([0f, 2f, 3f, 1f, 3f, 3f], model.Bounds);

        MeshAssetDescriptor mesh = ModelTestData.ReadMesh(archive, part.MeshPath);
        Assert.Equal(3u, mesh.VertexCount);
        Assert.Equal(3u, mesh.IndexCount);
        Assert.Equal(PrimitiveTopology.TriangleList, mesh.Topology);
        AssertBounds([-1f, 0f, 0f, 0f, 1f, 0f], mesh.Bounds);
        byte[] payload = ModelTestData.ReadPayload(archive, mesh.Payload);
        Assert.Equal((3 * 88) + (3 * 4), payload.Length);
        AssertIndices([0u, 1u, 2u], payload, 3);
        AssertVector(new Vector3(-1f, 0f, 0f), ModelTestData.PositionAt(payload, 1));
        AssertVector(Vector3.UnitY, ModelTestData.NormalAt(payload, 0));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GlbSourceIsReadFromItsBinaryChunk() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAssetBytes(
            "models/tri.glb",
            ModelTestData.BuildGlb(ModelTestData.GlbJson(), ModelTestData.TriangleBufferData()));
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/tri");
        ModelPartAsset part = Assert.Single(model.Parts);
        Assert.Equal("trimesh", part.Name);
        AssertTransform([1f, 2f, 3f, 0f, 0f, 0f, 1f, 1f, 1f, 1f], part.LocalTransform);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NodeChainIsComposedIntoThePartTransform() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/chain.gltf", ModelTestData.NodeChainGltf());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult result = workspace.Run(application);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ModelAssetDescriptor model = ModelTestData.ReadModel(archive, "models/chain");
        ModelPartAsset part = Assert.Single(model.Parts);
        AssertTransform([1f, 2f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f], part.LocalTransform);
        AssertBounds([0f, 2f, 0f, 1f, 3f, 0f], part.Bounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExternalGltfBufferIsATrackedInputAndAMissingBufferFails() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("models/external.gltf", ModelTestData.ExternalBufferGltf("triangle.bin"));
        string bufferPath = workspace.WriteAssetBytes(
            "models/triangle.bin",
            ModelTestData.TriangleBufferData());
        BakeApplication application = workspace.Builder().UseDefaultProcessors().Build();

        BakeRunResult first = workspace.Run(application);

        Assert.True(first.ExitCode == 0, first.Error + first.Output);
        BakeManifest? manifest = BakeManifestStore.TryRead(workspace.ManifestPath);
        Assert.NotNull(manifest);
        BakeManifestSource source = Assert.Single(manifest.Packs[0].Sources,
            source => source.Id == "models/external.gltf");
        Assert.Contains(source.Inputs, input => input.Path == "Assets/models/triangle.bin");

        File.Delete(bufferPath);
        BakeRunResult second = workspace.Run(
            application,
            "bake",
            "--output-root",
            workspace.OutputRoot,
            "--force");

        Assert.Equal(1, second.ExitCode);
        Assert.Contains("RB4009", second.Error);
        Assert.Contains("triangle.bin", second.Error);
    }

    private static void AssertIndices(uint[] expected, byte[] payload, int vertexCount) {
        var actual = new uint[expected.Length];
        for (int index = 0; index < expected.Length; index++) {
            actual[index] = ModelTestData.IndexAt(payload, vertexCount, index);
        }

        Assert.Equal(expected, actual);
    }

    private static void AssertBounds(float[] expected, float[] actual) {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++) {
            Assert.InRange(actual[index], expected[index] - TOLERANCE, expected[index] + TOLERANCE);
        }
    }

    private static void AssertVector(Vector2 expected, Vector2 actual) {
        Assert.InRange(actual.X, expected.X - TOLERANCE, expected.X + TOLERANCE);
        Assert.InRange(actual.Y, expected.Y - TOLERANCE, expected.Y + TOLERANCE);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual) {
        Assert.InRange(actual.X, expected.X - TOLERANCE, expected.X + TOLERANCE);
        Assert.InRange(actual.Y, expected.Y - TOLERANCE, expected.Y + TOLERANCE);
        Assert.InRange(actual.Z, expected.Z - TOLERANCE, expected.Z + TOLERANCE);
    }

    private static void AssertVector(Vector4 expected, Vector4 actual) {
        Assert.InRange(actual.X, expected.X - TOLERANCE, expected.X + TOLERANCE);
        Assert.InRange(actual.Y, expected.Y - TOLERANCE, expected.Y + TOLERANCE);
        Assert.InRange(actual.Z, expected.Z - TOLERANCE, expected.Z + TOLERANCE);
        Assert.InRange(actual.W, expected.W - TOLERANCE, expected.W + TOLERANCE);
    }

    private static void AssertIdentityTransform(float[] actual)
        => AssertTransform([0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f], actual);

    private static void AssertTransform(float[] expected, float[] actual) {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++) {
            Assert.InRange(actual[index], expected[index] - TOLERANCE, expected[index] + TOLERANCE);
        }
    }
}
