using System.Text;

namespace RayBork.Internal;

/// <summary>The <see cref="ITextMetrics"/> adapter of one parsed font face.</summary>
/// <remarks>
/// Metrics are in em units and need no glyph texture; an unmapped rune falls back to glyph
/// zero (<c>.notdef</c>), so every rune reports a glyph. The instance holds the glyph pipeline,
/// which owns the parsed face.
/// </remarks>
internal sealed class FontTextMetrics : ITextMetrics
{
    private readonly FontGlyphPipeline _pipeline;
    private readonly int _unitsPerEm;

    /// <summary>Initializes the adapter over the glyph pipeline that owns the face.</summary>
    internal FontTextMetrics(FontGlyphPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        _pipeline = pipeline;
        _unitsPerEm = pipeline.Face.UnitsPerEm;
        LineHeight =
            (pipeline.Face.Ascender - pipeline.Face.Descender + pipeline.Face.LineGap)
            / (float)_unitsPerEm;
        Ascent = pipeline.Face.Ascender / (float)_unitsPerEm;
        Descent = -pipeline.Face.Descender / (float)_unitsPerEm;
    }

    /// <inheritdoc/>
    public float LineHeight { get; }

    /// <inheritdoc/>
    public float Ascent { get; }

    /// <summary>Gets the descent below the baseline in em units.</summary>
    internal float Descent { get; }

    /// <inheritdoc/>
    /// <remarks>Unmapped runes map to <c>.notdef</c> and still report a glyph.</remarks>
    public bool TryGetGlyphMetrics(Rune rune, out TextGlyphMetrics metrics)
    {
        int glyphId = SfntFace.NOTDEF_GLYPH_ID;
        _pipeline.Face.TryGetGlyphId(rune, out glyphId);
        metrics = _pipeline.GetMetrics(glyphId);
        return true;
    }

    /// <inheritdoc/>
    public float GetKerning(int leftGlyphId, int rightGlyphId) =>
        _pipeline.Face.GetKerning(leftGlyphId, rightGlyphId) / (float)_unitsPerEm;
}
