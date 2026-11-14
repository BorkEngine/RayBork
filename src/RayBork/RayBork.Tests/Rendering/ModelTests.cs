using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the public <see cref="Model"/> and <see cref="ModelPart"/> composition contract without a
/// pack or a GPU device: construction validation, computed bounds, and ownership.
/// </summary>
public sealed class ModelTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructionComputesPartBoundsFromTheMeshAndLocalTransform() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        Transform local = new(
            new Vector3(1f, 2f, 3f),
            Quaternion.Identity,
            new Vector3(2f, 2f, 2f));
        using Model model = new(new ModelPart("part", mesh, material, localTransform: local));

        ModelPart part = Assert.Single(model.Parts);
        Assert.Equal(new BoundingBox(new Vector3(-1f, 0f, 1f), new Vector3(3f, 4f, 5f)), part.Bounds);
        Assert.Equal(part.Bounds, model.Bounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PartBoundsEncloseTheMeshAfterARotatedLocalTransform() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        Transform local = new(
            Vector3.Zero,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f),
            Vector3.One);
        using Model model = new(new ModelPart("part", mesh, material, localTransform: local));

        BoundingBox bounds = model.Bounds;
        float halfDiagonal = MathF.Sqrt(2f);
        Assert.Equal(-halfDiagonal, bounds.Min.X, 1e-5f);
        Assert.Equal(-1f, bounds.Min.Y, 1e-5f);
        Assert.Equal(-halfDiagonal, bounds.Min.Z, 1e-5f);
        Assert.Equal(halfDiagonal, bounds.Max.X, 1e-5f);
        Assert.Equal(1f, bounds.Max.Y, 1e-5f);
        Assert.Equal(halfDiagonal, bounds.Max.Z, 1e-5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelBoundsAreTheUnionOfEveryPartBounds() {
        Mesh firstMesh = RenderingTestAssets.CreateMesh(
            "meshes/first",
            bounds: new BoundingBox(new Vector3(-1f), new Vector3(1f)));
        Mesh secondMesh = RenderingTestAssets.CreateMesh(
            "meshes/second",
            bounds: new BoundingBox(new Vector3(2f), new Vector3(4f)));
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/first");
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/second");
        using Model model = new(
            new ModelPart("first", firstMesh, firstMaterial),
            new ModelPart("second", secondMesh, secondMaterial));

        Assert.Equal(new BoundingBox(new Vector3(-1f), new Vector3(4f)), model.Bounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PartDefaultsToIdentityTransformAndTheMaterialRenderState() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader(defaultRenderState: RenderState.AlphaBlend);
        using Model model = new(new ModelPart("part", mesh, material));

        ModelPart part = model.Parts[0];
        Assert.Equal(Transform.Identity.Position, part.LocalTransform.Position);
        Assert.Equal(Transform.Identity.Rotation, part.LocalTransform.Rotation);
        Assert.Equal(Transform.Identity.Scale, part.LocalTransform.Scale);
        Assert.Equal(RenderState.AlphaBlend, part.RenderState);
        Assert.Equal(mesh.Bounds, part.Bounds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PartKeepsAnExplicitRenderStateAndLocalTransform() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        Transform local = new(new Vector3(4f, 5f, 6f));

        ModelPart part = new(
            "part",
            mesh,
            material,
            RenderState.Additive,
            local);

        Assert.Equal(RenderState.Additive, part.RenderState);
        Assert.Equal(new Vector3(4f, 5f, 6f), part.LocalTransform.Position);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorsValidateTheirArguments() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();

        Assert.Throws<ArgumentNullException>(() => new Model((ModelPart[])null!));
        Assert.Throws<ArgumentNullException>(() => new Model((IEnumerable<ModelPart>)null!));
        Assert.Throws<ArgumentException>(() => new Model());
        Assert.Throws<ArgumentException>(() => new Model(default(ModelPart)));
        Assert.Throws<ArgumentException>(() => new Model(new ModelPart(" ", mesh, material)));
        Assert.Throws<ArgumentNullException>(() => new Model(new ModelPart("part", null!, material)));
        Assert.Throws<ArgumentNullException>(() => new Model(new ModelPart("part", mesh, null!)));

        Assert.Throws<ArgumentNullException>(() => new ModelPart(null!, mesh, material));
        Assert.Throws<ArgumentException>(() => new ModelPart(" ", mesh, material));
        Assert.Throws<ArgumentNullException>(() => new ModelPart("part", null!, material));
        Assert.Throws<ArgumentNullException>(() => new ModelPart("part", mesh, null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelPartRejectsDisposedAssets() {
        Mesh disposedMesh = RenderingTestAssets.CreateMesh("meshes/disposed");
        Shader liveMaterial = RenderingTestAssets.CreateShader("shaders/live");
        disposedMesh.Lifetime.ForceDispose();

        Assert.Throws<ObjectDisposedException>(
            () => new ModelPart("part", disposedMesh, liveMaterial));

        Mesh liveMesh = RenderingTestAssets.CreateMesh("meshes/live");
        Shader disposedMaterial = RenderingTestAssets.CreateShader("shaders/disposed");
        disposedMaterial.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => new ModelPart("part", liveMesh, disposedMaterial));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelRejectsPartsWhoseAssetsBecameDisposed() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        ModelPart part = new("part", mesh, material);
        mesh.Lifetime.ForceDispose();

        Assert.Throws<ObjectDisposedException>(() => new Model(part));
        material.Dispose();

        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/second");
        ModelPart secondPart = new("part", secondMesh, secondMaterial);
        secondMaterial.Dispose();

        Assert.Throws<ObjectDisposedException>(() => new Model(secondPart));
        secondMesh.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelOwnsOneReferenceToEveryDistinctMesh() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        Model model = new(
            new ModelPart("first", mesh, material),
            new ModelPart("second", mesh, material));

        model.Dispose();

        Assert.True(material.IsDisposed);
        Assert.False(mesh.IsDisposed);

        mesh.Dispose();
        Assert.True(mesh.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelDisposesEveryDistinctMaterialAndReleasesEachProgramOnce() {
        List<GpuAssetResource> destroyed = [];
        ShaderProgram program = RenderingTestAssets.CreateProgram("shaders/shared", destroyed: destroyed);
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Model model = new(
            new ModelPart("first", mesh, first),
            new ModelPart("second", mesh, second));

        model.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
        Assert.False(program.IsDisposed);
        Assert.Empty(destroyed);

        RenderingTestAssets.ReleaseProgram(program);
        Assert.True(program.IsDisposed);
        Assert.Single(destroyed);

        mesh.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelDisposeIsIdempotentAndHidesPartsAndBounds() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        Model model = new(new ModelPart("part", mesh, material));

        model.Dispose();
        model.Dispose();

        Assert.True(model.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = model.Parts);
        Assert.Throws<ObjectDisposedException>(() => _ = model.Bounds);
        mesh.Dispose();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PartsAreExposedInConstructionOrder() {
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/first");
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/second");
        using Model model = new(
            new ModelPart("first", firstMesh, firstMaterial),
            new ModelPart("second", secondMesh, secondMaterial));

        Assert.Equal(new[] { "first", "second" }, model.Parts.Select(part => part.Name));
        Assert.Same(firstMesh, model.Parts[0].Mesh);
        Assert.Same(secondMaterial, model.Parts[1].Shader);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EnumerableConstructorMaterializesTheParts() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        ModelPart[] parts = [new("part", mesh, material)];

        using Model model = new(parts.AsEnumerable());

        Assert.Single(model.Parts);
        Assert.Equal("part", model.Parts[0].Name);
    }
}
