using System.Numerics;
using System.Text;

namespace RayBork;

/// <summary>
/// One laid-out glyph: the pen advance, the padded MSDF quad, and the glyph texture's UV rectangle.
/// </summary>
/// <remarks>
/// <para>
/// All size values are in em units: multiply by the requested text size to get world units. The quad
/// is the final padded glyph box, so <see cref="Size"/> includes the MSDF padding. For a glyph returned
/// by <see cref="Font.TryGetGlyph"/>, <see cref="Uv0"/> is <c>(0, 0)</c> and <see cref="Uv1"/> is
/// <c>(1, 1)</c> because one glyph is one whole texture; there is no atlas.
/// </para>
/// <para>
/// A <see cref="Glyph"/> value is only produced by <see cref="Font.TryGetGlyph"/> for a glyph whose
/// MSDF texture is already generated. The default value is not a usable glyph: it has a zero
/// <see cref="Rune"/> (U+0000), zero vectors, and zero UVs.
/// </para>
/// </remarks>
public readonly struct Glyph
{
    /// <summary>
    /// Initializes one glyph from its rune, pen advance, and padded quad.
    /// </summary>
    /// <param name="rune">The rune that produced the glyph.</param>
    /// <param name="advance">The horizontal pen advance in em units.</param>
    /// <param name="bearing">The quad's bottom-left corner relative to the baseline in em units.</param>
    /// <param name="size">The padded quad size in em units.</param>
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
    /// Gets the rune that produced this glyph. An unmapped rune reports itself here and uses the
    /// <c>.notdef</c> outline.
    /// </summary>
    public Rune Rune { get; }

    /// <summary>
    /// Gets the horizontal pen advance in em units. The vertical component is always <c>0</c>.
    /// </summary>
    public Vector2 Advance { get; }

    /// <summary>
    /// Gets the quad's bottom-left corner relative to the pen position on the baseline, in em units.
    /// </summary>
    public Vector2 Bearing { get; }

    /// <summary>
    /// Gets the padded quad size in em units. It includes the MSDF padding.
    /// </summary>
    public Vector2 Size { get; }

    /// <summary>
    /// Gets the texture coordinate of the quad's bottom-left corner. For a glyph returned by
    /// <see cref="Font.TryGetGlyph"/> it is always <c>(0, 0)</c> because the glyph texture holds only
    /// this glyph.
    /// </summary>
    public Vector2 Uv0 { get; }

    /// <summary>
    /// Gets the texture coordinate of the quad's top-right corner. For a glyph returned by
    /// <see cref="Font.TryGetGlyph"/> it is always <c>(1, 1)</c> because the glyph texture holds only
    /// this glyph.
    /// </summary>
    public Vector2 Uv1 { get; }
}
