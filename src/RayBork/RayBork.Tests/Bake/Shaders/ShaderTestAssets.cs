using System.Buffers.Binary;
using RayBork.Bake.Shaders;

namespace RayBork.Tests.Bake.Shaders;

/// <summary>Minimal GLSL stages, a complete shader source, and SPIR-V assertions for tests.</summary>
internal static class ShaderTestAssets {
    internal const string VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;

        void main_vertex() {
            gl_Position = vec4(aPosition, 1.0);
        }
        """;

    internal const string FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = vec4(1.0, 0.5, 0.25, 1.0);
        }
        """;

    internal const string COMPLETE_TOML = """
        [shader]
        vertex = "castle.vert"
        fragment = "castle.frag"
        vertex_entry = "main_vertex"
        fragment_entry = "main_fragment"

        [[shader.uniforms]]
        name = "uTint"
        type = "vec4"
        default = [1.0, 1.0, 1.0, 1.0]

        [[shader.textures]]
        name = "mainTexture"

        [shader.render_state]
        blend = "alpha"
        cull = "none"
        depth_test = "disabled"
        depth_write = false
        """;

    /// <summary>Writes the complete shader asset under one base path.</summary>
    /// <param name="workspace">The test workspace.</param>
    /// <param name="basePath">The canonical base path, with <c>/</c> separators.</param>
    /// <param name="toml">The shader TOML source.</param>
    /// <param name="vertexSource">The vertex GLSL, or <see langword="null"/> to use the default.</param>
    /// <param name="fragmentSource">The fragment GLSL, or <see langword="null"/> to use the default.</param>
    internal static void Write(
        TestInfrastructure.TempBakeWorkspace workspace,
        string basePath,
        string toml,
        string? vertexSource = null,
        string? fragmentSource = null) {
        workspace.WriteAsset(basePath + ".toml", toml);
        workspace.WriteAsset(basePath + ".vert", vertexSource ?? VERTEX_SOURCE);
        workspace.WriteAsset(basePath + ".frag", fragmentSource ?? FRAGMENT_SOURCE);
    }

    /// <summary>Asserts that a payload is a SPIR-V module with the requested entry point.</summary>
    /// <param name="payload">The module bytes.</param>
    /// <param name="entryPoint">The required entry point.</param>
    /// <param name="executionModel">The required execution model.</param>
    internal static void AssertSpirv(
        ReadOnlySpan<byte> payload,
        string entryPoint,
        SpirvExecutionModel executionModel) {
        Assert.True(payload.Length >= 20, "The module is shorter than a SPIR-V header.");
        Assert.Equal(0, payload.Length % 4);
        Assert.Equal(0x07230203u, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.True(
            SpirvEntryPointInspector.TryValidate(payload, entryPoint, executionModel, out string message),
            message);
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        Assert.Equal(1u, version >> 16);
    }
}
