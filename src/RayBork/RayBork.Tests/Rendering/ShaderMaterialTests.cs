using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the public shader material identity contract: a material is an independent instance over one
/// internal program, and disposing one material never invalidates a sibling.
/// </summary>
public sealed class ShaderMaterialTests {
    private const string VALUE_UNIFORM = RenderingTestAssets.VALUE_UNIFORM;
    private const string TEXTURE_BINDING = RenderingTestAssets.TEXTURE_BINDING;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MaterialsOverOneProgramAreDistinctAndShareTheProgram() {
        ShaderProgram program = RenderingTestAssets.CreateProgram(defaultValue: 0.5f);
        try {
            Shader first = program.CreateMaterial();
            Shader second = program.CreateMaterial();
            try {
                Assert.NotSame(first, second);
                Assert.Same(program, first.Program);
                Assert.Same(program, second.Program);
                Assert.Equal(program.Path, first.Path);
                Assert.Equal(program.Path, second.Path);
                Assert.Equal(program.Stages, first.Stages);
                Assert.Equal(program.DefaultRenderState, first.DefaultRenderState);
                Assert.Equal(
                    program.Uniforms.Select(uniform => uniform.Name),
                    first.Uniforms.Select(uniform => uniform.Name));
                Assert.Equal(
                    program.Textures.Select(texture => texture.Name),
                    first.Textures.Select(texture => texture.Name));
                Assert.False(program.IsDisposed);
            } finally {
                first.Dispose();
                second.Dispose();
            }

            Assert.False(program.IsDisposed);
        } finally {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposingOneMaterialLeavesTheSiblingUsable() {
        var destroyed = new List<GpuAssetResource>();
        ShaderProgram program = RenderingTestAssets.CreateProgram(destroyed: destroyed);
        Shader disposedMaterial = program.CreateMaterial();
        Shader survivingMaterial = program.CreateMaterial();
        try {
            OnTickThread(() => disposedMaterial.SetUniform(VALUE_UNIFORM, 7f));
            disposedMaterial.Dispose();

            Assert.True(disposedMaterial.IsDisposed);
            Assert.False(survivingMaterial.IsDisposed);
            Assert.False(program.IsDisposed);
            Assert.Empty(destroyed);

            OnTickThread(() => {
                survivingMaterial.SetUniform(VALUE_UNIFORM, 3f);
                Assert.Equal(
                    3f,
                    RenderingTestAssets.ReadInstanceFloat(
                        survivingMaterial,
                        survivingMaterial.InstanceData,
                        VALUE_UNIFORM));
                survivingMaterial.ResetUniforms();
            });
        } finally {
            if (!survivingMaterial.IsDisposed) {
                survivingMaterial.Dispose();
            }

            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryMaterialStartsWithItsOwnCopyOfTheProgramBakeDefaults() {
        ShaderProgram program = RenderingTestAssets.CreateProgram(defaultValue: 1.25f);
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        try {
            OnTickThread(() => {
                first.SetUniform(VALUE_UNIFORM, 9f);
                first.SetUniform(TEXTURE_BINDING, null);

                Assert.Equal(
                    9f,
                    RenderingTestAssets.ReadInstanceFloat(first, first.InstanceData, VALUE_UNIFORM));
                Assert.Equal(
                    1.25f,
                    RenderingTestAssets.ReadInstanceFloat(second, second.InstanceData, VALUE_UNIFORM));
                Assert.Equal(
                    1.25f,
                    RenderingTestAssets.ReadInstanceFloat(second, second.DefaultInstanceData, VALUE_UNIFORM));

                first.ResetUniforms();
                Assert.Equal(
                    1.25f,
                    RenderingTestAssets.ReadInstanceFloat(first, first.InstanceData, VALUE_UNIFORM));
            });
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextureBindingOwnershipIsPerMaterial() {
        var destroyed = new List<GpuAssetResource>();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        Texture texture = RenderingTestAssets.CreateTexture("textures/material", destroyed);
        try {
            OnTickThread(() => {
                first.SetUniform(TEXTURE_BINDING, texture);
                Assert.Same(texture, first.TextureValues[0]);
                Assert.Null(second.TextureValues[0]);
            });

            texture.Dispose();
            Assert.False(texture.IsDisposed);
            Assert.Empty(destroyed);

            // Disposing the material that bound the texture releases that one owner reference.
            first.Dispose();
            Assert.True(texture.IsDisposed);
            Assert.Single(destroyed);
            Assert.Null(second.TextureValues[0]);
        } finally {
            if (!first.IsDisposed) {
                first.Dispose();
            }

            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResettingOneMaterialDoesNotChangeTheSiblingCursor() {
        ShaderProgram program = RenderingTestAssets.CreateProgram(defaultValue: 0.25f);
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        try {
            OnTickThread(() => {
                first.SetUniform(VALUE_UNIFORM, 5f);
                second.SetUniform(VALUE_UNIFORM, 6f);

                first.ResetUniforms();

                Assert.Equal(
                    0.25f,
                    RenderingTestAssets.ReadInstanceFloat(first, first.InstanceData, VALUE_UNIFORM));
                Assert.Equal(
                    6f,
                    RenderingTestAssets.ReadInstanceFloat(second, second.InstanceData, VALUE_UNIFORM));
            });
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheLastMaterialDisposalReleasesTheProgramAndQueuesItsDestructionOnce() {
        var destroyed = new List<GpuAssetResource>();
        ShaderProgram program = RenderingTestAssets.CreateProgram(destroyed: destroyed);
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();

        // Drop the helper's catalog-style reference; the two materials keep the program alive.
        RenderingTestAssets.ReleaseProgram(program);
        Assert.False(program.IsDisposed);

        first.Dispose();
        Assert.False(program.IsDisposed);
        Assert.Empty(destroyed);

        second.Dispose();
        Assert.True(program.IsDisposed);
        Assert.Single(destroyed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreatingAMaterialFromADisposedProgramThrows() {
        var destroyed = new List<GpuAssetResource>();
        ShaderProgram program = RenderingTestAssets.CreateProgram(destroyed: destroyed);
        Shader material = program.CreateMaterial();
        try {
            RenderingTestAssets.ReleaseProgram(program);
            material.Dispose();

            Assert.True(program.IsDisposed);
            Assert.Single(destroyed);
            Assert.Throws<ObjectDisposedException>(program.CreateMaterial);
        } finally {
            if (!material.IsDisposed) {
                material.Dispose();
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AMaterialWhoseProgramWasForceDisposedReportsDisposedAndRejectsReads() {
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Shader material = program.CreateMaterial();
        RenderingTestAssets.ReleaseProgram(program);
        program.Lifetime.ForceDispose();

        Assert.True(material.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = material.Stages);
        Assert.Throws<ObjectDisposedException>(() => _ = material.Uniforms);
        Assert.Throws<ObjectDisposedException>(() => _ = material.Textures);
        Assert.Throws<ObjectDisposedException>(() => _ = material.DefaultRenderState);

        // Material disposal throws once the program was force-disposed; the catalog owns the teardown
        // of leaked references in that shutdown path.
        Assert.Throws<ObjectDisposedException>(material.Dispose);
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
