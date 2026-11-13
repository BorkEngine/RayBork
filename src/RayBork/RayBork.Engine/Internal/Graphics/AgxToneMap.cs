using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The CPU reference for the present pass's AgX tone map.
/// </summary>
/// <remarks>
/// <para>
/// The formulas mirror <c>Shaders/Present.frag.glsl</c> exactly: the combined Rec.709 to Rec.2020 and
/// AgX inset matrix, the allenwp curve, the clamp to the output maximum, and the combined inverse
/// outset and Rec.2020 to Rec.709 matrix. A change to the present transform must update both this
/// type and the pinned values in <c>AgxToneMapTests</c>.
/// </para>
/// <para>
/// The AgX implementation and the curve are derived from Godot 4.7's <c>tonemap.glsl</c> (MIT). The
/// AgX base transform follows EaryChow's AgX LUT generator that Blender uses
/// (https://github.com/EaryChow/AgX_LUT_Gen), and the curve is allenwp's tonemapping curve
/// (https://allenwp.com/blog/2025/05/29/allenwp-tonemapping-curve/). The combined matrices preserve
/// neutral gray, so a neutral input stays neutral through the inset, curve, and outset.
/// </para>
/// <para>
/// The color math is evaluated in double precision and rounded to <see cref="float"/> only at the
/// end, so the pinned constants do not drift with the JIT or platform. The present shader evaluates
/// the same formulas in 32-bit floats, so the GPU probes compare with tolerances that exceed
/// single-precision rounding. This type is a test and documentation reference; the renderer never
/// calls it per frame.
/// </para>
/// </remarks>
internal static class AgxToneMap {
    /// <summary>The default AgX white in scene-linear units, matching Godot's environment default.</summary>
    internal const float DEFAULT_AGX_WHITE = 16.29f;

    /// <summary>The default AgX contrast, matching Godot's environment default.</summary>
    internal const float DEFAULT_AGX_CONTRAST = 1.25f;

    private const double REC709_RED_WEIGHT = 0.2126;
    private const double REC709_GREEN_WEIGHT = 0.7152;
    private const double REC709_BLUE_WEIGHT = 0.0722;
    private const float SRGB_LINEAR_CUTOFF = 0.0031308f;

    // Columns of the combined inset and outset matrices in GLSL mat3 constructor order. GLSL
    // multiplies a column-major matrix by a column vector, so applying a matrix is the sum of each
    // column scaled by the matching input channel. The inset combines Rec.709 to Rec.2020 with the
    // Blender AgX inset; the outset combines the inverse AgX outset with Rec.2020 to Rec.709.
    private static readonly double[] _inset = [
        0.544814746488245, 0.140416948464053, 0.0888104196149096,
        0.373787398372697, 0.754137554567394, 0.178871756420858,
        0.0813978551390581, 0.105445496968552, 0.732317823964232,
    ];

    private static readonly double[] _outset = [
        1.96488741169489, -0.299313364904742, -0.164352742528393,
        -0.855988495690215, 1.32639796461980, -0.238183969428088,
        -0.108898916004672, -0.0270845997150571, 1.40253671195648,
    ];

    /// <summary>
    /// Applies the AgX base transform to a linear scene color, without any look transform.
    /// </summary>
    /// <remarks>
    /// The input must be non-negative; the combined inset matrix has only positive coefficients, so
    /// the curve never sees a negative channel. The result is the display-linear response after the
    /// curve's output-maximum clamp and the inverse outset, and it may have small negative channels
    /// for saturated colors. Callers clamp or reject the negative range.
    /// </remarks>
    /// <param name="color">The linear scene color, with HDR values allowed.</param>
    /// <param name="parameters">The curve parameters for the output configuration.</param>
    /// <returns>The AgX display-linear response.</returns>
    internal static Vector3 Agx(Vector3 color, in AgxCurveParameters parameters) {
        (double r, double g, double b) = Multiply(_inset, color.X, color.Y, color.Z);
        r = Math.Min(parameters.OutputMax, Curve(r, parameters));
        g = Math.Min(parameters.OutputMax, Curve(g, parameters));
        b = Math.Min(parameters.OutputMax, Curve(b, parameters));
        (r, g, b) = Multiply(_outset, r, g, b);
        return new Vector3((float)r, (float)g, (float)b);
    }

