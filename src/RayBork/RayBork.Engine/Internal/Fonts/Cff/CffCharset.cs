namespace RayBork.Internal;

/// <summary>The CFF charset: the string identifier (or CID) of every glyph in the face.</summary>
/// <remarks>
/// Glyph 0 is <c>.notdef</c> with SID 0, and formats 0, 1, and 2 decode one 16-bit identifier
/// per glyph; the predefined Expert and ExpertSubset charsets are unsupported. A SID-sorted lookup
/// resolves <c>seac</c> components, and the instance is immutable and thread-safe.
/// </remarks>
internal sealed class CffCharset
{
    /// <summary>The glyph count of the predefined ISOAdobe charset.</summary>
    internal const int ISO_ADOBE_GLYPH_COUNT = 229;

    private const int FORMAT_0 = 0;
    private const int FORMAT_1 = 1;
    private const int FORMAT_2 = 2;
    private const int PREDEFINED_ISO_ADOBE = 0;
    private const int PREDEFINED_EXPERT = 1;
    private const int PREDEFINED_EXPERT_SUBSET = 2;
    private const int MAX_SID = 0xFFFF;

    private readonly ReadOnlyMemory<SidGlyph> _lookup;

    private CffCharset(ushort[] sids)
    {
        SidGlyph[] lookup = new SidGlyph[sids.Length];
        for (int glyphId = 0; glyphId < sids.Length; glyphId++)
        {
            lookup[glyphId] = new SidGlyph(sids[glyphId], (ushort)glyphId);
        }

        Array.Sort(lookup, static (first, second) =>
        {
            int bySid = first.Sid.CompareTo(second.Sid);
            return bySid != 0 ? bySid : first.GlyphId.CompareTo(second.GlyphId);
        });
        _lookup = lookup;
    }

    /// <summary>Parses the charset of a face.</summary>
    internal static CffCharset Parse(
        ReadOnlyMemory<byte> table,
        int glyphCount,
        int offset,
        string context
    )
    {
        if (glyphCount < 1)
        {
            throw new FontFormatException($"{context} needs at least one glyph");
        }

        ushort[] sids = new ushort[glyphCount];
        if (offset is PREDEFINED_ISO_ADOBE or PREDEFINED_EXPERT or PREDEFINED_EXPERT_SUBSET)
        {
            if (offset == PREDEFINED_EXPERT)
            {
                throw new FontUnsupportedException(
                    $"{context} uses the predefined Expert charset; only the predefined ISOAdobe charset is supported"
                );
            }

            if (offset == PREDEFINED_EXPERT_SUBSET)
            {
                throw new FontUnsupportedException(
                    $"{context} uses the predefined ExpertSubset charset; only the predefined ISOAdobe charset is supported"
                );
            }

            if (glyphCount > ISO_ADOBE_GLYPH_COUNT)
            {
                throw new FontFormatException(
                    $"{context} is the predefined ISOAdobe charset with {ISO_ADOBE_GLYPH_COUNT} glyphs but the face has {glyphCount} glyph(s)"
                );
            }

            for (int glyphId = 0; glyphId < glyphCount; glyphId++)
            {
                sids[glyphId] = (ushort)glyphId;
            }

            return new CffCharset(sids);
        }

        SfntReader reader = new(table.Span, context);
        reader.Require(offset, 1);
        int format = reader.ReadByte(offset);
        if (format is not FORMAT_0 and not FORMAT_1 and not FORMAT_2)
        {
            throw new FontFormatException(
                $"{context} uses the charset format {format}; formats 0, 1, and 2 are supported"
            );
        }

        int position = offset + 1;
        int assigned = 1;
        while (assigned < glyphCount)
        {
            switch (format)
            {
                case FORMAT_0:
                    reader.Require(position, (glyphCount - assigned) * sizeof(ushort));
                    for (; assigned < glyphCount; assigned++)
                    {
                        sids[assigned] = reader.ReadUInt16(position);
                        position += sizeof(ushort);
                    }

                    break;
                case FORMAT_1:
                case FORMAT_2:
                {
                    reader.Require(
                        position,
                        sizeof(ushort) + (format == FORMAT_1 ? 1 : sizeof(ushort))
                    );
                    int firstSid = reader.ReadUInt16(position);
                    int rangeLength =
                        format == FORMAT_1
                            ? reader.ReadByte(position + sizeof(ushort))
                            : reader.ReadUInt16(position + sizeof(ushort));
                    position += format == FORMAT_1 ? 3 : 4;
                    int left = rangeLength;
                    if (firstSid + left > MAX_SID)
                    {
                        throw new FontFormatException(
                            $"{context} has a range starting at SID {firstSid} with {left} following glyph(s); the last SID exceeds {MAX_SID}"
                        );
                    }

                    if (left >= glyphCount - assigned)
                    {
                        throw new FontFormatException(
                            $"{context} has a range that covers {left + 1} glyph(s) but only {glyphCount - assigned} glyph(s) remain"
                        );
                    }

                    for (int index = 0; index <= left; index++)
                    {
                        sids[assigned++] = (ushort)(firstSid + index);
                    }

                    break;
                }

                default:
                    throw new FontFormatException(
                        $"{context} uses the charset format {format}; formats 0, 1, and 2 are supported"
                    );
            }
        }

        return new CffCharset(sids);
    }

    /// <summary>Finds the first glyph that carries one SID.</summary>
    internal bool TryGetGlyphId(int sid, out int glyphId)
    {
        if ((uint)sid > MAX_SID)
        {
            glyphId = 0;
            return false;
        }

        ushort value = (ushort)sid;
        ReadOnlySpan<SidGlyph> lookup = _lookup.Span;
        int low = 0;
        int high = lookup.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (lookup[middle].Sid < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low < lookup.Length && lookup[low].Sid == value)
        {
            glyphId = lookup[low].GlyphId;
            return true;
        }

        glyphId = 0;
        return false;
    }

    private readonly struct SidGlyph
    {
        internal SidGlyph(ushort sid, ushort glyphId)
        {
            Sid = sid;
            GlyphId = glyphId;
        }

        internal ushort Sid { get; }

        internal ushort GlyphId { get; }
    }
}
