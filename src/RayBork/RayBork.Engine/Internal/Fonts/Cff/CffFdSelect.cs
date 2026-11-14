namespace RayBork.Internal;

/// <summary>
/// The CID-keyed CFF FDSelect table: the Font DICT index that each glyph uses.
/// </summary>
/// <remarks>
/// <para>
/// Format 0 stores one byte per glyph. Format 3 stores ranges of <c>(first glyph, Font DICT index)</c>
/// pairs followed by one sentinel; the ranges must start at glyph 0, increase strictly, and end at the
/// face's glyph count. Format 4 belongs to CFF2 and raises
/// <see cref="FontUnsupportedException"/>. Every Font DICT index is validated against the FDArray
/// count at parse time, so a glyph read can index the per-FD private tables directly.
/// </para>
/// <para>
/// The instance is immutable and safe to read from any thread. Format 0 keeps the table slice;
/// format 3 keeps one parallel pair of arrays and resolves a glyph with a binary search.
/// </para>
/// </remarks>
internal sealed class CffFdSelect {
    private const int FORMAT_0 = 0;
    private const int FORMAT_3 = 3;
    private const int FORMAT_4 = 4;
    private const int FORMAT_0_HEADER_LENGTH = 1;
    private const int FORMAT_3_HEADER_LENGTH = 3;
    private const int FORMAT_3_RANGE_LENGTH = 3;
    private const int FORMAT_3_SENTINEL_LENGTH = 2;

    private readonly ReadOnlyMemory<byte> _fontDictIndices;
    private readonly ReadOnlyMemory<int> _firstGlyphs;
    private readonly ReadOnlyMemory<byte> _rangeFontDictIndices;

    private CffFdSelect(
        ReadOnlyMemory<byte> fontDictIndices,
        ReadOnlyMemory<int> firstGlyphs,
        ReadOnlyMemory<byte> rangeFontDictIndices) {
        _fontDictIndices = fontDictIndices;
        _firstGlyphs = firstGlyphs;
        _rangeFontDictIndices = rangeFontDictIndices;
    }

