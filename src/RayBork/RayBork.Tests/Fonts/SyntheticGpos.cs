using System.Buffers.Binary;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Builds a synthetic GPOS table with selectable scripts, features, lookups, and PairPos subtables.
/// </summary>
/// <remarks>
/// The default is one script, one feature, one lookup, and one PairPos subtable derived from
/// <see cref="Pairs"/>. Adding explicit lookups, features, or scripts switches to an explicit
/// layout: every lookup is a PairPos with its own pairs, every feature references those lookup
/// indices, and every script references the feature indices. Coverage and class definition formats
/// are selectable, the lookup can be wrapped in an extension positioning lookup (type 9), and the
/// validation-hostile variants (reversed coverage, pair sets, and class definition ranges; invalid
/// value formats) are available for negative tests.
/// </remarks>
internal sealed class SyntheticGpos
{
    internal int PairPosFormat { get; set; } = 1;

    internal int CoverageFormat { get; set; } = 1;

    internal int ClassDefFormat { get; set; } = 1;

    internal string FeatureTag { get; set; } = "kern";

    internal string ScriptTag { get; set; } = "latn";

    internal bool ExtensionLookup { get; set; }

    internal int ExtensionInnerLookupType { get; set; } = 2;

    internal int SubtablesPerLookup { get; set; } = 1;

    internal bool AliasLookupSubtables { get; set; }

    internal bool AliasLookupListEntries { get; set; }

    internal int ValueFormat1 { get; set; } = 0x0004;

    internal int ValueFormat2 { get; set; }

    internal bool ReverseCoverage { get; set; }

    internal bool ReversePairSets { get; set; }

    internal bool ReverseClassDefRanges { get; set; }

    internal List<(int Left, int Right, int XAdvance, int SecondXAdvance)> Pairs { get; } = [];

    private readonly List<SyntheticLookup> _lookups = [];
    private readonly List<SyntheticFeature> _features = [];
    private readonly List<SyntheticScript> _scripts = [];

    /// <summary>Adds one positioned pair to the default single lookup.</summary>
    internal SyntheticGpos AddPair(int left, int right, int xAdvance, int secondXAdvance = 0)
    {
        Pairs.Add((left, right, xAdvance, secondXAdvance));
        return this;
    }

    /// <summary>Adds an explicit lookup with its own pairs.</summary>
    internal SyntheticGpos AddLookup(params (int Left, int Right, int XAdvance)[] pairs) =>
        AddLookup(0, pairs);

    /// <summary>Adds an explicit lookup with a per-lookup subtable count (0 uses the shared option).</summary>
    internal SyntheticGpos AddLookup(
        int subtableCount,
        params (int Left, int Right, int XAdvance)[] pairs
    )
    {
        _lookups.Add(
            new SyntheticLookup(
                [.. pairs.Select(pair => (pair.Left, pair.Right, pair.XAdvance, 0))],
                subtableCount
            )
        );
        return this;
    }

    /// <summary>Adds an explicit feature with the lookup indices it references.</summary>
    internal SyntheticGpos AddFeature(string tag, params int[] lookupIndices)
    {
        _features.Add(new SyntheticFeature(tag, lookupIndices));
        return this;
    }

    /// <summary>Adds an explicit script whose default language system references feature indices.</summary>
    internal SyntheticGpos AddScript(string tag, params int[] featureIndices)
    {
        _scripts.Add(new SyntheticScript(tag, featureIndices));
        return this;
    }

