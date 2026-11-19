using System.Globalization;
using System.Numerics;

namespace RayBork;

/// <summary>An immutable linear RGBA color stored as a <see cref="Vector4"/>.</summary>
/// <remarks>
/// Components are linear and may exceed <c>1</c> for HDR; presets are sRGB-authored colors, stored
/// in linear space. Convert with <see cref="FromSrgb(float, float, float, float)"/> and
/// <see cref="ToSrgb"/>; alpha is never gamma-encoded.
/// </remarks>
public readonly struct Color : IEquatable<Color>, IFormattable
{
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

    /// <summary>CSS AliceBlue.</summary>
    public static readonly Color AliceBlue = FromSrgb(0.9411765f, 0.9725491f, 1f);

    /// <summary>CSS AntiqueWhite.</summary>
    public static readonly Color AntiqueWhite = FromSrgb(0.9803922f, 0.9215687f, 0.8431373f);

    /// <summary>CSS Aquamarine.</summary>
    public static readonly Color Aquamarine = FromSrgb(0.4980392f, 1f, 0.8313726f);

    /// <summary>CSS Azure.</summary>
    public static readonly Color Azure = FromSrgb(0.9411765f, 1f, 1f);

    /// <summary>CSS Beige.</summary>
    public static readonly Color Beige = FromSrgb(0.9607844f, 0.9607844f, 0.8627452f);

    /// <summary>CSS Bisque.</summary>
    public static readonly Color Bisque = FromSrgb(1f, 0.8941177f, 0.7686275f);

    /// <summary>CSS Black.</summary>
    public static readonly Color Black = FromSrgb(0f, 0f, 0f);

    /// <summary>CSS BlanchedAlmond.</summary>
    public static readonly Color BlanchedAlmond = FromSrgb(1f, 0.9215687f, 0.8039216f);

    /// <summary>CSS Blue.</summary>
    public static readonly Color Blue = FromSrgb(0f, 0f, 1f);

    /// <summary>CSS BlueViolet.</summary>
    public static readonly Color BlueViolet = FromSrgb(0.5411765f, 0.1686275f, 0.8862746f);

    /// <summary>CSS Brown.</summary>
    public static readonly Color Brown = FromSrgb(0.6470588f, 0.1647059f, 0.1647059f);

    /// <summary>CSS Burlywood.</summary>
    public static readonly Color Burlywood = FromSrgb(0.8705883f, 0.7215686f, 0.5294118f);

    /// <summary>CSS CadetBlue.</summary>
    public static readonly Color CadetBlue = FromSrgb(0.372549f, 0.6196079f, 0.627451f);

    /// <summary>CSS Chartreuse.</summary>
    public static readonly Color Chartreuse = FromSrgb(0.4980392f, 1f, 0f);

    /// <summary>CSS Chocolate.</summary>
    public static readonly Color Chocolate = FromSrgb(0.8235295f, 0.4117647f, 0.1176471f);

    /// <summary>Fully transparent black preset (alpha <c>0</c>).</summary>
    public static readonly Color Clear = FromSrgb(0f, 0f, 0f, 0f);

    /// <summary>CSS Coral.</summary>
    public static readonly Color Coral = FromSrgb(1f, 0.4980392f, 0.3137255f);

    /// <summary>CSS CornflowerBlue.</summary>
    public static readonly Color CornflowerBlue = FromSrgb(0.3921569f, 0.5843138f, 0.9294118f);

    /// <summary>CSS Cornsilk.</summary>
    public static readonly Color Cornsilk = FromSrgb(1f, 0.9725491f, 0.8627452f);

    /// <summary>CSS Crimson.</summary>
    public static readonly Color Crimson = FromSrgb(0.8627452f, 0.07843138f, 0.2352941f);

    /// <summary>CSS Cyan.</summary>
    public static readonly Color Cyan = FromSrgb(0f, 1f, 1f);

    /// <summary>CSS DarkBlue.</summary>
    public static readonly Color DarkBlue = FromSrgb(0f, 0f, 0.5450981f);

    /// <summary>CSS DarkCyan.</summary>
    public static readonly Color DarkCyan = FromSrgb(0f, 0.5450981f, 0.5450981f);

    /// <summary>CSS DarkGoldenRod.</summary>
    public static readonly Color DarkGoldenRod = FromSrgb(0.7215686f, 0.5254902f, 0.04313726f);

    /// <summary>CSS DarkGray.</summary>
    public static readonly Color DarkGray = FromSrgb(0.6627451f, 0.6627451f, 0.6627451f);

    /// <summary>CSS DarkGreen.</summary>
    public static readonly Color DarkGreen = FromSrgb(0f, 0.3921569f, 0f);

    /// <summary>CSS DarkKhaki.</summary>
    public static readonly Color DarkKhaki = FromSrgb(0.7411765f, 0.7176471f, 0.4196079f);

    /// <summary>CSS DarkMagenta.</summary>
    public static readonly Color DarkMagenta = FromSrgb(0.5450981f, 0f, 0.5450981f);

    /// <summary>CSS DarkOliveGreen.</summary>
    public static readonly Color DarkOliveGreen = FromSrgb(0.3333333f, 0.4196079f, 0.1843137f);

    /// <summary>CSS DarkOrange.</summary>
    public static readonly Color DarkOrange = FromSrgb(1f, 0.5490196f, 0f);

    /// <summary>CSS DarkOrchid.</summary>
    public static readonly Color DarkOrchid = FromSrgb(0.6f, 0.1960784f, 0.8000001f);

    /// <summary>CSS DarkRed.</summary>
    public static readonly Color DarkRed = FromSrgb(0.5450981f, 0f, 0f);

    /// <summary>CSS DarkSalmon.</summary>
    public static readonly Color DarkSalmon = FromSrgb(0.9137256f, 0.5882353f, 0.4784314f);

    /// <summary>CSS DarkSeaGreen.</summary>
    public static readonly Color DarkSeaGreen = FromSrgb(0.5607843f, 0.7372549f, 0.5607843f);

    /// <summary>CSS DarkSlateBlue.</summary>
    public static readonly Color DarkSlateBlue = FromSrgb(0.282353f, 0.2392157f, 0.5450981f);

    /// <summary>CSS DarkSlateGray.</summary>
    public static readonly Color DarkSlateGray = FromSrgb(0.1843137f, 0.3098039f, 0.3098039f);

    /// <summary>CSS DarkTurquoise.</summary>
    public static readonly Color DarkTurquoise = FromSrgb(0f, 0.8078432f, 0.8196079f);

    /// <summary>CSS DarkViolet.</summary>
    public static readonly Color DarkViolet = FromSrgb(0.5803922f, 0f, 0.8274511f);

    /// <summary>CSS DeepPink.</summary>
    public static readonly Color DeepPink = FromSrgb(1f, 0.07843138f, 0.5764706f);

    /// <summary>CSS DeepSkyBlue.</summary>
    public static readonly Color DeepSkyBlue = FromSrgb(0f, 0.7490196f, 1f);

    /// <summary>CSS DimGray.</summary>
    public static readonly Color DimGray = FromSrgb(0.4117647f, 0.4117647f, 0.4117647f);

    /// <summary>CSS DodgerBlue.</summary>
    public static readonly Color DodgerBlue = FromSrgb(0.1176471f, 0.5647059f, 1f);

    /// <summary>CSS Firebrick.</summary>
    public static readonly Color Firebrick = FromSrgb(0.6980392f, 0.1333333f, 0.1333333f);

    /// <summary>CSS FloralWhite.</summary>
    public static readonly Color FloralWhite = FromSrgb(1f, 0.9803922f, 0.9411765f);

    /// <summary>CSS ForestGreen.</summary>
    public static readonly Color ForestGreen = FromSrgb(0.1333333f, 0.5450981f, 0.1333333f);

    /// <summary>CSS Gainsboro.</summary>
    public static readonly Color Gainsboro = FromSrgb(0.8627452f, 0.8627452f, 0.8627452f);

    /// <summary>CSS GhostWhite.</summary>
    public static readonly Color GhostWhite = FromSrgb(0.9725491f, 0.9725491f, 1f);

    /// <summary>CSS Gold.</summary>
    public static readonly Color Gold = FromSrgb(1f, 0.8431373f, 0f);

    /// <summary>CSS GoldenRod.</summary>
    public static readonly Color GoldenRod = FromSrgb(0.854902f, 0.6470588f, 0.1254902f);

    /// <summary>CSS Gray.</summary>
    public static readonly Color Gray = FromSrgb(0.5f, 0.5f, 0.5f);

    /// <summary>Bork2d Gray1 preset.</summary>
    public static readonly Color Gray1 = FromSrgb(0.1f, 0.1f, 0.1f);

    /// <summary>Bork2d Gray2 preset.</summary>
    public static readonly Color Gray2 = FromSrgb(0.2f, 0.2f, 0.2f);

    /// <summary>Bork2d Gray3 preset.</summary>
    public static readonly Color Gray3 = FromSrgb(0.3f, 0.3f, 0.3f);

    /// <summary>Bork2d Gray4 preset.</summary>
    public static readonly Color Gray4 = FromSrgb(0.4f, 0.4f, 0.4f);

    /// <summary>Bork2d Gray5 preset.</summary>
    public static readonly Color Gray5 = FromSrgb(0.5f, 0.5f, 0.5f);

    /// <summary>Bork2d Gray6 preset.</summary>
    public static readonly Color Gray6 = FromSrgb(0.6f, 0.6f, 0.6f);

    /// <summary>Bork2d Gray7 preset.</summary>
    public static readonly Color Gray7 = FromSrgb(0.7f, 0.7f, 0.7f);

    /// <summary>Bork2d Gray8 preset.</summary>
    public static readonly Color Gray8 = FromSrgb(0.8f, 0.8f, 0.8f);

    /// <summary>Bork2d Gray9 preset.</summary>
    public static readonly Color Gray9 = FromSrgb(0.9f, 0.9f, 0.9f);

    /// <summary>CSS Green.</summary>
    public static readonly Color Green = FromSrgb(0f, 1f, 0f);

    /// <summary>CSS GreenYellow.</summary>
    public static readonly Color GreenYellow = FromSrgb(0.6784314f, 1f, 0.1843137f);

    /// <summary>CSS Honeydew.</summary>
    public static readonly Color Honeydew = FromSrgb(0.9411765f, 1f, 0.9411765f);

    /// <summary>CSS HotPink.</summary>
    public static readonly Color HotPink = FromSrgb(1f, 0.4117647f, 0.7058824f);

    /// <summary>CSS IndianRed.</summary>
    public static readonly Color IndianRed = FromSrgb(0.8039216f, 0.3607843f, 0.3607843f);

    /// <summary>CSS Indigo.</summary>
    public static readonly Color Indigo = FromSrgb(0.2941177f, 0f, 0.509804f);

    /// <summary>CSS Ivory.</summary>
    public static readonly Color Ivory = FromSrgb(1f, 1f, 0.9411765f);

    /// <summary>CSS Khaki.</summary>
    public static readonly Color Khaki = FromSrgb(0.9411765f, 0.9019608f, 0.5490196f);

    /// <summary>CSS Lavender.</summary>
    public static readonly Color Lavender = FromSrgb(0.9019608f, 0.9019608f, 0.9803922f);

    /// <summary>CSS LavenderBlush.</summary>
    public static readonly Color LavenderBlush = FromSrgb(1f, 0.9411765f, 0.9607844f);

    /// <summary>CSS LawnGreen.</summary>
    public static readonly Color LawnGreen = FromSrgb(0.4862745f, 0.9882354f, 0f);

    /// <summary>CSS LemonChiffon.</summary>
    public static readonly Color LemonChiffon = FromSrgb(1f, 0.9803922f, 0.8039216f);

    /// <summary>CSS LightBlue.</summary>
    public static readonly Color LightBlue = FromSrgb(0.6784314f, 0.8470589f, 0.9019608f);

    /// <summary>CSS LightCoral.</summary>
    public static readonly Color LightCoral = FromSrgb(0.9411765f, 0.5019608f, 0.5019608f);

    /// <summary>CSS LightCyan.</summary>
    public static readonly Color LightCyan = FromSrgb(0.8784314f, 1f, 1f);

    /// <summary>Bork2d LightGoldenRod preset.</summary>
    public static readonly Color LightGoldenRod = FromSrgb(0.9333334f, 0.8666667f, 0.509804f);

    /// <summary>CSS LightGoldenRodYellow.</summary>
    public static readonly Color LightGoldenRodYellow = FromSrgb(
        0.9803922f,
        0.9803922f,
        0.8235295f
    );

    /// <summary>CSS LightGray.</summary>
    public static readonly Color LightGray = FromSrgb(0.8274511f, 0.8274511f, 0.8274511f);

    /// <summary>CSS LightGreen.</summary>
    public static readonly Color LightGreen = FromSrgb(0.5647059f, 0.9333334f, 0.5647059f);

    /// <summary>CSS LightPink.</summary>
    public static readonly Color LightPink = FromSrgb(1f, 0.7137255f, 0.7568628f);

    /// <summary>CSS LightSalmon.</summary>
    public static readonly Color LightSalmon = FromSrgb(1f, 0.627451f, 0.4784314f);

    /// <summary>CSS LightSeaGreen.</summary>
    public static readonly Color LightSeaGreen = FromSrgb(0.1254902f, 0.6980392f, 0.6666667f);

    /// <summary>CSS LightSkyBlue.</summary>
    public static readonly Color LightSkyBlue = FromSrgb(0.5294118f, 0.8078432f, 0.9803922f);

    /// <summary>Bork2d LightSlateBlue preset.</summary>
    public static readonly Color LightSlateBlue = FromSrgb(0.5176471f, 0.4392157f, 1f);

    /// <summary>CSS LightSlateGray.</summary>
    public static readonly Color LightSlateGray = FromSrgb(0.4666667f, 0.5333334f, 0.6f);

    /// <summary>CSS LightSteelBlue.</summary>
    public static readonly Color LightSteelBlue = FromSrgb(0.6901961f, 0.7686275f, 0.8705883f);

    /// <summary>CSS LightYellow.</summary>
    public static readonly Color LightYellow = FromSrgb(1f, 1f, 0.8784314f);

    /// <summary>CSS LimeGreen.</summary>
    public static readonly Color LimeGreen = FromSrgb(0.1960784f, 0.8039216f, 0.1960784f);

    /// <summary>CSS Linen.</summary>
    public static readonly Color Linen = FromSrgb(0.9803922f, 0.9411765f, 0.9019608f);

    /// <summary>CSS Magenta.</summary>
    public static readonly Color Magenta = FromSrgb(1f, 0f, 1f);

    /// <summary>CSS Maroon.</summary>
    public static readonly Color Maroon = FromSrgb(0.6901961f, 0.1882353f, 0.3764706f);

    /// <summary>CSS MediumAquamarine.</summary>
    public static readonly Color MediumAquamarine = FromSrgb(0.4f, 0.8039216f, 0.6666667f);

    /// <summary>CSS MediumBlue.</summary>
    public static readonly Color MediumBlue = FromSrgb(0f, 0f, 0.8039216f);

    /// <summary>CSS MediumOrchid.</summary>
    public static readonly Color MediumOrchid = FromSrgb(0.7294118f, 0.3333333f, 0.8274511f);

    /// <summary>CSS MediumPurple.</summary>
    public static readonly Color MediumPurple = FromSrgb(0.5764706f, 0.4392157f, 0.8588236f);

    /// <summary>CSS MediumSeaGreen.</summary>
    public static readonly Color MediumSeaGreen = FromSrgb(0.2352941f, 0.7019608f, 0.4431373f);

    /// <summary>CSS MediumSlateBlue.</summary>
    public static readonly Color MediumSlateBlue = FromSrgb(0.482353f, 0.4078432f, 0.9333334f);

    /// <summary>CSS MediumSpringGreen.</summary>
    public static readonly Color MediumSpringGreen = FromSrgb(0f, 0.9803922f, 0.6039216f);

    /// <summary>CSS MediumTurquoise.</summary>
    public static readonly Color MediumTurquoise = FromSrgb(0.282353f, 0.8196079f, 0.8000001f);

    /// <summary>CSS MediumVioletRed.</summary>
    public static readonly Color MediumVioletRed = FromSrgb(0.7803922f, 0.08235294f, 0.5215687f);

    /// <summary>CSS MidnightBlue.</summary>
    public static readonly Color MidnightBlue = FromSrgb(0.09803922f, 0.09803922f, 0.4392157f);

    /// <summary>CSS MintCream.</summary>
    public static readonly Color MintCream = FromSrgb(0.9607844f, 1f, 0.9803922f);

    /// <summary>CSS MistyRose.</summary>
    public static readonly Color MistyRose = FromSrgb(1f, 0.8941177f, 0.882353f);

    /// <summary>CSS Moccasin.</summary>
    public static readonly Color Moccasin = FromSrgb(1f, 0.8941177f, 0.7098039f);

    /// <summary>CSS NavajoWhite.</summary>
    public static readonly Color NavajoWhite = FromSrgb(1f, 0.8705883f, 0.6784314f);

    /// <summary>CSS Navy.</summary>
    public static readonly Color NavyBlue = FromSrgb(0f, 0f, 0.5019608f);

    /// <summary>CSS OldLace.</summary>
    public static readonly Color OldLace = FromSrgb(0.9921569f, 0.9607844f, 0.9019608f);

    /// <summary>CSS Olive.</summary>
    public static readonly Color Olive = FromSrgb(0.5019608f, 0.5019608f, 0f);

    /// <summary>CSS OliveDrab.</summary>
    public static readonly Color OliveDrab = FromSrgb(0.4196079f, 0.5568628f, 0.1372549f);

    /// <summary>CSS Orange.</summary>
    public static readonly Color Orange = FromSrgb(1f, 0.6470588f, 0f);

    /// <summary>CSS OrangeRed.</summary>
    public static readonly Color OrangeRed = FromSrgb(1f, 0.2705882f, 0f);

    /// <summary>CSS Orchid.</summary>
    public static readonly Color Orchid = FromSrgb(0.854902f, 0.4392157f, 0.8392158f);

    /// <summary>CSS PaleGoldenRod.</summary>
    public static readonly Color PaleGoldenRod = FromSrgb(0.9333334f, 0.909804f, 0.6666667f);

    /// <summary>CSS PaleGreen.</summary>
    public static readonly Color PaleGreen = FromSrgb(0.5960785f, 0.9843138f, 0.5960785f);

    /// <summary>CSS PaleTurquoise.</summary>
    public static readonly Color PaleTurquoise = FromSrgb(0.6862745f, 0.9333334f, 0.9333334f);

    /// <summary>CSS PaleVioletRed.</summary>
    public static readonly Color PaleVioletRed = FromSrgb(0.8588236f, 0.4392157f, 0.5764706f);

    /// <summary>CSS PapayaWhip.</summary>
    public static readonly Color PapayaWhip = FromSrgb(1f, 0.937255f, 0.8352942f);

    /// <summary>CSS PeachPuff.</summary>
    public static readonly Color PeachPuff = FromSrgb(1f, 0.854902f, 0.7254902f);

    /// <summary>CSS Peru.</summary>
    public static readonly Color Peru = FromSrgb(0.8039216f, 0.5215687f, 0.2470588f);

    /// <summary>CSS Pink.</summary>
    public static readonly Color Pink = FromSrgb(1f, 0.7529413f, 0.7960785f);

    /// <summary>CSS Plum.</summary>
    public static readonly Color Plum = FromSrgb(0.8666667f, 0.627451f, 0.8666667f);

    /// <summary>CSS PowderBlue.</summary>
    public static readonly Color PowderBlue = FromSrgb(0.6901961f, 0.8784314f, 0.9019608f);

    /// <summary>CSS Purple.</summary>
    public static readonly Color Purple = FromSrgb(0.627451f, 0.1254902f, 0.9411765f);

    /// <summary>CSS RebeccaPurple.</summary>
    public static readonly Color RebeccaPurple = FromSrgb(0.4f, 0.2f, 0.6f);

    /// <summary>CSS Red.</summary>
    public static readonly Color Red = FromSrgb(1f, 0f, 0f);

    /// <summary>CSS RosyBrown.</summary>
    public static readonly Color RosyBrown = FromSrgb(0.7372549f, 0.5607843f, 0.5607843f);

    /// <summary>CSS RoyalBlue.</summary>
    public static readonly Color RoyalBlue = FromSrgb(0.254902f, 0.4117647f, 0.882353f);

    /// <summary>CSS SaddleBrown.</summary>
    public static readonly Color SaddleBrown = FromSrgb(0.5450981f, 0.2705882f, 0.07450981f);

    /// <summary>CSS Salmon.</summary>
    public static readonly Color Salmon = FromSrgb(0.9803922f, 0.5019608f, 0.4470589f);

    /// <summary>CSS SandyBrown.</summary>
    public static readonly Color SandyBrown = FromSrgb(0.9568628f, 0.6431373f, 0.3764706f);

    /// <summary>CSS SeaGreen.</summary>
    public static readonly Color SeaGreen = FromSrgb(0.1803922f, 0.5450981f, 0.3411765f);

    /// <summary>CSS Seashell.</summary>
    public static readonly Color Seashell = FromSrgb(1f, 0.9607844f, 0.9333334f);

    /// <summary>CSS Sienna.</summary>
    public static readonly Color Sienna = FromSrgb(0.627451f, 0.3215686f, 0.1764706f);

    /// <summary>CSS Silver.</summary>
    public static readonly Color Silver = FromSrgb(0.7529413f, 0.7529413f, 0.7529413f);

    /// <summary>CSS SkyBlue.</summary>
    public static readonly Color SkyBlue = FromSrgb(0.5294118f, 0.8078432f, 0.9215687f);

    /// <summary>CSS SlateBlue.</summary>
    public static readonly Color SlateBlue = FromSrgb(0.4156863f, 0.3529412f, 0.8039216f);

    /// <summary>CSS SlateGray.</summary>
    public static readonly Color SlateGray = FromSrgb(0.4392157f, 0.5019608f, 0.5647059f);

    /// <summary>CSS Snow.</summary>
    public static readonly Color Snow = FromSrgb(1f, 0.9803922f, 0.9803922f);

    /// <summary>Bork2d SoftBlue preset.</summary>
    public static readonly Color SoftBlue = FromSrgb(0.1882353f, 0.682353f, 0.7490196f);

    /// <summary>Bork2d SoftGreen preset.</summary>
    public static readonly Color SoftGreen = FromSrgb(0.5490196f, 0.7882354f, 0.1411765f);

    /// <summary>Bork2d SoftRed preset.</summary>
    public static readonly Color SoftRed = FromSrgb(0.8627452f, 0.1921569f, 0.1960784f);

    /// <summary>Bork2d SoftYellow preset.</summary>
    public static readonly Color SoftYellow = FromSrgb(1f, 0.9333334f, 0.5490196f);

    /// <summary>CSS SpringGreen.</summary>
    public static readonly Color SpringGreen = FromSrgb(0f, 1f, 0.4980392f);

    /// <summary>CSS SteelBlue.</summary>
    public static readonly Color SteelBlue = FromSrgb(0.2745098f, 0.509804f, 0.7058824f);

    /// <summary>CSS Tan.</summary>
    public static readonly Color Tan = FromSrgb(0.8235295f, 0.7058824f, 0.5490196f);

    /// <summary>CSS Teal.</summary>
    public static readonly Color Teal = FromSrgb(0f, 0.5019608f, 0.5019608f);

    /// <summary>CSS Thistle.</summary>
    public static readonly Color Thistle = FromSrgb(0.8470589f, 0.7490196f, 0.8470589f);

    /// <summary>CSS Tomato.</summary>
    public static readonly Color Tomato = FromSrgb(1f, 0.3882353f, 0.2784314f);

    /// <summary>CSS Turquoise.</summary>
    public static readonly Color Turquoise = FromSrgb(0.2509804f, 0.8784314f, 0.8156863f);

    /// <summary>CSS Violet.</summary>
    public static readonly Color Violet = FromSrgb(0.9333334f, 0.509804f, 0.9333334f);

    /// <summary>CSS VioletRed.</summary>
    public static readonly Color VioletRed = FromSrgb(0.8156863f, 0.1254902f, 0.5647059f);

    /// <summary>CSS Wheat.</summary>
    public static readonly Color Wheat = FromSrgb(0.9607844f, 0.8705883f, 0.7019608f);

    /// <summary>CSS White.</summary>
    public static readonly Color White = FromSrgb(1f, 1f, 1f);

    /// <summary>CSS WhiteSmoke.</summary>
    public static readonly Color WhiteSmoke = FromSrgb(0.9607844f, 0.9607844f, 0.9607844f);

    /// <summary>CSS Yellow.</summary>
    public static readonly Color Yellow = FromSrgb(1f, 235f / 255f, 4f / 255f);

    /// <summary>CSS YellowGreen.</summary>
    public static readonly Color YellowGreen = FromSrgb(0.6039216f, 0.8039216f, 0.1960784f);

    /// <summary>Alias of <see cref="Gray"/> using the British spelling.</summary>
    public static readonly Color Grey = Gray;

    /// <summary>Alias of <see cref="Clear"/>; fully transparent black.</summary>
    public static readonly Color Transparent = Clear;

    /// <summary>Alias of <see cref="Yellow"/> under a friendly name.</summary>
    public static readonly Color YellowNice = Yellow;

    /// <summary>Initializes a color from linear red, green, blue, and alpha components.</summary>
    public Color(float r, float g, float b, float a)
    {
        _rgba = new Vector4(r, g, b, a);
    }

    /// <summary>Initializes an opaque color from linear red, green, and blue components.</summary>
    public Color(float r, float g, float b)
    {
        _rgba = new Vector4(r, g, b, 1f);
    }

    private Color(Vector4 rgba)
    {
        _rgba = rgba;
    }

    /// <summary>Gets the linear red component.</summary>
    public float R => _rgba.X;

    /// <summary>Gets the linear green component.</summary>
    public float G => _rgba.Y;

    /// <summary>Gets the linear blue component.</summary>
    public float B => _rgba.Z;

    /// <summary>Gets the alpha component, which is never gamma-encoded.</summary>
    public float A => _rgba.W;

    /// <summary>Gets the component at a zero-based index: R, G, B, then A.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The index is not 0 through 3.</exception>
    public float this[int index]
    {
        get
        {
            return index switch
            {
                0 => _rgba.X,
                1 => _rgba.Y,
                2 => _rgba.Z,
                3 => _rgba.W,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "Color component index must be between 0 and 3."
                ),
            };
        }
    }

    /// <summary>Gets the <c>0.299 R + 0.587 G + 0.114 B</c> luminance; alpha is ignored.</summary>
    public float Grayscale =>
        (GRAYSCALE_RED_WEIGHT * _rgba.X)
        + (GRAYSCALE_GREEN_WEIGHT * _rgba.Y)
        + (GRAYSCALE_BLUE_WEIGHT * _rgba.Z);

    /// <summary>Gets the largest red, green, blue, or alpha component.</summary>
    public float MaxColorComponent =>
        MathF.Max(MathF.Max(_rgba.X, _rgba.Y), MathF.Max(_rgba.Z, _rgba.W));

    /// <summary>Linearly interpolates two colors, clamping the factor to <c>[0, 1]</c>.</summary>
    public static Color Lerp(Color a, Color b, float t) =>
        new(Vector4.Lerp(a._rgba, b._rgba, Math.Clamp(t, 0f, 1f)));

    /// <summary>Linearly interpolates two colors without clamping; may extrapolate.</summary>
    public static Color LerpUnclamped(Color a, Color b, float t) =>
        new(Vector4.Lerp(a._rgba, b._rgba, t));

    /// <summary>Converts the color to OkLab lightness, green-red, and blue-yellow.</summary>
    public void ToOkLab(out float l, out float a, out float b)
    {
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

    /// <summary>Creates a linear color from OkLab components; alpha defaults to <c>1</c>.</summary>
    public static Color FromOkLab(float l, float a, float b, float alpha = 1f)
    {
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

    /// <summary>Encodes linear RGB with the piecewise sRGB transfer function.</summary>
    /// <returns>The sRGB-encoded RGBA vector; alpha is unchanged and unclamped.</returns>
    public Vector4 ToSrgb()
    {
        return new Vector4(
            EncodeSrgbComponent(_rgba.X),
            EncodeSrgbComponent(_rgba.Y),
            EncodeSrgbComponent(_rgba.Z),
            _rgba.W
        );
    }

    /// <summary>Creates a linear color from sRGB-encoded RGB and linear alpha.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Component outside <c>[0, 1]</c>.</exception>
    public static Color FromSrgb(float r, float g, float b, float a = 1f)
    {
        ValidateSrgbComponent(r, nameof(r));
        ValidateSrgbComponent(g, nameof(g));
        ValidateSrgbComponent(b, nameof(b));
        ValidateSrgbComponent(a, nameof(a));

        return new Color(DecodeSrgbComponent(r), DecodeSrgbComponent(g), DecodeSrgbComponent(b), a);
    }

    /// <summary>Creates a linear color from 8-bit sRGB-encoded RGB and linear alpha bytes.</summary>
    /// <remarks>RGB bytes decode as <c>value / 255</c>; alpha is only scaled.</remarks>
    public static Color FromSrgbBytes(byte r, byte g, byte b, byte a = 255)
    {
        return new Color(
            DecodeSrgbComponent(r / 255f),
            DecodeSrgbComponent(g / 255f),
            DecodeSrgbComponent(b / 255f),
            a / 255f
        );
    }

    /// <summary>Interpolates two colors in OkLab, clamping the factor to <c>[0, 1]</c>.</summary>
    public static Color LerpOkLab(Color a, Color b, float t) =>
        LerpOkLabUnclamped(a, b, Math.Clamp(t, 0f, 1f));

    /// <summary>Interpolates two colors in OkLab without clamping.</summary>
    public static Color LerpOkLabUnclamped(Color a, Color b, float t)
    {
        a.ToOkLab(out float aL, out float aA, out float aB);
        b.ToOkLab(out float bL, out float bA, out float bB);

        return FromOkLab(
            aL + ((bL - aL) * t),
            aA + ((bA - aA) * t),
            aB + ((bB - aB) * t),
            a.A + ((b.A - a.A) * t)
        );
    }

    /// <summary>Converts Unity-style HSV components to an opaque linear color.</summary>
    /// <param name="h">Hue in <c>[0, 1]</c>, wrapped around the color wheel.</param>
    /// <param name="s">Saturation, clamped to <c>[0, 1]</c>.</param>
    /// <param name="v">Value, clamped to <c>[0, 1]</c>.</param>
    public static Color HSVToRGB(float h, float s, float v) => HSVToRGB(h, s, v, true);

    /// <summary>Converts Unity-style HSV to a linear color, with an HDR option.</summary>
    /// <param name="h">Hue in <c>[0, 1]</c>, wrapped around the color wheel.</param>
    /// <param name="s">Saturation, clamped to <c>[0, 1]</c>.</param>
    /// <param name="v">Value clamped to <c>[0, 1]</c> unless <paramref name="hdr"/>.</param>
    /// <param name="hdr"><see langword="true"/> to keep HDR values above <c>1</c>.</param>
    public static Color HSVToRGB(float h, float s, float v, bool hdr)
    {
        h -= MathF.Floor(h);
        s = Math.Clamp(s, 0f, 1f);
        v = hdr ? MathF.Max(v, 0f) : Math.Clamp(v, 0f, 1f);

        float sector = h * 6f;
        int index = (int)MathF.Floor(sector);
        float fraction = sector - MathF.Floor(sector);

        float p = v * (1f - s);
        float q = v * (1f - (s * fraction));
        float t = v * (1f - (s * (1f - fraction)));

        return index switch
        {
            0 => new Color(v, t, p, 1f),
            1 => new Color(q, v, p, 1f),
            2 => new Color(p, v, t, 1f),
            3 => new Color(p, q, v, 1f),
            4 => new Color(t, p, v, 1f),
            _ => new Color(v, p, q, 1f),
        };
    }

    /// <summary>Converts a linear color to Unity-style HSV with hue in <c>[0, 1]</c>.</summary>
    public static void RGBToHSV(Color rgb, out float h, out float s, out float v)
    {
        float red = rgb._rgba.X;
        float green = rgb._rgba.Y;
        float blue = rgb._rgba.Z;

        float max = MathF.Max(red, MathF.Max(green, blue));
        float min = MathF.Min(red, MathF.Min(green, blue));
        float delta = max - min;

        v = max;

        if (delta <= 0f || max <= 0f)
        {
            h = 0f;
            s = 0f;
            return;
        }

        s = delta / max;

        if (max == red)
        {
            h = (green - blue) / delta;
        }
        else if (max == green)
        {
            h = ((blue - red) / delta) + 2f;
        }
        else
        {
            h = ((red - green) / delta) + 4f;
        }

        h /= 6f;

        if (h < 0f)
        {
            h += 1f;
        }
    }

    /// <summary>Adds two colors component-wise.</summary>
    public static Color operator +(Color a, Color b) => new(a._rgba + b._rgba);

    /// <summary>Subtracts two colors component-wise.</summary>
    public static Color operator -(Color a, Color b) => new(a._rgba - b._rgba);

    /// <summary>Multiplies two colors component-wise.</summary>
    public static Color operator *(Color a, Color b) => new(a._rgba * b._rgba);

    /// <summary>Multiplies every component, including alpha, by a scalar.</summary>
    public static Color operator *(Color a, float b) => new(a._rgba * b);

    /// <summary>Multiplies every component, including alpha, by a scalar.</summary>
    public static Color operator *(float a, Color b) => new(b._rgba * a);

    /// <summary>Divides every component, including alpha, by a scalar.</summary>
    public static Color operator /(Color a, float b) => new(a._rgba / b);

    /// <summary>Converts a color to its linear RGBA <see cref="Vector4"/> storage.</summary>
    public static implicit operator Vector4(Color color) => color._rgba;

    /// <summary>Converts a linear RGBA vector to a color.</summary>
    public static implicit operator Color(Vector4 value) => new(value);

    /// <summary>Determines whether two colors have exactly equal components.</summary>
    public bool Equals(Color other) => _rgba == other._rgba;

    /// <summary>Determines whether this color equals another object.</summary>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <summary>Returns a hash code derived from the RGBA components.</summary>
    public override int GetHashCode() => _rgba.GetHashCode();

    /// <summary>Formats the color as <c>RGBA(r, g, b, a)</c> using the current culture.</summary>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>Formats as <c>RGBA(r, g, b, a)</c>; format defaults to <c>"G"</c>.</summary>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        string componentFormat = string.IsNullOrEmpty(format) ? "G" : format;

        return string.Format(
            formatProvider,
            "RGBA({0:"
                + componentFormat
                + "}, {1:"
                + componentFormat
                + "}, {2:"
                + componentFormat
                + "}, {3:"
                + componentFormat
                + "})",
            _rgba.X,
            _rgba.Y,
            _rgba.Z,
            _rgba.W
        );
    }

    private static float DecodeSrgbComponent(float value) =>
        value <= SRGB_DECODE_THRESHOLD
            ? value / SRGB_SLOPE
            : MathF.Pow((value + SRGB_OFFSET) / SRGB_SCALE, SRGB_GAMMA);

    private static float EncodeSrgbComponent(float value) =>
        value <= SRGB_ENCODE_THRESHOLD
            ? SRGB_SLOPE * value
            : (SRGB_SCALE * MathF.Pow(value, SRGB_INVERSE_GAMMA)) - SRGB_OFFSET;

    private static void ValidateSrgbComponent(float value, string paramName)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "sRGB components must be finite and between 0 and 1."
            );
        }
    }
}