    /// <summary>
    /// Computes the Rec.709 luminance of a linear RGB color.
    /// </summary>
    /// <param name="color">The linear RGB color.</param>
    /// <returns>The Rec.709 luminance.</returns>
    internal static double Luminance(Vector3 color) =>
        (REC709_RED_WEIGHT * color.X) + (REC709_GREEN_WEIGHT * color.Y) + (REC709_BLUE_WEIGHT * color.Z);

    /// <summary>
    /// Applies the complete present transform: exposure, tone mapping, and the output encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDR encodings apply AgX with an output maximum of <c>1.0</c> and clamp the display-linear
    /// result to <c>[0, 1]</c>; the manual sRGB encoding is applied before the clamp. The scRGB HDR
    /// encoding applies AgX with the display headroom as its output maximum, so the curve maps the
    /// AgX white to the display peak in paper-white units, then scales the result by paper white.
    /// <see cref="TonemappingMode.None"/> bypasses AgX and writes the exposed scene value directly,
    /// with no curve or upper clamp beyond the existing encoding behavior.
    /// </para>
    /// <para>
    /// <paramref name="hdr"/> supplies the paper white and the headroom the same way
    /// <see cref="Vulkan12Backend"/> does, so the reference cannot diverge from the pipeline
    /// configuration.
    /// </para>
    /// </remarks>
    /// <param name="sceneColor">The linear scene color sampled from the resolved scene target.</param>
    /// <param name="exposureMultiplier">The exposure multiplier, <c>2^exposureEv</c>.</param>
    /// <param name="tonemapping">The tone-mapping mode.</param>
    /// <param name="encoding">The output encoding.</param>
    /// <param name="hdr">The cached window HDR state.</param>
    /// <param name="agxWhite">The AgX white in scene-linear units.</param>
    /// <param name="agxContrast">The AgX contrast.</param>
    /// <returns>The display-referred present value for the encoding.</returns>
    internal static Vector3 Present(
        Vector3 sceneColor,
        float exposureMultiplier,
        TonemappingMode tonemapping,
        PresentEncoding encoding,
        WindowHdrState hdr,
        float agxWhite = DEFAULT_AGX_WHITE,
        float agxContrast = DEFAULT_AGX_CONTRAST) {
        float paperWhite = hdr.HdrEnabled ? hdr.SdrWhiteLevel : 1f;
        Vector3 scene = Vector3.Max(sceneColor, Vector3.Zero) * exposureMultiplier;

        if (encoding == PresentEncoding.ScRgbHdr) {
            // The headroom is the curve's output maximum: white maps to the display peak, and the
            // curve result is in paper-white units.
            float outputMax = hdr.HdrEnabled ? hdr.HdrHeadroom : 1f;
            Vector3 display = tonemapping == TonemappingMode.AgX
                ? Agx(scene, AgxCurveParameters.Create(agxWhite, agxContrast, outputMax))
                : scene;
            return Vector3.Max(display * paperWhite, Vector3.Zero);
        }

        Vector3 color = tonemapping == TonemappingMode.AgX
            ? Agx(scene, AgxCurveParameters.Create(agxWhite, agxContrast, 1f))
            : scene;
        if (encoding == PresentEncoding.ManualSrgb) {
            color = SrgbEncode(color);
        }

        return Vector3.Clamp(color, Vector3.Zero, Vector3.One);
    }

    /// <summary>
    /// Encodes one display-linear channel with the piecewise sRGB transfer function.
    /// </summary>
    /// <param name="channel">The display-linear channel.</param>
    /// <returns>The sRGB-encoded value.</returns>
    internal static float SrgbEncodeChannel(float channel) =>
        channel <= SRGB_LINEAR_CUTOFF
            ? 12.92f * channel
            : (1.055f * MathF.Pow(channel, 1f / 2.4f)) - 0.055f;

    /// <summary>
    /// Encodes a display-linear color with the piecewise sRGB transfer function.
    /// </summary>
    /// <param name="color">The display-linear color.</param>
    /// <returns>The sRGB-encoded color.</returns>
    internal static Vector3 SrgbEncode(Vector3 color) => new(
        SrgbEncodeChannel(color.X),
        SrgbEncodeChannel(color.Y),
        SrgbEncodeChannel(color.Z));

    /// <summary>
    /// Evaluates the allenwp curve for one non-negative channel.
    /// </summary>
    private static double Curve(double x, in AgxCurveParameters parameters) {
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
        double b) => (
            (matrix[0] * r) + (matrix[3] * g) + (matrix[6] * b),
            (matrix[1] * r) + (matrix[4] * g) + (matrix[7] * b),
            (matrix[2] * r) + (matrix[5] * g) + (matrix[8] * b));
}