    /// <summary>Builds the complete GPOS table bytes.</summary>
    internal byte[] Build(int glyphCount)
    {
        List<SyntheticLookup> lookups;
        List<SyntheticFeature> features;
        List<SyntheticScript> scripts;
        if (_lookups.Count == 0 && _features.Count == 0 && _scripts.Count == 0)
        {
            if (Pairs.Count == 0)
            {
                throw new InvalidOperationException("A synthetic GPOS needs at least one pair.");
            }

            lookups = [new SyntheticLookup(Pairs, 0)];
            features = [new SyntheticFeature(FeatureTag, [0])];
            scripts = [new SyntheticScript(ScriptTag, [0])];
        }
        else
        {
            if (_lookups.Count == 0 || _features.Count == 0 || _scripts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Explicit GPOS layouts need at least one lookup, feature, and script."
                );
            }

            if (Pairs.Count > 0)
            {
                throw new InvalidOperationException(
                    "A GPOS cannot mix the default pairs with explicit lookups."
                );
            }

            lookups = _lookups;
            features = _features;
            scripts = _scripts;
        }

        if (SubtablesPerLookup < 1)
        {
            throw new InvalidOperationException("A GPOS lookup needs at least one subtable.");
        }

        foreach (SyntheticLookup lookup in lookups)
        {
            foreach ((int left, int right, int xAdvance, int secondXAdvance) in lookup.Pairs)
            {
                if (left < 0 || left >= glyphCount || right < 0 || right >= glyphCount)
                {
                    throw new InvalidOperationException(
                        $"Synthetic GPOS pair ({left}, {right}) is outside the face."
                    );
                }

                if (
                    xAdvance is < short.MinValue or > short.MaxValue
                    || secondXAdvance is < short.MinValue or > short.MaxValue
                )
                {
                    throw new InvalidOperationException(
                        "Synthetic GPOS advances must fit a signed 16-bit value."
                    );
                }
            }
        }

        foreach (SyntheticFeature feature in features)
        {
            if (feature.Tag.Length != 4)
            {
                throw new InvalidOperationException("Synthetic feature tags are four characters.");
            }

            foreach (int lookupIndex in feature.LookupIndices)
            {
                if (lookupIndex < 0 || lookupIndex >= lookups.Count)
                {
                    throw new InvalidOperationException(
                        $"Synthetic GPOS feature references lookup {lookupIndex}."
                    );
                }
            }
        }

        foreach (SyntheticScript script in scripts)
        {
            if (script.Tag.Length != 4)
            {
                throw new InvalidOperationException("Synthetic script tags are four characters.");
            }

            foreach (int featureIndex in script.FeatureIndices)
            {
                if (featureIndex < 0 || featureIndex >= features.Count)
                {
                    throw new InvalidOperationException(
                        $"Synthetic GPOS script references feature {featureIndex}."
                    );
                }
            }
        }

        List<byte[]> lookupTables = [];
        foreach (SyntheticLookup lookup in lookups)
        {
            byte[] pairPositioning = BuildPairPositioning(lookup.Pairs);
            int subtableCount =
                lookup.SubtableCount > 0 ? lookup.SubtableCount : SubtablesPerLookup;
            byte[][] subtables = new byte[subtableCount][];
            for (int index = 0; index < subtables.Length; index++)
            {
                subtables[index] = pairPositioning;
            }

            lookupTables.Add(BuildLookup(subtables));
        }

        byte[] lookupList = BuildLookupList(lookupTables);
        byte[] featureList = BuildFeatureList(features);
        byte[] scriptList = BuildScriptList(scripts);

        // The three list offsets are 16-bit, so the largest list goes last and the small lists keep
        // every offset inside 64 KB.
        (int Kind, byte[] Data)[] lists = [(0, scriptList), (1, featureList), (2, lookupList)];
        Array.Sort(lists, static (first, second) =>
        {
            int byLength = first.Data.Length.CompareTo(second.Data.Length);
            return byLength != 0 ? byLength : first.Kind.CompareTo(second.Kind);
        });
        int[] offsets = new int[3];
        int position = 10;
        foreach ((int kind, byte[] data) in lists)
        {
            offsets[kind] = position;
            position += data.Length;
        }

