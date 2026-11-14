using System.Buffers.Binary;

namespace RayBork.Tests.Fonts;

/// <summary>One synthetic cmap encoding record and its mappings.</summary>
internal readonly struct SyntheticCmapRecord {
    internal SyntheticCmapRecord(
        int platformId,
        int encodingId,
        int format,
        (int Codepoint, int GlyphId)[] mappings) {
        PlatformId = platformId;
        EncodingId = encodingId;
        Format = format;
        Mappings = mappings;
    }

    internal int PlatformId { get; }

    internal int EncodingId { get; }

    internal int Format { get; }

    internal (int Codepoint, int GlyphId)[] Mappings { get; }
}

/// <summary>Encodes synthetic <c>cmap</c> tables in formats 0, 4, 6, and 12.</summary>
internal static class SyntheticCmap {
    internal static byte[] Build(IReadOnlyList<SyntheticCmapRecord> records) {
        byte[][] subtables = new byte[records.Count][];
        for (int index = 0; index < records.Count; index++) {
            subtables[index] = BuildSubtable(records[index].Format, records[index].Mappings);
        }

        int headerLength = 4 + (8 * records.Count);
        int totalLength = headerLength;
        for (int index = 0; index < subtables.Length; index++) {
            totalLength += subtables[index].Length;
        }

        byte[] cmap = new byte[totalLength];
        BinaryPrimitives.WriteUInt16BigEndian(cmap, 0);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), (ushort)records.Count);
        int offset = headerLength;
        for (int index = 0; index < records.Count; index++) {
            int record = 4 + (8 * index);
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(record), (ushort)records[index].PlatformId);
            BinaryPrimitives.WriteUInt16BigEndian(
                cmap.AsSpan(record + 2),
                (ushort)records[index].EncodingId);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(record + 4), (uint)offset);
            subtables[index].CopyTo(cmap, offset);
            offset += subtables[index].Length;
        }

        return cmap;
    }

    private static byte[] BuildSubtable(int format, (int Codepoint, int GlyphId)[] mappings) {
        return format switch {
            0 => BuildFormat0(Sort(mappings, 0xFF)),
            4 => BuildFormat4(Sort(mappings, 0xFFFE)),
            6 => BuildFormat6(Sort(mappings, 0xFFFF)),
            12 => BuildFormat12(Sort(mappings, 0x10FFFF)),
            _ => throw new ArgumentException($"Unsupported synthetic cmap format {format}.", nameof(format)),
        };
    }

    private static (int Codepoint, int GlyphId)[] Sort(
        (int Codepoint, int GlyphId)[] mappings,
        int maximumCodepoint) {
        if (mappings.Length == 0) {
            throw new ArgumentException("A synthetic cmap subtable needs at least one mapping.", nameof(mappings));
        }

        (int Codepoint, int GlyphId)[] sorted = [.. mappings];
        Array.Sort(sorted, static (first, second) => first.Codepoint.CompareTo(second.Codepoint));
        for (int index = 0; index < sorted.Length; index++) {
            if (sorted[index].Codepoint < 0 || sorted[index].Codepoint > maximumCodepoint) {
                throw new ArgumentException(
                    $"Synthetic cmap codepoint {sorted[index].Codepoint} is outside 0..{maximumCodepoint}.",
                    nameof(mappings));
            }

            if (sorted[index].GlyphId is < 0 or > 0xFFFF) {
                throw new ArgumentException(
                    $"Synthetic glyph identifier {sorted[index].GlyphId} is outside 0..65535.",
                    nameof(mappings));
            }

            if (index > 0 && sorted[index].Codepoint == sorted[index - 1].Codepoint) {
                throw new ArgumentException(
                    $"Synthetic cmap maps U+{sorted[index].Codepoint:X4} twice.",
                    nameof(mappings));
            }
        }

        return sorted;
    }

    private static byte[] BuildFormat0((int Codepoint, int GlyphId)[] mappings) {
        byte[] table = new byte[262];
        BinaryPrimitives.WriteUInt16BigEndian(table, 0);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), 262);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), 0);
        foreach ((int codepoint, int glyphId) in mappings) {
            if (codepoint > 0xFF || glyphId > 0xFF) {
                throw new ArgumentException(
                    $"Synthetic format 0 cmap cannot map U+{codepoint:X4} to glyph {glyphId}.",
                    nameof(mappings));
            }

            table[6 + codepoint] = (byte)glyphId;
        }

        return table;
    }

    private static byte[] BuildFormat4((int Codepoint, int GlyphId)[] mappings) {
        List<(int Start, int End, int Delta)> segments = [];
        int index = 0;
        while (index < mappings.Length) {
            int start = mappings[index].Codepoint;
            int startGlyph = mappings[index].GlyphId;
            int end = start;
            index++;
            while (index < mappings.Length
                && mappings[index].Codepoint == end + 1
                && mappings[index].GlyphId == startGlyph + (mappings[index].Codepoint - start)) {
                end = mappings[index].Codepoint;
                index++;
            }

            segments.Add((start, end, (startGlyph - start) & 0xFFFF));
        }

        segments.Add((0xFFFF, 0xFFFF, 1));
        int segmentCount = segments.Count;
        byte[] table = new byte[16 + (8 * segmentCount)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 4);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)table.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)(segmentCount * 2));
        int searchRange = 2;
        int entrySelector = 0;
        while ((searchRange * 2) <= (segmentCount * 2)) {
            searchRange *= 2;
            entrySelector++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(10), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(
            table.AsSpan(12),
            (ushort)((segmentCount * 2) - searchRange));

        int endCodeOffset = 14;
        int startCodeOffset = endCodeOffset + (2 * segmentCount) + 2;
        int deltaOffset = startCodeOffset + (2 * segmentCount);
        int rangeOffset = deltaOffset + (2 * segmentCount);
        for (int segment = 0; segment < segmentCount; segment++) {
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(endCodeOffset + (2 * segment)),
                (ushort)segments[segment].End);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(startCodeOffset + (2 * segment)),
                (ushort)segments[segment].Start);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(deltaOffset + (2 * segment)),
                (ushort)segments[segment].Delta);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rangeOffset + (2 * segment)), 0);
        }

        return table;
    }

    private static byte[] BuildFormat6((int Codepoint, int GlyphId)[] mappings) {
        int firstCode = mappings[0].Codepoint;
        int lastCode = mappings[^1].Codepoint;
        int entryCount = lastCode - firstCode + 1;
        byte[] table = new byte[10 + (2 * entryCount)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 6);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)table.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)firstCode);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)entryCount);
        foreach ((int codepoint, int glyphId) in mappings) {
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(10 + (2 * (codepoint - firstCode))),
                (ushort)glyphId);
        }

        return table;
    }

    private static byte[] BuildFormat12((int Codepoint, int GlyphId)[] mappings) {
        List<(int Start, int End, int StartGlyph)> groups = [];
        int index = 0;
        while (index < mappings.Length) {
            int start = mappings[index].Codepoint;
            int startGlyph = mappings[index].GlyphId;
            int end = start;
            index++;
            while (index < mappings.Length
                && mappings[index].Codepoint == end + 1
                && mappings[index].GlyphId == startGlyph + (mappings[index].Codepoint - start)) {
                end = mappings[index].Codepoint;
                index++;
            }

            groups.Add((start, end, startGlyph));
        }

        byte[] table = new byte[16 + (12 * groups.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 12);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), (uint)table.Length);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(12), (uint)groups.Count);
        for (int group = 0; group < groups.Count; group++) {
            int offset = 16 + (12 * group);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(offset), (uint)groups[group].Start);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(offset + 4), (uint)groups[group].End);
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(offset + 8), (uint)groups[group].StartGlyph);
        }

        return table;
    }
}
