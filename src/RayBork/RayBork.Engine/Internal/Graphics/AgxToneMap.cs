using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The CPU reference for the present pass's AgX tone map.
/// </summary>
/// <remarks>
/// Mirrors the present shader's matrices, allenwp curve, and output clamp in double precision, with
/// a final rounding to <see cref="float"/>. The renderer never calls it per frame.
/// </remarks>
internal static class AgxToneMap
{
    /// <summary>The default AgX white in scene-linear units, matching Godot.</summary>
    internal const float DEFAULT_AGX_WHITE = 16.29f;

    /// <summary>The default AgX contrast, matching Godot's environment default.</summary>
    internal const float DEFAULT_AGX_CONTRAST = 1.25f;

    private const double REC709_RED_WEIGHT = 0.2126;
    private const double REC709_GREEN_WEIGHT = 0.7152;
    private const double REC709_BLUE_WEIGHT = 0.0722;
    private const float SRGB_LINEAR_CUTOFF = 0.0031308f;
    private const float SRGB_ENCODED_CUTOFF = 0.04045f;

    // Columns of the combined inset and outset matrices in GLSL mat3 constructor order. GLSL
    // multiplies a column-major matrix by a column vector, so applying a matrix is the sum of each
    // column scaled by the matching input channel. The inset combines Rec.709 to Rec.2020 with the
    // Blender AgX inset; the outset combines the inverse AgX outset with Rec.2020 to Rec.709.
    private static readonly double[] _inset =
    [
        0.544814746488245,
        0.140416948464053,
        0.0888104196149096,
        0.373787398372697,
        0.754137554567394,
        0.178871756420858,
        0.0813978551390581,
        0.105445496968552,
        0.732317823964232,
    ];

    private static readonly double[] _outset =
    [
        1.96488741169489,
        -0.299313364904742,
        -0.164352742528393,
        -0.855988495690215,
        1.32639796461980,
        -0.238183969428088,
        -0.108898916004672,
        -0.0270845997150571,
        1.40253671195648,
    ];

    /// <summary>
    /// Applies the AgX base transform to a linear scene color, without a look transform.
    /// </summary>
    /// <remarks>
    /// The input must be non-negative, and saturated colors may leave small negative channels. The
    /// result is display-linear after the output-maximum clamp and the inverse outset.
    /// </remarks>
    /// <param name="color">The linear scene color, with HDR values allowed.</param>
    /// <param name="parameters">The curve parameters for the output configuration.</param>
    /// <returns>The AgX display-linear response.</returns>
    internal static Vector3 Agx(Vector3 color, in AgxCurveParameters parameters)
    {
        (double r, double g, double b) = Multiply(_inset, color.X, color.Y, color.Z);
        r = Math.Min(parameters.OutputMax, Curve(r, parameters));
        g = Math.Min(parameters.OutputMax, Curve(g, parameters));
        b = Math.Min(parameters.OutputMax, Curve(b, parameters));
        (r, g, b) = Multiply(_outset, r, g, b);
        return new Vector3((float)r, (float)g, (float)b);
    }

    /// <summary>Computes the Rec.709 luminance of a linear RGB color.</summary>
    internal static double Luminance(Vector3 color) =>
        (REC709_RED_WEIGHT * color.X)
        + (REC709_GREEN_WEIGHT * color.Y)
        + (REC709_BLUE_WEIGHT * color.Z);

