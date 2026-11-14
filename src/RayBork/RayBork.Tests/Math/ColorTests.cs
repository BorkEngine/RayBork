using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace RayBork.Tests;

public sealed class ColorTests {
    private const float TOLERANCE = 1e-5f;
    private const float OKLAB_TOLERANCE = 1e-4f;
    private const float PALETTE_TOLERANCE = 1e-6f;

    private const string PRESET_NAMES = """
        AliceBlue AntiqueWhite Aquamarine Azure Beige Bisque Black BlanchedAlmond Blue
        BlueViolet Brown Burlywood CadetBlue Chartreuse Chocolate Clear Coral CornflowerBlue
        Cornsilk Crimson Cyan DarkBlue DarkCyan DarkGoldenRod DarkGray DarkGreen DarkKhaki
        DarkMagenta DarkOliveGreen DarkOrange DarkOrchid DarkRed DarkSalmon DarkSeaGreen
        DarkSlateBlue DarkSlateGray DarkTurquoise DarkViolet DeepPink DeepSkyBlue DimGray
        DodgerBlue Firebrick FloralWhite ForestGreen Gainsboro GhostWhite Gold GoldenRod Gray
        Gray1 Gray2 Gray3 Gray4 Gray5 Gray6 Gray7 Gray8 Gray9 Green GreenYellow Grey Honeydew
        HotPink IndianRed Indigo Ivory Khaki Lavender LavenderBlush LawnGreen LemonChiffon
        LightBlue LightCoral LightCyan LightGoldenRod LightGoldenRodYellow LightGray LightGreen
        LightPink LightSalmon LightSeaGreen LightSkyBlue LightSlateBlue LightSlateGray
        LightSteelBlue LightYellow LimeGreen Linen Magenta Maroon MediumAquamarine MediumBlue
        MediumOrchid MediumPurple MediumSeaGreen MediumSlateBlue MediumSpringGreen
        MediumTurquoise MediumVioletRed MidnightBlue MintCream MistyRose Moccasin NavajoWhite
        NavyBlue OldLace Olive OliveDrab Orange OrangeRed Orchid PaleGoldenRod PaleGreen
        PaleTurquoise PaleVioletRed PapayaWhip PeachPuff Peru Pink Plum PowderBlue Purple
        RebeccaPurple Red RosyBrown RoyalBlue SaddleBrown Salmon SandyBrown SeaGreen Seashell
        Sienna Silver SkyBlue SlateBlue SlateGray Snow SoftBlue SoftGreen SoftRed SoftYellow
        SpringGreen SteelBlue Tan Teal Thistle Tomato Transparent Turquoise Violet VioletRed
        Wheat White WhiteSmoke Yellow YellowGreen YellowNice
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorStoresLinearComponents() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal(0.1f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.3f, color.B);
        Assert.Equal(0.4f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ThreeComponentConstructorDefaultsAlphaToOne() {
        Color color = new(0.1f, 0.2f, 0.3f);

        Assert.Equal(0.1f, color.R);
        Assert.Equal(0.2f, color.G);
        Assert.Equal(0.3f, color.B);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IndexerReturnsComponentsAndThrowsOutOfRange() {
        Color color = new(1f, 2f, 3f, 4f);

        Assert.Equal(1f, color[0]);
        Assert.Equal(2f, color[1]);
        Assert.Equal(3f, color[2]);
        Assert.Equal(4f, color[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = color[-1]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = color[4]; });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OperatorsWorkComponentWise() {
        Color left = new(1f, 2f, 3f, 4f);
        Color right = new(5f, 6f, 7f, 8f);

        Assert.Equal(new Color(6f, 8f, 10f, 12f), left + right);
        Assert.Equal(new Color(-4f, -4f, -4f, -4f), left - right);
        Assert.Equal(new Color(5f, 12f, 21f, 32f), left * right);
        Assert.Equal(new Color(0.5f, 1f, 1.5f, 2f), left * 0.5f);
        Assert.Equal(new Color(0.5f, 1f, 1.5f, 2f), 0.5f * left);
        Assert.Equal(new Color(2f, 4f, 6f, 8f), left / 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ImplicitVector4ConversionsRoundTrip() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);

        Vector4 vector = color;
        Color roundTrip = vector;

        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 0.4f), vector);
        Assert.Equal(color, roundTrip);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EqualityIsExactPerComponent() {
        Color color = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color same = new(0.1f, 0.2f, 0.3f, 0.4f);
        Color different = new(0.1f, 0.2f, 0.3f, 0.400001f);

        Assert.True(color.Equals(same));
        Assert.True(same.Equals(color));
        Assert.False(color.Equals(different));
        Assert.True(color.Equals((object)same));
        Assert.False(color.Equals(null));
        Assert.False(color.Equals("not a color"));
        Assert.Equal(color.GetHashCode(), same.GetHashCode());
    }

    [Theory]
    [InlineData("AliceBlue", 0.9411765f, 0.9725491f, 1f, 1f)]
    [InlineData("AntiqueWhite", 0.9803922f, 0.9215687f, 0.8431373f, 1f)]
    [InlineData("Aquamarine", 0.4980392f, 1f, 0.8313726f, 1f)]
    [InlineData("Azure", 0.9411765f, 1f, 1f, 1f)]
    [InlineData("Beige", 0.9607844f, 0.9607844f, 0.8627452f, 1f)]
    [InlineData("Bisque", 1f, 0.8941177f, 0.7686275f, 1f)]
    [InlineData("Black", 0f, 0f, 0f, 1f)]
    [InlineData("BlanchedAlmond", 1f, 0.9215687f, 0.8039216f, 1f)]
    [InlineData("Blue", 0f, 0f, 1f, 1f)]
    [InlineData("BlueViolet", 0.5411765f, 0.1686275f, 0.8862746f, 1f)]
    [InlineData("Brown", 0.6470588f, 0.1647059f, 0.1647059f, 1f)]
    [InlineData("Burlywood", 0.8705883f, 0.7215686f, 0.5294118f, 1f)]
    [InlineData("CadetBlue", 0.372549f, 0.6196079f, 0.627451f, 1f)]
    [InlineData("Chartreuse", 0.4980392f, 1f, 0f, 1f)]
    [InlineData("Chocolate", 0.8235295f, 0.4117647f, 0.1176471f, 1f)]
    [InlineData("Clear", 0f, 0f, 0f, 0f)]
    [InlineData("Coral", 1f, 0.4980392f, 0.3137255f, 1f)]
    [InlineData("CornflowerBlue", 0.3921569f, 0.5843138f, 0.9294118f, 1f)]
    [InlineData("Cornsilk", 1f, 0.9725491f, 0.8627452f, 1f)]
    [InlineData("Crimson", 0.8627452f, 0.07843138f, 0.2352941f, 1f)]
    [InlineData("Cyan", 0f, 1f, 1f, 1f)]
    [InlineData("DarkBlue", 0f, 0f, 0.5450981f, 1f)]
    [InlineData("DarkCyan", 0f, 0.5450981f, 0.5450981f, 1f)]
    [InlineData("DarkGoldenRod", 0.7215686f, 0.5254902f, 0.04313726f, 1f)]
    [InlineData("DarkGray", 0.6627451f, 0.6627451f, 0.6627451f, 1f)]
    [InlineData("DarkGreen", 0f, 0.3921569f, 0f, 1f)]
    [InlineData("DarkKhaki", 0.7411765f, 0.7176471f, 0.4196079f, 1f)]
    [InlineData("DarkMagenta", 0.5450981f, 0f, 0.5450981f, 1f)]
    [InlineData("DarkOliveGreen", 0.3333333f, 0.4196079f, 0.1843137f, 1f)]
    [InlineData("DarkOrange", 1f, 0.5490196f, 0f, 1f)]
    [InlineData("DarkOrchid", 0.6f, 0.1960784f, 0.8000001f, 1f)]
    [InlineData("DarkRed", 0.5450981f, 0f, 0f, 1f)]
    [InlineData("DarkSalmon", 0.9137256f, 0.5882353f, 0.4784314f, 1f)]
    [InlineData("DarkSeaGreen", 0.5607843f, 0.7372549f, 0.5607843f, 1f)]
    [InlineData("DarkSlateBlue", 0.282353f, 0.2392157f, 0.5450981f, 1f)]
    [InlineData("DarkSlateGray", 0.1843137f, 0.3098039f, 0.3098039f, 1f)]
    [InlineData("DarkTurquoise", 0f, 0.8078432f, 0.8196079f, 1f)]
    [InlineData("DarkViolet", 0.5803922f, 0f, 0.8274511f, 1f)]
    [InlineData("DeepPink", 1f, 0.07843138f, 0.5764706f, 1f)]
    [InlineData("DeepSkyBlue", 0f, 0.7490196f, 1f, 1f)]
    [InlineData("DimGray", 0.4117647f, 0.4117647f, 0.4117647f, 1f)]
    [InlineData("DodgerBlue", 0.1176471f, 0.5647059f, 1f, 1f)]
    [InlineData("Firebrick", 0.6980392f, 0.1333333f, 0.1333333f, 1f)]
    [InlineData("FloralWhite", 1f, 0.9803922f, 0.9411765f, 1f)]
    [InlineData("ForestGreen", 0.1333333f, 0.5450981f, 0.1333333f, 1f)]
    [InlineData("Gainsboro", 0.8627452f, 0.8627452f, 0.8627452f, 1f)]
    [InlineData("GhostWhite", 0.9725491f, 0.9725491f, 1f, 1f)]
    [InlineData("Gold", 1f, 0.8431373f, 0f, 1f)]
    [InlineData("GoldenRod", 0.854902f, 0.6470588f, 0.1254902f, 1f)]
    [InlineData("Gray", 0.5f, 0.5f, 0.5f, 1f)]
    [InlineData("Gray1", 0.1f, 0.1f, 0.1f, 1f)]
    [InlineData("Gray2", 0.2f, 0.2f, 0.2f, 1f)]
    [InlineData("Gray3", 0.3f, 0.3f, 0.3f, 1f)]
    [InlineData("Gray4", 0.4f, 0.4f, 0.4f, 1f)]
    [InlineData("Gray5", 0.5f, 0.5f, 0.5f, 1f)]
    [InlineData("Gray6", 0.6f, 0.6f, 0.6f, 1f)]
    [InlineData("Gray7", 0.7f, 0.7f, 0.7f, 1f)]
    [InlineData("Gray8", 0.8f, 0.8f, 0.8f, 1f)]
    [InlineData("Gray9", 0.9f, 0.9f, 0.9f, 1f)]
    [InlineData("Green", 0f, 1f, 0f, 1f)]
    [InlineData("GreenYellow", 0.6784314f, 1f, 0.1843137f, 1f)]
    [InlineData("Honeydew", 0.9411765f, 1f, 0.9411765f, 1f)]
    [InlineData("HotPink", 1f, 0.4117647f, 0.7058824f, 1f)]
    [InlineData("IndianRed", 0.8039216f, 0.3607843f, 0.3607843f, 1f)]
    [InlineData("Indigo", 0.2941177f, 0f, 0.509804f, 1f)]
    [InlineData("Ivory", 1f, 1f, 0.9411765f, 1f)]
    [InlineData("Khaki", 0.9411765f, 0.9019608f, 0.5490196f, 1f)]
    [InlineData("Lavender", 0.9019608f, 0.9019608f, 0.9803922f, 1f)]
    [InlineData("LavenderBlush", 1f, 0.9411765f, 0.9607844f, 1f)]
    [InlineData("LawnGreen", 0.4862745f, 0.9882354f, 0f, 1f)]
    [InlineData("LemonChiffon", 1f, 0.9803922f, 0.8039216f, 1f)]
    [InlineData("LightBlue", 0.6784314f, 0.8470589f, 0.9019608f, 1f)]
    [InlineData("LightCoral", 0.9411765f, 0.5019608f, 0.5019608f, 1f)]
    [InlineData("LightCyan", 0.8784314f, 1f, 1f, 1f)]
    [InlineData("LightGoldenRod", 0.9333334f, 0.8666667f, 0.509804f, 1f)]
    [InlineData("LightGoldenRodYellow", 0.9803922f, 0.9803922f, 0.8235295f, 1f)]
    [InlineData("LightGray", 0.8274511f, 0.8274511f, 0.8274511f, 1f)]
    [InlineData("LightGreen", 0.5647059f, 0.9333334f, 0.5647059f, 1f)]
    [InlineData("LightPink", 1f, 0.7137255f, 0.7568628f, 1f)]
    [InlineData("LightSalmon", 1f, 0.627451f, 0.4784314f, 1f)]
    [InlineData("LightSeaGreen", 0.1254902f, 0.6980392f, 0.6666667f, 1f)]
    [InlineData("LightSkyBlue", 0.5294118f, 0.8078432f, 0.9803922f, 1f)]
    [InlineData("LightSlateBlue", 0.5176471f, 0.4392157f, 1f, 1f)]
    [InlineData("LightSlateGray", 0.4666667f, 0.5333334f, 0.6f, 1f)]
    [InlineData("LightSteelBlue", 0.6901961f, 0.7686275f, 0.8705883f, 1f)]
    [InlineData("LightYellow", 1f, 1f, 0.8784314f, 1f)]
    [InlineData("LimeGreen", 0.1960784f, 0.8039216f, 0.1960784f, 1f)]
    [InlineData("Linen", 0.9803922f, 0.9411765f, 0.9019608f, 1f)]
    [InlineData("Magenta", 1f, 0f, 1f, 1f)]
    [InlineData("Maroon", 0.6901961f, 0.1882353f, 0.3764706f, 1f)]
    [InlineData("MediumAquamarine", 0.4f, 0.8039216f, 0.6666667f, 1f)]
    [InlineData("MediumBlue", 0f, 0f, 0.8039216f, 1f)]
    [InlineData("MediumOrchid", 0.7294118f, 0.3333333f, 0.8274511f, 1f)]
    [InlineData("MediumPurple", 0.5764706f, 0.4392157f, 0.8588236f, 1f)]
    [InlineData("MediumSeaGreen", 0.2352941f, 0.7019608f, 0.4431373f, 1f)]
    [InlineData("MediumSlateBlue", 0.482353f, 0.4078432f, 0.9333334f, 1f)]
    [InlineData("MediumSpringGreen", 0f, 0.9803922f, 0.6039216f, 1f)]
    [InlineData("MediumTurquoise", 0.282353f, 0.8196079f, 0.8000001f, 1f)]
    [InlineData("MediumVioletRed", 0.7803922f, 0.08235294f, 0.5215687f, 1f)]
    [InlineData("MidnightBlue", 0.09803922f, 0.09803922f, 0.4392157f, 1f)]
    [InlineData("MintCream", 0.9607844f, 1f, 0.9803922f, 1f)]
    [InlineData("MistyRose", 1f, 0.8941177f, 0.882353f, 1f)]
    [InlineData("Moccasin", 1f, 0.8941177f, 0.7098039f, 1f)]
    [InlineData("NavajoWhite", 1f, 0.8705883f, 0.6784314f, 1f)]
    [InlineData("NavyBlue", 0f, 0f, 0.5019608f, 1f)]
    [InlineData("OldLace", 0.9921569f, 0.9607844f, 0.9019608f, 1f)]
    [InlineData("Olive", 0.5019608f, 0.5019608f, 0f, 1f)]
    [InlineData("OliveDrab", 0.4196079f, 0.5568628f, 0.1372549f, 1f)]
    [InlineData("Orange", 1f, 0.6470588f, 0f, 1f)]
    [InlineData("OrangeRed", 1f, 0.2705882f, 0f, 1f)]
    [InlineData("Orchid", 0.854902f, 0.4392157f, 0.8392158f, 1f)]
    [InlineData("PaleGoldenRod", 0.9333334f, 0.909804f, 0.6666667f, 1f)]
    [InlineData("PaleGreen", 0.5960785f, 0.9843138f, 0.5960785f, 1f)]
    [InlineData("PaleTurquoise", 0.6862745f, 0.9333334f, 0.9333334f, 1f)]
    [InlineData("PaleVioletRed", 0.8588236f, 0.4392157f, 0.5764706f, 1f)]
    [InlineData("PapayaWhip", 1f, 0.937255f, 0.8352942f, 1f)]
    [InlineData("PeachPuff", 1f, 0.854902f, 0.7254902f, 1f)]
    [InlineData("Peru", 0.8039216f, 0.5215687f, 0.2470588f, 1f)]
    [InlineData("Pink", 1f, 0.7529413f, 0.7960785f, 1f)]
    [InlineData("Plum", 0.8666667f, 0.627451f, 0.8666667f, 1f)]
    [InlineData("PowderBlue", 0.6901961f, 0.8784314f, 0.9019608f, 1f)]
    [InlineData("Purple", 0.627451f, 0.1254902f, 0.9411765f, 1f)]
    [InlineData("RebeccaPurple", 0.4f, 0.2f, 0.6f, 1f)]
    [InlineData("Red", 1f, 0f, 0f, 1f)]
    [InlineData("RosyBrown", 0.7372549f, 0.5607843f, 0.5607843f, 1f)]
    [InlineData("RoyalBlue", 0.254902f, 0.4117647f, 0.882353f, 1f)]
    [InlineData("SaddleBrown", 0.5450981f, 0.2705882f, 0.07450981f, 1f)]
    [InlineData("Salmon", 0.9803922f, 0.5019608f, 0.4470589f, 1f)]
    [InlineData("SandyBrown", 0.9568628f, 0.6431373f, 0.3764706f, 1f)]
    [InlineData("SeaGreen", 0.1803922f, 0.5450981f, 0.3411765f, 1f)]
    [InlineData("Seashell", 1f, 0.9607844f, 0.9333334f, 1f)]
    [InlineData("Sienna", 0.627451f, 0.3215686f, 0.1764706f, 1f)]
    [InlineData("Silver", 0.7529413f, 0.7529413f, 0.7529413f, 1f)]
    [InlineData("SkyBlue", 0.5294118f, 0.8078432f, 0.9215687f, 1f)]
    [InlineData("SlateBlue", 0.4156863f, 0.3529412f, 0.8039216f, 1f)]
    [InlineData("SlateGray", 0.4392157f, 0.5019608f, 0.5647059f, 1f)]
    [InlineData("Snow", 1f, 0.9803922f, 0.9803922f, 1f)]
    [InlineData("SoftBlue", 0.1882353f, 0.682353f, 0.7490196f, 1f)]
    [InlineData("SoftGreen", 0.5490196f, 0.7882354f, 0.1411765f, 1f)]
    [InlineData("SoftRed", 0.8627452f, 0.1921569f, 0.1960784f, 1f)]
    [InlineData("SoftYellow", 1f, 0.9333334f, 0.5490196f, 1f)]
    [InlineData("SpringGreen", 0f, 1f, 0.4980392f, 1f)]
    [InlineData("SteelBlue", 0.2745098f, 0.509804f, 0.7058824f, 1f)]
    [InlineData("Tan", 0.8235295f, 0.7058824f, 0.5490196f, 1f)]
    [InlineData("Teal", 0f, 0.5019608f, 0.5019608f, 1f)]
    [InlineData("Thistle", 0.8470589f, 0.7490196f, 0.8470589f, 1f)]
    [InlineData("Tomato", 1f, 0.3882353f, 0.2784314f, 1f)]
    [InlineData("Turquoise", 0.2509804f, 0.8784314f, 0.8156863f, 1f)]
    [InlineData("Violet", 0.9333334f, 0.509804f, 0.9333334f, 1f)]
    [InlineData("VioletRed", 0.8156863f, 0.1254902f, 0.5647059f, 1f)]
    [InlineData("Wheat", 0.9607844f, 0.8705883f, 0.7019608f, 1f)]
    [InlineData("White", 1f, 1f, 1f, 1f)]
    [InlineData("WhiteSmoke", 0.9607844f, 0.9607844f, 0.9607844f, 1f)]
    [InlineData("Yellow", 1f, 235f / 255f, 4f / 255f, 1f)]
    [InlineData("YellowGreen", 0.6039216f, 0.8039216f, 0.1960784f, 1f)]
    [InlineData("Grey", 0.5f, 0.5f, 0.5f, 1f)]
    [InlineData("Transparent", 0f, 0f, 0f, 0f)]
    [InlineData("YellowNice", 1f, 235f / 255f, 4f / 255f, 1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresetStoresTheLinearizedSrgbSource(string name, float r, float g, float b, float a) {
        FieldInfo? field = typeof(Color).GetField(name, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        Color preset = (Color)field!.GetValue(null)!;

        Assert.Equal(Color.FromSrgb(r, g, b, a), preset);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PaletteExposesExactlyTheBork2dPresetNames() {
        string[] expected = PRESET_NAMES.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] actual = typeof(Color).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Color))
            .Select(field => field.Name)
            .ToArray();
        Array.Sort(expected, StringComparer.Ordinal);
        Array.Sort(actual, StringComparer.Ordinal);

        Assert.Equal(158, expected.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PalettePinsTheLinearizedDisplayValues() {
        AssertComponents(Color.Gray, 0.21404114f, 0.21404114f, 0.21404114f);
        AssertComponents(Color.Grey, 0.21404114f, 0.21404114f, 0.21404114f);
        AssertComponents(Color.WhiteSmoke, 0.913099f, 0.913099f, 0.913099f);
        AssertComponents(Color.Yellow, 1f, 0.8307699f, 0.0012141f);
        AssertComponents(Color.DarkCyan, 0f, 0.2581829f, 0.2581829f);

        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Clear);
        Assert.Equal(new Color(0f, 0f, 0f, 0f), Color.Transparent);
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.Black);
        Assert.Equal(new Color(1f, 1f, 1f, 1f), Color.White);
        Assert.Equal(new Color(1f, 0f, 0f, 1f), Color.Red);
        Assert.Equal(new Color(0f, 1f, 0f, 1f), Color.Green);
        Assert.Equal(new Color(0f, 0f, 1f, 1f), Color.Blue);
        Assert.Equal(new Color(0f, 1f, 1f, 1f), Color.Cyan);
        Assert.Equal(new Color(1f, 0f, 1f, 1f), Color.Magenta);

        Assert.Equal(Color.Gray, Color.Grey);
        Assert.Equal(Color.Clear, Color.Transparent);
        Assert.Equal(Color.Yellow, Color.YellowNice);

        // Playing the linear value back through the display encode returns the authored sRGB source.
        Assert.Equal(245f / 255f, Color.WhiteSmoke.ToSrgb().X, PALETTE_TOLERANCE);
        Assert.Equal(235f / 255f, Color.Yellow.ToSrgb().Y, PALETTE_TOLERANCE);
        Assert.Equal(4f / 255f, Color.Yellow.ToSrgb().Z, PALETTE_TOLERANCE);
        Assert.Equal(Color.Gray.ToSrgb(), Color.Gray5.ToSrgb());
    }

    private static void AssertComponents(Color color, float r, float g, float b, float a = 1f) {
        Assert.Equal(r, color.R, PALETTE_TOLERANCE);
        Assert.Equal(g, color.G, PALETTE_TOLERANCE);
        Assert.Equal(b, color.B, PALETTE_TOLERANCE);
        Assert.Equal(a, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GrayscaleUsesPerceivedLuminanceWeights() {
        Assert.Equal(0f, Color.Black.Grayscale);
        Assert.Equal(1f, Color.White.Grayscale);
        Assert.Equal(0.299f, new Color(1f, 0f, 0f).Grayscale);
        Assert.Equal(0.587f, new Color(0f, 1f, 0f).Grayscale);
        Assert.Equal(0.114f, new Color(0f, 0f, 1f).Grayscale);
        Assert.Equal(0.5f, new Color(0.5f, 0.5f, 0.5f).Grayscale, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MaxColorComponentIncludesAlpha() {
        Assert.Equal(0.9f, new Color(0.1f, 0.2f, 0.3f, 0.9f).MaxColorComponent);
        Assert.Equal(1f, Color.White.MaxColorComponent);
        Assert.Equal(0f, Color.Clear.MaxColorComponent);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LerpClampsWhileLerpUnclampedExtrapolates() {
        Color black = new(0f, 0f, 0f, 1f);
        Color white = new(1f, 1f, 1f, 1f);

        Assert.Equal(new Color(0.5f, 0.5f, 0.5f, 1f), Color.Lerp(black, white, 0.5f));
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.Lerp(black, white, -1f));
        Assert.Equal(new Color(1f, 1f, 1f, 1f), Color.Lerp(black, white, 2f));
        Assert.Equal(new Color(2f, 2f, 2f, 1f), Color.LerpUnclamped(black, white, 2f));
        Assert.Equal(new Color(-1f, -1f, -1f, 1f), Color.LerpUnclamped(black, white, -1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OkLabMatchesKnownValues() {
        Color.White.ToOkLab(out float whiteL, out float whiteA, out float whiteB);
        Assert.Equal(1f, whiteL, OKLAB_TOLERANCE);
        Assert.Equal(0f, whiteA, OKLAB_TOLERANCE);
        Assert.Equal(0f, whiteB, OKLAB_TOLERANCE);

        new Color(0.5f, 0.5f, 0.5f).ToOkLab(out float grayL, out float grayA, out float grayB);
        Assert.Equal(0.7937005f, grayL, OKLAB_TOLERANCE);
        Assert.Equal(0f, grayA, OKLAB_TOLERANCE);
        Assert.Equal(0f, grayB, OKLAB_TOLERANCE);

        Color.Red.ToOkLab(out float redL, out float redA, out float redB);
        Assert.Equal(0.6279554f, redL, OKLAB_TOLERANCE);
        Assert.Equal(0.2248631f, redA, OKLAB_TOLERANCE);
        Assert.Equal(0.1258463f, redB, OKLAB_TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OkLabRoundTripsLinearColors() {
        Color color = new(0.2f, 0.6f, 0.3f, 0.4f);

        color.ToOkLab(out float l, out float a, out float b);
        Color roundTrip = Color.FromOkLab(l, a, b, color.A);

        Assert.Equal(color.R, roundTrip.R, TOLERANCE);
        Assert.Equal(color.G, roundTrip.G, TOLERANCE);
        Assert.Equal(color.B, roundTrip.B, TOLERANCE);
        Assert.Equal(color.A, roundTrip.A, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LerpOkLabInterpolatesPerceptuallyAndClamps() {
        Color middle = Color.LerpOkLab(Color.Black, Color.White, 0.5f);
        Assert.Equal(0.125f, middle.R, TOLERANCE);
        Assert.Equal(0.125f, middle.G, TOLERANCE);
        Assert.Equal(0.125f, middle.B, TOLERANCE);
        Assert.Equal(1f, middle.A);

        AssertColorEqual(Color.Red, Color.LerpOkLab(Color.Red, Color.Blue, -1f));
        AssertColorEqual(Color.Blue, Color.LerpOkLab(Color.Red, Color.Blue, 2f));

        Color extrapolated = Color.LerpOkLabUnclamped(Color.Black, Color.White, 2f);
        Assert.Equal(8f, extrapolated.R, 1e-3f);
        Assert.Equal(8f, extrapolated.G, 1e-3f);
        Assert.Equal(8f, extrapolated.B, 1e-3f);
    }

    [Theory]
    [InlineData(0f, 1f, 0f, 0f)]
    [InlineData(1f / 6f, 1f, 1f, 0f)]
    [InlineData(1f / 3f, 0f, 1f, 0f)]
    [InlineData(0.5f, 0f, 1f, 1f)]
    [InlineData(2f / 3f, 0f, 0f, 1f)]
    [InlineData(5f / 6f, 1f, 0f, 1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBProducesThePrimarySectors(
        float hue,
        float expectedRed,
        float expectedGreen,
        float expectedBlue) {
        Color color = Color.HSVToRGB(hue, 1f, 1f);

        Assert.Equal(expectedRed, color.R, TOLERANCE);
        Assert.Equal(expectedGreen, color.G, TOLERANCE);
        Assert.Equal(expectedBlue, color.B, TOLERANCE);
        Assert.Equal(1f, color.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBWrapsHueAndHandlesZeroSaturation() {
        Assert.Equal(Color.Red, Color.HSVToRGB(1f, 1f, 1f));
        Assert.Equal(Color.Magenta, Color.HSVToRGB(-1f / 6f, 1f, 1f));
        Assert.Equal(Color.Cyan, Color.HSVToRGB(1.5f, 1f, 1f));
        Assert.Equal(new Color(0.25f, 0.25f, 0.25f, 1f), Color.HSVToRGB(0.4f, 0f, 0.25f));
        Assert.Equal(new Color(0f, 0f, 0f, 1f), Color.HSVToRGB(0.4f, 1f, 0f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBSupportsHdrValues() {
        Color hdr = Color.HSVToRGB(0f, 1f, 2f);
        Color clamped = Color.HSVToRGB(0f, 1f, 2f, false);

        Assert.Equal(2f, hdr.R);
        Assert.Equal(0f, hdr.G);
        Assert.Equal(0f, hdr.B);
        Assert.Equal(1f, hdr.A);

        Assert.Equal(1f, clamped.R);
        Assert.Equal(0f, clamped.G);
        Assert.Equal(0f, clamped.B);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 0f, 1f, 1f)]
    [InlineData(0f, 1f, 0f, 1f / 3f, 1f, 1f)]
    [InlineData(0f, 0f, 1f, 2f / 3f, 1f, 1f)]
    [InlineData(1f, 1f, 0f, 1f / 6f, 1f, 1f)]
    [InlineData(0f, 1f, 1f, 0.5f, 1f, 1f)]
    [InlineData(1f, 0f, 1f, 5f / 6f, 1f, 1f)]
    [InlineData(1f, 1f, 1f, 0f, 0f, 1f)]
    [InlineData(0f, 0f, 0f, 0f, 0f, 0f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RGBToHSVProducesExpectedValues(
        float red,
        float green,
        float blue,
        float expectedHue,
        float expectedSaturation,
        float expectedValue) {
        Color.RGBToHSV(new Color(red, green, blue), out float hue, out float saturation, out float value);

        Assert.Equal(expectedHue, hue, TOLERANCE);
        Assert.Equal(expectedSaturation, saturation, TOLERANCE);
        Assert.Equal(expectedValue, value, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HSVToRGBRoundTripsArbitraryColors() {
        Color original = new(0.2f, 0.5f, 0.8f);

        Color.RGBToHSV(original, out float hue, out float saturation, out float value);
        Color roundTrip = Color.HSVToRGB(hue, saturation, value);

        Assert.Equal(original.R, roundTrip.R, TOLERANCE);
        Assert.Equal(original.G, roundTrip.G, TOLERANCE);
        Assert.Equal(original.B, roundTrip.B, TOLERANCE);
        Assert.Equal(1f, roundTrip.A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RGBToHSVPreservesHdrValue() {
        Color.RGBToHSV(new Color(2f, 0f, 0f), out float hue, out float saturation, out float value);

        Assert.Equal(0f, hue);
        Assert.Equal(1f, saturation);
        Assert.Equal(2f, value);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToStringUsesStableRgbaForm() {
        Color color = new(1f, 0.5f, 0.25f, 0.75f);

        Assert.Equal("RGBA(1, 0.5, 0.25, 0.75)", color.ToString(null, CultureInfo.InvariantCulture));
        Assert.Equal("RGBA(1.00, 0.50, 0.25, 0.75)", color.ToString("F2", CultureInfo.InvariantCulture));
        Assert.Equal("RGBA(1, 0, 0, 1)", Color.Red.ToString());
        Assert.Equal(
            color.ToString(null, CultureInfo.InvariantCulture),
            ((IFormattable)color).ToString(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSrgbDecodesKnownValues() {
        Color middleGray = Color.FromSrgb(0.5f, 0.5f, 0.5f);

        Assert.Equal(0.21404114f, middleGray.R, TOLERANCE);
        Assert.Equal(0.21404114f, middleGray.G, TOLERANCE);
        Assert.Equal(0.21404114f, middleGray.B, TOLERANCE);
        Assert.Equal(1f, middleGray.A);

        Assert.Equal(0f, Color.FromSrgb(0f, 0f, 0f).R);
        Assert.Equal(1f, Color.FromSrgb(1f, 1f, 1f).R, TOLERANCE);
        Assert.Equal(0.5f, new Color(0.21404114f, 0.21404114f, 0.21404114f).ToSrgb().X, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSrgbUsesThePiecewiseEotfAtTheBoundary() {
        float atThreshold = Color.FromSrgb(0.04045f, 0.04045f, 0.04045f).R;
        float aboveThreshold = Color.FromSrgb(0.04045001f, 0.04045001f, 0.04045001f).R;

        Assert.Equal(0.04045f / 12.92f, atThreshold);
        Assert.Equal(0.0031308f, atThreshold, 1e-8f);
        Assert.Equal(0.0031308f, aboveThreshold, 1e-6f);
        Assert.True(aboveThreshold > atThreshold);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToSrgbUsesThePiecewiseOetfAtTheBoundary() {
        float atThreshold = new Color(0.0031308f, 0.0031308f, 0.0031308f).ToSrgb().X;
        float aboveThreshold = new Color(0.0031309f, 0.0031309f, 0.0031309f).ToSrgb().X;

        Assert.Equal(0.0031308f * 12.92f, atThreshold);
        Assert.Equal(0.04045f, atThreshold, 1e-6f);
        Assert.Equal(0.04045f, aboveThreshold, 1e-5f);
        Assert.True(aboveThreshold > atThreshold);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSrgbBytesDecodesDeterministicallyAndPassesAlpha() {
        Color middleGray = Color.FromSrgbBytes((byte)128, (byte)128, (byte)128, (byte)51);

        Assert.Equal(0.2158605f, middleGray.R, TOLERANCE);
        Assert.Equal(0.2158605f, middleGray.G, TOLERANCE);
        Assert.Equal(0.2158605f, middleGray.B, TOLERANCE);
        Assert.Equal(51f / 255f, middleGray.A);
        Assert.Equal(middleGray, Color.FromSrgbBytes((byte)128, (byte)128, (byte)128, (byte)51));

        Assert.Equal(0f, Color.FromSrgbBytes((byte)0, (byte)0, (byte)0).R);
        Assert.Equal(1f, Color.FromSrgbBytes((byte)255, (byte)255, (byte)255).R);
        Assert.Equal(1f, Color.FromSrgbBytes((byte)10, (byte)10, (byte)10).A);
        Assert.Equal((10f / 255f) / 12.92f, Color.FromSrgbBytes((byte)10, (byte)10, (byte)10).R);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SrgbRoundTripsInRangeColors() {
        Color original = new(0.2f, 0.6f, 0.3f, 0.4f);

        Vector4 encoded = original.ToSrgb();
        Color roundTrip = Color.FromSrgb(encoded.X, encoded.Y, encoded.Z, encoded.W);

        Assert.Equal(original.R, roundTrip.R, TOLERANCE);
        Assert.Equal(original.G, roundTrip.G, TOLERANCE);
        Assert.Equal(original.B, roundTrip.B, TOLERANCE);
        Assert.Equal(original.A, roundTrip.A, TOLERANCE);

        Vector4 display = Color.FromSrgb(0.25f, 0.5f, 0.75f).ToSrgb();

        Assert.Equal(0.25f, display.X, TOLERANCE);
        Assert.Equal(0.5f, display.Y, TOLERANCE);
        Assert.Equal(0.75f, display.Z, TOLERANCE);
        Assert.Equal(1f, display.W);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SrgbConversionPassesAlphaThrough() {
        Vector4 encoded = new Color(0.1f, 0.2f, 0.3f, 0.25f).ToSrgb();

        Assert.Equal(0.25f, encoded.W);
        Assert.Equal(0.25f, Color.FromSrgb(0.1f, 0.2f, 0.3f, 0.25f).A);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToSrgbExtrapolatesOutOfRangeComponentsWithoutClamping() {
        Vector4 encoded = new Color(2f, -0.5f, 0.5f, 1f).ToSrgb();

        Assert.Equal(1.353256f, encoded.X, 1e-4f);
        Assert.Equal(-6.46f, encoded.Y, 1e-5f);
        Assert.Equal(0.735357f, encoded.Z, 1e-4f);
        Assert.Equal(1f, encoded.W);
        Assert.True(encoded.X > 1f);
        Assert.True(encoded.Y < 0f);
    }

    [Theory]
    [InlineData(-0.01f, 0.5f, 0.5f, 1f)]
    [InlineData(float.NegativeInfinity, 0.5f, 0.5f, 1f)]
    [InlineData(0.5f, 1.01f, 0.5f, 1f)]
    [InlineData(0.5f, 0.5f, float.NaN, 1f)]
    [InlineData(0.5f, 0.5f, 0.5f, -0.25f)]
    [InlineData(0.5f, 0.5f, 0.5f, float.PositiveInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSrgbRejectsNonFiniteOrOutOfRangeComponents(float r, float g, float b, float a) {
        Assert.Throws<ArgumentOutOfRangeException>(() => Color.FromSrgb(r, g, b, a));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSrgbReportsTheOutOfRangeParameter() {
        ArgumentOutOfRangeException rgbException = Assert.Throws<ArgumentOutOfRangeException>(
            () => Color.FromSrgb(-0.1f, 0.5f, 0.5f));
        ArgumentOutOfRangeException alphaException = Assert.Throws<ArgumentOutOfRangeException>(
            () => Color.FromSrgb(0.5f, 0.5f, 0.5f, 1.5f));

        Assert.Equal("r", rgbException.ParamName);
        Assert.Equal("a", alphaException.ParamName);
    }

    private static void AssertColorEqual(Color expected, Color actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.R, actual.R, tolerance);
        Assert.Equal(expected.G, actual.G, tolerance);
        Assert.Equal(expected.B, actual.B, tolerance);
        Assert.Equal(expected.A, actual.A, tolerance);
    }
}
