namespace RayBork.Internal;

/// <summary>
/// The CFF charset: the string identifier (or CID) of every glyph in the face.
/// </summary>
/// <remarks>
/// <para>
/// Glyph 0 is always <c>.notdef</c> with SID 0. Charset formats 0, 1, and 2 are decoded into one
/// 16-bit identifier per glyph; the table is validated against the face's glyph count so a range can
/// never run past the last glyph. The predefined ISOAdobe charset (offset 0) is the identity mapping
/// over its 229 glyphs; the predefined Expert and ExpertSubset charsets are valid CFF but outside the
/// supported subset and raise <see cref="FontUnsupportedException"/>.
/// </para>
/// <para>
/// The instance is immutable and safe to read from any thread. <see cref="TryGetGlyphId"/> resolves
/// the deprecated <c>seac</c> form through a SID-sorted lookup built once at parse, so a face with
/// many <c>seac</c> glyphs is O(log glyphs) per component instead of a linear scan.
/// </para>
/// </remarks>
internal sealed class CffCharset {
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

    private CffCharset(ushort[] sids) {
        SidGlyph[] lookup = new SidGlyph[sids.Length];
        for (int glyphId = 0; glyphId < sids.Length; glyphId++) {
            lookup[glyphId] = new SidGlyph(sids[glyphId], (ushort)glyphId);
        }

        Array.Sort(lookup, static (first, second) => {
            int bySid = first.Sid.CompareTo(second.Sid);
            return bySid != 0 ? bySid : first.GlyphId.CompareTo(second.GlyphId);
        });
        _lookup = lookup;
    }

    /// <summary>Parses the charset of a face.</summary>
    /// <param name="table">The complete <c>CFF </c> table.</param>
    /// <param name="glyphCount">The face's glyph count.</param>
    /// <param name="offset">The Top DICT charset offset, where 0, 1, and 2 are predefined charsets.</param>
    /// <param name="context">A description used in failure reasons.</param>
    /// <returns>The parsed charset.</returns>
    /// <exception cref="FontFormatException">
    /// The charset is truncated, uses an unsupported format, overflows 16-bit identifiers, or does
    /// not map exactly the face's glyphs.
    /// </exception>
    /// <exception cref="FontUnsupportedException">
    /// The charset is a predefined Expert or ExpertSubset charset.
    /// </exception>
    internal static CffCharset Parse(
        ReadOnlyMemory<byte> table,
        int glyphCount,
        int offset,
        string context) {
        if (glyphCount < 1) {
            throw new FontFormatException($"{context} needs at least one glyph");
        }

        ushort[] sids = new ushort[glyphCount];
        if (offset is PREDEFINED_ISO_ADOBE or PREDEFINED_EXPERT or PREDEFINED_EXPERT_SUBSET) {
            if (offset == PREDEFINED_EXPERT) {
                throw new FontUnsupportedException(
                    $"{context} uses the predefined Expert charset; only the predefined ISOAdobe charset is supported");
            }

            if (offset == PREDEFINED_EXPERT_SUBSET) {
                throw new FontUnsupportedException(
                    $"{context} uses the predefined ExpertSubset charset; only the predefined ISOAdobe charset is supported");
            }

            if (glyphCount > ISO_ADOBE_GLYPH_COUNT) {
                throw new FontFormatException(
                    $"{context} is the predefined ISOAdobe charset with {ISO_ADOBE_GLYPH_COUNT} glyphs but the face has {glyphCount} glyph(s)");
            }

            for (int glyphId = 0; glyphId < glyphCount; glyphId++) {
                sids[glyphId] = (ushort)glyphId;
            }

            return new CffCharset(sids);
        }

        SfntReader reader = new(table.Span, context);
        reader.Require(offset, 1);
        int format = reader.ReadByte(offset);
        if (format is not FORMAT_0 and not FORMAT_1 and not FORMAT_2) {
            throw new FontFormatException(
                $"{context} uses the charset format {format}; formats 0, 1, and 2 are supported");
        }

        int position = offset + 1;
        int assigned = 1;
        while (assigned < glyphCount) {
            switch (format) {
                case FORMAT_0:
                    reader.Require(position, (glyphCount - assigned) * sizeof(ushort));
                    for (; assigned < glyphCount; assigned++) {
                        sids[assigned] = reader.ReadUInt16(position);
                        position += sizeof(ushort);
                    }

                    break;
                case FORMAT_1:
                case FORMAT_2: {
                    reader.Require(position, sizeof(ushort) + (format == FORMAT_1 ? 1 : sizeof(ushort)));
                    int firstSid = reader.ReadUInt16(position);
                    int rangeLength = format == FORMAT_1
                        ? reader.ReadByte(position + sizeof(ushort))
                        : reader.ReadUInt16(position + sizeof(ushort));
                    position += format == FORMAT_1 ? 3 : 4;
                    int left = rangeLength;
                    if (firstSid + left > MAX_SID) {
                        throw new FontFormatException(
                            $"{context} has a range starting at SID {firstSid} with {left} following glyph(s); the last SID exceeds {MAX_SID}");
                    }

                    if (left >= glyphCount - assigned) {
                        throw new FontFormatException(
                            $"{context} has a range that covers {left + 1} glyph(s) but only {glyphCount - assigned} glyph(s) remain");
                    }

                    for (int index = 0; index <= left; index++) {
                        sids[assigned++] = (ushort)(firstSid + index);
                    }

                    break;
                }

                default:
                    throw new FontFormatException(
                        $"{context} uses the charset format {format}; formats 0, 1, and 2 are supported");
            }
        }

        return new CffCharset(sids);
    }

    /// <summary>Finds the first glyph that carries one SID.</summary>
    /// <param name="sid">The SID to resolve.</param>
    /// <param name="glyphId">The matching glyph identifier, or zero when none matches.</param>
    /// <returns><see langword="true"/> when the SID maps to a glyph.</returns>
    internal bool TryGetGlyphId(int sid, out int glyphId) {
        if ((uint)sid > MAX_SID) {
            glyphId = 0;
            return false;
        }

        ushort value = (ushort)sid;
        ReadOnlySpan<SidGlyph> lookup = _lookup.Span;
        int low = 0;
        int high = lookup.Length - 1;
        while (low <= high) {
            int middle = low + ((high - low) / 2);
            int comparison = lookup[middle].Sid.CompareTo(value);
            if (comparison == 0) {
                while (middle > 0 && lookup[middle - 1].Sid == value) {
                    middle--;
                }

                glyphId = lookup[middle].GlyphId;
                return true;
            }

            if (comparison < 0) {
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        glyphId = 0;
        return false;
    }

    private readonly struct SidGlyph {
        internal SidGlyph(ushort sid, ushort glyphId) {
            Sid = sid;
            GlyphId = glyphId;
        }

        internal ushort Sid { get; }

        internal ushort GlyphId { get; }
    }
}