    /// <summary>Applies exposure, AgX tone mapping, and the output encoding.</summary>
    /// <remarks>
    /// SDR output applies AgX with an output maximum of <c>1.0</c> and clamps to <c>[0, 1]</c>;
    /// scRGB HDR maps AgX white to the display peak and scales by paper white.
    /// <see cref="TonemappingMode.None"/> bypasses the curve.
    /// </remarks>
    /// <param name="sceneColor">The linear scene color from the resolved scene target.</param>
    /// <param name="exposureMultiplier">The exposure multiplier, <c>2^exposureEv</c>.</param>
    /// <param name="tonemapping">The tone-mapping mode.</param>
    /// <param name="encoding">The output encoding.</param>
    /// <param name="hdr">The cached window HDR state supplying paper white and headroom.</param>
    /// <param name="agxWhite">The AgX white in scene-linear units.</param>
    /// <param name="agxContrast">The AgX contrast.</param>
    /// <returns>The display-referred value for the encoding.</returns>
    internal static Vector3 Present(
        Vector3 sceneColor,
        float exposureMultiplier,
        TonemappingMode tonemapping,
        PresentEncoding encoding,
        WindowHdrState hdr,
        float agxWhite = DEFAULT_AGX_WHITE,
        float agxContrast = DEFAULT_AGX_CONTRAST
    )
    {
        float paperWhite = hdr.HdrEnabled ? hdr.SdrWhiteLevel : 1f;
        Vector3 scene = Vector3.Max(sceneColor, Vector3.Zero) * exposureMultiplier;

        if (encoding == PresentEncoding.ScRgbHdr)
        {
            // The headroom is the curve's output maximum: white maps to the display peak, and the
            // curve result is in paper-white units.
            float outputMax = hdr.HdrEnabled ? hdr.HdrHeadroom : 1f;
            Vector3 display =
                tonemapping == TonemappingMode.AgX
                    ? Agx(scene, AgxCurveParameters.Create(agxWhite, agxContrast, outputMax))
                    : scene;
            return Vector3.Max(display * paperWhite, Vector3.Zero);
        }

        Vector3 color =
            tonemapping == TonemappingMode.AgX
                ? Agx(scene, AgxCurveParameters.Create(agxWhite, agxContrast, 1f))
                : scene;
        if (encoding == PresentEncoding.ManualSrgb)
        {
            color = SrgbEncode(color);
        }

        return Vector3.Clamp(color, Vector3.Zero, Vector3.One);
    }

    /// <summary>Encodes one display-linear channel with the piecewise sRGB curve.</summary>
    internal static float SrgbEncodeChannel(float channel) =>
        channel <= SRGB_LINEAR_CUTOFF
            ? 12.92f * channel
            : (1.055f * MathF.Pow(channel, 1f / 2.4f)) - 0.055f;

    /// <summary>Encodes a display-linear color with the piecewise sRGB transfer function.</summary>
    internal static Vector3 SrgbEncode(Vector3 color) =>
        new(SrgbEncodeChannel(color.X), SrgbEncodeChannel(color.Y), SrgbEncodeChannel(color.Z));

    /// <summary>Decodes one 8-bit sRGB-encoded channel to display-linear.</summary>
    /// <remarks>Inverse of the 8-bit quantization of <see cref="SrgbEncodeChannel"/>.</remarks>
    /// <param name="encoded">The sRGB-encoded byte.</param>
    /// <returns>The display-linear channel.</returns>
    internal static float SrgbDecodeChannel(byte encoded)
    {
        float channel = encoded / 255f;
        return channel <= SRGB_ENCODED_CUTOFF
            ? channel / 12.92f
            : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>Evaluates the allenwp curve for one non-negative channel.</summary>
    private static double Curve(double x, in AgxCurveParameters parameters)
    {
        double shoulderMax = parameters.OutputMax - AgxCurveParameters.CROSSOVER_POINT;
        double s = x - AgxCurveParameters.CROSSOVER_POINT;
        double slopeS = parameters.Slope * s;
        s = slopeS * (1.0 + (s / parameters.W)) / (1.0 + (slopeS / shoulderMax));
        s += AgxCurveParameters.CROSSOVER_POINT;

        double t = Math.Pow(x, parameters.Contrast);
        t /= t + parameters.ToeA;

        return x < AgxCurveParameters.CROSSOVER_POINT ? t : s;
    }

    private static (double R, double G, double B) Multiply(
        double[] matrix,
        double r,
        double g,
        double b
    ) =>
        (
            (matrix[0] * r) + (matrix[3] * g) + (matrix[6] * b),
            (matrix[1] * r) + (matrix[4] * g) + (matrix[7] * b),
            (matrix[2] * r) + (matrix[5] * g) + (matrix[8] * b)
        );
}