    /// <summary>Parses an FDSelect table.</summary>
    /// <param name="table">The complete <c>CFF </c> table.</param>
    /// <param name="glyphCount">The face's glyph count.</param>
    /// <param name="fontDictCount">The FDArray Font DICT count.</param>
    /// <param name="offset">The Top DICT FDSelect offset.</param>
    /// <param name="context">A description used in failure reasons.</param>
    /// <returns>The parsed FDSelect.</returns>
    /// <exception cref="FontFormatException">
    /// The table is truncated, uses an unknown format, has decreasing or out-of-range ranges, does
    /// not end at the glyph count, or names a Font DICT outside the FDArray.
    /// </exception>
    /// <exception cref="FontUnsupportedException">The table uses FDSelect format 4, which is CFF2.</exception>
    internal static CffFdSelect Parse(
        ReadOnlyMemory<byte> table,
        int glyphCount,
        int fontDictCount,
        int offset,
        string context) {
        if (fontDictCount < 1) {
            throw new FontFormatException($"{context} needs at least one Font DICT");
        }

        SfntReader reader = new(table.Span, context);
        reader.Require(offset, 1);
        int format = reader.ReadByte(offset);
        switch (format) {
            case FORMAT_0: {
                reader.Require(offset + FORMAT_0_HEADER_LENGTH, glyphCount);
                ReadOnlyMemory<byte> indices = table.Slice(offset + FORMAT_0_HEADER_LENGTH, glyphCount);
                ReadOnlySpan<byte> span = indices.Span;
                for (int glyphId = 0; glyphId < span.Length; glyphId++) {
                    if (span[glyphId] >= fontDictCount) {
                        throw new FontFormatException(
                            $"{context} maps glyph {glyphId} to Font DICT {span[glyphId]}; the FDArray has {fontDictCount} DICT(s)");
                    }
                }

                return new CffFdSelect(indices, ReadOnlyMemory<int>.Empty, ReadOnlyMemory<byte>.Empty);
            }

            case FORMAT_3: {
                reader.Require(offset + 1, sizeof(ushort));
                int rangeCount = reader.ReadUInt16(offset + 1);
                if (rangeCount == 0) {
                    throw new FontFormatException($"{context} declares zero ranges");
                }

                if (rangeCount > glyphCount) {
                    throw new FontFormatException(
                        $"{context} declares {rangeCount} ranges for {glyphCount} glyph(s); each range must cover at least one glyph");
                }

                int rangesOffset = offset + FORMAT_3_HEADER_LENGTH;
                long rangesLength = ((long)rangeCount * FORMAT_3_RANGE_LENGTH) + FORMAT_3_SENTINEL_LENGTH;
                if (!reader.Fits(rangesOffset, rangesLength)) {
                    throw new FontFormatException(
                        $"{context} is truncated: {rangeCount} ranges and a sentinel need {rangesLength} byte(s) at offset {rangesOffset}, outside the {reader.Length}-byte table");
                }

                int[] firstGlyphs = new int[rangeCount];
                byte[] fontDictIndices = new byte[rangeCount];
                int previousFirst = -1;
                for (int index = 0; index < rangeCount; index++) {
                    int rangeOffset = rangesOffset + (index * FORMAT_3_RANGE_LENGTH);
                    int firstGlyph = reader.ReadUInt16(rangeOffset);
                    int fontDict = reader.ReadByte(rangeOffset + sizeof(ushort));
                    if (index == 0 && firstGlyph != 0) {
                        throw new FontFormatException(
                            $"{context} starts its first range at glyph {firstGlyph}; ranges must start at glyph 0");
                    }

                    if (firstGlyph <= previousFirst) {
                        throw new FontFormatException(
                            $"{context} has a non-increasing range start {firstGlyph} after {previousFirst}");
                    }

                    if (firstGlyph >= glyphCount) {
                        throw new FontFormatException(
                            $"{context} starts a range at glyph {firstGlyph}; the face has {glyphCount} glyph(s)");
                    }

                    if (fontDict >= fontDictCount) {
                        throw new FontFormatException(
                            $"{context} maps glyph {firstGlyph} to Font DICT {fontDict}; the FDArray has {fontDictCount} DICT(s)");
                    }

                    firstGlyphs[index] = firstGlyph;
                    fontDictIndices[index] = (byte)fontDict;
                    previousFirst = firstGlyph;
                }

                int sentinel = reader.ReadUInt16(rangesOffset + (rangeCount * FORMAT_3_RANGE_LENGTH));
                if (sentinel != glyphCount) {
                    throw new FontFormatException(
                        $"{context} ends its ranges at glyph {sentinel}; the face has {glyphCount} glyph(s)");
                }

                return new CffFdSelect(
                    ReadOnlyMemory<byte>.Empty,
                    firstGlyphs,
                    fontDictIndices);
            }

            case FORMAT_4:
                throw new FontUnsupportedException(
                    $"{context} uses the FDSelect format 4; format 4 is a CFF2 feature");
            default:
                throw new FontFormatException(
                    $"{context} uses the FDSelect format {format}; formats 0 and 3 are supported");
        }
    }

    /// <summary>Gets the Font DICT index of one glyph.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, glyphCount)</c>.</param>
    /// <returns>The Font DICT index, in <c>[0, FDArray count)</c>.</returns>
    internal int GetFontDictIndex(int glyphId) {
        if (!_fontDictIndices.IsEmpty) {
            return _fontDictIndices.Span[glyphId];
        }

        ReadOnlySpan<int> firstGlyphs = _firstGlyphs.Span;
        int low = 0;
        int high = firstGlyphs.Length - 1;
        int range = 0;
        while (low <= high) {
            int middle = low + ((high - low) / 2);
            if (firstGlyphs[middle] <= glyphId) {
                range = middle;
                low = middle + 1;
            } else {
                high = middle - 1;
            }
        }

        return _rangeFontDictIndices.Span[range];
    }
}
