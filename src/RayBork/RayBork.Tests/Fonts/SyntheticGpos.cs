using System.Buffers.Binary;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Builds a synthetic GPOS table with one script, one feature, one lookup, and one PairPos subtable.
/// </summary>
/// <remarks>
/// The pair lookups are derived from the configured glyph pairs: format 1 groups the pairs into one
/// pair set per covered left glyph, and format 2 assigns one class to every distinct left and right
/// glyph and fills the class matrix. Coverage and class definition formats are selectable, and the
/// lookup can be wrapped in an extension positioning lookup (type 9).
/// </remarks>
internal sealed class SyntheticGpos {
    internal int PairPosFormat { get; set; } = 1;

    internal int CoverageFormat { get; set; } = 1;

    internal int ClassDefFormat { get; set; } = 1;

    internal string FeatureTag { get; set; } = "kern";

    internal string ScriptTag { get; set; } = "latn";

    internal bool ExtensionLookup { get; set; }

    internal int ValueFormat1 { get; set; } = 0x0004;

    internal int ValueFormat2 { get; set; }

    internal List<(int Left, int Right, int XAdvance, int SecondXAdvance)> Pairs { get; } = [];

    /// <summary>Adds one positioned pair.</summary>
    internal SyntheticGpos AddPair(int left, int right, int xAdvance, int secondXAdvance = 0) {
        Pairs.Add((left, right, xAdvance, secondXAdvance));
        return this;
    }

