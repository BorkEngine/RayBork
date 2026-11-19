namespace RayBork.Internal;

/// <summary>The layout metrics of one mapped glyph, in em units and y-up.</summary>
/// <remarks>The quad size includes MSDF padding; a negative glyph id means no glyph.</remarks>
/// <param name="GlyphId">The font glyph id; non-negative.</param>
/// <param name="AdvanceX">The horizontal pen advance in em units.</param>
/// <param name="BearingX">The quad's left edge from the pen position.</param>
/// <param name="BearingY">The quad's bottom edge from the baseline.</param>
/// <param name="Width">The padded quad width in em units.</param>
/// <param name="Height">The padded quad height in em units.</param>
internal readonly record struct TextGlyphMetrics(
    int GlyphId,
    float AdvanceX,
    float BearingX,
    float BearingY,
    float Width,
    float Height
);
