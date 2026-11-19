using System.Buffers.Binary;
using System.Text;

namespace RayBork.Internal;

/// <summary>The selected Unicode <c>cmap</c> subtable and its codepoint lookup.</summary>
/// <remarks>
/// Formats 0, 4, 6, and 12 are chosen deterministically: Unicode subtables win, then format 12,
/// 4, 6, and 0, followed by full-repertoire over BMP encodings and directory order. A mapping to
/// glyph 0 or past <c>numGlyphs</c> is unmapped, and the instance is immutable and thread-safe.
/// </remarks>
internal sealed class CmapTable
{
    private const int FORMAT_0 = 0;
    private const int FORMAT_4 = 4;
    private const int FORMAT_6 = 6;
    private const int FORMAT_12 = 12;
    private const int PLATFORM_UNICODE = 0;
    private const int PLATFORM_MACINTOSH = 1;
    private const int PLATFORM_ISO = 2;
    private const int PLATFORM_WINDOWS = 3;
    private const int UNICODE_VARIATION_ENCODING = 5;
    private const int WINDOWS_UNICODE_BMP_ENCODING = 1;
    private const int WINDOWS_UNICODE_FULL_ENCODING = 10;
    private const int SURROGATE_FIRST = 0xD800;
    private const int SURROGATE_LAST = 0xDFFF;
    private const int MAX_CODEPOINT = 0x10FFFF;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly string _context;
    private readonly int _format;
    private readonly int _glyphCount;
    private readonly int _mappedGlyphCount;

    private CmapTable(ReadOnlyMemory<byte> data, int format, int glyphCount, int mappedGlyphCount)
    {
        _data = data;
        _glyphCount = glyphCount;
        _format = format;
        _mappedGlyphCount = mappedGlyphCount;
        _context = format switch
        {
            FORMAT_0 => "the 'cmap' format 0 subtable",
            FORMAT_4 => "the 'cmap' format 4 subtable",
            FORMAT_6 => "the 'cmap' format 6 subtable",
            _ => "the 'cmap' format 12 subtable",
        };
    }

    /// <summary>Gets the selected subtable format: 0, 4, 6, or 12.</summary>
    internal int Format => _format;

    /// <summary>Gets the mapped-glyph count, excluding surrogate codepoints.</summary>
    internal int MappedGlyphCount => _mappedGlyphCount;

    /// <summary>Resolves a Unicode scalar value to a real glyph identifier, if mapped.</summary>
    internal bool TryGetGlyphId(Rune rune, out int glyphId) =>
        TryGetGlyphId(rune.Value, out glyphId);

    /// <summary>Parses and selects the face's Unicode cmap subtable.</summary>
    internal static CmapTable Parse(ReadOnlyMemory<byte> cmap, int glyphCount)
    {
        SfntReader reader = new(cmap.Span, "the 'cmap' table");
        if (reader.Length < 4)
        {
            throw new FontFormatException(
                $"the 'cmap' table is {reader.Length} byte(s) long; the version and encoding record count are required"
            );
        }

        int version = reader.ReadUInt16(0);
        if (version != 0)
        {
            throw new FontFormatException(
                $"the 'cmap' table version is {version}; version 0 is required"
            );
        }

        int recordCount = reader.ReadUInt16(2);
        if (!reader.Fits(4, (long)recordCount * 8))
        {
            throw new FontFormatException(
                $"the 'cmap' table stores {recordCount} encoding record(s) but is only {reader.Length} byte(s) long"
            );
        }

        int bestGroup = int.MaxValue;
        int bestFormatRank = int.MaxValue;
        int bestEncodingRank = int.MaxValue;
        int bestFormat = FORMAT_0;
        int bestOffset = -1;
        for (int index = 0; index < recordCount; index++)
        {
            int record = 4 + (index * 8);
            int platformId = reader.ReadUInt16(record);
            int encodingId = reader.ReadUInt16(record + 2);
            uint subtableOffset = reader.ReadUInt32(record + 4);
            if (subtableOffset > int.MaxValue - 2 || !reader.Fits((int)subtableOffset, 2))
            {
                continue;
            }

            int format = reader.ReadUInt16((int)subtableOffset);
            int formatRank = FormatRank(format);
            if (formatRank < 0)
            {
                continue;
            }

            (int group, int encodingRank) = RankEncoding(platformId, encodingId);
            if (group > bestGroup)
            {
                continue;
            }

            bool better =
                group < bestGroup
                || formatRank < bestFormatRank
                || (formatRank == bestFormatRank && encodingRank < bestEncodingRank);
            if (!better)
            {
                continue;
            }

            bestGroup = group;
            bestFormatRank = formatRank;
            bestEncodingRank = encodingRank;
            bestFormat = format;
            bestOffset = (int)subtableOffset;
        }

        if (bestOffset < 0)
        {
            throw new FontFormatException(
                "the 'cmap' table has no supported subtable; formats 0, 4, 6, and 12 are supported"
            );
        }

        int subtableLength = ValidateSubtable(reader, bestOffset, bestFormat);
        ReadOnlyMemory<byte> subtable = cmap.Slice(bestOffset, subtableLength);
        int mappedGlyphCount = CountMappedGlyphs(subtable.Span, bestFormat, glyphCount);
        return new CmapTable(subtable, bestFormat, glyphCount, mappedGlyphCount);
    }