    /// <summary>Builds the complete GPOS table bytes.</summary>
    internal byte[] Build(int glyphCount) {
        if (Pairs.Count == 0) {
            throw new InvalidOperationException("A synthetic GPOS needs at least one pair.");
        }

        if (FeatureTag.Length != 4 || ScriptTag.Length != 4) {
            throw new InvalidOperationException("Synthetic feature and script tags are four characters.");
        }

        foreach ((int left, int right, int xAdvance, int secondXAdvance) in Pairs) {
            if (left < 0 || left >= glyphCount || right < 0 || right >= glyphCount) {
                throw new InvalidOperationException(
                    $"Synthetic GPOS pair ({left}, {right}) is outside the face.");
            }

            if (xAdvance is < short.MinValue or > short.MaxValue
                || secondXAdvance is < short.MinValue or > short.MaxValue) {
                throw new InvalidOperationException("Synthetic GPOS advances must fit a signed 16-bit value.");
            }
        }

        byte[] pairPositioning = PairPosFormat == 1 ? BuildPairPos1() : BuildPairPos2();
        byte[] lookup = BuildLookup(pairPositioning);
        byte[] lookupList = BuildLookupList(lookup);
        byte[] featureList = BuildFeatureList();
        byte[] scriptList = BuildScriptList();

        byte[] gpos = new byte[10 + scriptList.Length + featureList.Length + lookupList.Length];
        BinaryPrimitives.WriteUInt16BigEndian(gpos, 1);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(4), 10);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(6), (ushort)(10 + scriptList.Length));
        BinaryPrimitives.WriteUInt16BigEndian(
            gpos.AsSpan(8),
            (ushort)(10 + scriptList.Length + featureList.Length));
        scriptList.CopyTo(gpos, 10);
        featureList.CopyTo(gpos, 10 + scriptList.Length);
        lookupList.CopyTo(gpos, 10 + scriptList.Length + featureList.Length);
        return gpos;
    }

    private byte[] BuildPairPos1() {
        SortedDictionary<int, List<(int Right, int XAdvance, int SecondXAdvance)>> byLeft = [];
        foreach ((int left, int right, int xAdvance, int secondXAdvance) in Pairs) {
            if (!byLeft.TryGetValue(left, out List<(int, int, int)>? values)) {
                values = [];
                byLeft[left] = values;
            }

            values.Add((right, xAdvance, secondXAdvance));
        }

        foreach (List<(int Right, int XAdvance, int SecondXAdvance)> values in byLeft.Values) {
            values.Sort(static (first, second) => first.Right.CompareTo(second.Right));
        }

        int[] leftGlyphs = [.. byLeft.Keys];
        byte[] coverage = BuildCoverage(leftGlyphs);
        int recordLength = ValueRecordSize(ValueFormat1) + ValueRecordSize(ValueFormat2) + 2;
        int pairSetLength = 0;
        foreach (List<(int Right, int XAdvance, int SecondXAdvance)> values in byLeft.Values) {
            pairSetLength += 2 + (values.Count * recordLength);
        }

        byte[] table = new byte[10 + (leftGlyphs.Length * 2) + pairSetLength + coverage.Length];
        BinaryPrimitives.WriteUInt16BigEndian(table, 1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)ValueFormat1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)ValueFormat2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)leftGlyphs.Length);
        int pairSetOffset = 10 + (leftGlyphs.Length * 2);
        for (int index = 0; index < leftGlyphs.Length; index++) {
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(10 + (2 * index)),
                (ushort)pairSetOffset);
            List<(int Right, int XAdvance, int SecondXAdvance)> values = byLeft[leftGlyphs[index]];
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(pairSetOffset), (ushort)values.Count);
            int recordOffset = pairSetOffset + 2;
            foreach ((int right, int xAdvance, int secondXAdvance) in values) {
                BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(recordOffset), (ushort)right);
                recordOffset += 2;
                WriteValueRecord(table.AsSpan(recordOffset), ValueFormat1, xAdvance);
                recordOffset += ValueRecordSize(ValueFormat1);
                WriteValueRecord(table.AsSpan(recordOffset), ValueFormat2, secondXAdvance);
                recordOffset += ValueRecordSize(ValueFormat2);
            }

            pairSetOffset += 2 + (values.Count * recordLength);
        }

        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)pairSetOffset);
        coverage.CopyTo(table, pairSetOffset);
        return table;
    }

    private byte[] BuildPairPos2() {
        SortedSet<int> leftGlyphs = [];
        SortedSet<int> rightGlyphs = [];
        foreach ((int left, int right, _, _) in Pairs) {
            leftGlyphs.Add(left);
            rightGlyphs.Add(right);
        }

        SortedDictionary<int, int> leftClasses = [];
        int leftClass = 1;
        foreach (int glyph in leftGlyphs) {
            leftClasses[glyph] = leftClass++;
        }

        SortedDictionary<int, int> rightClasses = [];
        int rightClass = 1;
        foreach (int glyph in rightGlyphs) {
            rightClasses[glyph] = rightClass++;
        }

        int class1Count = leftClasses.Count + 1;
        int class2Count = rightClasses.Count + 1;
        int classRecordLength = ValueRecordSize(ValueFormat1) + ValueRecordSize(ValueFormat2);
        (int XAdvance, int SecondXAdvance)[] matrix = new (int, int)[class1Count * class2Count];
        foreach ((int left, int right, int xAdvance, int secondXAdvance) in Pairs) {
            int class1 = leftClasses[left];
            int class2 = rightClasses[right];
            matrix[(class1 * class2Count) + class2] = (xAdvance, secondXAdvance);
        }

        byte[] coverage = BuildCoverage([.. leftGlyphs]);
        byte[] classDef1 = BuildClassDefinition(leftClasses, ClassDefFormat);
        byte[] classDef2 = BuildClassDefinition(rightClasses, ClassDefFormat);
        int matrixLength = class1Count * class2Count * classRecordLength;
        byte[] table = new byte[16 + matrixLength + coverage.Length + classDef1.Length + classDef2.Length];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)ValueFormat1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)ValueFormat2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(12), (ushort)class1Count);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14), (ushort)class2Count);
        int offset = 16;
        for (int class1 = 0; class1 < class1Count; class1++) {
            for (int class2 = 0; class2 < class2Count; class2++) {
                (int xAdvance, int secondXAdvance) = matrix[(class1 * class2Count) + class2];
                WriteValueRecord(table.AsSpan(offset), ValueFormat1, xAdvance);
                offset += ValueRecordSize(ValueFormat1);
                WriteValueRecord(table.AsSpan(offset), ValueFormat2, secondXAdvance);
                offset += ValueRecordSize(ValueFormat2);
            }
        }

        int coverageOffset = offset;
        coverage.CopyTo(table, coverageOffset);
        int classDef1Offset = coverageOffset + coverage.Length;
        classDef1.CopyTo(table, classDef1Offset);
        int classDef2Offset = classDef1Offset + classDef1.Length;
        classDef2.CopyTo(table, classDef2Offset);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)coverageOffset);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)classDef1Offset);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(10), (ushort)classDef2Offset);
        return table;
    }

    private byte[] BuildLookup(byte[] pairPositioning) {
        int subtableOffset = ExtensionLookup ? 16 : 8;
        byte[] lookup = new byte[subtableOffset + pairPositioning.Length];
        BinaryPrimitives.WriteUInt16BigEndian(lookup, (ushort)(ExtensionLookup ? 9 : 2));
        BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(6), 8);
        if (ExtensionLookup) {
            BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(10), 2);
            BinaryPrimitives.WriteUInt32BigEndian(lookup.AsSpan(12), 8);
        }

        pairPositioning.CopyTo(lookup, subtableOffset);
        return lookup;
    }

    private static byte[] BuildLookupList(byte[] lookup) {
        byte[] lookupList = new byte[4 + lookup.Length];
        BinaryPrimitives.WriteUInt16BigEndian(lookupList, 1);
        BinaryPrimitives.WriteUInt16BigEndian(lookupList.AsSpan(2), 4);
        lookup.CopyTo(lookupList, 4);
        return lookupList;
    }

    private byte[] BuildFeatureList() {
        byte[] featureList = new byte[8 + 6];
        BinaryPrimitives.WriteUInt16BigEndian(featureList, 1);
        featureList[2] = (byte)FeatureTag[0];
        featureList[3] = (byte)FeatureTag[1];
        featureList[4] = (byte)FeatureTag[2];
        featureList[5] = (byte)FeatureTag[3];
        BinaryPrimitives.WriteUInt16BigEndian(featureList.AsSpan(6), 8);
        BinaryPrimitives.WriteUInt16BigEndian(featureList.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16BigEndian(featureList.AsSpan(12), 0);
        return featureList;
    }

    private byte[] BuildScriptList() {
        byte[] scriptList = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(scriptList, 1);
        scriptList[2] = (byte)ScriptTag[0];
        scriptList[3] = (byte)ScriptTag[1];
        scriptList[4] = (byte)ScriptTag[2];
        scriptList[5] = (byte)ScriptTag[3];
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(6), 8);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(10), 0);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(14), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(18), 0);
        return scriptList;
    }

    private byte[] BuildCoverage(int[] glyphs) {
        if (CoverageFormat == 1) {
            byte[] coverage = new byte[4 + (2 * glyphs.Length)];
            BinaryPrimitives.WriteUInt16BigEndian(coverage, 1);
            BinaryPrimitives.WriteUInt16BigEndian(coverage.AsSpan(2), (ushort)glyphs.Length);
            for (int index = 0; index < glyphs.Length; index++) {
                BinaryPrimitives.WriteUInt16BigEndian(
                    coverage.AsSpan(4 + (2 * index)),
                    (ushort)glyphs[index]);
            }

            return coverage;
        }

        List<(int Start, int End, int StartCoverageIndex)> ranges = [];
        int rangeStart = glyphs[0];
        int rangeEnd = glyphs[0];
        int rangeStartIndex = 0;
        for (int index = 1; index < glyphs.Length; index++) {
            if (glyphs[index] == rangeEnd + 1) {
                rangeEnd = glyphs[index];
                continue;
            }

            ranges.Add((rangeStart, rangeEnd, rangeStartIndex));
            rangeStart = glyphs[index];
            rangeEnd = glyphs[index];
            rangeStartIndex = index;
        }

        ranges.Add((rangeStart, rangeEnd, rangeStartIndex));
        byte[] table = new byte[4 + (6 * ranges.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)ranges.Count);
        for (int index = 0; index < ranges.Count; index++) {
            int offset = 4 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(offset), (ushort)ranges[index].Start);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(offset + 2), (ushort)ranges[index].End);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 4),
                (ushort)ranges[index].StartCoverageIndex);
        }

        return table;
    }

    private static byte[] BuildClassDefinition(SortedDictionary<int, int> classes, int format) {
        int[] glyphs = [.. classes.Keys];
        if (format == 1) {
            int startGlyph = glyphs[0];
            int glyphCount = glyphs[^1] - startGlyph + 1;
            byte[] definition = new byte[6 + (2 * glyphCount)];
            BinaryPrimitives.WriteUInt16BigEndian(definition, 1);
            BinaryPrimitives.WriteUInt16BigEndian(definition.AsSpan(2), (ushort)startGlyph);
            BinaryPrimitives.WriteUInt16BigEndian(definition.AsSpan(4), (ushort)glyphCount);
            foreach ((int glyph, int classValue) in classes) {
                BinaryPrimitives.WriteUInt16BigEndian(
                    definition.AsSpan(6 + (2 * (glyph - startGlyph))),
                    (ushort)classValue);
            }

            return definition;
        }

        List<(int Start, int End, int ClassValue)> ranges = [];
        int rangeStart = glyphs[0];
        int rangeEnd = glyphs[0];
        int rangeClass = classes[glyphs[0]];
        for (int index = 1; index < glyphs.Length; index++) {
            if (glyphs[index] == rangeEnd + 1 && classes[glyphs[index]] == rangeClass) {
                rangeEnd = glyphs[index];
                continue;
            }

            ranges.Add((rangeStart, rangeEnd, rangeClass));
            rangeStart = glyphs[index];
            rangeEnd = glyphs[index];
            rangeClass = classes[glyphs[index]];
        }

        ranges.Add((rangeStart, rangeEnd, rangeClass));
        byte[] table = new byte[4 + (6 * ranges.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)ranges.Count);
        for (int index = 0; index < ranges.Count; index++) {
            int offset = 4 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(offset), (ushort)ranges[index].Start);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(offset + 2), (ushort)ranges[index].End);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 4),
                (ushort)ranges[index].ClassValue);
        }

        return table;
    }

    private static void WriteValueRecord(Span<byte> target, int valueFormat, int xAdvance) {
        int offset = 0;
        for (int bit = 0; bit < 8; bit++) {
            if ((valueFormat & (1 << bit)) == 0) {
                continue;
            }

            int value = bit == 2 ? xAdvance : 0;
            BinaryPrimitives.WriteInt16BigEndian(target[offset..], (short)value);
            offset += 2;
        }
    }

    private static int ValueRecordSize(int valueFormat) {
        int size = 0;
        for (int bit = 0; bit < 8; bit++) {
            if ((valueFormat & (1 << bit)) != 0) {
                size += 2;
            }
        }

        return size;
    }
}
