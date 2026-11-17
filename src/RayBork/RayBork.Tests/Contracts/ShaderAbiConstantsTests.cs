using RayBork.Internal;

namespace RayBork.Tests;

public sealed class ShaderAbiConstantsTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FrameBlockIsTwoHundredFiftySixBytesWithTheDocumentedOffsets()
    {
        Assert.Equal(256, ShaderAbiFrame.BLOCK_SIZE);
        Assert.Equal(0, ShaderAbiFrame.VIEW_PROJECTION_OFFSET);
        Assert.Equal(64, ShaderAbiFrame.VIEW_OFFSET);
        Assert.Equal(128, ShaderAbiFrame.PROJECTION_OFFSET);
        Assert.Equal(192, ShaderAbiFrame.CAMERA_POSITION_OFFSET);
        Assert.Equal(208, ShaderAbiFrame.VIEWPORT_OFFSET);
        Assert.Equal(224, ShaderAbiFrame.TIME_OFFSET);
        Assert.Equal(240, ShaderAbiFrame.SCREEN_SIZE_OFFSET);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheFrameStructArrayStrideIsTheBlockSize()
    {
        // Every member is a mat4 or vec4, so std430 keeps the std140 offsets and a 256-byte stride.
        // The last member is the vec4 at offset 240, which ends exactly at the block size.
        Assert.Equal(256, ShaderAbiFrame.BLOCK_SIZE);
        Assert.Equal(
            ShaderAbiFrame.BLOCK_SIZE,
            ShaderAbiFrame.SCREEN_SIZE_OFFSET + (4 * sizeof(float))
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BindlessTextureCapacityMatchesTheFixedShaderArray()
    {
        Assert.Equal(2048u, VulkanDeviceCapabilities.TARGET_BINDLESS_TEXTURE_CAPACITY);
        Assert.Equal(
            ShaderAbiNames.TEXTURE_CAPACITY,
            VulkanDeviceCapabilities.TARGET_BINDLESS_TEXTURE_CAPACITY
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GeneratedGlslIdentifiersMatchTheShaderAbiContract()
    {
        Assert.Equal(2, ShaderAbiNames.ABI_VERSION);
        Assert.Equal("RayBorkFrame", ShaderAbiNames.FRAME_TYPE);
        Assert.Equal("RayBorkFrameBuffer", ShaderAbiNames.FRAME_BLOCK);
        Assert.Equal("rbFrameBlock", ShaderAbiNames.FRAME_BLOCK_INSTANCE);
        Assert.Equal("frames", ShaderAbiNames.FRAME_FIELD);
        Assert.Equal("RayBorkPushConstants", ShaderAbiNames.PUSH_CONSTANTS);
        Assert.Equal("rbPushConstants", ShaderAbiNames.PUSH_CONSTANTS_INSTANCE);
        Assert.Equal("passIndex", ShaderAbiNames.PASS_INDEX);
        Assert.Equal("RayBorkTextures", ShaderAbiNames.TEXTURES);
        Assert.Equal(2048u, ShaderAbiNames.TEXTURE_CAPACITY);
        Assert.Equal("RayBorkInstance", ShaderAbiNames.INSTANCE_TYPE);
        Assert.Equal("RayBorkInstanceBuffer", ShaderAbiNames.INSTANCE_BUFFER);
        Assert.Equal("RayBorkInstances", ShaderAbiNames.INSTANCES);
        Assert.Equal("rbFrame", ShaderAbiNames.FRAME_ACCESSOR);
        Assert.Equal("rbInstance", ShaderAbiNames.INSTANCE_ACCESSOR);
        Assert.Equal("rbInstanceIndex", ShaderAbiNames.INSTANCE_INDEX);
        Assert.Equal("rbInstanceIndexOut", ShaderAbiNames.INSTANCE_INDEX_OUT);
        Assert.Equal("rbUserVertexMain", ShaderAbiNames.USER_VERTEX_MAIN);
        Assert.Equal("rbUserFragmentMain", ShaderAbiNames.USER_FRAGMENT_MAIN);
        Assert.Equal("main_vertex", ShaderAbiNames.VERTEX_ENTRY_POINT);
        Assert.Equal("main_fragment", ShaderAbiNames.FRAGMENT_ENTRY_POINT);
        Assert.Equal("main", ShaderAbiNames.GLSL_ENTRY_POINT);
        Assert.Equal(15, ShaderAbiNames.RESERVED_VARYING_LOCATION);
    }
}
