using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The CPU reference for the present pass's AgX tone map and HDR highlight extension.
/// </summary>
/// <remarks>
/// <para>
/// The formulas mirror <c>Shaders/Present.frag.glsl</c> exactly, including the column-major AgX
/// matrices, the log2 range, and the polynomial contrast approximation. A change to the present
/// transform must update both this type and the pinned values in <c>AgxHighlightTests</c>.
/// </para>
/// <para>
/// The AgX math is evaluated in double precision and rounded to <see cref="float"/> only at the end,
/// so the pinned constants and the neutral-white response do not drift with the JIT or platform.
/// The present shader evaluates the same formulas in 32-bit floats, so the GPU probes compare with
/// tolerances that exceed single-precision rounding. This type is a test and documentation
/// reference; the renderer never calls it per frame.
/// </para>
/// </remarks>
internal static class AgxToneMap {
    /// <summary>The log2 lower bound AgX clamps the inset scene color to, in EV.</summary>
    internal const double AGX_MIN_EV = -12.47393;

    /// <summary>The log2 upper bound AgX clamps the inset scene color to, in EV.</summary>
    internal const double AGX_MAX_EV = 4.026069;

    /// <summary>
    /// The scene-linear luminance at which AgX clamps its log range: <c>2^4.026069</c>.
    /// </summary>
    /// <remarks>
    /// This is the AgX white point and the knee of the HDR highlight extension. Above it AgX no
    /// longer responds to scene luminance, so the extension maps the remaining scene luminance into
    /// the display headroom.
    /// </remarks>
    internal const double AGX_WHITE_SCENE_LUMINANCE = 16.291742365295857;

    /// <summary>
    /// The Rec.709 luminance of AgX's neutral-white response for a very large scene luminance.
    /// </summary>
    /// <remarks>
    /// The scRGB HDR path normalizes AgX's display white to <c>1.0</c> with this value before it
    /// scales to the window's paper white. The normalized path is about <c>1.7%</c> brighter than
    /// the raw M2 AgX response, which is the raw AgX neutral white being slightly below <c>1.0</c>.
    /// The constant must match <c>AGX_OUTPUT_WHITE_LUMINANCE</c> in <c>Present.frag.glsl</c>.
    /// </remarks>
    internal const double AGX_OUTPUT_WHITE_LUMINANCE = 0.9829795037669735;

    private const double REC709_RED_WEIGHT = 0.2126;
    private const double REC709_GREEN_WEIGHT = 0.7152;
    private const double REC709_BLUE_WEIGHT = 0.0722;
    private const double MIN_LOG_INPUT = 1e-10;
    private const double MIN_DISPLAY_LUMINANCE = 1e-5;
    private const float SRGB_LINEAR_CUTOFF = 0.0031308f;

    // Columns of the AgX inset and outset matrices in GLSL mat3 constructor order. GLSL multiplies a
    // column-major matrix by a column vector, so applying a matrix is the sum of each column scaled
    // by the matching input channel.
    private static readonly double[] _agxInset = [
        0.842479062253094, 0.0423282422610123, 0.0423756549057051,
        0.0784335999999992, 0.878468636469772, 0.0784336,
        0.0792237451477643, 0.0791661274605434, 0.879142973793104,
    ];

    private static readonly double[] _agxOutset = [
        1.19687900512017, -0.0980208811401368, -0.0990297440797205,
        -0.0528968517574562, 1.15190312990417, -0.0989611768448433,
        -0.0529716355144438, -0.0980434501171241, 1.15107367264116,
    ];

    /// <summary>
    /// Applies the AgX base transform to a linear scene color, without any look transform.
    /// </summary>
    /// <remarks>
    /// The result is AgX's display-linear response before encoding. It is not normalized and not
    /// clamped to <c>[0, 1]</c>; the SDR paths clamp it and the scRGB HDR path normalizes it.
    /// </remarks>
    /// <param name="color">The linear scene color, with HDR values allowed.</param>
    /// <returns>The AgX display-linear response.</returns>
    internal static Vector3 Agx(Vector3 color) {
        (double r, double g, double b) = Multiply(_agxInset, color.X, color.Y, color.Z);
        r = NormalizeLog(ClampLog2(r));
        g = NormalizeLog(ClampLog2(g));
        b = NormalizeLog(ClampLog2(b));
        r = DefaultContrast(r);
        g = DefaultContrast(g);
        b = DefaultContrast(b);
        (r, g, b) = Multiply(_agxOutset, r, g, b);
        return new Vector3(
            (float)Math.Max(r, 0.0),
            (float)Math.Max(g, 0.0),
            (float)Math.Max(b, 0.0));
    }

    /// <summary>
    /// Computes the Rec.709 luminance of a linear RGB color.
    /// </summary>
    /// <param name="color">The linear RGB color.</param>
    /// <returns>The Rec.709 luminance.</returns>
    internal static double Luminance(Vector3 color) =>
        (REC709_RED_WEIGHT * color.X) + (REC709_GREEN_WEIGHT * color.Y) + (REC709_BLUE_WEIGHT * color.Z);

