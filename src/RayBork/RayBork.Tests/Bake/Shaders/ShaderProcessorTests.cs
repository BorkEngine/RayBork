using RayBork.Bake;
using RayBork.Bake.Shaders;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;
using Silk.NET.Shaderc;

namespace RayBork.Tests.Bake.Shaders;

public sealed class ShaderProcessorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ValidShaderPairEmitsTheDescriptorAndBothSpirvPayloads() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, ShaderTestAssets.COMPLETE_TOML);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("shaders/castle", out PackEntry? entry));
        Assert.Equal(AssetEntryType.Shader, entry.Type);
        ShaderAssetDescriptor descriptor = ReadDescriptor(archive, entry);
        Assert.Equal("shaders/castle-vert", descriptor.VertexPayload);
        Assert.Equal("shaders/castle-frag", descriptor.FragmentPayload);
        Assert.Equal("main_vertex", descriptor.VertexEntry);
        Assert.Equal("main_fragment", descriptor.FragmentEntry);
        Assert.Equal(ShaderStageMask.Vertex | ShaderStageMask.Fragment, descriptor.Stages);
        Assert.Equal(
            new RenderState {
                Blend = BlendMode.Alpha,
                Cull = CullMode.None,
                DepthTest = DepthTest.Disabled,
                DepthWrite = false,
            },
            descriptor.DefaultRenderState);

        ShaderUniformAsset uniform = Assert.Single(descriptor.Uniforms);
        Assert.Equal("uTint", uniform.Name);
        Assert.Equal(ShaderUniformType.Vector4, uniform.Type);
        Assert.Equal([1f, 1f, 1f, 1f], uniform.Default);
        Assert.Equal(["mainTexture"], descriptor.Textures);

        AssertPayload(archive, "shaders/castle-vert", "main_vertex", SpirvExecutionModel.Vertex);
        AssertPayload(archive, "shaders/castle-frag", "main_fragment", SpirvExecutionModel.Fragment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NestedShaderEmitsNestedPayloadPaths() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(
            workspace,
            ShaderTestAssets.COMPLETE_TOML,
            basePath: "shaders/materials/castle");

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("shaders/materials/castle", out PackEntry? entry));
        ShaderAssetDescriptor descriptor = ReadDescriptor(archive, entry);
        Assert.Equal("shaders/materials/castle-vert", descriptor.VertexPayload);
        Assert.Equal("shaders/materials/castle-frag", descriptor.FragmentPayload);
        AssertPayload(archive, "shaders/materials/castle-vert", "main_vertex", SpirvExecutionModel.Vertex);
        AssertPayload(archive, "shaders/materials/castle-frag", "main_fragment", SpirvExecutionModel.Fragment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AbsentEntryPointsAndRenderStateUseTheM3Defaults() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"
            """);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ShaderAssetDescriptor descriptor = ReadDescriptor(archive, "shaders/castle");
        Assert.Equal("main_vertex", descriptor.VertexEntry);
        Assert.Equal("main_fragment", descriptor.FragmentEntry);
        Assert.Equal(RenderState.Opaque, descriptor.DefaultRenderState);
        Assert.Empty(descriptor.Uniforms);
        Assert.Empty(descriptor.Textures);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySupportedUniformTypeAndDefaultMapsToTheDescriptor() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.uniforms]]
            name = "uFloat"
            type = "float"
            default = 0.5

            [[shader.uniforms]]
            name = "uInt"
            type = "int"
            default = 2

            [[shader.uniforms]]
            name = "uUint"
            type = "uint"
            default = 3

            [[shader.uniforms]]
            name = "uBool"
            type = "bool"
            default = 1

            [[shader.uniforms]]
            name = "uVec2"
            type = "vec2"
            default = [1.0, 2.0]

            [[shader.uniforms]]
            name = "uVec3"
            type = "vec3"
            default = [1.0, 2.0, 3.0]

            [[shader.uniforms]]
            name = "uVec4"
            type = "vec4"
            default = [1.0, 2.0, 3.0, 4.0]

            [[shader.uniforms]]
            name = "uMat4"
            type = "mat4"
            default = [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0, 13.0, 14.0, 15.0, 16.0]

            [[shader.uniforms]]
            name = "uPlain"
            type = "float"
            """);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ShaderAssetDescriptor descriptor = ReadDescriptor(archive, "shaders/castle");
        Assert.Collection(
            descriptor.Uniforms,
            uniform => AssertUniform(uniform, "uFloat", ShaderUniformType.Float, [0.5f]),
            uniform => AssertUniform(uniform, "uInt", ShaderUniformType.Int, [2f]),
            uniform => AssertUniform(uniform, "uUint", ShaderUniformType.UInt, [3f]),
            uniform => AssertUniform(uniform, "uBool", ShaderUniformType.Bool, [1f]),
            uniform => AssertUniform(uniform, "uVec2", ShaderUniformType.Vector2, [1f, 2f]),
            uniform => AssertUniform(uniform, "uVec3", ShaderUniformType.Vector3, [1f, 2f, 3f]),
            uniform => AssertUniform(uniform, "uVec4", ShaderUniformType.Vector4, [1f, 2f, 3f, 4f]),
            uniform => AssertUniform(
                uniform,
                "uMat4",
                ShaderUniformType.Matrix4x4,
                [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f]),
            uniform => AssertUniform(uniform, "uPlain", ShaderUniformType.Float, []));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryRenderStateTokenMapsToTheEngineEnum() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [shader.render_state]
            blend = "multiply"
            cull = "front"
            depth_test = "less_or_equal"
            depth_write = true
            """);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        ShaderAssetDescriptor descriptor = ReadDescriptor(archive, "shaders/castle");
        Assert.Equal(
            new RenderState {
                Blend = BlendMode.Multiply,
                Cull = CullMode.Front,
                DepthTest = DepthTest.LessOrEqual,
                DepthWrite = true,
            },
            descriptor.DefaultRenderState);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidGlslIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            ShaderTestAssets.COMPLETE_TOML,
            "RB4008",
            fragment: "this is not GLSL at all");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingStageFileIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        ShaderTestAssets.Write(workspace, "shaders/castle", ShaderTestAssets.COMPLETE_TOML);
        File.Delete(Path.Combine(workspace.AssetRoot, "shaders", "castle.vert"));

        BakeRunResult result = workspace.Run(DefaultApplication(workspace));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains("does not exist", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RenamedEntryPointIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"
            vertex_entry = "main"
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingEntryPointInTheGlslIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"
            """,
            "RB4008",
            vertex: """
                #version 450
                void main() {
                    gl_Position = vec4(0.0);
                }
                """);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnsupportedUniformTypeIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.uniforms]]
            name = "uBasis"
            type = "mat3"
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrongDefaultLengthIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.uniforms]]
            name = "uTint"
            type = "vec4"
            default = [1.0, 1.0, 1.0]
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateUniformNamesAreRb4008AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.uniforms]]
            name = "uTint"
            type = "vec4"

            [[shader.uniforms]]
            name = "uTint"
            type = "vec4"
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DuplicateTextureNamesAreRb4008AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.textures]]
            name = "mainTexture"

            [[shader.textures]]
            name = "mainTexture"
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownRenderStateTokenIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [shader.render_state]
            blend = "screen"
            """,
            "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StagePathOutsideTheSourceDirectoryIsRb4008AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("shaders/castle.toml", """
            [shader]
            vertex = "../outside.vert"
            fragment = "castle.frag"
            """);
        workspace.WriteAsset("outside.vert", ShaderTestAssets.VERTEX_SOURCE);
        workspace.WriteAsset("shaders/castle.frag", ShaderTestAssets.FRAGMENT_SOURCE);

        BakeRunResult result = workspace.Run(DefaultApplication(workspace));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains("outside the shader source's directory subtree", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownSidecarKeysAreRb4002AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"
            pipeline = "forward"
            """,
            "RB4002");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownUniformKeysAreRb4002AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [[shader.uniforms]]
            name = "uTint"
            type = "vec4"
            size = 4
            """,
            "RB4002");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MissingRequiredStageKeyIsRb4002AndEmitsNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        workspace.WriteAsset("shaders/castle.toml", """
            [shader]
            fragment = "castle.frag"
            """);

        BakeRunResult result = workspace.Run(DefaultApplication(workspace));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4002", result.Error);
        Assert.Contains("vertex", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WrongSidecarValueTypesAreRb4002AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        AssertFailure(
            workspace,
            """
            [shader]
            vertex = "castle.vert"
            fragment = "castle.frag"

            [shader.render_state]
            depth_write = "yes"
            """,
            "RB4002");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MoreThanSixteenTextureBindingsAreRb4008AndEmitNoOutputs() {
        using var workspace = new TempBakeWorkspace();
        var toml = new System.Text.StringBuilder();
        toml.AppendLine("[shader]");
        toml.AppendLine("vertex = \"castle.vert\"");
        toml.AppendLine("fragment = \"castle.frag\"");
        for (int binding = 0; binding < 17; binding++) {
            toml.AppendLine("[[shader.textures]]");
            toml.AppendLine($"name = \"texture{binding}\"");
        }

        AssertFailure(workspace, toml.ToString(), "RB4008");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompilerSupportsTheMainEntryPointUsedByToolProcessors() {
        const string source = """
            #version 450
            void main() {
                gl_Position = vec4(0.0);
            }
            """;

        bool compiled = GlslCompiler.TryCompile(
            source,
            "present.vert",
            ShaderKind.GlslVertexShader,
            "main",
            out byte[] spirV,
            out string message);

        Assert.True(compiled, message);
        Assert.True(
            SpirvEntryPointInspector.TryValidate(
                spirV,
                "main",
                SpirvExecutionModel.Vertex,
                out string problem),
            problem);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SpirvInspectorRejectsTheWrongExecutionModel() {
        bool compiled = GlslCompiler.TryCompile(
            ShaderTestAssets.VERTEX_SOURCE,
            "castle.vert",
            ShaderKind.GlslVertexShader,
            "main_vertex",
            out byte[] spirV,
            out string message);
        Assert.True(compiled, message);

        Assert.False(
            SpirvEntryPointInspector.TryValidate(
                spirV,
                "main_vertex",
                SpirvExecutionModel.Fragment,
                out string problem));
        Assert.Contains("Vertex", problem);

        Assert.False(
            SpirvEntryPointInspector.TryValidate(
                [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20],
                "main_vertex",
                SpirvExecutionModel.Vertex,
                out string malformed));
        Assert.Contains("magic", malformed);
    }

    private static BakeRunResult RunDefault(
        TempBakeWorkspace workspace,
        string toml,
        string basePath = "shaders/castle") {
        ShaderTestAssets.Write(workspace, basePath, toml);
        return workspace.Run(DefaultApplication(workspace));
    }

    private static BakeApplication DefaultApplication(TempBakeWorkspace workspace)
        => workspace.Builder().UseDefaultProcessors().Build();

    private static void AssertFailure(
        TempBakeWorkspace workspace,
        string toml,
        string code,
        string? vertex = null,
        string? fragment = null) {
        ShaderTestAssets.Write(workspace, "shaders/castle", toml, vertex, fragment);
        BakeRunResult result = workspace.Run(DefaultApplication(workspace));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(code, result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    private static ShaderAssetDescriptor ReadDescriptor(PackArchive archive, PackEntry entry)
        => AssetSerialization.Deserialize<ShaderAssetDescriptor>(archive.ReadPayload(entry));

    private static ShaderAssetDescriptor ReadDescriptor(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        return ReadDescriptor(archive, entry);
    }

    private static void AssertPayload(
        PackArchive archive,
        string path,
        string entryPoint,
        SpirvExecutionModel executionModel) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry));
        Assert.Equal(AssetEntryType.Raw, entry.Type);
        Assert.Equal(16u, entry.Alignment);
        ShaderTestAssets.AssertSpirv(archive.ReadPayload(entry), entryPoint, executionModel);
    }

    private static void AssertUniform(
        ShaderUniformAsset uniform,
        string name,
        ShaderUniformType type,
        float[] defaultValue) {
        Assert.Equal(name, uniform.Name);
        Assert.Equal(type, uniform.Type);
        Assert.Equal(defaultValue, uniform.Default);
    }
}
