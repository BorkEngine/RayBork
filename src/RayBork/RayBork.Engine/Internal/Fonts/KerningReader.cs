namespace RayBork.Internal;

/// <summary>
/// The kerning data of a face: legacy <c>kern</c> pairs and GPOS pair positioning.
/// </summary>
/// <remarks>
/// <para>
/// When the face has GPOS pair positioning referenced by its <c>kern</c> feature, GPOS is
/// authoritative: <see cref="GetKerning"/> returns its value and a pair that GPOS does not position
/// has zero kerning even when the legacy <c>kern</c> table declares it. The <c>kern</c> table is the
/// fallback for a face without GPOS pair positioning. A face with neither table, or a pair neither
/// source declares, has zero kerning.
/// </para>
/// <para>
/// Values are in font units. The instance is immutable after construction and safe to read from any
/// thread without locks.
/// </para>
/// </remarks>
internal sealed class KerningReader
{
    private readonly KernPairs? _kernPairs;
    private readonly GposKerning? _gpos;

    private KerningReader(KernPairs? kernPairs, GposKerning? gpos)
    {
        _kernPairs = kernPairs;
        _gpos = gpos;
    }

    /// <summary>Parses the optional kerning tables of a face.</summary>
    /// <param name="kern">The complete <c>kern</c> table bytes, or empty when absent.</param>
    /// <param name="gpos">The complete <c>GPOS</c> table bytes, or empty when absent.</param>
    /// <param name="glyphCount">The face's <c>numGlyphs</c>.</param>
    /// <returns>The kerning lookup; it never returns <see langword="null"/>.</returns>
    /// <exception cref="FontFormatException">A present table is malformed.</exception>
    /// <exception cref="FontUnsupportedException">
    /// A present table is valid but exceeds a documented defensive parser limit.
    /// </exception>
    internal static KerningReader Parse(
        ReadOnlyMemory<byte> kern,
        ReadOnlyMemory<byte> gpos,
        int glyphCount
    )
    {
        KernPairs? kernPairs = kern.IsEmpty ? null : KernPairs.TryParse(kern, glyphCount);
        GposKerning? gposKerning = gpos.IsEmpty ? null : GposKerning.TryParse(gpos, glyphCount);
        return new KerningReader(kernPairs, gposKerning);
    }

    /// <summary>Looks up one horizontal pair.</summary>
    /// <param name="leftGlyphId">The left glyph identifier.</param>
    /// <param name="rightGlyphId">The right glyph identifier.</param>
    /// <returns>The kerning adjustment in font units, or zero when the pair is not kerned.</returns>
    internal int GetKerning(int leftGlyphId, int rightGlyphId)
    {
        if (_gpos is not null)
        {
            _gpos.TryGet(leftGlyphId, rightGlyphId, out int gposValue);
            return gposValue;
        }

        if (
            _kernPairs is not null
            && _kernPairs.TryGet(leftGlyphId, rightGlyphId, out int kernValue)
        )
        {
            return kernValue;
        }

        return 0;
    }
}
