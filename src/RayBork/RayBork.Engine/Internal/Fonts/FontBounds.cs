namespace RayBork.Internal;

/// <summary>
/// The bounding box of one glyph, in font units and y-up coordinates.
/// </summary>
/// <remarks>
/// For a TrueType glyph the values are the <c>xMin</c>, <c>yMin</c>, <c>xMax</c>, and <c>yMax</c>
/// fields of the glyph header. For a CFF glyph the CFF reader computes them as the outward-rounded
/// integer control-point hull of the resolved outline, so the box contains the whole outline. A glyph
/// without outline data has an all-zero box.
/// </remarks>
internal readonly struct FontBounds {
    /// <summary>Initializes a bounds box.</summary>
    /// <param name="xMin">The minimum x coordinate.</param>
    /// <param name="yMin">The minimum y coordinate.</param>
    /// <param name="xMax">The maximum x coordinate.</param>
    /// <param name="yMax">The maximum y coordinate.</param>
    internal FontBounds(int xMin, int yMin, int xMax, int yMax) {
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
    }

    /// <summary>Gets the minimum x coordinate.</summary>
    internal int XMin { get; }

    /// <summary>Gets the minimum y coordinate.</summary>
    internal int YMin { get; }

    /// <summary>Gets the maximum x coordinate.</summary>
    internal int XMax { get; }

    /// <summary>Gets the maximum y coordinate.</summary>
    internal int YMax { get; }
}
