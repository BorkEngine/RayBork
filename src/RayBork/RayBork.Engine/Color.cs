using System.Globalization;
using System.Numerics;

namespace RayBork;

/// <summary>
/// An immutable linear RGBA color stored as a <see cref="Vector4"/>.
/// </summary>
/// <remarks>
/// Components are normally in <c>[0, 1]</c>, but HDR rendering allows values above <c>1</c>.
/// Colors are authored in linear space; there is no implicit gamma conversion. Use
/// <see cref="FromSrgb(float, float, float, float)"/> and <see cref="ToSrgb"/> to convert
/// explicitly between linear and sRGB-encoded values.
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

    /// <summary>
    /// The fully transparent black color <c>(0, 0, 0, 0)</c>.
    /// </summary>
    public static readonly Color Clear = new(0f, 0f, 0f, 0f);

    /// <summary>
    /// The opaque black color <c>(0, 0, 0, 1)</c>.
    /// </summary>
    public static readonly Color Black = new(0f, 0f, 0f, 1f);

    /// <summary>
    /// The opaque white color <c>(1, 1, 1, 1)</c>.
    /// </summary>
    public static readonly Color White = new(1f, 1f, 1f, 1f);

    /// <summary>
    /// The opaque red color <c>(1, 0, 0, 1)</c>.
    /// </summary>
    public static readonly Color Red = new(1f, 0f, 0f, 1f);

    /// <summary>
    /// The opaque green color <c>(0, 1, 0, 1)</c>.
    /// </summary>
    public static readonly Color Green = new(0f, 1f, 0f, 1f);

    /// <summary>
    /// The opaque blue color <c>(0, 0, 1, 1)</c>.
    /// </summary>
    public static readonly Color Blue = new(0f, 0f, 1f, 1f);

    /// <summary>
    /// The opaque yellow color <c>(1, 1, 0, 1)</c>.
    /// </summary>
    public static readonly Color Yellow = new(1f, 1f, 0f, 1f);

    /// <summary>
    /// The opaque cyan color <c>(0, 1, 1, 1)</c>.
    /// </summary>
    public static readonly Color Cyan = new(0f, 1f, 1f, 1f);

    /// <summary>
    /// The opaque magenta color <c>(1, 0, 1, 1)</c>.
    /// </summary>
    public static readonly Color Magenta = new(1f, 0f, 1f, 1f);

    /// <summary>
    /// The opaque mid-gray color <c>(0.5, 0.5, 0.5, 1)</c>.
    /// </summary>
    public static readonly Color Gray = new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// The opaque mid-gray color <c>(0.5, 0.5, 0.5, 1)</c>. This alias matches Unity's spelling.
    /// </summary>
    public static readonly Color Grey = new(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// The opaque dark cyan color <c>(0, 0.5, 0.5, 1)</c>.
    /// </summary>
    public static readonly Color DarkCyan = new(0f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// The fully transparent black color <c>(0, 0, 0, 0)</c>.
    /// </summary>
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);

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

    // RS0026 flags multiple overloads with optional parameters. The float and byte forms must both be
    // callable without alpha, so the shared default is deliberate rather than an overload ambiguity.
#pragma warning disable RS0026
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
    public static Color FromSrgb(byte r, byte g, byte b, byte a = 255) {
        return new Color(
            DecodeSrgbComponent(r / 255f),
            DecodeSrgbComponent(g / 255f),
            DecodeSrgbComponent(b / 255f),
            a / 255f);
    }
#pragma warning restore RS0026

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
