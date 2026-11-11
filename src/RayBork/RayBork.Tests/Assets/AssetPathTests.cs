using RayBork.Internal;

namespace RayBork.Tests.Assets;

public sealed class AssetPathTests {
    [Theory]
    [InlineData("a")]
    [InlineData("textures/white")]
    [InlineData("a/b-c_d/012")]
    [InlineData("data/settings")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeAcceptsCanonicalPaths(string path) {
        Assert.Equal(path, AssetPath.Normalize(path));
        Assert.True(AssetPath.IsCanonical(path));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeStripsOneOptionalLeadingSlash() {
        Assert.Equal("textures/white", AssetPath.Normalize("/textures/white"));
        Assert.Equal("a/b", AssetPath.Normalize("/a/b"));
    }

    [Theory]
    [InlineData("Textures/White", "textures/white")]
    [InlineData("DATA/Settings", "data/settings")]
    [InlineData("/MODELS/Cube", "models/cube")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeLowercasesWithInvariantRules(string input, string expected) {
        Assert.Equal(expected, AssetPath.Normalize(input));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeRejectsBackslashesWithAnActionableMessage() {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AssetPath.Normalize("textures\\white"));

        Assert.Contains("forward slashes", exception.Message);
        Assert.Equal("path", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("//textures/white")]
    [InlineData("textures//white")]
    [InlineData("textures/")]
    [InlineData(".")]
    [InlineData("./textures")]
    [InlineData("textures/.")]
    [InlineData("..")]
    [InlineData("../textures")]
    [InlineData("textures/..")]
    [InlineData("textures/white.png")]
    [InlineData("textures/white texture")]
    [InlineData("textures/Café")]
    [InlineData("textures/white?")]
    [InlineData("textures/white*")]
    [InlineData("textures\\white")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeRejectsInvalidPaths(string path) {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AssetPath.Normalize(path));

        Assert.Contains(path, exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeRejectsNull() {
        Assert.Throws<ArgumentNullException>(() => AssetPath.Normalize(null));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizeAcceptsTheMaximumPathLengthAndRejectsOneByteMore() {
        string maximum = new('a', AssetPath.MAX_PATH_BYTES);
        string oversize = new('a', AssetPath.MAX_PATH_BYTES + 1);

        Assert.Equal(maximum, AssetPath.Normalize(maximum));
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => AssetPath.Normalize(oversize));
        Assert.Contains("exceeds", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToPublicPrependsSlash() {
        Assert.Equal("/textures/white", AssetPath.ToPublic("textures/white"));
        Assert.Equal("/a", AssetPath.ToPublic("a"));
    }

    [Theory]
    [InlineData("/textures/white")]
    [InlineData("Textures/White")]
    [InlineData("textures/white/")]
    [InlineData("textures/white.png")]
    [InlineData("")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToPublicRejectsNonCanonicalInput(string path) {
        Assert.Throws<ArgumentException>(() => AssetPath.ToPublic(path));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToPublicRejectsNull() {
        Assert.Throws<ArgumentException>(() => AssetPath.ToPublic(null!));
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("a/b", true)]
    [InlineData("/a", false)]
    [InlineData("A", false)]
    [InlineData("a/", false)]
    [InlineData("a//b", false)]
    [InlineData("a.b", false)]
    [InlineData("a b", false)]
    [InlineData("a\\b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IsCanonicalMatchesTheDocumentedGrammar(string? path, bool expected) {
        Assert.Equal(expected, AssetPath.IsCanonical(path));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TryValidateCanonicalReportsAReason() {
        Assert.True(AssetPath.TryValidateCanonical("a/b", out string? validReason));
        Assert.Null(validReason);

        Assert.False(AssetPath.TryValidateCanonical("A", out string? invalidReason));
        Assert.NotNull(invalidReason);
        Assert.Contains("invalid character", invalidReason);

        Assert.False(AssetPath.TryValidateCanonical(null, out string? nullReason));
        Assert.NotNull(nullReason);
    }
}