        byte[] gpos = new byte[position];
        BinaryPrimitives.WriteUInt16BigEndian(gpos, 1);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(4), (ushort)offsets[0]);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(6), (ushort)offsets[1]);
        BinaryPrimitives.WriteUInt16BigEndian(gpos.AsSpan(8), (ushort)offsets[2]);
        scriptList.CopyTo(gpos, offsets[0]);
        featureList.CopyTo(gpos, offsets[1]);
        lookupList.CopyTo(gpos, offsets[2]);
        return gpos;
    }

    private byte[] BuildPairPositioning(
        IReadOnlyList<(int Left, int Right, int XAdvance, int SecondXAdvance)> pairs
    ) => PairPosFormat == 1 ? BuildPairPos1(pairs) : BuildPairPos2(pairs);

    private byte[] BuildPairPos1(
        IReadOnlyList<(int Left, int Right, int XAdvance, int SecondXAdvance)> pairs
    )
    {
        SortedDictionary<int, List<(int Right, int XAdvance, int SecondXAdvance)>> byLeft = [];
        foreach ((int left, int right, int xAdvance, int secondXAdvance) in pairs)
        {
            if (!byLeft.TryGetValue(left, out List<(int, int, int)>? values))
            {
                values = [];
                byLeft[left] = values;
            }

            values.Add((right, xAdvance, secondXAdvance));
        }

        foreach (List<(int Right, int XAdvance, int SecondXAdvance)> values in byLeft.Values)
        {
            values.Sort(static (first, second) => first.Right.CompareTo(second.Right));
            if (ReversePairSets)
            {
                values.Reverse();
            }
        }

        int[] leftGlyphs = [.. byLeft.Keys];
        if (ReverseCoverage)
        {
            Array.Reverse(leftGlyphs);
        }

        byte[] coverage = BuildCoverage(leftGlyphs);
        int recordLength = ValueRecordSize(ValueFormat1) + ValueRecordSize(ValueFormat2) + 2;
        int pairSetLength = 0;
        foreach (int left in leftGlyphs)
        {
            pairSetLength += 2 + (byLeft[left].Count * recordLength);
        }

        byte[] table = new byte[10 + (leftGlyphs.Length * 2) + pairSetLength + coverage.Length];
        BinaryPrimitives.WriteUInt16BigEndian(table, 1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)ValueFormat1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)ValueFormat2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)leftGlyphs.Length);
        int pairSetOffset = 10 + (leftGlyphs.Length * 2);
        for (int index = 0; index < leftGlyphs.Length; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(10 + (2 * index)),
                (ushort)pairSetOffset
            );
            List<(int Right, int XAdvance, int SecondXAdvance)> values = byLeft[leftGlyphs[index]];
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(pairSetOffset),
                (ushort)values.Count
            );
            int recordOffset = pairSetOffset + 2;
            foreach ((int right, int xAdvance, int secondXAdvance) in values)
            {
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

    private byte[] BuildPairPos2(
        IReadOnlyList<(int Left, int Right, int XAdvance, int SecondXAdvance)> pairs
    )
    {
        SortedSet<int> leftGlyphs = [];
        SortedSet<int> rightGlyphs = [];
        foreach ((int left, int right, _, _) in pairs)
        {
            leftGlyphs.Add(left);
            rightGlyphs.Add(right);
        }

        SortedDictionary<int, int> leftClasses = [];
        int leftClass = 1;
        foreach (int glyph in leftGlyphs)
        {
            leftClasses[glyph] = leftClass++;
        }

        SortedDictionary<int, int> rightClasses = [];
        int rightClass = 1;
        foreach (int glyph in rightGlyphs)
        {
            rightClasses[glyph] = rightClass++;
        }

        int class1Count = leftClasses.Count + 1;
        int class2Count = rightClasses.Count + 1;
        int classRecordLength = ValueRecordSize(ValueFormat1) + ValueRecordSize(ValueFormat2);
        (int XAdvance, int SecondXAdvance)[] matrix = new (int, int)[class1Count * class2Count];
        foreach ((int left, int right, int xAdvance, int secondXAdvance) in pairs)
        {
            int class1 = leftClasses[left];
            int class2 = rightClasses[right];
            matrix[(class1 * class2Count) + class2] = (xAdvance, secondXAdvance);
        }

        int[] coverageGlyphs = [.. leftGlyphs];
        if (ReverseCoverage)
        {
            Array.Reverse(coverageGlyphs);
        }

        byte[] coverage = BuildCoverage(coverageGlyphs);
        byte[] classDef1 = BuildClassDefinition(leftClasses, ClassDefFormat);
        byte[] classDef2 = BuildClassDefinition(rightClasses, ClassDefFormat);
        int matrixLength = class1Count * class2Count * classRecordLength;
        byte[] table = new byte[
            16 + matrixLength + coverage.Length + classDef1.Length + classDef2.Length
        ];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)ValueFormat1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), (ushort)ValueFormat2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(12), (ushort)class1Count);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14), (ushort)class2Count);
        int offset = 16;
        for (int class1 = 0; class1 < class1Count; class1++)
        {
            for (int class2 = 0; class2 < class2Count; class2++)
            {
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

    private byte[] BuildLookup(IReadOnlyList<byte[]> pairPositionings)
    {
        int subtableOffset = 6 + (2 * pairPositionings.Count);
        if (ExtensionLookup)
        {
            subtableOffset += pairPositionings.Count * 8;
        }

        if (AliasLookupSubtables && ExtensionLookup)
        {
            throw new InvalidOperationException(
                "An aliased subtable lookup cannot use extension lookups."
            );
        }

        int totalLength = subtableOffset;
        if (AliasLookupSubtables)
        {
            totalLength += pairPositionings[0].Length;
        }
        else
        {
            foreach (byte[] pairPositioning in pairPositionings)
            {
                totalLength += (ExtensionLookup ? 8 : 0) + pairPositioning.Length;
            }
        }

        byte[] lookup = new byte[totalLength];
        BinaryPrimitives.WriteUInt16BigEndian(lookup, (ushort)(ExtensionLookup ? 9 : 2));
        BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(4), (ushort)pairPositionings.Count);
        int offset = subtableOffset;
        for (int index = 0; index < pairPositionings.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(6 + (2 * index)), (ushort)offset);
            if (AliasLookupSubtables)
            {
                continue;
            }

            if (ExtensionLookup)
            {
                BinaryPrimitives.WriteUInt16BigEndian(lookup.AsSpan(offset), 1);
                BinaryPrimitives.WriteUInt16BigEndian(
                    lookup.AsSpan(offset + 2),
                    (ushort)ExtensionInnerLookupType
                );
                BinaryPrimitives.WriteUInt32BigEndian(lookup.AsSpan(offset + 4), 8);
                offset += 8;
            }

            pairPositionings[index].CopyTo(lookup, offset);
            offset += pairPositionings[index].Length;
        }

        if (AliasLookupSubtables)
        {
            pairPositionings[0].CopyTo(lookup, subtableOffset);
        }

        return lookup;
    }

    private byte[] BuildLookupList(IReadOnlyList<byte[]> lookups)
    {
        if (AliasLookupListEntries)
        {
            int aliasedOffset = 2 + (2 * lookups.Count);
            byte[] aliased = new byte[aliasedOffset + lookups[0].Length];
            BinaryPrimitives.WriteUInt16BigEndian(aliased, (ushort)lookups.Count);
            for (int index = 0; index < lookups.Count; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    aliased.AsSpan(2 + (2 * index)),
                    (ushort)aliasedOffset
                );
            }

            lookups[0].CopyTo(aliased, aliasedOffset);
            return aliased;
        }

        int offset = 2 + (2 * lookups.Count);
        byte[] lookupList = new byte[offset + TotalLength(lookups)];
        BinaryPrimitives.WriteUInt16BigEndian(lookupList, (ushort)lookups.Count);
        for (int index = 0; index < lookups.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                lookupList.AsSpan(2 + (2 * index)),
                (ushort)offset
            );
            lookups[index].CopyTo(lookupList, offset);
            offset += lookups[index].Length;
        }

        return lookupList;
    }

    private static byte[] BuildFeatureList(IReadOnlyList<SyntheticFeature> features)
    {
        int offset = 2 + (6 * features.Count);
        List<byte[]> tables = [];
        foreach (SyntheticFeature feature in features)
        {
            byte[] table = new byte[4 + (2 * feature.LookupIndices.Length)];
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(2),
                (ushort)feature.LookupIndices.Length
            );
            for (int index = 0; index < feature.LookupIndices.Length; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    table.AsSpan(4 + (2 * index)),
                    (ushort)feature.LookupIndices[index]
                );
            }

            tables.Add(table);
        }

        byte[] featureList = new byte[offset + TotalLength(tables)];
        BinaryPrimitives.WriteUInt16BigEndian(featureList, (ushort)features.Count);
        for (int index = 0; index < features.Count; index++)
        {
            int record = 2 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(featureList.AsSpan(record + 4), (ushort)offset);
            WriteTag(featureList.AsSpan(record), features[index].Tag);
            tables[index].CopyTo(featureList, offset);
            offset += tables[index].Length;
        }

        return featureList;
    }

    private static byte[] BuildScriptList(IReadOnlyList<SyntheticScript> scripts)
    {
        int offset = 2 + (6 * scripts.Count);
        List<byte[]> tables = [];
        foreach (SyntheticScript script in scripts)
        {
            byte[] table = new byte[4 + 6 + (2 * script.FeatureIndices.Length)];
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0), 4);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), 0xFFFF);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(8),
                (ushort)script.FeatureIndices.Length
            );
            for (int index = 0; index < script.FeatureIndices.Length; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    table.AsSpan(10 + (2 * index)),
                    (ushort)script.FeatureIndices[index]
                );
            }

            tables.Add(table);
        }

        byte[] scriptList = new byte[offset + TotalLength(tables)];
        BinaryPrimitives.WriteUInt16BigEndian(scriptList, (ushort)scripts.Count);
        for (int index = 0; index < scripts.Count; index++)
        {
            int record = 2 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(scriptList.AsSpan(record + 4), (ushort)offset);
            WriteTag(scriptList.AsSpan(record), scripts[index].Tag);
            tables[index].CopyTo(scriptList, offset);
            offset += tables[index].Length;
        }

        return scriptList;
    }

    private byte[] BuildCoverage(int[] glyphs)
    {
        if (CoverageFormat == 1)
        {
            byte[] coverage = new byte[4 + (2 * glyphs.Length)];
            BinaryPrimitives.WriteUInt16BigEndian(coverage, 1);
            BinaryPrimitives.WriteUInt16BigEndian(coverage.AsSpan(2), (ushort)glyphs.Length);
            for (int index = 0; index < glyphs.Length; index++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    coverage.AsSpan(4 + (2 * index)),
                    (ushort)glyphs[index]
                );
            }

            return coverage;
        }

        List<(int Start, int End, int StartCoverageIndex)> ranges = [];
        for (int index = 0; index < glyphs.Length; index++)
        {
            if (ranges.Count > 0 && glyphs[index] == ranges[^1].End + 1)
            {
                ranges[^1] = (ranges[^1].Start, glyphs[index], ranges[^1].StartCoverageIndex);
            }
            else
            {
                ranges.Add((glyphs[index], glyphs[index], index));
            }
        }

        byte[] table = new byte[4 + (6 * ranges.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)ranges.Count);
        for (int index = 0; index < ranges.Count; index++)
        {
            int offset = 4 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset),
                (ushort)ranges[index].Start
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 2),
                (ushort)ranges[index].End
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 4),
                (ushort)ranges[index].StartCoverageIndex
            );
        }

        return table;
    }

    private byte[] BuildClassDefinition(SortedDictionary<int, int> classes, int format)
    {
        int[] glyphs = [.. classes.Keys];
        if (format == 1)
        {
            int startGlyph = glyphs[0];
            int glyphCount = glyphs[^1] - startGlyph + 1;
            byte[] definition = new byte[6 + (2 * glyphCount)];
            BinaryPrimitives.WriteUInt16BigEndian(definition, 1);
            BinaryPrimitives.WriteUInt16BigEndian(definition.AsSpan(2), (ushort)startGlyph);
            BinaryPrimitives.WriteUInt16BigEndian(definition.AsSpan(4), (ushort)glyphCount);
            foreach ((int glyph, int classValue) in classes)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    definition.AsSpan(6 + (2 * (glyph - startGlyph))),
                    (ushort)classValue
                );
            }

            return definition;
        }

        List<(int Start, int End, int ClassValue)> ranges = [];
        for (int index = 0; index < glyphs.Length; index++)
        {
            int classValue = classes[glyphs[index]];
            if (
                ranges.Count > 0
                && glyphs[index] == ranges[^1].End + 1
                && ranges[^1].ClassValue == classValue
            )
            {
                ranges[^1] = (ranges[^1].Start, glyphs[index], classValue);
            }
            else
            {
                ranges.Add((glyphs[index], glyphs[index], classValue));
            }
        }

        if (ReverseClassDefRanges)
        {
            ranges.Reverse();
        }

        byte[] table = new byte[4 + (6 * ranges.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)ranges.Count);
        for (int index = 0; index < ranges.Count; index++)
        {
            int offset = 4 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset),
                (ushort)ranges[index].Start
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 2),
                (ushort)ranges[index].End
            );
            BinaryPrimitives.WriteUInt16BigEndian(
                table.AsSpan(offset + 4),
                (ushort)ranges[index].ClassValue
            );
        }

        return table;
    }

    private static void WriteTag(Span<byte> target, string tag)
    {
        target[0] = (byte)tag[0];
        target[1] = (byte)tag[1];
        target[2] = (byte)tag[2];
        target[3] = (byte)tag[3];
    }

    private static int TotalLength(IReadOnlyList<byte[]> buffers)
    {
        int length = 0;
        foreach (byte[] buffer in buffers)
        {
            length += buffer.Length;
        }

        return length;
    }

    private static void WriteValueRecord(Span<byte> target, int valueFormat, int xAdvance)
    {
        int offset = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) == 0)
            {
                continue;
            }

            int value = bit == 2 ? xAdvance : 0;
            BinaryPrimitives.WriteInt16BigEndian(target[offset..], (short)value);
            offset += 2;
        }
    }

    private static int ValueRecordSize(int valueFormat)
    {
        int size = 0;
        for (int bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) != 0)
            {
                size += 2;
            }
        }

        return size;
    }

    private readonly struct SyntheticLookup
    {
        internal SyntheticLookup(
            IReadOnlyList<(int Left, int Right, int XAdvance, int SecondXAdvance)> pairs,
            int subtableCount
        )
        {
            Pairs = pairs;
            SubtableCount = subtableCount;
        }

        internal IReadOnlyList<(
            int Left,
            int Right,
            int XAdvance,
            int SecondXAdvance
        )> Pairs { get; }

        internal int SubtableCount { get; }
    }

    private readonly struct SyntheticFeature
    {
        internal SyntheticFeature(string tag, int[] lookupIndices)
        {
            Tag = tag;
            LookupIndices = lookupIndices;
        }

        internal string Tag { get; }

        internal int[] LookupIndices { get; }
    }

    private readonly struct SyntheticScript
    {
        internal SyntheticScript(string tag, int[] featureIndices)
        {
            Tag = tag;
            FeatureIndices = featureIndices;
        }

        internal string Tag { get; }

        internal int[] FeatureIndices { get; }
    }
}
