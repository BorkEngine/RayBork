namespace RayBork.Internal;

/// <summary>
/// The precomputed allenwp tone-mapping curve parameters for one AgX output configuration.
/// </summary>
/// <remarks>
/// <para>
/// The formulas match Godot 4.7's <c>environment_get_tonemap_parameters</c> for AgX. The curve and
/// its parameter derivation are derived from Godot's <c>tonemap.glsl</c> (MIT); the AgX base
/// transform follows EaryChow's AgX LUT generator that Blender uses. The parameters are evaluated
/// in double precision so the CPU reference and its pinned tests do not drift with the platform.
/// The present pipeline rounds them to <see cref="float"/> for its push constants, and the shader
/// evaluates the same curve in 32-bit floats.
/// </para>
/// <para>
/// <see cref="OutputMax"/> is <c>1.0</c> on an SDR window and the display headroom (peak divided by
/// paper white) on the scRGB HDR path. The curve maps <see cref="White"/> to <see cref="OutputMax"/>
/// and clamps above it, so on the HDR path the curve output is in paper-white units up to the
/// display peak.
/// </para>
/// </remarks>
internal readonly struct AgxCurveParameters {
    /// <summary>
    /// The allenwp curve crossover: 18% middle gray, where the toe and shoulder intersections meet.
    /// </summary>
    internal const double CROSSOVER_POINT = 0.18;

    /// <summary>
    /// The lowest supported AgX white. Godot clamps below this because AgX behaves poorly with a
    /// white near SDR.
    /// </summary>
    internal const double MIN_WHITE = 2.0;

    private AgxCurveParameters(double outputMax, double white, double contrast, double toeA, double slope, double w) {
        OutputMax = outputMax;
        White = white;
        Contrast = contrast;
        ToeA = toeA;
        Slope = slope;
        W = w;
    }

    /// <summary>Gets the curve output maximum: <c>1.0</c> for SDR or the HDR display headroom.</summary>
    internal double OutputMax { get; }

    /// <summary>Gets the scene-linear white the curve maps to <see cref="OutputMax"/>.</summary>
    internal double White { get; }

    /// <summary>Gets the curve contrast.</summary>
    internal double Contrast { get; }

    /// <summary>Gets the toe intersection parameter that makes the toe meet the crossover point.</summary>
    internal double ToeA { get; }

    /// <summary>Gets the shoulder slope at the crossover point.</summary>
    internal double Slope { get; }

    /// <summary>Gets the shoulder width that places the curve's white at <see cref="OutputMax"/>.</summary>
    internal double W { get; }

    /// <summary>
    /// Derives the curve parameters for an AgX configuration and an output maximum.
    /// </summary>
    /// <param name="agxWhite">
    /// The AgX white in scene-linear units before the output maximum is applied. Values below
    /// <see cref="MIN_WHITE"/> are raised to it.
    /// </param>
    /// <param name="agxContrast">The AgX contrast.</param>
    /// <param name="outputMax">
    /// The curve output maximum: <c>1.0</c> for SDR, or the HDR display headroom.
    /// </param>
    /// <returns>The derived curve parameters.</returns>
    internal static AgxCurveParameters Create(float agxWhite, float agxContrast, float outputMax) {
        double contrast = agxContrast;
        double white = Math.Max(MIN_WHITE, agxWhite) * outputMax;
        double shoulderMax = outputMax - CROSSOVER_POINT;
        double toeA = ((1.0 / CROSSOVER_POINT) - 1.0) * Math.Pow(CROSSOVER_POINT, contrast);
        double slopeDenominator = Math.Pow(CROSSOVER_POINT, contrast) + toeA;
        double slope = (contrast * Math.Pow(CROSSOVER_POINT, contrast - 1.0) * toeA)
            / (slopeDenominator * slopeDenominator);
        double w = (white - CROSSOVER_POINT) * (white - CROSSOVER_POINT) / shoulderMax * slope;
        return new AgxCurveParameters(outputMax, white, contrast, toeA, slope, w);
    }
}
