namespace RayBork.Internal;

/// <summary>
/// The per-font parameters that the glyph pipeline and the MSDF generator share.
/// </summary>
/// <remarks>
/// <para>
/// The later <c>Font</c> asset creates one instance from the baked font descriptor: the face's
/// <c>head</c> <c>unitsPerEm</c> plus the authored MSDF pixel size, range, and padding.
/// <see cref="UnitsPerEm"/> must equal the parsed face's unit, and <see cref="FontGlyphPipeline"/>
/// rejects a mismatch instead of silently producing wrong metrics.
/// </para>
/// <para>
/// Every value is validated in the constructor: the units, pixel size, and range are positive, the
/// padding is non-negative, and the pixel size is greater than twice the padding so a glyph box
/// remains after the empty border. The defaults match <see cref="MsdfGenerator"/>: a 32 px padded
/// texture, a 4 px range, and 2 px padding.
/// </para>
/// </remarks>
internal readonly record struct FontGlyphSettings {
    /// <summary>
    /// Initializes validated glyph settings.
    /// </summary>
    /// <param name="unitsPerEm">The face's font units per em; it must be positive.</param>
    /// <param name="msdfPixelSize">
    /// The complete padded texture side in pixels; it must be positive and greater than twice
    /// <paramref name="msdfPadding"/>.
    /// </param>
    /// <param name="msdfRange">The distance range in pixels; it must be positive.</param>
    /// <param name="msdfPadding">The empty border in pixels; it must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its supported range.</exception>
    internal FontGlyphSettings(
        int unitsPerEm,
        int msdfPixelSize,
        int msdfRange,
        int msdfPadding) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerEm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(msdfPixelSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(msdfRange);
        ArgumentOutOfRangeException.ThrowIfNegative(msdfPadding);
        if (msdfPixelSize <= (2L * msdfPadding)) {
            throw new ArgumentOutOfRangeException(
                nameof(msdfPixelSize),
                msdfPixelSize,
                "The pixel size must be greater than twice the padding so that a glyph box remains.");
        }

        UnitsPerEm = unitsPerEm;
        MsdfPixelSize = msdfPixelSize;
        MsdfRange = msdfRange;
        MsdfPadding = msdfPadding;
    }

    /// <summary>Gets the face's font units per em.</summary>
    internal int UnitsPerEm { get; }

    /// <summary>Gets the complete padded glyph texture side in pixels.</summary>
    internal int MsdfPixelSize { get; }

    /// <summary>Gets the distance range in pixels.</summary>
    internal int MsdfRange { get; }

    /// <summary>Gets the empty border around the glyph box in pixels.</summary>
    internal int MsdfPadding { get; }

    /// <summary>
    /// Creates the generator's default settings for one face.
    /// </summary>
    /// <param name="unitsPerEm">The face's font units per em.</param>
    /// <returns>The settings a font without an authored override uses.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="unitsPerEm"/> is not positive.</exception>
    internal static FontGlyphSettings CreateDefault(int unitsPerEm) => new(
        unitsPerEm,
        MsdfGenerator.DEFAULT_PIXEL_SIZE,
        MsdfGenerator.DEFAULT_RANGE,
        MsdfGenerator.DEFAULT_PADDING);
}