    /// <summary>
    /// Gets whether the HDR highlight extension is active for a present configuration.
    /// </summary>
    /// <remarks>
    /// The extension needs the scRGB HDR encoding, AgX, an HDR-enabled window, and headroom above
    /// SDR white. The backend projects this rule into the present push constants, and
    /// <see cref="Present"/> applies the same rule to the CPU reference.
    /// </remarks>
    /// <param name="encoding">The output encoding.</param>
    /// <param name="tonemapping">The tone-mapping mode.</param>
    /// <param name="hdr">The cached window HDR state.</param>
    /// <returns><see langword="true"/> when the extension is active.</returns>
    internal static bool IsHdrHighlightActive(
        PresentEncoding encoding,
        TonemappingMode tonemapping,
        WindowHdrState hdr) =>
        encoding == PresentEncoding.ScRgbHdr
        && tonemapping == TonemappingMode.AgX
        && hdr.HdrEnabled
        && hdr.HdrHeadroom > 1f;

    /// <summary>
    /// Applies the complete present transform: exposure, tone mapping, the HDR highlight extension,
    /// and the output encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The SDR encodings apply the raw AgX response and clamp to <c>[0, 1]</c>, exactly as M2 did.
    /// The scRGB HDR encoding normalizes AgX's neutral white to <c>1.0</c>, scales it to paper
    /// white, and, above the AgX white point, extends the scene luminance into the display headroom
    /// at constant chromaticity. <see cref="TonemappingMode.None"/> bypasses AgX and writes the
    /// exposed scene value directly on the scRGB path.
    /// </para>
    /// <para>
    /// <paramref name="hdr"/> supplies the paper white, the peak, and the highlight switch the same
    /// way <see cref="Vulkan12Backend.Submit"/> does, so the reference cannot diverge from the
    /// pipeline configuration.
    /// </para>
    /// </remarks>
    /// <param name="sceneColor">The linear scene color sampled from the resolved scene target.</param>
    /// <param name="exposureMultiplier">The exposure multiplier, <c>2^exposureEv</c>.</param>
    /// <param name="tonemapping">The tone-mapping mode.</param>
    /// <param name="encoding">The output encoding.</param>
    /// <param name="hdr">The cached window HDR state.</param>
    /// <returns>The display-referred present value for the encoding.</returns>
    internal static Vector3 Present(
        Vector3 sceneColor,
        float exposureMultiplier,
        TonemappingMode tonemapping,
        PresentEncoding encoding,
        WindowHdrState hdr) {
        float paperWhite = hdr.HdrEnabled ? hdr.SdrWhiteLevel : 1f;
        float peak = paperWhite * (hdr.HdrEnabled ? hdr.HdrHeadroom : 1f);
        Vector3 scene = Vector3.Max(sceneColor, Vector3.Zero) * exposureMultiplier;

        if (encoding != PresentEncoding.ScRgbHdr) {
            Vector3 display = tonemapping == TonemappingMode.AgX ? Agx(scene) : scene;
            if (encoding == PresentEncoding.ManualSrgb) {
                display = SrgbEncode(display);
            }

            return Vector3.Clamp(display, Vector3.Zero, Vector3.One);
        }

        if (tonemapping != TonemappingMode.AgX) {
            return scene * paperWhite;
        }

        Vector3 tone = Agx(scene);
        Vector3 normalized = tone / (float)AGX_OUTPUT_WHITE_LUMINANCE;
        double sceneLuminance = Luminance(scene);
        if (IsHdrHighlightActive(encoding, tonemapping, hdr)
            && peak > paperWhite
            && sceneLuminance > AGX_WHITE_SCENE_LUMINANCE) {
            // Extend the luminance above the AgX knee from paper white to peak at constant
            // chromaticity: scaling the normalized AgX color keeps its channel ratios.
            double displayLuminance = Luminance(normalized);
            double ratio = sceneLuminance / AGX_WHITE_SCENE_LUMINANCE;
            double headroom = peak / (double)paperWhite;
            double progress = Math.Clamp((ratio - 1.0) / (headroom - 1.0), 0.0, 1.0);
            double outputLuminance = paperWhite + (((double)peak - paperWhite) * progress);
            float scale = (float)(outputLuminance / Math.Max(displayLuminance, MIN_DISPLAY_LUMINANCE));
            return normalized * scale;
        }

        return normalized * paperWhite;
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

    private static double ClampLog2(double channel) =>
        Math.Clamp(Math.Log2(Math.Max(channel, MIN_LOG_INPUT)), AGX_MIN_EV, AGX_MAX_EV);

    private static double NormalizeLog(double channel) =>
        (channel - AGX_MIN_EV) / (AGX_MAX_EV - AGX_MIN_EV);

    private static double DefaultContrast(double x) {
        double x2 = x * x;
        double x4 = x2 * x2;
        return (15.5 * x4 * x2) - (40.14 * x4 * x) + (31.96 * x4) - (6.868 * x2 * x)
            + (0.4298 * x2) + (0.1191 * x) - 0.00232;
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
