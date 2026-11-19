using System.Numerics;
using System.Text;

namespace RayBork;

/// <summary>
/// One laid-out glyph: the pen advance, the padded MSDF quad, and the glyph texture's UV rectangle.
/// </summary>
/// <remarks>
/// All sizes are in em units, and the quad includes the MSDF padding. A value is produced only for a
/// glyph with a generated texture, whose UVs cover the whole texture.
/// </remarks>
public readonly struct Glyph
{
    /// <summary>Initializes one glyph from its rune, pen advance, and padded quad.</summary>
    internal Glyph(Rune rune, Vector2 advance, Vector2 bearing, Vector2 size)
    {
        Rune = rune;
        Advance = advance;
        Bearing = bearing;
        Size = size;
        Uv0 = Vector2.Zero;
        Uv1 = Vector2.One;
    }

    /// <summary>
    /// Gets the rune that produced this glyph; an unmapped rune uses the <c>.notdef</c> outline.
    /// </summary>
    public Rune Rune { get; }

    /// <summary>
    /// Gets the horizontal pen advance in em units; the vertical component is always <c>0</c>.
    /// </summary>
    public Vector2 Advance { get; }

    /// <summary>
    /// Gets the quad's bottom-left corner relative to the baseline, in em units.
    /// </summary>
    public Vector2 Bearing { get; }

    /// <summary>Gets the padded quad size in em units, including the MSDF padding.</summary>
    public Vector2 Size { get; }

    /// <summary>
    /// Gets the UV of the quad's bottom-left corner; always <c>(0, 0)</c> for a single-glyph texture.
    /// </summary>
    public Vector2 Uv0 { get; }

    /// <summary>
    /// Gets the UV of the quad's top-right corner; always <c>(1, 1)</c> for a single-glyph texture.
    /// </summary>
    public Vector2 Uv1 { get; }
}
