namespace RayBork.Internal;

/// <summary>Per-font parameters shared by the glyph pipeline and the MSDF generator.</summary>
/// <remarks>A <see cref="UnitsPerEm"/> mismatch with the parsed face is rejected.</remarks>
internal readonly record struct FontGlyphSettings
{
    /// <summary>Initializes validated glyph settings.</summary>
    /// <param name="unitsPerEm">The face's font units per em; must be positive.</param>
    /// <param name="msdfPixelSize">The padded side in px; must exceed twice the padding.</param>
    /// <param name="msdfRange">The distance range in px; must be positive.</param>
    /// <param name="msdfPadding">The empty border in px; must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    internal FontGlyphSettings(int unitsPerEm, int msdfPixelSize, int msdfRange, int msdfPadding)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerEm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(msdfPixelSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(msdfRange);
        ArgumentOutOfRangeException.ThrowIfNegative(msdfPadding);
        if (msdfPixelSize <= (2L * msdfPadding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(msdfPixelSize),
                msdfPixelSize,
                "The pixel size must be greater than twice the padding so that a glyph box remains."
            );
        }

        UnitsPerEm = unitsPerEm;
        MsdfPixelSize = msdfPixelSize;
        MsdfRange = msdfRange;
        MsdfPadding = msdfPadding;
    }

    /// <summary>Gets the face's font units per em.</summary>
    internal int UnitsPerEm { get; }

    /// <summary>Gets the padded glyph texture side in px.</summary>
    internal int MsdfPixelSize { get; }

    /// <summary>Gets the distance range in px.</summary>
    internal int MsdfRange { get; }

    /// <summary>Gets the empty border around the glyph box in px.</summary>
    internal int MsdfPadding { get; }

    /// <summary>Creates the defaults a font without an authored override uses.</summary>
    /// <param name="unitsPerEm">The face's font units per em; must be positive.</param>
    /// <returns>A 32 px padded texture with a 4 px range and 2 px padding.</returns>
    internal static FontGlyphSettings CreateDefault(int unitsPerEm) =>
        new(
            unitsPerEm,
            MsdfGenerator.DEFAULT_PIXEL_SIZE,
            MsdfGenerator.DEFAULT_RANGE,
            MsdfGenerator.DEFAULT_PADDING
        );
}