    private bool TryGetGlyphId(int codepoint, out int glyphId)
    {
        glyphId = 0;
        if (codepoint < 0 || codepoint > MAX_CODEPOINT)
        {
            return false;
        }

        switch (_format)
        {
            case FORMAT_0:
                return TryGetFormat0GlyphId(codepoint, out glyphId);
            case FORMAT_4:
                return TryGetFormat4GlyphId(codepoint, out glyphId);
            case FORMAT_6:
                return TryGetFormat6GlyphId(codepoint, out glyphId);
            default:
                return TryGetFormat12GlyphId(codepoint, out glyphId);
        }
    }

    private bool TryGetFormat0GlyphId(int codepoint, out int glyphId)
    {
        glyphId = 0;
        if (codepoint >= 256)
        {
            return false;
        }

        SfntReader reader = new(_data.Span, _context);
        return TryAccept(reader.ReadByte(6 + codepoint), out glyphId);
    }

    private bool TryGetFormat4GlyphId(int codepoint, out int glyphId)
    {
        glyphId = 0;
        if (codepoint > 0xFFFF)
        {
            return false;
        }

        SfntReader reader = new(_data.Span, _context);
        int segmentCount = reader.ReadUInt16(6) / 2;
        int endCodeOffset = 14;
        int startCodeOffset = 16 + (2 * segmentCount);
        int idDeltaOffset = startCodeOffset + (2 * segmentCount);
        int idRangeOffsetOffset = idDeltaOffset + (2 * segmentCount);

        int low = 0;
        int high = segmentCount - 1;
        int segment = -1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (reader.ReadUInt16(endCodeOffset + (2 * middle)) >= codepoint)
            {
                segment = middle;
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        if (segment < 0)
        {
            return false;
        }

        int startCode = reader.ReadUInt16(startCodeOffset + (2 * segment));
        if (codepoint < startCode)
        {
            return false;
        }

        int idDelta = reader.ReadInt16(idDeltaOffset + (2 * segment));
        int idRangeOffset = reader.ReadUInt16(idRangeOffsetOffset + (2 * segment));
        int rawGlyph;
        if (idRangeOffset == 0)
        {
            rawGlyph = codepoint + idDelta;
        }
        else
        {
            int glyphOffset =
                idRangeOffsetOffset + (2 * segment) + idRangeOffset + (2 * (codepoint - startCode));
            if (!reader.TryReadUInt16(glyphOffset, out ushort indirectGlyph) || indirectGlyph == 0)
            {
                return false;
            }

            rawGlyph = indirectGlyph + idDelta;
        }

        return TryAccept(rawGlyph & 0xFFFF, out glyphId);
    }

    private bool TryGetFormat6GlyphId(int codepoint, out int glyphId)
    {
        glyphId = 0;
        if (codepoint > 0xFFFF)
        {
            return false;
        }

        SfntReader reader = new(_data.Span, _context);
        int firstCode = reader.ReadUInt16(6);
        int entryCount = reader.ReadUInt16(8);
        int index = codepoint - firstCode;
        if (index < 0 || index >= entryCount)
        {
            return false;
        }

        return TryAccept(reader.ReadUInt16(10 + (2 * index)), out glyphId);
    }

    private bool TryGetFormat12GlyphId(int codepoint, out int glyphId)
    {
        glyphId = 0;
        SfntReader reader = new(_data.Span, _context);
        int groupCount = (int)reader.ReadUInt32(12);
        int low = 0;
        int high = groupCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int group = 16 + (12 * middle);
            uint startCode = reader.ReadUInt32(group);
            uint endCode = reader.ReadUInt32(group + 4);
            if ((uint)codepoint < startCode)
            {
                high = middle - 1;
            }
            else if ((uint)codepoint > endCode)
            {
                low = middle + 1;
            }
            else
            {
                long glyph = (long)reader.ReadUInt32(group + 8) + (codepoint - startCode);
                return TryAccept(glyph, out glyphId);
            }
        }

        return false;
    }

