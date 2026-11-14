using System.Globalization;
using System.Numerics;

namespace RayBork;

/// <summary>
/// An immutable linear RGBA color stored as a <see cref="Vector4"/>.
/// </summary>
/// <remarks>
/// <para>
/// Components are normally in <c>[0, 1]</c>, but HDR rendering allows values above <c>1</c>.
/// Colors are authored in linear space; there is no implicit gamma conversion. Use
/// <see cref="FromSrgb(float, float, float, float)"/> and <see cref="ToSrgb"/> to convert
/// explicitly between linear and sRGB-encoded values.
/// </para>
/// <para>
/// The named presets are the Bork2d/Unity sRGB colors converted to linear by the same piecewise
/// sRGB EOTF as <see cref="FromSrgb(float, float, float, float)"/>: each preset's documented sRGB
/// source is its display-authored color, and the stored value is linear. Alpha is never
/// gamma-encoded. Use the presets for named display colors and <see cref="FromSrgb(float, float,
/// float, float)"/> for other display-authored components; the type itself stays linear.
/// </para>
/// </remarks>
public readonly struct Color : IEquatable<Color>, IFormattable {
    private const float GRAYSCALE_RED_WEIGHT = 0.299f;
    private const float GRAYSCALE_GREEN_WEIGHT = 0.587f;
    private const float GRAYSCALE_BLUE_WEIGHT = 0.114f;
    private const float SRGB_DECODE_THRESHOLD = 0.04045f;
    private const float SRGB_ENCODE_THRESHOLD = 0.0031308f;
    private const float SRGB_SLOPE = 12.92f;
    private const float SRGB_OFFSET = 0.055f;
    private const float SRGB_SCALE = 1.055f;
    private const float SRGB_GAMMA = 2.4f;
    private const float SRGB_INVERSE_GAMMA = 1f / 2.4f;

    private readonly Vector4 _rgba;

    /// <summary>The Bork2d/Unity <c>AliceBlue</c> preset; its sRGB source is <c>RGBA(0.9411765, 0.9725491, 1, 1)</c>, stored linear as <c>RGBA(0.8713672, 0.9386859, 1, 1)</c>.</summary>
    public static readonly Color AliceBlue = FromSrgb(0.9411765f, 0.9725491f, 1f);

    /// <summary>The Bork2d/Unity <c>AntiqueWhite</c> preset; its sRGB source is <c>RGBA(0.9803922, 0.9215687, 0.8431373, 1)</c>, stored linear as <c>RGBA(0.9559735, 0.8307701, 0.6795426, 1)</c>.</summary>
    public static readonly Color AntiqueWhite = FromSrgb(0.9803922f, 0.9215687f, 0.8431373f);

    /// <summary>The Bork2d/Unity <c>Aquamarine</c> preset; its sRGB source is <c>RGBA(0.4980392, 1, 0.8313726, 1)</c>, stored linear as <c>RGBA(0.2122307, 1, 0.658375, 1)</c>.</summary>
    public static readonly Color Aquamarine = FromSrgb(0.4980392f, 1f, 0.8313726f);

    /// <summary>The Bork2d/Unity <c>Azure</c> preset; its sRGB source is <c>RGBA(0.9411765, 1, 1, 1)</c>, stored linear as <c>RGBA(0.8713672, 1, 1, 1)</c>.</summary>
    public static readonly Color Azure = FromSrgb(0.9411765f, 1f, 1f);

    /// <summary>The Bork2d/Unity <c>Beige</c> preset; its sRGB source is <c>RGBA(0.9607844, 0.9607844, 0.8627452, 1)</c>, stored linear as <c>RGBA(0.913099, 0.913099, 0.7156939, 1)</c>.</summary>
    public static readonly Color Beige = FromSrgb(0.9607844f, 0.9607844f, 0.8627452f);

    /// <summary>The Bork2d/Unity <c>Bisque</c> preset; its sRGB source is <c>RGBA(1, 0.8941177, 0.7686275, 1)</c>, stored linear as <c>RGBA(1, 0.7758225, 0.5520116, 1)</c>.</summary>
    public static readonly Color Bisque = FromSrgb(1f, 0.8941177f, 0.7686275f);

    /// <summary>The Bork2d/Unity <c>Black</c> preset; its sRGB source is <c>RGBA(0, 0, 0, 1)</c>, stored linear as <c>RGBA(0, 0, 0, 1)</c>.</summary>
    public static readonly Color Black = FromSrgb(0f, 0f, 0f);

    /// <summary>The Bork2d/Unity <c>BlanchedAlmond</c> preset; its sRGB source is <c>RGBA(1, 0.9215687, 0.8039216, 1)</c>, stored linear as <c>RGBA(1, 0.8307701, 0.6104956, 1)</c>.</summary>
    public static readonly Color BlanchedAlmond = FromSrgb(1f, 0.9215687f, 0.8039216f);

    /// <summary>The Bork2d/Unity <c>Blue</c> preset; its sRGB source is <c>RGBA(0, 0, 1, 1)</c>, stored linear as <c>RGBA(0, 0, 1, 1)</c>.</summary>
    public static readonly Color Blue = FromSrgb(0f, 0f, 1f);

    /// <summary>The Bork2d/Unity <c>BlueViolet</c> preset; its sRGB source is <c>RGBA(0.5411765, 0.1686275, 0.8862746, 1)</c>, stored linear as <c>RGBA(0.2541522, 0.02415765, 0.7605247, 1)</c>.</summary>
    public static readonly Color BlueViolet = FromSrgb(0.5411765f, 0.1686275f, 0.8862746f);

    /// <summary>The Bork2d/Unity <c>Brown</c> preset; its sRGB source is <c>RGBA(0.6470588, 0.1647059, 0.1647059, 1)</c>, stored linear as <c>RGBA(0.3762621, 0.02315337, 0.02315337, 1)</c>.</summary>
    public static readonly Color Brown = FromSrgb(0.6470588f, 0.1647059f, 0.1647059f);

    /// <summary>The Bork2d/Unity <c>Burlywood</c> preset; its sRGB source is <c>RGBA(0.8705883, 0.7215686, 0.5294118, 1)</c>, stored linear as <c>RGBA(0.730461, 0.4793201, 0.2422812, 1)</c>.</summary>
    public static readonly Color Burlywood = FromSrgb(0.8705883f, 0.7215686f, 0.5294118f);

    /// <summary>The Bork2d/Unity <c>CadetBlue</c> preset; its sRGB source is <c>RGBA(0.372549, 0.6196079, 0.627451, 1)</c>, stored linear as <c>RGBA(0.1144354, 0.3419145, 0.3515327, 1)</c>.</summary>
    public static readonly Color CadetBlue = FromSrgb(0.372549f, 0.6196079f, 0.627451f);

    /// <summary>The Bork2d/Unity <c>Chartreuse</c> preset; its sRGB source is <c>RGBA(0.4980392, 1, 0, 1)</c>, stored linear as <c>RGBA(0.2122307, 1, 0, 1)</c>.</summary>
    public static readonly Color Chartreuse = FromSrgb(0.4980392f, 1f, 0f);

    /// <summary>The Bork2d/Unity <c>Chocolate</c> preset; its sRGB source is <c>RGBA(0.8235295, 0.4117647, 0.1176471, 1)</c>, stored linear as <c>RGBA(0.6444799, 0.1412633, 0.01298304, 1)</c>.</summary>
    public static readonly Color Chocolate = FromSrgb(0.8235295f, 0.4117647f, 0.1176471f);

    /// <summary>The Bork2d/Unity <c>Clear</c> preset; its sRGB source is <c>RGBA(0, 0, 0, 0)</c>, stored linear as <c>RGBA(0, 0, 0, 0)</c>.</summary>
    public static readonly Color Clear = FromSrgb(0f, 0f, 0f, 0f);

    /// <summary>The Bork2d/Unity <c>Coral</c> preset; its sRGB source is <c>RGBA(1, 0.4980392, 0.3137255, 1)</c>, stored linear as <c>RGBA(1, 0.2122307, 0.08021983, 1)</c>.</summary>
    public static readonly Color Coral = FromSrgb(1f, 0.4980392f, 0.3137255f);

    /// <summary>The Bork2d/Unity <c>CornflowerBlue</c> preset; its sRGB source is <c>RGBA(0.3921569, 0.5843138, 0.9294118, 1)</c>, stored linear as <c>RGBA(0.1274377, 0.3005439, 0.8468735, 1)</c>.</summary>
    public static readonly Color CornflowerBlue = FromSrgb(0.3921569f, 0.5843138f, 0.9294118f);

    /// <summary>The Bork2d/Unity <c>Cornsilk</c> preset; its sRGB source is <c>RGBA(1, 0.9725491, 0.8627452, 1)</c>, stored linear as <c>RGBA(1, 0.9386859, 0.7156939, 1)</c>.</summary>
    public static readonly Color Cornsilk = FromSrgb(1f, 0.9725491f, 0.8627452f);

    /// <summary>The Bork2d/Unity <c>Crimson</c> preset; its sRGB source is <c>RGBA(0.8627452, 0.07843138, 0.2352941, 1)</c>, stored linear as <c>RGBA(0.7156939, 0.00699541, 0.0451862, 1)</c>.</summary>
    public static readonly Color Crimson = FromSrgb(0.8627452f, 0.07843138f, 0.2352941f);

    /// <summary>The Bork2d/Unity <c>Cyan</c> preset; its sRGB source is <c>RGBA(0, 1, 1, 1)</c>, stored linear as <c>RGBA(0, 1, 1, 1)</c>.</summary>
    public static readonly Color Cyan = FromSrgb(0f, 1f, 1f);

    /// <summary>The Bork2d/Unity <c>DarkBlue</c> preset; its sRGB source is <c>RGBA(0, 0, 0.5450981, 1)</c>, stored linear as <c>RGBA(0, 0, 0.258183, 1)</c>.</summary>
    public static readonly Color DarkBlue = FromSrgb(0f, 0f, 0.5450981f);

    /// <summary>The Bork2d/Unity <c>DarkCyan</c> preset; its sRGB source is <c>RGBA(0, 0.5450981, 0.5450981, 1)</c>, stored linear as <c>RGBA(0, 0.258183, 0.258183, 1)</c>.</summary>
    public static readonly Color DarkCyan = FromSrgb(0f, 0.5450981f, 0.5450981f);

    /// <summary>The Bork2d/Unity <c>DarkGoldenRod</c> preset; its sRGB source is <c>RGBA(0.7215686, 0.5254902, 0.04313726, 1)</c>, stored linear as <c>RGBA(0.4793201, 0.2383977, 0.003346536, 1)</c>.</summary>
    public static readonly Color DarkGoldenRod = FromSrgb(0.7215686f, 0.5254902f, 0.04313726f);

    /// <summary>The Bork2d/Unity <c>DarkGray</c> preset; its sRGB source is <c>RGBA(0.6627451, 0.6627451, 0.6627451, 1)</c>, stored linear as <c>RGBA(0.3967553, 0.3967553, 0.3967553, 1)</c>.</summary>
    public static readonly Color DarkGray = FromSrgb(0.6627451f, 0.6627451f, 0.6627451f);

    /// <summary>The Bork2d/Unity <c>DarkGreen</c> preset; its sRGB source is <c>RGBA(0, 0.3921569, 0, 1)</c>, stored linear as <c>RGBA(0, 0.1274377, 0, 1)</c>.</summary>
    public static readonly Color DarkGreen = FromSrgb(0f, 0.3921569f, 0f);

    /// <summary>The Bork2d/Unity <c>DarkKhaki</c> preset; its sRGB source is <c>RGBA(0.7411765, 0.7176471, 0.4196079, 1)</c>, stored linear as <c>RGBA(0.5088814, 0.4735315, 0.1470273, 1)</c>.</summary>
    public static readonly Color DarkKhaki = FromSrgb(0.7411765f, 0.7176471f, 0.4196079f);

    /// <summary>The Bork2d/Unity <c>DarkMagenta</c> preset; its sRGB source is <c>RGBA(0.5450981, 0, 0.5450981, 1)</c>, stored linear as <c>RGBA(0.258183, 0, 0.258183, 1)</c>.</summary>
    public static readonly Color DarkMagenta = FromSrgb(0.5450981f, 0f, 0.5450981f);

    /// <summary>The Bork2d/Unity <c>DarkOliveGreen</c> preset; its sRGB source is <c>RGBA(0.3333333, 0.4196079, 0.1843137, 1)</c>, stored linear as <c>RGBA(0.09084171, 0.1470273, 0.02842603, 1)</c>.</summary>
    public static readonly Color DarkOliveGreen = FromSrgb(0.3333333f, 0.4196079f, 0.1843137f);

    /// <summary>The Bork2d/Unity <c>DarkOrange</c> preset; its sRGB source is <c>RGBA(1, 0.5490196, 0, 1)</c>, stored linear as <c>RGBA(1, 0.2622507, 0, 1)</c>.</summary>
    public static readonly Color DarkOrange = FromSrgb(1f, 0.5490196f, 0f);

    /// <summary>The Bork2d/Unity <c>DarkOrchid</c> preset; its sRGB source is <c>RGBA(0.6, 0.1960784, 0.8000001, 1)</c>, stored linear as <c>RGBA(0.3185468, 0.03189602, 0.6038275, 1)</c>.</summary>
    public static readonly Color DarkOrchid = FromSrgb(0.6f, 0.1960784f, 0.8000001f);

    /// <summary>The Bork2d/Unity <c>DarkRed</c> preset; its sRGB source is <c>RGBA(0.5450981, 0, 0, 1)</c>, stored linear as <c>RGBA(0.258183, 0, 0, 1)</c>.</summary>
    public static readonly Color DarkRed = FromSrgb(0.5450981f, 0f, 0f);

    /// <summary>The Bork2d/Unity <c>DarkSalmon</c> preset; its sRGB source is <c>RGBA(0.9137256, 0.5882353, 0.4784314, 1)</c>, stored linear as <c>RGBA(0.8148469, 0.3049874, 0.1946179, 1)</c>.</summary>
    public static readonly Color DarkSalmon = FromSrgb(0.9137256f, 0.5882353f, 0.4784314f);

    /// <summary>The Bork2d/Unity <c>DarkSeaGreen</c> preset; its sRGB source is <c>RGBA(0.5607843, 0.7372549, 0.5607843, 1)</c>, stored linear as <c>RGBA(0.2746773, 0.5028866, 0.2746773, 1)</c>.</summary>
    public static readonly Color DarkSeaGreen = FromSrgb(0.5607843f, 0.7372549f, 0.5607843f);

    /// <summary>The Bork2d/Unity <c>DarkSlateBlue</c> preset; its sRGB source is <c>RGBA(0.282353, 0.2392157, 0.5450981, 1)</c>, stored linear as <c>RGBA(0.06480331, 0.04666509, 0.258183, 1)</c>.</summary>
    public static readonly Color DarkSlateBlue = FromSrgb(0.282353f, 0.2392157f, 0.5450981f);

    /// <summary>The Bork2d/Unity <c>DarkSlateGray</c> preset; its sRGB source is <c>RGBA(0.1843137, 0.3098039, 0.3098039, 1)</c>, stored linear as <c>RGBA(0.02842603, 0.07818741, 0.07818741, 1)</c>.</summary>
    public static readonly Color DarkSlateGray = FromSrgb(0.1843137f, 0.3098039f, 0.3098039f);

    /// <summary>The Bork2d/Unity <c>DarkTurquoise</c> preset; its sRGB source is <c>RGBA(0, 0.8078432, 0.8196079, 1)</c>, stored linear as <c>RGBA(0, 0.6172068, 0.637597, 1)</c>.</summary>
    public static readonly Color DarkTurquoise = FromSrgb(0f, 0.8078432f, 0.8196079f);

    /// <summary>The Bork2d/Unity <c>DarkViolet</c> preset; its sRGB source is <c>RGBA(0.5803922, 0, 0.8274511, 1)</c>, stored linear as <c>RGBA(0.2961383, 0, 0.6514059, 1)</c>.</summary>
    public static readonly Color DarkViolet = FromSrgb(0.5803922f, 0f, 0.8274511f);

    /// <summary>The Bork2d/Unity <c>DeepPink</c> preset; its sRGB source is <c>RGBA(1, 0.07843138, 0.5764706, 1)</c>, stored linear as <c>RGBA(1, 0.00699541, 0.2917707, 1)</c>.</summary>
    public static readonly Color DeepPink = FromSrgb(1f, 0.07843138f, 0.5764706f);

    /// <summary>The Bork2d/Unity <c>DeepSkyBlue</c> preset; its sRGB source is <c>RGBA(0, 0.7490196, 1, 1)</c>, stored linear as <c>RGBA(0, 0.5209957, 1, 1)</c>.</summary>
    public static readonly Color DeepSkyBlue = FromSrgb(0f, 0.7490196f, 1f);

    /// <summary>The Bork2d/Unity <c>DimGray</c> preset; its sRGB source is <c>RGBA(0.4117647, 0.4117647, 0.4117647, 1)</c>, stored linear as <c>RGBA(0.1412633, 0.1412633, 0.1412633, 1)</c>.</summary>
    public static readonly Color DimGray = FromSrgb(0.4117647f, 0.4117647f, 0.4117647f);

    /// <summary>The Bork2d/Unity <c>DodgerBlue</c> preset; its sRGB source is <c>RGBA(0.1176471, 0.5647059, 1, 1)</c>, stored linear as <c>RGBA(0.01298304, 0.2788943, 1, 1)</c>.</summary>
    public static readonly Color DodgerBlue = FromSrgb(0.1176471f, 0.5647059f, 1f);

    /// <summary>The Bork2d/Unity <c>Firebrick</c> preset; its sRGB source is <c>RGBA(0.6980392, 0.1333333, 0.1333333, 1)</c>, stored linear as <c>RGBA(0.4452012, 0.01599629, 0.01599629, 1)</c>.</summary>
    public static readonly Color Firebrick = FromSrgb(0.6980392f, 0.1333333f, 0.1333333f);

    /// <summary>The Bork2d/Unity <c>FloralWhite</c> preset; its sRGB source is <c>RGBA(1, 0.9803922, 0.9411765, 1)</c>, stored linear as <c>RGBA(1, 0.9559735, 0.8713672, 1)</c>.</summary>
    public static readonly Color FloralWhite = FromSrgb(1f, 0.9803922f, 0.9411765f);

    /// <summary>The Bork2d/Unity <c>ForestGreen</c> preset; its sRGB source is <c>RGBA(0.1333333, 0.5450981, 0.1333333, 1)</c>, stored linear as <c>RGBA(0.01599629, 0.258183, 0.01599629, 1)</c>.</summary>
    public static readonly Color ForestGreen = FromSrgb(0.1333333f, 0.5450981f, 0.1333333f);

    /// <summary>The Bork2d/Unity <c>Gainsboro</c> preset; its sRGB source is <c>RGBA(0.8627452, 0.8627452, 0.8627452, 1)</c>, stored linear as <c>RGBA(0.7156939, 0.7156939, 0.7156939, 1)</c>.</summary>
    public static readonly Color Gainsboro = FromSrgb(0.8627452f, 0.8627452f, 0.8627452f);

    /// <summary>The Bork2d/Unity <c>GhostWhite</c> preset; its sRGB source is <c>RGBA(0.9725491, 0.9725491, 1, 1)</c>, stored linear as <c>RGBA(0.9386859, 0.9386859, 1, 1)</c>.</summary>
    public static readonly Color GhostWhite = FromSrgb(0.9725491f, 0.9725491f, 1f);

    /// <summary>The Bork2d/Unity <c>Gold</c> preset; its sRGB source is <c>RGBA(1, 0.8431373, 0, 1)</c>, stored linear as <c>RGBA(1, 0.6795426, 0, 1)</c>.</summary>
    public static readonly Color Gold = FromSrgb(1f, 0.8431373f, 0f);

    /// <summary>The Bork2d/Unity <c>GoldenRod</c> preset; its sRGB source is <c>RGBA(0.854902, 0.6470588, 0.1254902, 1)</c>, stored linear as <c>RGBA(0.7011021, 0.3762621, 0.01444384, 1)</c>.</summary>
    public static readonly Color GoldenRod = FromSrgb(0.854902f, 0.6470588f, 0.1254902f);

    /// <summary>The Bork2d/Unity <c>Gray</c> preset; its sRGB source is <c>RGBA(0.5, 0.5, 0.5, 1)</c>, stored linear as <c>RGBA(0.2140411, 0.2140411, 0.2140411, 1)</c>.</summary>
    public static readonly Color Gray = FromSrgb(0.5f, 0.5f, 0.5f);

    /// <summary>The Bork2d/Unity <c>Gray1</c> preset; its sRGB source is <c>RGBA(0.1, 0.1, 0.1, 1)</c>, stored linear as <c>RGBA(0.01002283, 0.01002283, 0.01002283, 1)</c>.</summary>
    public static readonly Color Gray1 = FromSrgb(0.1f, 0.1f, 0.1f);

    /// <summary>The Bork2d/Unity <c>Gray2</c> preset; its sRGB source is <c>RGBA(0.2, 0.2, 0.2, 1)</c>, stored linear as <c>RGBA(0.03310476, 0.03310476, 0.03310476, 1)</c>.</summary>
    public static readonly Color Gray2 = FromSrgb(0.2f, 0.2f, 0.2f);

    /// <summary>The Bork2d/Unity <c>Gray3</c> preset; its sRGB source is <c>RGBA(0.3, 0.3, 0.3, 1)</c>, stored linear as <c>RGBA(0.07323897, 0.07323897, 0.07323897, 1)</c>.</summary>
    public static readonly Color Gray3 = FromSrgb(0.3f, 0.3f, 0.3f);

    /// <summary>The Bork2d/Unity <c>Gray4</c> preset; its sRGB source is <c>RGBA(0.4, 0.4, 0.4, 1)</c>, stored linear as <c>RGBA(0.1328683, 0.1328683, 0.1328683, 1)</c>.</summary>
    public static readonly Color Gray4 = FromSrgb(0.4f, 0.4f, 0.4f);

    /// <summary>The Bork2d/Unity <c>Gray5</c> preset; its sRGB source is <c>RGBA(0.5, 0.5, 0.5, 1)</c>, stored linear as <c>RGBA(0.2140411, 0.2140411, 0.2140411, 1)</c>.</summary>
    public static readonly Color Gray5 = FromSrgb(0.5f, 0.5f, 0.5f);

    /// <summary>The Bork2d/Unity <c>Gray6</c> preset; its sRGB source is <c>RGBA(0.6, 0.6, 0.6, 1)</c>, stored linear as <c>RGBA(0.3185468, 0.3185468, 0.3185468, 1)</c>.</summary>
    public static readonly Color Gray6 = FromSrgb(0.6f, 0.6f, 0.6f);

    /// <summary>The Bork2d/Unity <c>Gray7</c> preset; its sRGB source is <c>RGBA(0.7, 0.7, 0.7, 1)</c>, stored linear as <c>RGBA(0.4479884, 0.4479884, 0.4479884, 1)</c>.</summary>
    public static readonly Color Gray7 = FromSrgb(0.7f, 0.7f, 0.7f);

    /// <summary>The Bork2d/Unity <c>Gray8</c> preset; its sRGB source is <c>RGBA(0.8, 0.8, 0.8, 1)</c>, stored linear as <c>RGBA(0.6038274, 0.6038274, 0.6038274, 1)</c>.</summary>
    public static readonly Color Gray8 = FromSrgb(0.8f, 0.8f, 0.8f);

    /// <summary>The Bork2d/Unity <c>Gray9</c> preset; its sRGB source is <c>RGBA(0.9, 0.9, 0.9, 1)</c>, stored linear as <c>RGBA(0.7874123, 0.7874123, 0.7874123, 1)</c>.</summary>
    public static readonly Color Gray9 = FromSrgb(0.9f, 0.9f, 0.9f);

    /// <summary>The Bork2d/Unity <c>Green</c> preset; its sRGB source is <c>RGBA(0, 1, 0, 1)</c>, stored linear as <c>RGBA(0, 1, 0, 1)</c>.</summary>
    public static readonly Color Green = FromSrgb(0f, 1f, 0f);

    /// <summary>The Bork2d/Unity <c>GreenYellow</c> preset; its sRGB source is <c>RGBA(0.6784314, 1, 0.1843137, 1)</c>, stored linear as <c>RGBA(0.4178852, 1, 0.02842603, 1)</c>.</summary>
    public static readonly Color GreenYellow = FromSrgb(0.6784314f, 1f, 0.1843137f);

    /// <summary>The Bork2d/Unity <c>Honeydew</c> preset; its sRGB source is <c>RGBA(0.9411765, 1, 0.9411765, 1)</c>, stored linear as <c>RGBA(0.8713672, 1, 0.8713672, 1)</c>.</summary>
    public static readonly Color Honeydew = FromSrgb(0.9411765f, 1f, 0.9411765f);

    /// <summary>The Bork2d/Unity <c>HotPink</c> preset; its sRGB source is <c>RGBA(1, 0.4117647, 0.7058824, 1)</c>, stored linear as <c>RGBA(1, 0.1412633, 0.4564111, 1)</c>.</summary>
    public static readonly Color HotPink = FromSrgb(1f, 0.4117647f, 0.7058824f);

    /// <summary>The Bork2d/Unity <c>IndianRed</c> preset; its sRGB source is <c>RGBA(0.8039216, 0.3607843, 0.3607843, 1)</c>, stored linear as <c>RGBA(0.6104956, 0.1070231, 0.1070231, 1)</c>.</summary>
    public static readonly Color IndianRed = FromSrgb(0.8039216f, 0.3607843f, 0.3607843f);

    /// <summary>The Bork2d/Unity <c>Indigo</c> preset; its sRGB source is <c>RGBA(0.2941177, 0, 0.509804, 1)</c>, stored linear as <c>RGBA(0.07036012, 0, 0.223228, 1)</c>.</summary>
    public static readonly Color Indigo = FromSrgb(0.2941177f, 0f, 0.509804f);

    /// <summary>The Bork2d/Unity <c>Ivory</c> preset; its sRGB source is <c>RGBA(1, 1, 0.9411765, 1)</c>, stored linear as <c>RGBA(1, 1, 0.8713672, 1)</c>.</summary>
    public static readonly Color Ivory = FromSrgb(1f, 1f, 0.9411765f);

    /// <summary>The Bork2d/Unity <c>Khaki</c> preset; its sRGB source is <c>RGBA(0.9411765, 0.9019608, 0.5490196, 1)</c>, stored linear as <c>RGBA(0.8713672, 0.791298, 0.2622507, 1)</c>.</summary>
    public static readonly Color Khaki = FromSrgb(0.9411765f, 0.9019608f, 0.5490196f);

    /// <summary>The Bork2d/Unity <c>Lavender</c> preset; its sRGB source is <c>RGBA(0.9019608, 0.9019608, 0.9803922, 1)</c>, stored linear as <c>RGBA(0.791298, 0.791298, 0.9559735, 1)</c>.</summary>
    public static readonly Color Lavender = FromSrgb(0.9019608f, 0.9019608f, 0.9803922f);

    /// <summary>The Bork2d/Unity <c>LavenderBlush</c> preset; its sRGB source is <c>RGBA(1, 0.9411765, 0.9607844, 1)</c>, stored linear as <c>RGBA(1, 0.8713672, 0.913099, 1)</c>.</summary>
    public static readonly Color LavenderBlush = FromSrgb(1f, 0.9411765f, 0.9607844f);

    /// <summary>The Bork2d/Unity <c>LawnGreen</c> preset; its sRGB source is <c>RGBA(0.4862745, 0.9882354, 0, 1)</c>, stored linear as <c>RGBA(0.2015562, 0.9734457, 0, 1)</c>.</summary>
    public static readonly Color LawnGreen = FromSrgb(0.4862745f, 0.9882354f, 0f);

    /// <summary>The Bork2d/Unity <c>LemonChiffon</c> preset; its sRGB source is <c>RGBA(1, 0.9803922, 0.8039216, 1)</c>, stored linear as <c>RGBA(1, 0.9559735, 0.6104956, 1)</c>.</summary>
    public static readonly Color LemonChiffon = FromSrgb(1f, 0.9803922f, 0.8039216f);

    /// <summary>The Bork2d/Unity <c>LightBlue</c> preset; its sRGB source is <c>RGBA(0.6784314, 0.8470589, 0.9019608, 1)</c>, stored linear as <c>RGBA(0.4178852, 0.6866854, 0.791298, 1)</c>.</summary>
    public static readonly Color LightBlue = FromSrgb(0.6784314f, 0.8470589f, 0.9019608f);

    /// <summary>The Bork2d/Unity <c>LightCoral</c> preset; its sRGB source is <c>RGBA(0.9411765, 0.5019608, 0.5019608, 1)</c>, stored linear as <c>RGBA(0.8713672, 0.2158605, 0.2158605, 1)</c>.</summary>
    public static readonly Color LightCoral = FromSrgb(0.9411765f, 0.5019608f, 0.5019608f);

    /// <summary>The Bork2d/Unity <c>LightCyan</c> preset; its sRGB source is <c>RGBA(0.8784314, 1, 1, 1)</c>, stored linear as <c>RGBA(0.7454044, 1, 1, 1)</c>.</summary>
    public static readonly Color LightCyan = FromSrgb(0.8784314f, 1f, 1f);

    /// <summary>The Bork2d/Unity <c>LightGoldenRod</c> preset; its sRGB source is <c>RGBA(0.9333334, 0.8666667, 0.509804, 1)</c>, stored linear as <c>RGBA(0.8549928, 0.7230552, 0.223228, 1)</c>.</summary>
    public static readonly Color LightGoldenRod = FromSrgb(0.9333334f, 0.8666667f, 0.509804f);

    /// <summary>The Bork2d/Unity <c>LightGoldenRodYellow</c> preset; its sRGB source is <c>RGBA(0.9803922, 0.9803922, 0.8235295, 1)</c>, stored linear as <c>RGBA(0.9559735, 0.9559735, 0.6444799, 1)</c>.</summary>
    public static readonly Color LightGoldenRodYellow = FromSrgb(0.9803922f, 0.9803922f, 0.8235295f);

    /// <summary>The Bork2d/Unity <c>LightGray</c> preset; its sRGB source is <c>RGBA(0.8274511, 0.8274511, 0.8274511, 1)</c>, stored linear as <c>RGBA(0.6514059, 0.6514059, 0.6514059, 1)</c>.</summary>
    public static readonly Color LightGray = FromSrgb(0.8274511f, 0.8274511f, 0.8274511f);

    /// <summary>The Bork2d/Unity <c>LightGreen</c> preset; its sRGB source is <c>RGBA(0.5647059, 0.9333334, 0.5647059, 1)</c>, stored linear as <c>RGBA(0.2788943, 0.8549928, 0.2788943, 1)</c>.</summary>
    public static readonly Color LightGreen = FromSrgb(0.5647059f, 0.9333334f, 0.5647059f);

    /// <summary>The Bork2d/Unity <c>LightPink</c> preset; its sRGB source is <c>RGBA(1, 0.7137255, 0.7568628, 1)</c>, stored linear as <c>RGBA(1, 0.4677838, 0.5332766, 1)</c>.</summary>
    public static readonly Color LightPink = FromSrgb(1f, 0.7137255f, 0.7568628f);

    /// <summary>The Bork2d/Unity <c>LightSalmon</c> preset; its sRGB source is <c>RGBA(1, 0.627451, 0.4784314, 1)</c>, stored linear as <c>RGBA(1, 0.3515327, 0.1946179, 1)</c>.</summary>
    public static readonly Color LightSalmon = FromSrgb(1f, 0.627451f, 0.4784314f);

    /// <summary>The Bork2d/Unity <c>LightSeaGreen</c> preset; its sRGB source is <c>RGBA(0.1254902, 0.6980392, 0.6666667, 1)</c>, stored linear as <c>RGBA(0.01444384, 0.4452012, 0.4019779, 1)</c>.</summary>
    public static readonly Color LightSeaGreen = FromSrgb(0.1254902f, 0.6980392f, 0.6666667f);

    /// <summary>The Bork2d/Unity <c>LightSkyBlue</c> preset; its sRGB source is <c>RGBA(0.5294118, 0.8078432, 0.9803922, 1)</c>, stored linear as <c>RGBA(0.2422812, 0.6172068, 0.9559735, 1)</c>.</summary>
    public static readonly Color LightSkyBlue = FromSrgb(0.5294118f, 0.8078432f, 0.9803922f);

    /// <summary>The Bork2d/Unity <c>LightSlateBlue</c> preset; its sRGB source is <c>RGBA(0.5176471, 0.4392157, 1, 1)</c>, stored linear as <c>RGBA(0.2307401, 0.1620294, 1, 1)</c>.</summary>
    public static readonly Color LightSlateBlue = FromSrgb(0.5176471f, 0.4392157f, 1f);

    /// <summary>The Bork2d/Unity <c>LightSlateGray</c> preset; its sRGB source is <c>RGBA(0.4666667, 0.5333334, 0.6, 1)</c>, stored linear as <c>RGBA(0.184475, 0.2462015, 0.3185468, 1)</c>.</summary>
    public static readonly Color LightSlateGray = FromSrgb(0.4666667f, 0.5333334f, 0.6f);

    /// <summary>The Bork2d/Unity <c>LightSteelBlue</c> preset; its sRGB source is <c>RGBA(0.6901961, 0.7686275, 0.8705883, 1)</c>, stored linear as <c>RGBA(0.4341537, 0.5520116, 0.730461, 1)</c>.</summary>
    public static readonly Color LightSteelBlue = FromSrgb(0.6901961f, 0.7686275f, 0.8705883f);

    /// <summary>The Bork2d/Unity <c>LightYellow</c> preset; its sRGB source is <c>RGBA(1, 1, 0.8784314, 1)</c>, stored linear as <c>RGBA(1, 1, 0.7454044, 1)</c>.</summary>
    public static readonly Color LightYellow = FromSrgb(1f, 1f, 0.8784314f);

    /// <summary>The Bork2d/Unity <c>LimeGreen</c> preset; its sRGB source is <c>RGBA(0.1960784, 0.8039216, 0.1960784, 1)</c>, stored linear as <c>RGBA(0.03189602, 0.6104956, 0.03189602, 1)</c>.</summary>
    public static readonly Color LimeGreen = FromSrgb(0.1960784f, 0.8039216f, 0.1960784f);

    /// <summary>The Bork2d/Unity <c>Linen</c> preset; its sRGB source is <c>RGBA(0.9803922, 0.9411765, 0.9019608, 1)</c>, stored linear as <c>RGBA(0.9559735, 0.8713672, 0.791298, 1)</c>.</summary>
    public static readonly Color Linen = FromSrgb(0.9803922f, 0.9411765f, 0.9019608f);

    /// <summary>The Bork2d/Unity <c>Magenta</c> preset; its sRGB source is <c>RGBA(1, 0, 1, 1)</c>, stored linear as <c>RGBA(1, 0, 1, 1)</c>.</summary>
    public static readonly Color Magenta = FromSrgb(1f, 0f, 1f);

    /// <summary>The Bork2d/Unity <c>Maroon</c> preset; its sRGB source is <c>RGBA(0.6901961, 0.1882353, 0.3764706, 1)</c>, stored linear as <c>RGBA(0.4341537, 0.02955683, 0.1169707, 1)</c>.</summary>
    public static readonly Color Maroon = FromSrgb(0.6901961f, 0.1882353f, 0.3764706f);

    /// <summary>The Bork2d/Unity <c>MediumAquamarine</c> preset; its sRGB source is <c>RGBA(0.4, 0.8039216, 0.6666667, 1)</c>, stored linear as <c>RGBA(0.1328683, 0.6104956, 0.4019779, 1)</c>.</summary>
    public static readonly Color MediumAquamarine = FromSrgb(0.4f, 0.8039216f, 0.6666667f);

    /// <summary>The Bork2d/Unity <c>MediumBlue</c> preset; its sRGB source is <c>RGBA(0, 0, 0.8039216, 1)</c>, stored linear as <c>RGBA(0, 0, 0.6104956, 1)</c>.</summary>
    public static readonly Color MediumBlue = FromSrgb(0f, 0f, 0.8039216f);

    /// <summary>The Bork2d/Unity <c>MediumOrchid</c> preset; its sRGB source is <c>RGBA(0.7294118, 0.3333333, 0.8274511, 1)</c>, stored linear as <c>RGBA(0.4910209, 0.09084171, 0.6514059, 1)</c>.</summary>
    public static readonly Color MediumOrchid = FromSrgb(0.7294118f, 0.3333333f, 0.8274511f);

    /// <summary>The Bork2d/Unity <c>MediumPurple</c> preset; its sRGB source is <c>RGBA(0.5764706, 0.4392157, 0.8588236, 1)</c>, stored linear as <c>RGBA(0.2917707, 0.1620294, 0.708376, 1)</c>.</summary>
    public static readonly Color MediumPurple = FromSrgb(0.5764706f, 0.4392157f, 0.8588236f);

    /// <summary>The Bork2d/Unity <c>MediumSeaGreen</c> preset; its sRGB source is <c>RGBA(0.2352941, 0.7019608, 0.4431373, 1)</c>, stored linear as <c>RGBA(0.0451862, 0.4507858, 0.1651322, 1)</c>.</summary>
    public static readonly Color MediumSeaGreen = FromSrgb(0.2352941f, 0.7019608f, 0.4431373f);

    /// <summary>The Bork2d/Unity <c>MediumSlateBlue</c> preset; its sRGB source is <c>RGBA(0.482353, 0.4078432, 0.9333334, 1)</c>, stored linear as <c>RGBA(0.1980693, 0.1384317, 0.8549928, 1)</c>.</summary>
    public static readonly Color MediumSlateBlue = FromSrgb(0.482353f, 0.4078432f, 0.9333334f);

    /// <summary>The Bork2d/Unity <c>MediumSpringGreen</c> preset; its sRGB source is <c>RGBA(0, 0.9803922, 0.6039216, 1)</c>, stored linear as <c>RGBA(0, 0.9559735, 0.3231432, 1)</c>.</summary>
    public static readonly Color MediumSpringGreen = FromSrgb(0f, 0.9803922f, 0.6039216f);

    /// <summary>The Bork2d/Unity <c>MediumTurquoise</c> preset; its sRGB source is <c>RGBA(0.282353, 0.8196079, 0.8000001, 1)</c>, stored linear as <c>RGBA(0.06480331, 0.637597, 0.6038275, 1)</c>.</summary>
    public static readonly Color MediumTurquoise = FromSrgb(0.282353f, 0.8196079f, 0.8000001f);

    /// <summary>The Bork2d/Unity <c>MediumVioletRed</c> preset; its sRGB source is <c>RGBA(0.7803922, 0.08235294, 0.5215687, 1)</c>, stored linear as <c>RGBA(0.571125, 0.007499032, 0.2345507, 1)</c>.</summary>
    public static readonly Color MediumVioletRed = FromSrgb(0.7803922f, 0.08235294f, 0.5215687f);

    /// <summary>The Bork2d/Unity <c>MidnightBlue</c> preset; its sRGB source is <c>RGBA(0.09803922, 0.09803922, 0.4392157, 1)</c>, stored linear as <c>RGBA(0.009721218, 0.009721218, 0.1620294, 1)</c>.</summary>
    public static readonly Color MidnightBlue = FromSrgb(0.09803922f, 0.09803922f, 0.4392157f);

    /// <summary>The Bork2d/Unity <c>MintCream</c> preset; its sRGB source is <c>RGBA(0.9607844, 1, 0.9803922, 1)</c>, stored linear as <c>RGBA(0.913099, 1, 0.9559735, 1)</c>.</summary>
    public static readonly Color MintCream = FromSrgb(0.9607844f, 1f, 0.9803922f);

    /// <summary>The Bork2d/Unity <c>MistyRose</c> preset; its sRGB source is <c>RGBA(1, 0.8941177, 0.882353, 1)</c>, stored linear as <c>RGBA(1, 0.7758225, 0.7529424, 1)</c>.</summary>
    public static readonly Color MistyRose = FromSrgb(1f, 0.8941177f, 0.882353f);

    /// <summary>The Bork2d/Unity <c>Moccasin</c> preset; its sRGB source is <c>RGBA(1, 0.8941177, 0.7098039, 1)</c>, stored linear as <c>RGBA(1, 0.7758225, 0.462077, 1)</c>.</summary>
    public static readonly Color Moccasin = FromSrgb(1f, 0.8941177f, 0.7098039f);

    /// <summary>The Bork2d/Unity <c>NavajoWhite</c> preset; its sRGB source is <c>RGBA(1, 0.8705883, 0.6784314, 1)</c>, stored linear as <c>RGBA(1, 0.730461, 0.4178852, 1)</c>.</summary>
    public static readonly Color NavajoWhite = FromSrgb(1f, 0.8705883f, 0.6784314f);

    /// <summary>The Bork2d/Unity <c>NavyBlue</c> preset; its sRGB source is <c>RGBA(0, 0, 0.5019608, 1)</c>, stored linear as <c>RGBA(0, 0, 0.2158605, 1)</c>.</summary>
    public static readonly Color NavyBlue = FromSrgb(0f, 0f, 0.5019608f);

    /// <summary>The Bork2d/Unity <c>OldLace</c> preset; its sRGB source is <c>RGBA(0.9921569, 0.9607844, 0.9019608, 1)</c>, stored linear as <c>RGBA(0.9822509, 0.913099, 0.791298, 1)</c>.</summary>
    public static readonly Color OldLace = FromSrgb(0.9921569f, 0.9607844f, 0.9019608f);

    /// <summary>The Bork2d/Unity <c>Olive</c> preset; its sRGB source is <c>RGBA(0.5019608, 0.5019608, 0, 1)</c>, stored linear as <c>RGBA(0.2158605, 0.2158605, 0, 1)</c>.</summary>
    public static readonly Color Olive = FromSrgb(0.5019608f, 0.5019608f, 0f);

    /// <summary>The Bork2d/Unity <c>OliveDrab</c> preset; its sRGB source is <c>RGBA(0.4196079, 0.5568628, 0.1372549, 1)</c>, stored linear as <c>RGBA(0.1470273, 0.2704979, 0.01680738, 1)</c>.</summary>
    public static readonly Color OliveDrab = FromSrgb(0.4196079f, 0.5568628f, 0.1372549f);

    /// <summary>The Bork2d/Unity <c>Orange</c> preset; its sRGB source is <c>RGBA(1, 0.6470588, 0, 1)</c>, stored linear as <c>RGBA(1, 0.3762621, 0, 1)</c>.</summary>
    public static readonly Color Orange = FromSrgb(1f, 0.6470588f, 0f);

    /// <summary>The Bork2d/Unity <c>OrangeRed</c> preset; its sRGB source is <c>RGBA(1, 0.2705882, 0, 1)</c>, stored linear as <c>RGBA(1, 0.05951123, 0, 1)</c>.</summary>
    public static readonly Color OrangeRed = FromSrgb(1f, 0.2705882f, 0f);

    /// <summary>The Bork2d/Unity <c>Orchid</c> preset; its sRGB source is <c>RGBA(0.854902, 0.4392157, 0.8392158, 1)</c>, stored linear as <c>RGBA(0.7011021, 0.1620294, 0.6724434, 1)</c>.</summary>
    public static readonly Color Orchid = FromSrgb(0.854902f, 0.4392157f, 0.8392158f);

    /// <summary>The Bork2d/Unity <c>PaleGoldenRod</c> preset; its sRGB source is <c>RGBA(0.9333334, 0.909804, 0.6666667, 1)</c>, stored linear as <c>RGBA(0.8549928, 0.8069525, 0.4019779, 1)</c>.</summary>
    public static readonly Color PaleGoldenRod = FromSrgb(0.9333334f, 0.909804f, 0.6666667f);

    /// <summary>The Bork2d/Unity <c>PaleGreen</c> preset; its sRGB source is <c>RGBA(0.5960785, 0.9843138, 0.5960785, 1)</c>, stored linear as <c>RGBA(0.3139888, 0.9646866, 0.3139888, 1)</c>.</summary>
    public static readonly Color PaleGreen = FromSrgb(0.5960785f, 0.9843138f, 0.5960785f);

    /// <summary>The Bork2d/Unity <c>PaleTurquoise</c> preset; its sRGB source is <c>RGBA(0.6862745, 0.9333334, 0.9333334, 1)</c>, stored linear as <c>RGBA(0.4286906, 0.8549928, 0.8549928, 1)</c>.</summary>
    public static readonly Color PaleTurquoise = FromSrgb(0.6862745f, 0.9333334f, 0.9333334f);

    /// <summary>The Bork2d/Unity <c>PaleVioletRed</c> preset; its sRGB source is <c>RGBA(0.8588236, 0.4392157, 0.5764706, 1)</c>, stored linear as <c>RGBA(0.708376, 0.1620294, 0.2917707, 1)</c>.</summary>
    public static readonly Color PaleVioletRed = FromSrgb(0.8588236f, 0.4392157f, 0.5764706f);

    /// <summary>The Bork2d/Unity <c>PapayaWhip</c> preset; its sRGB source is <c>RGBA(1, 0.937255, 0.8352942, 1)</c>, stored linear as <c>RGBA(1, 0.8631576, 0.6653875, 1)</c>.</summary>
    public static readonly Color PapayaWhip = FromSrgb(1f, 0.937255f, 0.8352942f);

    /// <summary>The Bork2d/Unity <c>PeachPuff</c> preset; its sRGB source is <c>RGBA(1, 0.854902, 0.7254902, 1)</c>, stored linear as <c>RGBA(1, 0.7011021, 0.48515, 1)</c>.</summary>
    public static readonly Color PeachPuff = FromSrgb(1f, 0.854902f, 0.7254902f);

    /// <summary>The Bork2d/Unity <c>Peru</c> preset; its sRGB source is <c>RGBA(0.8039216, 0.5215687, 0.2470588, 1)</c>, stored linear as <c>RGBA(0.6104956, 0.2345507, 0.04970655, 1)</c>.</summary>
    public static readonly Color Peru = FromSrgb(0.8039216f, 0.5215687f, 0.2470588f);

    /// <summary>The Bork2d/Unity <c>Pink</c> preset; its sRGB source is <c>RGBA(1, 0.7529413, 0.7960785, 1)</c>, stored linear as <c>RGBA(1, 0.5271155, 0.597202, 1)</c>.</summary>
    public static readonly Color Pink = FromSrgb(1f, 0.7529413f, 0.7960785f);

    /// <summary>The Bork2d/Unity <c>Plum</c> preset; its sRGB source is <c>RGBA(0.8666667, 0.627451, 0.8666667, 1)</c>, stored linear as <c>RGBA(0.7230552, 0.3515327, 0.7230552, 1)</c>.</summary>
    public static readonly Color Plum = FromSrgb(0.8666667f, 0.627451f, 0.8666667f);

    /// <summary>The Bork2d/Unity <c>PowderBlue</c> preset; its sRGB source is <c>RGBA(0.6901961, 0.8784314, 0.9019608, 1)</c>, stored linear as <c>RGBA(0.4341537, 0.7454044, 0.791298, 1)</c>.</summary>
    public static readonly Color PowderBlue = FromSrgb(0.6901961f, 0.8784314f, 0.9019608f);

    /// <summary>The Bork2d/Unity <c>Purple</c> preset; its sRGB source is <c>RGBA(0.627451, 0.1254902, 0.9411765, 1)</c>, stored linear as <c>RGBA(0.3515327, 0.01444384, 0.8713672, 1)</c>.</summary>
    public static readonly Color Purple = FromSrgb(0.627451f, 0.1254902f, 0.9411765f);

    /// <summary>The Bork2d/Unity <c>RebeccaPurple</c> preset; its sRGB source is <c>RGBA(0.4, 0.2, 0.6, 1)</c>, stored linear as <c>RGBA(0.1328683, 0.03310476, 0.3185468, 1)</c>.</summary>
    public static readonly Color RebeccaPurple = FromSrgb(0.4f, 0.2f, 0.6f);

    /// <summary>The Bork2d/Unity <c>Red</c> preset; its sRGB source is <c>RGBA(1, 0, 0, 1)</c>, stored linear as <c>RGBA(1, 0, 0, 1)</c>.</summary>
    public static readonly Color Red = FromSrgb(1f, 0f, 0f);

    /// <summary>The Bork2d/Unity <c>RosyBrown</c> preset; its sRGB source is <c>RGBA(0.7372549, 0.5607843, 0.5607843, 1)</c>, stored linear as <c>RGBA(0.5028866, 0.2746773, 0.2746773, 1)</c>.</summary>
    public static readonly Color RosyBrown = FromSrgb(0.7372549f, 0.5607843f, 0.5607843f);

    /// <summary>The Bork2d/Unity <c>RoyalBlue</c> preset; its sRGB source is <c>RGBA(0.254902, 0.4117647, 0.882353, 1)</c>, stored linear as <c>RGBA(0.05286067, 0.1412633, 0.7529424, 1)</c>.</summary>
    public static readonly Color RoyalBlue = FromSrgb(0.254902f, 0.4117647f, 0.882353f);

    /// <summary>The Bork2d/Unity <c>SaddleBrown</c> preset; its sRGB source is <c>RGBA(0.5450981, 0.2705882, 0.07450981, 1)</c>, stored linear as <c>RGBA(0.258183, 0.05951123, 0.006512091, 1)</c>.</summary>
    public static readonly Color SaddleBrown = FromSrgb(0.5450981f, 0.2705882f, 0.07450981f);

    /// <summary>The Bork2d/Unity <c>Salmon</c> preset; its sRGB source is <c>RGBA(0.9803922, 0.5019608, 0.4470589, 1)</c>, stored linear as <c>RGBA(0.9559735, 0.2158605, 0.1682695, 1)</c>.</summary>
    public static readonly Color Salmon = FromSrgb(0.9803922f, 0.5019608f, 0.4470589f);

    /// <summary>The Bork2d/Unity <c>SandyBrown</c> preset; its sRGB source is <c>RGBA(0.9568628, 0.6431373, 0.3764706, 1)</c>, stored linear as <c>RGBA(0.9046614, 0.3712378, 0.1169707, 1)</c>.</summary>
    public static readonly Color SandyBrown = FromSrgb(0.9568628f, 0.6431373f, 0.3764706f);

    /// <summary>The Bork2d/Unity <c>SeaGreen</c> preset; its sRGB source is <c>RGBA(0.1803922, 0.5450981, 0.3411765, 1)</c>, stored linear as <c>RGBA(0.0273209, 0.258183, 0.0953075, 1)</c>.</summary>
    public static readonly Color SeaGreen = FromSrgb(0.1803922f, 0.5450981f, 0.3411765f);

    /// <summary>The Bork2d/Unity <c>Seashell</c> preset; its sRGB source is <c>RGBA(1, 0.9607844, 0.9333334, 1)</c>, stored linear as <c>RGBA(1, 0.913099, 0.8549928, 1)</c>.</summary>
    public static readonly Color Seashell = FromSrgb(1f, 0.9607844f, 0.9333334f);

    /// <summary>The Bork2d/Unity <c>Sienna</c> preset; its sRGB source is <c>RGBA(0.627451, 0.3215686, 0.1764706, 1)</c>, stored linear as <c>RGBA(0.3515327, 0.0843762, 0.02624122, 1)</c>.</summary>
    public static readonly Color Sienna = FromSrgb(0.627451f, 0.3215686f, 0.1764706f);

    /// <summary>The Bork2d/Unity <c>Silver</c> preset; its sRGB source is <c>RGBA(0.7529413, 0.7529413, 0.7529413, 1)</c>, stored linear as <c>RGBA(0.5271155, 0.5271155, 0.5271155, 1)</c>.</summary>
    public static readonly Color Silver = FromSrgb(0.7529413f, 0.7529413f, 0.7529413f);

    /// <summary>The Bork2d/Unity <c>SkyBlue</c> preset; its sRGB source is <c>RGBA(0.5294118, 0.8078432, 0.9215687, 1)</c>, stored linear as <c>RGBA(0.2422812, 0.6172068, 0.8307701, 1)</c>.</summary>
    public static readonly Color SkyBlue = FromSrgb(0.5294118f, 0.8078432f, 0.9215687f);

    /// <summary>The Bork2d/Unity <c>SlateBlue</c> preset; its sRGB source is <c>RGBA(0.4156863, 0.3529412, 0.8039216, 1)</c>, stored linear as <c>RGBA(0.1441285, 0.1022417, 0.6104956, 1)</c>.</summary>
    public static readonly Color SlateBlue = FromSrgb(0.4156863f, 0.3529412f, 0.8039216f);

    /// <summary>The Bork2d/Unity <c>SlateGray</c> preset; its sRGB source is <c>RGBA(0.4392157, 0.5019608, 0.5647059, 1)</c>, stored linear as <c>RGBA(0.1620294, 0.2158605, 0.2788943, 1)</c>.</summary>
    public static readonly Color SlateGray = FromSrgb(0.4392157f, 0.5019608f, 0.5647059f);

    /// <summary>The Bork2d/Unity <c>Snow</c> preset; its sRGB source is <c>RGBA(1, 0.9803922, 0.9803922, 1)</c>, stored linear as <c>RGBA(1, 0.9559735, 0.9559735, 1)</c>.</summary>
    public static readonly Color Snow = FromSrgb(1f, 0.9803922f, 0.9803922f);

    /// <summary>The Bork2d/Unity <c>SoftBlue</c> preset; its sRGB source is <c>RGBA(0.1882353, 0.682353, 0.7490196, 1)</c>, stored linear as <c>RGBA(0.02955683, 0.4232678, 0.5209957, 1)</c>.</summary>
    public static readonly Color SoftBlue = FromSrgb(0.1882353f, 0.682353f, 0.7490196f);

    /// <summary>The Bork2d/Unity <c>SoftGreen</c> preset; its sRGB source is <c>RGBA(0.5490196, 0.7882354, 0.1411765, 1)</c>, stored linear as <c>RGBA(0.2622507, 0.5840787, 0.01764196, 1)</c>.</summary>
    public static readonly Color SoftGreen = FromSrgb(0.5490196f, 0.7882354f, 0.1411765f);

    /// <summary>The Bork2d/Unity <c>SoftRed</c> preset; its sRGB source is <c>RGBA(0.8627452, 0.1921569, 0.1960784, 1)</c>, stored linear as <c>RGBA(0.7156939, 0.03071345, 0.03189602, 1)</c>.</summary>
    public static readonly Color SoftRed = FromSrgb(0.8627452f, 0.1921569f, 0.1960784f);

    /// <summary>The Bork2d/Unity <c>SoftYellow</c> preset; its sRGB source is <c>RGBA(1, 0.9333334, 0.5490196, 1)</c>, stored linear as <c>RGBA(1, 0.8549928, 0.2622507, 1)</c>.</summary>
    public static readonly Color SoftYellow = FromSrgb(1f, 0.9333334f, 0.5490196f);

    /// <summary>The Bork2d/Unity <c>SpringGreen</c> preset; its sRGB source is <c>RGBA(0, 1, 0.4980392, 1)</c>, stored linear as <c>RGBA(0, 1, 0.2122307, 1)</c>.</summary>
    public static readonly Color SpringGreen = FromSrgb(0f, 1f, 0.4980392f);

    /// <summary>The Bork2d/Unity <c>SteelBlue</c> preset; its sRGB source is <c>RGBA(0.2745098, 0.509804, 0.7058824, 1)</c>, stored linear as <c>RGBA(0.06124606, 0.223228, 0.4564111, 1)</c>.</summary>
    public static readonly Color SteelBlue = FromSrgb(0.2745098f, 0.509804f, 0.7058824f);

    /// <summary>The Bork2d/Unity <c>Tan</c> preset; its sRGB source is <c>RGBA(0.8235295, 0.7058824, 0.5490196, 1)</c>, stored linear as <c>RGBA(0.6444799, 0.4564111, 0.2622507, 1)</c>.</summary>
    public static readonly Color Tan = FromSrgb(0.8235295f, 0.7058824f, 0.5490196f);

    /// <summary>The Bork2d/Unity <c>Teal</c> preset; its sRGB source is <c>RGBA(0, 0.5019608, 0.5019608, 1)</c>, stored linear as <c>RGBA(0, 0.2158605, 0.2158605, 1)</c>.</summary>
    public static readonly Color Teal = FromSrgb(0f, 0.5019608f, 0.5019608f);

    /// <summary>The Bork2d/Unity <c>Thistle</c> preset; its sRGB source is <c>RGBA(0.8470589, 0.7490196, 0.8470589, 1)</c>, stored linear as <c>RGBA(0.6866854, 0.5209957, 0.6866854, 1)</c>.</summary>
    public static readonly Color Thistle = FromSrgb(0.8470589f, 0.7490196f, 0.8470589f);

    /// <summary>The Bork2d/Unity <c>Tomato</c> preset; its sRGB source is <c>RGBA(1, 0.3882353, 0.2784314, 1)</c>, stored linear as <c>RGBA(1, 0.1247718, 0.06301003, 1)</c>.</summary>
    public static readonly Color Tomato = FromSrgb(1f, 0.3882353f, 0.2784314f);

    /// <summary>The Bork2d/Unity <c>Turquoise</c> preset; its sRGB source is <c>RGBA(0.2509804, 0.8784314, 0.8156863, 1)</c>, stored linear as <c>RGBA(0.05126947, 0.7454044, 0.6307572, 1)</c>.</summary>
    public static readonly Color Turquoise = FromSrgb(0.2509804f, 0.8784314f, 0.8156863f);

    /// <summary>The Bork2d/Unity <c>Violet</c> preset; its sRGB source is <c>RGBA(0.9333334, 0.509804, 0.9333334, 1)</c>, stored linear as <c>RGBA(0.8549928, 0.223228, 0.8549928, 1)</c>.</summary>
    public static readonly Color Violet = FromSrgb(0.9333334f, 0.509804f, 0.9333334f);

    /// <summary>The Bork2d/Unity <c>VioletRed</c> preset; its sRGB source is <c>RGBA(0.8156863, 0.1254902, 0.5647059, 1)</c>, stored linear as <c>RGBA(0.6307572, 0.01444384, 0.2788943, 1)</c>.</summary>
    public static readonly Color VioletRed = FromSrgb(0.8156863f, 0.1254902f, 0.5647059f);

    /// <summary>The Bork2d/Unity <c>Wheat</c> preset; its sRGB source is <c>RGBA(0.9607844, 0.8705883, 0.7019608, 1)</c>, stored linear as <c>RGBA(0.913099, 0.730461, 0.4507858, 1)</c>.</summary>
    public static readonly Color Wheat = FromSrgb(0.9607844f, 0.8705883f, 0.7019608f);

    /// <summary>The Bork2d/Unity <c>White</c> preset; its sRGB source is <c>RGBA(1, 1, 1, 1)</c>, stored linear as <c>RGBA(1, 1, 1, 1)</c>.</summary>
    public static readonly Color White = FromSrgb(1f, 1f, 1f);

    /// <summary>The Bork2d/Unity <c>WhiteSmoke</c> preset; its sRGB source is <c>RGBA(0.9607844, 0.9607844, 0.9607844, 1)</c>, stored linear as <c>RGBA(0.913099, 0.913099, 0.913099, 1)</c>.</summary>
    public static readonly Color WhiteSmoke = FromSrgb(0.9607844f, 0.9607844f, 0.9607844f);

    /// <summary>The Bork2d/Unity <c>Yellow</c> preset; its sRGB source is <c>RGBA(1, 0.9215686, 0.01568628, 1)</c>, stored linear as <c>RGBA(1, 0.83077, 0.001214108, 1)</c>.</summary>
    public static readonly Color Yellow = FromSrgb(1f, 235f / 255f, 4f / 255f);

    /// <summary>The Bork2d/Unity <c>YellowGreen</c> preset; its sRGB source is <c>RGBA(0.6039216, 0.8039216, 0.1960784, 1)</c>, stored linear as <c>RGBA(0.3231432, 0.6104956, 0.03189602, 1)</c>.</summary>
    public static readonly Color YellowGreen = FromSrgb(0.6039216f, 0.8039216f, 0.1960784f);

    /// <summary>The Bork2d/Unity <c>Gray</c> preset under its British spelling; an alias of <see cref="Gray"/>.</summary>
    public static readonly Color Grey = Gray;

    /// <summary>The fully transparent black preset; an alias of <see cref="Clear"/>. Alpha stays <c>0</c>.</summary>
    public static readonly Color Transparent = Clear;

    /// <summary>The Bork2d/Unity <c>Yellow</c> preset under its friendly alias; an alias of <see cref="Yellow"/>.</summary>
    public static readonly Color YellowNice = Yellow;

    /// <summary>
    /// Initializes a color from linear red, green, blue, and alpha components.
    /// </summary>
    /// <param name="r">The linear red component.</param>
    /// <param name="g">The linear green component.</param>
    /// <param name="b">The linear blue component.</param>
    /// <param name="a">The alpha component.</param>
    public Color(float r, float g, float b, float a) {
        _rgba = new Vector4(r, g, b, a);
    }

    /// <summary>
    /// Initializes an opaque color from linear red, green, and blue components.
    /// </summary>
    /// <param name="r">The linear red component.</param>
    /// <param name="g">The linear green component.</param>
    /// <param name="b">The linear blue component.</param>
    public Color(float r, float g, float b) {
        _rgba = new Vector4(r, g, b, 1f);
    }

    private Color(Vector4 rgba) {
        _rgba = rgba;
    }

    /// <summary>
    /// Gets the linear red component.
    /// </summary>
    public float R => _rgba.X;

    /// <summary>
    /// Gets the linear green component.
    /// </summary>
    public float G => _rgba.Y;

    /// <summary>
    /// Gets the linear blue component.
    /// </summary>
    public float B => _rgba.Z;

    /// <summary>
    /// Gets the alpha component.
    /// </summary>
    public float A => _rgba.W;

    /// <summary>
    /// Gets the component at the given index, where zero is red and three is alpha.
    /// </summary>
    /// <param name="index">The component index in the range 0 through 3.</param>
    /// <returns>The component at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is less than zero or greater than three.
    /// </exception>
    public float this[int index] {
        get {
            return index switch {
                0 => _rgba.X,
                1 => _rgba.Y,
                2 => _rgba.Z,
                3 => _rgba.W,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "Color component index must be between 0 and 3."),
            };
        }
    }

    /// <summary>
    /// Gets the perceived luminance of the color using the standard
    /// <c>0.299 R + 0.587 G + 0.114 B</c> weights. Alpha is ignored.
    /// </summary>
    public float Grayscale =>
        (GRAYSCALE_RED_WEIGHT * _rgba.X) + (GRAYSCALE_GREEN_WEIGHT * _rgba.Y) + (GRAYSCALE_BLUE_WEIGHT * _rgba.Z);

    /// <summary>
    /// Gets the largest of the red, green, blue, and alpha components.
    /// </summary>
    public float MaxColorComponent =>
        MathF.Max(MathF.Max(_rgba.X, _rgba.Y), MathF.Max(_rgba.Z, _rgba.W));

    /// <summary>
    /// Linearly interpolates two colors in linear space, clamping the interpolation factor.
    /// </summary>
    /// <param name="a">The color at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The color at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor, clamped to <c>[0, 1]</c>.</param>
    /// <returns>The interpolated color.</returns>
    public static Color Lerp(Color a, Color b, float t) =>
        new(Vector4.Lerp(a._rgba, b._rgba, Math.Clamp(t, 0f, 1f)));

    /// <summary>
    /// Linearly interpolates two colors in linear space without clamping the interpolation factor.
    /// </summary>
    /// <param name="a">The color at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The color at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor, which may lie outside <c>[0, 1]</c>.</param>
    /// <returns>The interpolated color, which may extrapolate beyond the endpoints.</returns>
    public static Color LerpUnclamped(Color a, Color b, float t) => new(Vector4.Lerp(a._rgba, b._rgba, t));

    /// <summary>
    /// Converts the color to the OkLab perceptual color space.
    /// </summary>
    /// <param name="l">The OkLab lightness component.</param>
    /// <param name="a">The OkLab green-red component.</param>
    /// <param name="b">The OkLab blue-yellow component.</param>
    public void ToOkLab(out float l, out float a, out float b) {
        float red = _rgba.X;
        float green = _rgba.Y;
        float blue = _rgba.Z;

        float lmsL = (0.4122214708f * red) + (0.5363325363f * green) + (0.0514459929f * blue);
        float lmsM = (0.2119034982f * red) + (0.6806995451f * green) + (0.1073969566f * blue);
        float lmsS = (0.0883024619f * red) + (0.2817188376f * green) + (0.6299787005f * blue);

        float lRoot = MathF.Cbrt(lmsL);
        float mRoot = MathF.Cbrt(lmsM);
        float sRoot = MathF.Cbrt(lmsS);

        l = (0.2104542553f * lRoot) + (0.7936177850f * mRoot) - (0.0040720468f * sRoot);
        a = (1.9779984951f * lRoot) - (2.4285922050f * mRoot) + (0.4505937099f * sRoot);
        b = (0.0259040371f * lRoot) + (0.7827717662f * mRoot) - (0.8086757660f * sRoot);
    }

    /// <summary>
    /// Creates a linear color from OkLab components.
    /// </summary>
    /// <param name="l">The OkLab lightness component.</param>
    /// <param name="a">The OkLab green-red component.</param>
    /// <param name="b">The OkLab blue-yellow component.</param>
    /// <param name="alpha">The alpha component.</param>
    /// <returns>The linear color.</returns>
    public static Color FromOkLab(float l, float a, float b, float alpha = 1f) {
        float lRoot = l + (0.3963377774f * a) + (0.2158037573f * b);
        float mRoot = l - (0.1055613458f * a) - (0.0638541728f * b);
        float sRoot = l - (0.0894841775f * a) - (1.2914855480f * b);

        float lmsL = lRoot * lRoot * lRoot;
        float lmsM = mRoot * mRoot * mRoot;
        float lmsS = sRoot * sRoot * sRoot;

        float red = (4.0767416621f * lmsL) - (3.3077115913f * lmsM) + (0.2309699292f * lmsS);
        float green = (-1.2684380046f * lmsL) + (2.6097574011f * lmsM) - (0.3413193965f * lmsS);
        float blue = (-0.0041960863f * lmsL) - (0.7034186147f * lmsM) + (1.7076147010f * lmsS);

        return new Color(red, green, blue, alpha);
    }

    /// <summary>
    /// Encodes the linear red, green, and blue components with the piecewise sRGB transfer function.
    /// Alpha is passed through unchanged.
    /// </summary>
    /// <returns>
    /// The sRGB-encoded RGBA components. The result is display-encoded, so it is a
    /// <see cref="Vector4"/> rather than a linear <see cref="Color"/>.
    /// </returns>
    /// <remarks>
    /// The result is not clamped. Components above <c>1</c> and below <c>0</c> extrapolate through
    /// the same piecewise formula; callers that write to a display or an 8-bit target must clamp.
    /// </remarks>
    public Vector4 ToSrgb() {
        return new Vector4(
            EncodeSrgbComponent(_rgba.X),
            EncodeSrgbComponent(_rgba.Y),
            EncodeSrgbComponent(_rgba.Z),
            _rgba.W);
    }

    /// <summary>
    /// Creates a linear color from sRGB-encoded red, green, blue, and alpha components.
    /// </summary>
    /// <param name="r">The sRGB-encoded red component in <c>[0, 1]</c>.</param>
    /// <param name="g">The sRGB-encoded green component in <c>[0, 1]</c>.</param>
    /// <param name="b">The sRGB-encoded blue component in <c>[0, 1]</c>.</param>
    /// <param name="a">The linear alpha component in <c>[0, 1]</c>; alpha is not gamma-encoded.</param>
    /// <returns>The linear color.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is not finite or lies outside <c>[0, 1]</c>.
    /// </exception>
    public static Color FromSrgb(float r, float g, float b, float a = 1f) {
        ValidateSrgbComponent(r, nameof(r));
        ValidateSrgbComponent(g, nameof(g));
        ValidateSrgbComponent(b, nameof(b));
        ValidateSrgbComponent(a, nameof(a));

        return new Color(
            DecodeSrgbComponent(r),
            DecodeSrgbComponent(g),
            DecodeSrgbComponent(b),
            a);
    }

    /// <summary>
    /// Creates a linear color from 8-bit sRGB-encoded red, green, blue, and alpha components.
    /// </summary>
    /// <param name="r">The sRGB-encoded red byte.</param>
    /// <param name="g">The sRGB-encoded green byte.</param>
    /// <param name="b">The sRGB-encoded blue byte.</param>
    /// <param name="a">The linear alpha byte; alpha is not gamma-encoded.</param>
    /// <returns>The linear color.</returns>
    /// <remarks>
    /// Each red, green, and blue byte is converted as <c>value / 255</c> and decoded through the
    /// piecewise sRGB transfer function. Alpha is converted the same way without the transfer
    /// function. The conversion is exact and deterministic for a given byte quadruple.
    /// </remarks>
    public static Color FromSrgbBytes(byte r, byte g, byte b, byte a = 255) {
        return new Color(
            DecodeSrgbComponent(r / 255f),
            DecodeSrgbComponent(g / 255f),
            DecodeSrgbComponent(b / 255f),
            a / 255f);
    }

    /// <summary>
    /// Interpolates two colors in OkLab space, clamping the interpolation factor.
    /// </summary>
    /// <param name="a">The color at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The color at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor, clamped to <c>[0, 1]</c>.</param>
    /// <returns>The interpolated color.</returns>
    public static Color LerpOkLab(Color a, Color b, float t) => LerpOkLabUnclamped(a, b, Math.Clamp(t, 0f, 1f));

    /// <summary>
    /// Interpolates two colors in OkLab space without clamping the interpolation factor.
    /// </summary>
    /// <param name="a">The color at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The color at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor, which may lie outside <c>[0, 1]</c>.</param>
    /// <returns>The interpolated color, which may extrapolate beyond the endpoints.</returns>
    public static Color LerpOkLabUnclamped(Color a, Color b, float t) {
        a.ToOkLab(out float aL, out float aA, out float aB);
        b.ToOkLab(out float bL, out float bA, out float bB);

        return FromOkLab(
            aL + ((bL - aL) * t),
            aA + ((bA - aA) * t),
            aB + ((bB - aB) * t),
            a.A + ((b.A - a.A) * t));
    }

    /// <summary>
    /// Converts Unity-style HSV components to a linear RGB color.
    /// </summary>
    /// <param name="h">The hue in <c>[0, 1]</c>, wrapped around the color wheel.</param>
    /// <param name="s">The saturation, clamped to <c>[0, 1]</c>.</param>
    /// <param name="v">The value, clamped to <c>[0, 1]</c>.</param>
    /// <returns>The opaque color.</returns>
    public static Color HSVToRGB(float h, float s, float v) => HSVToRGB(h, s, v, true);

    /// <summary>
    /// Converts Unity-style HSV components to a linear RGB color.
    /// </summary>
    /// <param name="h">The hue in <c>[0, 1]</c>, wrapped around the color wheel.</param>
    /// <param name="s">The saturation, clamped to <c>[0, 1]</c>.</param>
    /// <param name="v">
    /// The value. When <paramref name="hdr"/> is <see langword="true"/>, values above <c>1</c> are
    /// preserved; otherwise the value is clamped to <c>[0, 1]</c>.
    /// </param>
    /// <param name="hdr">
    /// <see langword="true"/> to allow HDR values above <c>1</c>; <see langword="false"/> to clamp.
    /// </param>
    /// <returns>The opaque color.</returns>
    public static Color HSVToRGB(float h, float s, float v, bool hdr) {
        h -= MathF.Floor(h);
        s = Math.Clamp(s, 0f, 1f);
        v = hdr ? MathF.Max(v, 0f) : Math.Clamp(v, 0f, 1f);

        float sector = h * 6f;
        int index = (int)MathF.Floor(sector);
        float fraction = sector - MathF.Floor(sector);

        float p = v * (1f - s);
        float q = v * (1f - (s * fraction));
        float t = v * (1f - (s * (1f - fraction)));

        return index switch {
            0 => new Color(v, t, p, 1f),
            1 => new Color(q, v, p, 1f),
            2 => new Color(p, v, t, 1f),
            3 => new Color(p, q, v, 1f),
            4 => new Color(t, p, v, 1f),
            _ => new Color(v, p, q, 1f),
        };
    }

    /// <summary>
    /// Converts a color to Unity-style HSV components.
    /// </summary>
    /// <param name="rgb">The color to convert.</param>
    /// <param name="h">The resulting hue in <c>[0, 1]</c>.</param>
    /// <param name="s">The resulting saturation.</param>
    /// <param name="v">The resulting value.</param>
    public static void RGBToHSV(Color rgb, out float h, out float s, out float v) {
        float red = rgb._rgba.X;
        float green = rgb._rgba.Y;
        float blue = rgb._rgba.Z;

        float max = MathF.Max(red, MathF.Max(green, blue));
        float min = MathF.Min(red, MathF.Min(green, blue));
        float delta = max - min;

        v = max;

        if (delta <= 0f || max <= 0f) {
            h = 0f;
            s = 0f;
            return;
        }

        s = delta / max;

        if (max == red) {
            h = (green - blue) / delta;
        } else if (max == green) {
            h = ((blue - red) / delta) + 2f;
        } else {
            h = ((red - green) / delta) + 4f;
        }

        h /= 6f;

        if (h < 0f) {
            h += 1f;
        }
    }

    /// <summary>
    /// Adds two colors component-wise.
    /// </summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise sum.</returns>
    public static Color operator +(Color a, Color b) => new(a._rgba + b._rgba);

    /// <summary>
    /// Subtracts two colors component-wise.
    /// </summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise difference.</returns>
    public static Color operator -(Color a, Color b) => new(a._rgba - b._rgba);

    /// <summary>
    /// Multiplies two colors component-wise.
    /// </summary>
    /// <param name="a">The left color.</param>
    /// <param name="b">The right color.</param>
    /// <returns>The component-wise product.</returns>
    public static Color operator *(Color a, Color b) => new(a._rgba * b._rgba);

    /// <summary>
    /// Multiplies every component of a color by a scalar.
    /// </summary>
    /// <param name="a">The color.</param>
    /// <param name="b">The scalar.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator *(Color a, float b) => new(a._rgba * b);

    /// <summary>
    /// Multiplies every component of a color by a scalar.
    /// </summary>
    /// <param name="a">The scalar.</param>
    /// <param name="b">The color.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator *(float a, Color b) => new(b._rgba * a);

    /// <summary>
    /// Divides every component of a color by a scalar.
    /// </summary>
    /// <param name="a">The color.</param>
    /// <param name="b">The scalar.</param>
    /// <returns>The scaled color.</returns>
    public static Color operator /(Color a, float b) => new(a._rgba / b);

    /// <summary>
    /// Converts a color to its <see cref="Vector4"/> storage.
    /// </summary>
    /// <param name="color">The color to convert.</param>
    /// <returns>The linear RGBA vector.</returns>
    public static implicit operator Vector4(Color color) => color._rgba;

    /// <summary>
    /// Converts a linear RGBA vector to a color.
    /// </summary>
    /// <param name="value">The linear RGBA vector.</param>
    /// <returns>The color.</returns>
    public static implicit operator Color(Vector4 value) => new(value);

    /// <summary>
    /// Determines whether two colors have exactly equal components.
    /// </summary>
    /// <param name="other">The color to compare with.</param>
    /// <returns><see langword="true"/> when every component is exactly equal.</returns>
    public bool Equals(Color other) => _rgba == other._rgba;

    /// <summary>
    /// Determines whether this color equals another object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal color.</returns>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <summary>
    /// Returns a hash code derived from the stored RGBA vector.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => _rgba.GetHashCode();

    /// <summary>
    /// Returns the color formatted as <c>RGBA(r, g, b, a)</c> using the current culture.
    /// </summary>
    /// <returns>The formatted color.</returns>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>
    /// Returns the color formatted as <c>RGBA(r, g, b, a)</c> using the given provider.
    /// </summary>
    /// <param name="format">The component format string. The default is used when this is null or empty.</param>
    /// <param name="formatProvider">The provider used to format the components.</param>
    /// <returns>The formatted color.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider) {
        string componentFormat = string.IsNullOrEmpty(format) ? "G" : format;

        return string.Format(
            formatProvider,
            "RGBA({0:" + componentFormat + "}, {1:" + componentFormat + "}, {2:" + componentFormat + "}, {3:" + componentFormat + "})",
            _rgba.X,
            _rgba.Y,
            _rgba.Z,
            _rgba.W);
    }

    private static float DecodeSrgbComponent(float value) =>
        value <= SRGB_DECODE_THRESHOLD
            ? value / SRGB_SLOPE
            : MathF.Pow((value + SRGB_OFFSET) / SRGB_SCALE, SRGB_GAMMA);

    private static float EncodeSrgbComponent(float value) =>
        value <= SRGB_ENCODE_THRESHOLD
            ? SRGB_SLOPE * value
            : (SRGB_SCALE * MathF.Pow(value, SRGB_INVERSE_GAMMA)) - SRGB_OFFSET;

    private static void ValidateSrgbComponent(float value, string paramName) {
        if (!float.IsFinite(value) || value < 0f || value > 1f) {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "sRGB components must be finite and between 0 and 1.");
        }
    }
}
