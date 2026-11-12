using RayBork.Bake;
using RayBork.Bake.Shaders;
using RayBork.Internal;
using RayBork.Tests.Bake.TestInfrastructure;

namespace RayBork.Tests.Bake.Shaders;

public sealed class ShaderPrologueTests {
    private const string ABI_TOML = """
        [shader]
        vertex = "abi.vert"
        fragment = "abi.frag"

        [[shader.uniforms]]
        name = "uTint"
        type = "vec4"
        default = [1.0, 1.0, 1.0, 1.0]

        [[shader.textures]]
        name = "mainTexture"
        """;

    private const string ABI_VERTEX = """
        #version 450

        layout(location = 0) in vec3 aPosition;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            vec4 world = rbInstance.Transform * vec4(aPosition, 1.0);
            gl_Position = rbFrame.ViewProjection * world;
            vUv = vec2(uTint.x, rbInstance.Color.a);
        }
        """;

    private const string ABI_FRAGMENT = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(mainTexture, vUv) * uTint;
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACustomAbiPairCompilesAndRenamesBothEntryPoints() {
        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, ABI_TOML, ABI_VERTEX, ABI_FRAGMENT);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        using PackArchive archive = PackArchive.Open(workspace.GetPackPath());
        Assert.True(archive.TryGetEntry("shaders/abi", out PackEntry? entry));
        ShaderAssetDescriptor descriptor = AssetSerialization.Deserialize<ShaderAssetDescriptor>(
            archive.ReadPayload(entry));
        Assert.Equal("main_vertex", descriptor.VertexEntry);
        Assert.Equal("main_fragment", descriptor.FragmentEntry);
        Assert.Equal(["mainTexture"], descriptor.Textures);
        Assert.Equal("uTint", Assert.Single(descriptor.Uniforms).Name);