    private bool TryAccept(long glyph, out int glyphId)
    {
        if (glyph <= 0 || glyph >= _glyphCount)
        {
            glyphId = 0;
            return false;
        }

        glyphId = (int)glyph;
        return true;
    }

    private static int FormatRank(int format) =>
        format switch
        {
            FORMAT_12 => 0,
            FORMAT_4 => 1,
            FORMAT_6 => 2,
            FORMAT_0 => 3,
            _ => -1,
        };

    private static (int Group, int EncodingRank) RankEncoding(int platformId, int encodingId)
    {
        bool unicode =
            (platformId == PLATFORM_UNICODE && encodingId != UNICODE_VARIATION_ENCODING)
            || (
                platformId == PLATFORM_WINDOWS
                && (
                    encodingId == WINDOWS_UNICODE_BMP_ENCODING
                    || encodingId == WINDOWS_UNICODE_FULL_ENCODING
                )
            );
        if (unicode)
        {
            int rank = platformId switch
            {
                PLATFORM_WINDOWS when encodingId == WINDOWS_UNICODE_FULL_ENCODING => 0,
                PLATFORM_UNICODE when encodingId is 4 or 6 => 1,
                PLATFORM_WINDOWS => 2,
                _ => 3,
            };
            return (0, rank);
        }

        int fallbackRank = platformId switch
        {
            PLATFORM_WINDOWS => 0,
            PLATFORM_MACINTOSH => 1,
            PLATFORM_ISO => 2,
            _ => 3,
        };
        return (1, fallbackRank);
    }

    private static int ValidateSubtable(SfntReader reader, int offset, int format)
    {
        switch (format)
        {
            case FORMAT_0:
                return ValidateFormat0(reader, offset);
            case FORMAT_4:
                return ValidateFormat4(reader, offset);
            case FORMAT_6:
                return ValidateFormat6(reader, offset);
            default:
                return ValidateFormat12(reader, offset);
        }
    }

    private static int ValidateFormat0(SfntReader reader, int offset)
    {
        reader.Require(offset, 6);
        int length = reader.ReadUInt16(offset + 2);
        if (length < 262)
        {
            throw new FontFormatException(
                $"the 'cmap' format 0 subtable at offset {offset} declares {length} byte(s); the 256-entry glyph array needs 262"
            );
        }

        reader.Require(offset, length);
        return length;
    }

    private static int ValidateFormat4(SfntReader reader, int offset)
    {
        reader.Require(offset, 14);
        int length = reader.ReadUInt16(offset + 2);
        if (length < 16)
        {
            throw new FontFormatException(
                $"the 'cmap' format 4 subtable at offset {offset} declares only {length} byte(s)"
            );
        }

        reader.Require(offset, length);
        int segmentCountX2 = reader.ReadUInt16(offset + 6);
        if (segmentCountX2 == 0 || (segmentCountX2 & 1) != 0)
        {
            throw new FontFormatException(
                $"the 'cmap' format 4 subtable at offset {offset} declares an invalid segment count {segmentCountX2}"
            );
        }

        int segmentCount = segmentCountX2 / 2;
        if (16 + (8L * segmentCount) > length)
        {
            throw new FontFormatException(
                $"the 'cmap' format 4 subtable at offset {offset} declares {segmentCount} segment(s) but only {length} byte(s)"
            );
        }

        int endCodeOffset = offset + 14;
        int startCodeOffset = offset + 16 + (2 * segmentCount);
        int previousEnd = -1;
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int endCode = reader.ReadUInt16(endCodeOffset + (2 * segment));
            int startCode = reader.ReadUInt16(startCodeOffset + (2 * segment));
            if (startCode > endCode)
            {
                throw new FontFormatException(
                    $"the 'cmap' format 4 subtable at offset {offset} has a segment starting at U+{startCode:X4} after its end U+{endCode:X4}"
                );
            }

            if (startCode <= previousEnd)
            {
                throw new FontFormatException(
                    $"the 'cmap' format 4 subtable at offset {offset} has overlapping or unsorted segments at U+{startCode:X4}"
                );
            }

            previousEnd = endCode;
        }

