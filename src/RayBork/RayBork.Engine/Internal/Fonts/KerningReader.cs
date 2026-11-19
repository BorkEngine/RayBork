namespace RayBork.Internal;

/// <summary>The kerning data of a face: legacy <c>kern</c> pairs and GPOS positioning.</summary>
/// <remarks>
/// GPOS is authoritative when it has kern pair positioning; otherwise the legacy <c>kern</c> table
/// is the fallback. Values are in font units, and the instance is immutable and safe to read from
/// any thread.
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

    /// <summary>Looks up one horizontal pair and returns its adjustment in font units, or zero.</summary>
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
