using System.Buffers.Binary;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class ShaderAssetTests {
    private const string VERTEX_SOURCE_FILE = "Present.vert.glsl";
    private const string FRAGMENT_SOURCE_FILE = "Present.frag.glsl";
    private const string VERTEX_MODULE_FILE = "Present.vert.spv";
    private const string FRAGMENT_MODULE_FILE = "Present.frag.spv";
    private const string GLSL_VERSION_DIRECTIVE = "#version 450";

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentShaderModulesAreWordAlignedSpirvWithSupportedVersion() {
        AssertSpirvHeader(ShaderAssets.PresentVertex, VERTEX_MODULE_FILE);
        AssertSpirvHeader(ShaderAssets.PresentFragment, FRAGMENT_MODULE_FILE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentVertexModuleHasSingleVertexMainEntryPoint() {
        SpirvEntryPoint entryPoint = Assert.Single(SpirvInspector.ReadEntryPoints(ShaderAssets.PresentVertex));

        Assert.Equal(SpirvInspector.EXECUTION_MODEL_VERTEX, entryPoint.ExecutionModel);
        Assert.Equal("main", entryPoint.Name);
        Assert.Equal(ShaderAssets.PRESENT_VERTEX_ENTRY_POINT, entryPoint.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentFragmentModuleHasSingleFragmentMainEntryPoint() {
        SpirvEntryPoint entryPoint = Assert.Single(SpirvInspector.ReadEntryPoints(ShaderAssets.PresentFragment));

        Assert.Equal(SpirvInspector.EXECUTION_MODEL_FRAGMENT, entryPoint.ExecutionModel);
        Assert.Equal("main", entryPoint.Name);
        Assert.Equal(ShaderAssets.PRESENT_FRAGMENT_ENTRY_POINT, entryPoint.Name);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentVertexAndFragmentModulesAreNotByteIdentical() {
        Assert.False(
            ShaderAssets.PresentVertex.SequenceEqual(ShaderAssets.PresentFragment),
            "The vertex and fragment present shaders must be distinct SPIR-V modules.");
    }

    [Theory]
    [InlineData(VERTEX_SOURCE_FILE)]
    [InlineData(FRAGMENT_SOURCE_FILE)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GlslSourceExistsNextToCommittedSpirvAndDeclaresMain(string sourceFileName) {
        string sourcePath = ShaderPathOf(sourceFileName);
        string modulePath = ShaderPathOf(Path.ChangeExtension(sourceFileName, ".spv"));

        Assert.True(File.Exists(sourcePath), $"Missing GLSL shader source '{sourcePath}'.");
        Assert.True(File.Exists(modulePath), $"Missing committed SPIR-V module '{modulePath}'.");

        string source = File.ReadAllText(sourcePath);
        Assert.StartsWith(GLSL_VERSION_DIRECTIVE, source, StringComparison.Ordinal);
        Assert.Contains("void main", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentVertexAssetIsStableAndMatchesCommittedSpirv() {
        byte[] firstRead = ShaderAssets.PresentVertex.ToArray();
        byte[] secondRead = ShaderAssets.PresentVertex.ToArray();

        Assert.Equal(firstRead, secondRead);
        Assert.Equal(ReadCommittedModule(VERTEX_MODULE_FILE), firstRead);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentFragmentAssetIsStableAndMatchesCommittedSpirv() {
        byte[] firstRead = ShaderAssets.PresentFragment.ToArray();
        byte[] secondRead = ShaderAssets.PresentFragment.ToArray();

        Assert.Equal(firstRead, secondRead);
        Assert.Equal(ReadCommittedModule(FRAGMENT_MODULE_FILE), firstRead);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultDrawShaderAppliesTheInstanceTintOnce() {
        string vertex = File.ReadAllText(DefaultShaderPathOf("default.vert"));
        string fragment = File.ReadAllText(DefaultShaderPathOf("default.frag"));

        Assert.Contains("vColor = aColor;", vertex, StringComparison.Ordinal);
        Assert.DoesNotContain("rbInstance.Color", vertex, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(fragment, "rbInstance.Color"));
        Assert.Contains("texture(mainTexture, vUv) * vColor * uTint * rbInstance.Color", fragment);
    }

    private static void AssertSpirvHeader(ReadOnlySpan<byte> module, string moduleFileName) {
        Assert.False(module.IsEmpty, $"The '{moduleFileName}' module must not be empty.");
        Assert.Equal(0, module.Length % sizeof(uint));

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(module);
        Assert.Equal(SpirvInspector.MAGIC_NUMBER, magic);

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(module.Slice(sizeof(uint), sizeof(uint)));
        Assert.Equal(1u, (version >> 16) & 0xFFu);
        Assert.True(
            ((version >> 8) & 0xFFu) <= 6u,
            $"The '{moduleFileName}' module must target SPIR-V 1.6 or earlier but has version word 0x{version:X8}.");
    }

    private static byte[] ReadCommittedModule(string moduleFileName) {
        string path = ShaderPathOf(moduleFileName);
        Assert.True(File.Exists(path), $"Missing committed SPIR-V module '{path}'.");
        return File.ReadAllBytes(path);
    }

    private static string ShaderPathOf(string fileName) =>
        RepositoryLayout.PathOf("src", "RayBork", "RayBork.Engine", "Shaders", fileName);

    private static string DefaultShaderPathOf(string fileName) =>
        RepositoryLayout.PathOf(
            "src",
            "RayBork",
            "RayBork.Engine",
            "BuiltInAssets",
            "Assets",
            "shaders",
            fileName);

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
