using RayBork.Internal;

namespace RayBork.Tests;

public sealed class ShaderAbiLayoutTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyMetadataProducesTheHeaderOnlyRecord() {
        var layout = new ShaderAbiLayout([], []);

        Assert.Equal(96, layout.InstanceRecordSize);
        Assert.Equal(0, layout.InstanceDataSize);
        Assert.Empty(layout.Uniforms);
        Assert.Empty(layout.Textures);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FixedHeaderOffsetsAreStable() {
        Assert.Equal(0, ShaderAbiLayout.TRANSFORM_OFFSET);
        Assert.Equal(64, ShaderAbiLayout.COLOR_OFFSET);
        Assert.Equal(80, ShaderAbiLayout.UV_RECT_OFFSET);
        Assert.Equal(96, ShaderAbiLayout.INSTANCE_HEADER_SIZE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextureIndexSlotsStartAtNinetySixAndOccupyFourBytesEach() {
        var layout = new ShaderAbiLayout([], ["mainTexture", "detailTexture"]);

        Assert.Collection(
            layout.Textures,
            texture => {
                Assert.Equal("mainTexture", texture.Name);
                Assert.Equal(0, texture.Slot);
                Assert.Equal(96, texture.IndexOffset);
            },
            texture => {
                Assert.Equal("detailTexture", texture.Name);
                Assert.Equal(1, texture.Slot);
                Assert.Equal(100, texture.IndexOffset);
            });
        Assert.Equal(96, layout.TextureIndexOffset(0));
        Assert.Equal(100, layout.TextureIndexOffset(1));
        Assert.Equal(112, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UniformsFollowTextureIndicesAndAlignToTheirStd430Rule() {
        var layout = new ShaderAbiLayout(
            [Uniform("uTint", ShaderUniformType.Vector4)],
            ["mainTexture"]);

        ShaderAbiUniform uniform = Assert.Single(layout.Uniforms);
        Assert.Equal(112, uniform.Offset);
        Assert.Equal(16, uniform.Size);
        Assert.Equal(4, uniform.ComponentCount);
        Assert.Equal(112, layout.UniformOffset(0));
        Assert.Equal(16, layout.UniformByteSize(0));
        Assert.Equal(128, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Vec3OccupiesTwelveBytesAndAPackedFloatFollowsAtPlusTwelve() {
        var layout = new ShaderAbiLayout(
            [
                Uniform("uDirection", ShaderUniformType.Vector3),
                Uniform("uWeight", ShaderUniformType.Float),
            ],
            []);

        Assert.Equal(96, layout.Uniforms[0].Offset);
        Assert.Equal(12, layout.Uniforms[0].Size);
        Assert.Equal(3, layout.Uniforms[0].ComponentCount);
        Assert.Equal(108, layout.Uniforms[1].Offset);
        Assert.Equal(4, layout.Uniforms[1].Size);
        Assert.Equal(112, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Vec2AlignsToEightBytes() {
        var layout = new ShaderAbiLayout(
            [
                Uniform("uWeight", ShaderUniformType.Float),
                Uniform("uOffset", ShaderUniformType.Vector2),
            ],
            []);

        Assert.Equal(96, layout.Uniforms[0].Offset);
        Assert.Equal(104, layout.Uniforms[1].Offset);
        Assert.Equal(8, layout.Uniforms[1].Size);
        Assert.Equal(112, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Mat4AlignsToSixteenBytesAndOccupiesSixtyFour() {
        var layout = new ShaderAbiLayout(
            [
                Uniform("uWeight", ShaderUniformType.Float),
                Uniform("uBasis", ShaderUniformType.Matrix4x4),
            ],
            []);

        Assert.Equal(96, layout.Uniforms[0].Offset);
        Assert.Equal(112, layout.Uniforms[1].Offset);
        Assert.Equal(64, layout.Uniforms[1].Size);
        Assert.Equal(16, layout.Uniforms[1].ComponentCount);
        Assert.Equal(176, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DeclarationOrderIsPreservedAndDrivesTheOffsets() {
        var layout = new ShaderAbiLayout(
            [
                Uniform("uFirst", ShaderUniformType.Vector4),
                Uniform("uSecond", ShaderUniformType.Float),
                Uniform("uThird", ShaderUniformType.Vector4),
            ],
            []);

        Assert.Collection(
            layout.Uniforms,
            uniform => {
                Assert.Equal("uFirst", uniform.Name);
                Assert.Equal(96, uniform.Offset);
            },
            uniform => {
                Assert.Equal("uSecond", uniform.Name);
                Assert.Equal(112, uniform.Offset);
            },
            uniform => {
                Assert.Equal("uThird", uniform.Name);
                Assert.Equal(128, uniform.Offset);
            });
        Assert.Equal(144, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScalarTypesShareFourByteSlotsAndRoundTheRecordToSixteenBytes() {
        var layout = new ShaderAbiLayout(
            [
                Uniform("uFloat", ShaderUniformType.Float),
                Uniform("uInt", ShaderUniformType.Int),
                Uniform("uUint", ShaderUniformType.UInt),
                Uniform("uBool", ShaderUniformType.Bool),
            ],
            []);

        Assert.Equal(96, layout.Uniforms[0].Offset);
        Assert.Equal(100, layout.Uniforms[1].Offset);
        Assert.Equal(104, layout.Uniforms[2].Offset);
        Assert.Equal(108, layout.Uniforms[3].Offset);
        Assert.Equal(112, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RecordSizeRoundsUpToAMultipleOfSixteen() {
        var layout = new ShaderAbiLayout([Uniform("uWeight", ShaderUniformType.Float)], []);

        Assert.Equal(100, layout.Uniforms[0].Offset + layout.Uniforms[0].Size);
        Assert.Equal(112, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheLargestSupportedRecordIsAccepted() {
        var layout = new ShaderAbiLayout(Matrices(254), []);

        Assert.Equal(16352, layout.InstanceRecordSize);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ARecordAboveSixteenKiBIsRejected() {
        Assert.Throws<InvalidOperationException>(() => new ShaderAbiLayout(Matrices(255), []));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyTextureNameIsRejected() {
        Assert.Throws<ArgumentException>(() => new ShaderAbiLayout([], ["mainTexture", ""]));
    }

    private static ShaderUniformAsset Uniform(
        string name,
        ShaderUniformType type,
        params float[] defaultValue) => new() {
            Name = name,
            Type = type,
            Default = defaultValue,
        };

    private static ShaderUniformAsset[] Matrices(int count) {
        var uniforms = new ShaderUniformAsset[count];
        for (int index = 0; index < count; index++) {
            uniforms[index] = Uniform($"uMatrix{index}", ShaderUniformType.Matrix4x4);
        }

        return uniforms;
    }
}
