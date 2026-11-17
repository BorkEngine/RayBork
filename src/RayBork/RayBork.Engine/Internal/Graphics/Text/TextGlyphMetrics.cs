namespace RayBork.Internal;

/// <summary>
/// The layout metrics of one mapped glyph, in em units and y-up. The quad is the final padded quad, so
/// its size includes the MSDF padding rather than the outline box.
/// </summary>
/// <param name="GlyphId">
/// The font's glyph id. It must be non-negative; the layout engine reserves a negative value for
/// "no glyph".
/// </param>
/// <param name="AdvanceX">The horizontal pen advance applied after the glyph.</param>
/// <param name="BearingX">The quad's left edge relative to the pen position on the baseline.</param>
/// <param name="BearingY">The quad's bottom edge relative to the baseline.</param>
/// <param name="Width">The quad's width, including the padding.</param>
/// <param name="Height">The quad's height, including the padding.</param>
internal readonly record struct TextGlyphMetrics(
    int GlyphId,
    float AdvanceX,
    float BearingX,
    float BearingY,
    float Width,
    float Height
);
