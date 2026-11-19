namespace RayBork.Internal;

/// <summary>The precomputed allenwp curve parameters for one AgX output configuration.</summary>
/// <remarks>
/// <see cref="OutputMax"/> is <c>1.0</c> for SDR or the display headroom on scRGB HDR, where the
/// curve output is in paper-white units up to the display peak. The values are derived in double
/// precision and rounded to <see cref="float"/> for the present push constants.
/// </remarks>
internal readonly struct AgxCurveParameters
{
    /// <summary>The 18% middle-gray crossover where the toe and shoulder intersect.</summary>
    internal const double CROSSOVER_POINT = 0.18;

    /// <summary>The lowest supported AgX white; lower values are raised to <c>2.0</c>.</summary>
    internal const double MIN_WHITE = 2.0;

    private AgxCurveParameters(
        double outputMax,
        double white,
        double contrast,
        double toeA,
        double slope,
        double w
    )
    {
        OutputMax = outputMax;
        White = white;
        Contrast = contrast;
        ToeA = toeA;
        Slope = slope;
        W = w;
    }

    /// <summary>Gets the curve output maximum: <c>1.0</c> for SDR or HDR headroom.</summary>
    internal double OutputMax { get; }

    /// <summary>Gets the scene-linear white the curve maps to <see cref="OutputMax"/>.</summary>
    internal double White { get; }

    /// <summary>Gets the curve contrast.</summary>
    internal double Contrast { get; }

    /// <summary>Gets the toe intersection parameter that meets the crossover point.</summary>
    internal double ToeA { get; }

    /// <summary>Gets the shoulder slope at the crossover point.</summary>
    internal double Slope { get; }

    /// <summary>Gets the shoulder width that puts white at <see cref="OutputMax"/>.</summary>
    internal double W { get; }

    /// <summary>Derives the curve parameters for an AgX configuration and output maximum.</summary>
    /// <remarks>An AgX white below <see cref="MIN_WHITE"/> is raised to it.</remarks>
    /// <returns>The derived curve parameters.</returns>
    internal static AgxCurveParameters Create(float agxWhite, float agxContrast, float outputMax)
    {
        double contrast = agxContrast;
        double white = Math.Max(MIN_WHITE, agxWhite) * outputMax;
        double shoulderMax = outputMax - CROSSOVER_POINT;
        double toeA = ((1.0 / CROSSOVER_POINT) - 1.0) * Math.Pow(CROSSOVER_POINT, contrast);
        double slopeDenominator = Math.Pow(CROSSOVER_POINT, contrast) + toeA;
        double slope =
            (contrast * Math.Pow(CROSSOVER_POINT, contrast - 1.0) * toeA)
            / (slopeDenominator * slopeDenominator);
        double w = (white - CROSSOVER_POINT) * (white - CROSSOVER_POINT) / shoulderMax * slope;
        return new AgxCurveParameters(outputMax, white, contrast, toeA, slope, w);
    }
}