        Assert.True(archive.TryGetEntry("shaders/abi-vert", out PackEntry? vertexEntry));
        Assert.True(archive.TryGetEntry("shaders/abi-frag", out PackEntry? fragmentEntry));
        ShaderTestAssets.AssertSpirv(
            archive.ReadPayload(vertexEntry),
            "main_vertex",
            SpirvExecutionModel.Vertex);
        ShaderTestAssets.AssertSpirv(
            archive.ReadPayload(fragmentEntry),
            "main_fragment",
            SpirvExecutionModel.Fragment);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ThePrologueFollowsTheVersionDirective() {
        string source = """
            #version 450

            void main_vertex() {
                gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(0.0);
            }
            """;

        Assert.True(
            ShaderPrologueGenerator.TryBuild(
                source,
                CreateLayout(),
                ShaderStageMask.Vertex,
                out string wrapped,
                out string message),
            message);

        Assert.Equal(1, CountOccurrences(wrapped, "#version"));
        int version = wrapped.IndexOf("#version 450", StringComparison.Ordinal);
        int extension = wrapped.IndexOf(
            "#extension GL_EXT_nonuniform_qualifier : require",
            StringComparison.Ordinal);
        int frame = wrapped.IndexOf("struct RayBorkFrame {", StringComparison.Ordinal);
        int wrapper = wrapped.IndexOf("void main() {", StringComparison.Ordinal);
        int macro = wrapped.IndexOf("#define main_vertex rbUserVertexMain", StringComparison.Ordinal);
        int user = wrapped.IndexOf("void main_vertex()", StringComparison.Ordinal);
        Assert.Equal(0, version);
        Assert.True(version < extension, "The extension directive must follow #version.");
        Assert.True(extension < frame, "The frame block must follow the extension directive.");
        Assert.True(frame < wrapper, "The wrapper must follow the generated declarations.");
        Assert.True(wrapper < macro, "The entry-point macro must follow the wrapper.");
        Assert.True(macro < user, "The entry-point macro must precede the user function.");

        Assert.Contains("layout(set = 0, binding = 0, std140) uniform RayBorkFrameBlock", wrapped);
        Assert.Contains("layout(set = 0, binding = 1) uniform sampler2D RayBorkTextures[];", wrapped);
        Assert.Contains("layout(set = 0, binding = 2, std430) readonly buffer RayBorkInstanceBuffer", wrapped);
        Assert.Contains("layout(location = 15) flat out uint rbInstanceIndexOut;", wrapped);
        Assert.Contains("rbInstanceIndexOut = uint(gl_InstanceIndex);", wrapped);
        Assert.Contains("#define rbFrame rbFrameBlock.frame", wrapped);
        Assert.Contains("#define rbInstance RayBorkInstances.instances[rbInstanceIndexOut]", wrapped);
        Assert.Contains(
            "#define mainTexture RayBorkTextures[nonuniformEXT(rbInstance.mainTextureIndex)]",
            wrapped);
        Assert.Contains("#define uTint rbInstance.uTint", wrapped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheFragmentPrologueUsesTheFlatInstanceIndexInput() {
        string source = """
            #version 450

            layout(location = 0) out vec4 oColor;

            void main_fragment() {
                oColor = uTint;
            }
            """;

        Assert.True(
            ShaderPrologueGenerator.TryBuild(
                source,
                CreateLayout(),
                ShaderStageMask.Fragment,
                out string wrapped,
                out string message),
            message);

        Assert.Contains("layout(location = 15) flat in uint rbInstanceIndex;", wrapped);
        Assert.Contains("#define rbInstance RayBorkInstances.instances[rbInstanceIndex]", wrapped);
        Assert.Contains("#define main_fragment rbUserFragmentMain", wrapped);
        Assert.DoesNotContain("gl_InstanceIndex", wrapped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASourceWithoutAVersionDirectiveGetsVersion450Prepended() {
        const string source = """
            void main_vertex() {
                gl_Position = vec4(0.0);
            }
            """;

        Assert.True(
            ShaderPrologueGenerator.TryBuild(
                source,
                CreateLayout(),
                ShaderStageMask.Vertex,
                out string wrapped,
                out string message),
            message);

        Assert.StartsWith("#version 450\n", wrapped, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(wrapped, "#version"));
        Assert.Contains("#extension GL_EXT_nonuniform_qualifier : require", wrapped);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheBakeCompilesAStageWithoutAVersionDirective() {
        const string vertex = """
            layout(location = 0) in vec3 aPosition;

            void main_vertex() {
                gl_Position = rbFrame.ViewProjection * vec4(aPosition, 1.0);
            }
            """;

        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, ABI_TOML, vertex, ABI_FRAGMENT);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
    }

    [Theory]
    [InlineData("main", "void main() { gl_Position = vec4(0.0); }")]
    [InlineData("RayBorkFrame", "struct RayBorkFrame { vec4 value; };")]
    [InlineData("RayBorkFrameBlock", "struct RayBorkFrameBlock { vec4 value; };")]
    [InlineData("RayBorkTextures", "vec4 RayBorkTextures;")]
    [InlineData("RayBorkInstance", "struct RayBorkInstance { vec4 value; };")]
    [InlineData("RayBorkInstanceBuffer", "struct RayBorkInstanceBuffer { vec4 value; };")]
    [InlineData("RayBorkInstances", "vec4 RayBorkInstances;")]
    [InlineData("rbFrame", "vec4 rbFrame;")]
    [InlineData("rbInstance", "vec4 rbInstance;")]
    [InlineData("rbInstanceIndex", "uint rbInstanceIndex;")]
    [InlineData("rbInstanceIndexOut", "uint rbInstanceIndexOut;")]
    [InlineData("rbUserVertexMain", "void rbUserVertexMain() { }")]
    [InlineData("rbUserFragmentMain", "void rbUserFragmentMain() { }")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReservedIdentifiersAreRb4008(string identifier, string declaration) {
        using var workspace = new TempBakeWorkspace();
        ShaderTestAssets.Write(
            workspace,
            "shaders/abi",
            ABI_TOML,
            VertexWith(declaration),
            ABI_FRAGMENT);

        BakeRunResult result = workspace.Run(
            workspace.Builder().UseDefaultProcessors().Build());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains(identifier, result.Error);
        Assert.Contains("generated by the M4 shader ABI", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Theory]
    [InlineData("uTint", "vec4 uTint;")]
    [InlineData("mainTexture", "sampler2D mainTexture;")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SidecarNamesCannotBeRedeclaredInGlsl(string identifier, string declaration) {
        using var workspace = new TempBakeWorkspace();
        ShaderTestAssets.Write(
            workspace,
            "shaders/abi",
            ABI_TOML,
            VertexWith(declaration),
            ABI_FRAGMENT);

        BakeRunResult result = workspace.Run(
            workspace.Builder().UseDefaultProcessors().Build());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains(identifier, result.Error);
        Assert.Contains("sidecar uniform or texture", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Theory]
    [InlineData("layout(set = 0, binding = 2) uniform sampler2D extra;")]
    [InlineData("layout(set = 1, binding = 0) uniform sampler2D extra;")]
    [InlineData("layout(set=0, binding=2) uniform sampler2D extra;")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LayoutSetQualifiersAreRb4008(string declaration) {
        using var workspace = new TempBakeWorkspace();
        ShaderTestAssets.Write(
            workspace,
            "shaders/abi",
            ABI_TOML,
            VertexWith(declaration),
            ABI_FRAGMENT);

        BakeRunResult result = workspace.Run(
            workspace.Builder().UseDefaultProcessors().Build());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains("layout(set", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Theory]
    [InlineData("layout(location = 15, component = 0) out vec4 vReserved;")]
    [InlineData("layout(location=15) out vec4 vReserved;")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReservedVaryingLocationFifteenIsRb4008(string declaration) {
        using var workspace = new TempBakeWorkspace();
        ShaderTestAssets.Write(
            workspace,
            "shaders/abi",
            ABI_TOML,
            VertexWith(declaration),
            ABI_FRAGMENT);

        BakeRunResult result = workspace.Run(
            workspace.Builder().UseDefaultProcessors().Build());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("RB4008", result.Error);
        Assert.Contains("location 15", result.Error);
        Assert.False(File.Exists(workspace.GetPackPath()));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CommentsDoNotTriggerReservedNameValidation() {
        const string vertex = """
            #version 450

            // rbFrame and uTint are used through the generated prologue.
            /* Declaring mainTexture here would be rejected, but this is a comment. */

            layout(location = 0) in vec3 aPosition;

            layout(location = 0) out vec2 vUv;

            void main_vertex() {
                gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(aPosition, 1.0);
                vUv = vec2(uTint.x, rbInstance.Color.a);
            }
            """;

        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, ABI_TOML, vertex, ABI_FRAGMENT);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnreservedAttributeLocationsAreAccepted() {
        const string vertex = """
            #version 450

            layout(location = 14) out vec4 vExtra;

            void main_vertex() {
                vExtra = vec4(1.0);
                gl_Position = rbFrame.ViewProjection * vec4(0.0);
            }
            """;

        using var workspace = new TempBakeWorkspace();
        BakeRunResult result = RunDefault(workspace, ABI_TOML, vertex, ABI_FRAGMENT);

        Assert.True(result.ExitCode == 0, result.Error + result.Output);
    }

    private static string VertexWith(string declaration) => $$"""
        #version 450

        {{declaration}}

        layout(location = 0) in vec3 aPosition;

        void main_vertex() {
            gl_Position = vec4(aPosition, 1.0);
        }
        """;

    private static BakeRunResult RunDefault(
        TempBakeWorkspace workspace,
        string toml,
        string vertex,
        string fragment) {
        ShaderTestAssets.Write(workspace, "shaders/abi", toml, vertex, fragment);
        return workspace.Run(workspace.Builder().UseDefaultProcessors().Build());
    }

    private static ShaderAbiLayout CreateLayout() => new(
        [
            new ShaderUniformAsset {
                Name = "uTint",
                Type = ShaderUniformType.Vector4,
                Default = [1f, 1f, 1f, 1f],
            },
        ],
        ["mainTexture"]);

    private static int CountOccurrences(string text, string value) {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += value.Length;
        }

        return count;
    }
}