        return length;
    }

    private static int ValidateFormat6(SfntReader reader, int offset)
    {
        reader.Require(offset, 10);
        int length = reader.ReadUInt16(offset + 2);
        if (length < 10)
        {
            throw new FontFormatException(
                $"the 'cmap' format 6 subtable at offset {offset} declares only {length} byte(s)"
            );
        }

        reader.Require(offset, length);
        int firstCode = reader.ReadUInt16(offset + 6);
        int entryCount = reader.ReadUInt16(offset + 8);
        if (firstCode + entryCount > 0x10000)
        {
            throw new FontFormatException(
                $"the 'cmap' format 6 subtable at offset {offset} maps past U+FFFF with first code {firstCode} and {entryCount} entries"
            );
        }

        if (10 + (2L * entryCount) > length)
        {
            throw new FontFormatException(
                $"the 'cmap' format 6 subtable at offset {offset} declares {entryCount} entries but only {length} byte(s)"
            );
        }

        return length;
    }

    private static int ValidateFormat12(SfntReader reader, int offset)
    {
        reader.Require(offset, 16);
        uint length = reader.ReadUInt32(offset + 4);
        if (length < 16 || length > int.MaxValue)
        {
            throw new FontFormatException(
                $"the 'cmap' format 12 subtable at offset {offset} declares an invalid length {length}"
            );
        }

        reader.Require(offset, (int)length);
        uint groupCount = reader.ReadUInt32(offset + 12);
        if (16 + (12L * groupCount) > length)
        {
            throw new FontFormatException(
                $"the 'cmap' format 12 subtable at offset {offset} declares {groupCount} group(s) but only {length} byte(s)"
            );
        }

        uint previousEnd = 0;
        bool first = true;
        for (uint group = 0; group < groupCount; group++)
        {
            int groupOffset = offset + 16 + (int)(12 * group);
            uint startCode = reader.ReadUInt32(groupOffset);
            uint endCode = reader.ReadUInt32(groupOffset + 4);
            uint startGlyph = reader.ReadUInt32(groupOffset + 8);
            if (startCode > endCode || endCode > MAX_CODEPOINT)
            {
                throw new FontFormatException(
                    $"the 'cmap' format 12 subtable at offset {offset} has an invalid group from U+{startCode:X} to U+{endCode:X}"
                );
            }

            if (!first && startCode <= previousEnd)
            {
                throw new FontFormatException(
                    $"the 'cmap' format 12 subtable at offset {offset} has overlapping or unsorted groups at U+{startCode:X}"
                );
            }

            ulong lastGlyph = (ulong)startGlyph + (endCode - startCode);
            if (lastGlyph > int.MaxValue)
            {
                throw new FontFormatException(
                    $"the 'cmap' format 12 subtable at offset {offset} maps past the maximum glyph identifier in the group from U+{startCode:X}"
                );
            }

            previousEnd = endCode;
            first = false;
        }

        return (int)length;
    }

    private static int CountMappedGlyphs(ReadOnlySpan<byte> data, int format, int glyphCount)
    {
        return format switch
        {
            FORMAT_0 => CountFormat0(data, glyphCount),
            FORMAT_4 => CountFormat4(data, glyphCount),
            FORMAT_6 => CountFormat6(data, glyphCount),
            _ => CountFormat12(data, glyphCount),
        };
    }

    private static int CountFormat0(ReadOnlySpan<byte> data, int glyphCount)
    {
        int count = 0;
        for (int codepoint = 0; codepoint < 256; codepoint++)
        {
            if (IsMappedGlyph(data[6 + codepoint], glyphCount))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFormat4(ReadOnlySpan<byte> data, int glyphCount)
    {
        int segmentCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..]) / 2;
        int endCodeOffset = 14;
        int startCodeOffset = 16 + (2 * segmentCount);
        int idDeltaOffset = startCodeOffset + (2 * segmentCount);
        int idRangeOffsetOffset = idDeltaOffset + (2 * segmentCount);
        int count = 0;
        for (int segment = 0; segment < segmentCount; segment++)
        {
            int startCode = BinaryPrimitives.ReadUInt16BigEndian(
                data[(startCodeOffset + (2 * segment))..]
            );
            int endCode = BinaryPrimitives.ReadUInt16BigEndian(
                data[(endCodeOffset + (2 * segment))..]
            );
            int idDelta = BinaryPrimitives.ReadInt16BigEndian(
                data[(idDeltaOffset + (2 * segment))..]
            );
            int idRangeOffset = BinaryPrimitives.ReadUInt16BigEndian(
                data[(idRangeOffsetOffset + (2 * segment))..]
            );
            if (idRangeOffset == 0)
            {
                int firstGlyph = (startCode + idDelta) & 0xFFFF;
                int width = endCode - startCode + 1;
                int valid = CountValidGlyphs(firstGlyph, width, glyphCount);
                int surrogateStart = Math.Max(startCode, SURROGATE_FIRST);
                int surrogateEnd = Math.Min(endCode, SURROGATE_LAST);
                if (surrogateStart <= surrogateEnd)
                {
                    int surrogateFirstGlyph = (surrogateStart + idDelta) & 0xFFFF;
                    valid -= CountValidGlyphs(
                        surrogateFirstGlyph,
                        surrogateEnd - surrogateStart + 1,
                        glyphCount
                    );
                }

                count += valid;
                continue;
            }

            for (int codepoint = startCode; codepoint <= endCode; codepoint++)
            {
                if (IsSurrogate(codepoint))
                {
                    continue;
                }

                int glyphOffset =
                    idRangeOffsetOffset
                    + (2 * segment)
                    + idRangeOffset
                    + (2 * (codepoint - startCode));
                if (glyphOffset < 0 || glyphOffset > data.Length - 2)
                {
                    continue;
                }

                int rawGlyph = BinaryPrimitives.ReadUInt16BigEndian(data[glyphOffset..]);
                if (rawGlyph == 0)
                {
                    continue;
                }

                if (IsMappedGlyph((rawGlyph + idDelta) & 0xFFFF, glyphCount))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountFormat6(ReadOnlySpan<byte> data, int glyphCount)
    {
        int firstCode = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
        int entryCount = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        int count = 0;
        for (int index = 0; index < entryCount; index++)
        {
            if (IsSurrogate(firstCode + index))
            {
                continue;
            }

            int glyph = BinaryPrimitives.ReadUInt16BigEndian(data[(10 + (2 * index))..]);
            if (IsMappedGlyph(glyph, glyphCount))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountFormat12(ReadOnlySpan<byte> data, int glyphCount)
    {
        int groupCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data[12..]);
        int count = 0;
        for (int index = 0; index < groupCount; index++)
        {
            int group = 16 + (12 * index);
            uint startCode = BinaryPrimitives.ReadUInt32BigEndian(data[group..]);
            uint endCode = BinaryPrimitives.ReadUInt32BigEndian(data[(group + 4)..]);
            uint startGlyph = BinaryPrimitives.ReadUInt32BigEndian(data[(group + 8)..]);
            long width = (long)endCode - startCode + 1;
            long firstValid = Math.Max(0, 1 - (long)startGlyph);
            long lastValid = Math.Min(width - 1, (glyphCount - 1) - (long)startGlyph);
            if (firstValid > lastValid)
            {
                continue;
            }

            long valid = lastValid - firstValid + 1;
            int surrogateStart = Math.Max((int)startCode, SURROGATE_FIRST);
            int surrogateEnd = Math.Min((int)endCode, SURROGATE_LAST);
            if (surrogateStart <= surrogateEnd)
            {
                long surrogateFirst = Math.Max(firstValid, surrogateStart - (long)startCode);
                long surrogateLast = Math.Min(lastValid, surrogateEnd - (long)startCode);
                if (surrogateFirst <= surrogateLast)
                {
                    valid -= surrogateLast - surrogateFirst + 1;
                }
            }

            count += (int)valid;
        }

        return count;
    }

    private static int CountValidGlyphs(int firstGlyph, int width, int glyphCount)
    {
        int count = CountInRange(firstGlyph, Math.Min(0xFFFF, firstGlyph + width - 1), glyphCount);
        int overflow = firstGlyph + width - 0x10000;
        if (overflow > 0)
        {
            count += CountInRange(0, overflow - 1, glyphCount);
        }

        return count;
    }

    private static int CountInRange(int low, int high, int glyphCount)
    {
        int first = Math.Max(low, 1);
        int last = Math.Min(high, glyphCount - 1);
        return first <= last ? last - first + 1 : 0;
    }

    private static bool IsMappedGlyph(int glyph, int glyphCount) =>
        glyph != 0 && glyph < glyphCount;

    private static bool IsSurrogate(int codepoint) =>
        codepoint is >= SURROGATE_FIRST and <= SURROGATE_LAST;
}
