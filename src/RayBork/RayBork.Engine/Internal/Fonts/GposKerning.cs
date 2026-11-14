using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The GPOS pair-positioning lookups that apply to the face's <c>kern</c> feature.
/// </summary>
/// <remarks>
/// <para>
/// Only the lookups referenced by a feature tagged <c>kern</c> contribute, and only the default
/// language system of the preferred script is considered. The <c>latn</c> script is preferred, then
/// <c>DFLT</c>, then the first script record in directory order. Pair positioning may be wrapped in
/// extension lookups (lookup type 9) and may be either format 1 (glyph pairs) or format 2
/// (class-based pairs); coverage formats 1 and 2 and class definition formats 1 and 2 are accepted.
/// </para>
/// <para>
/// Only the first glyph's <c>XAdvance</c> contributes. The other value record fields are skipped by
/// their value-format bits and the second glyph's value record is ignored. The first lookup, in
/// lookup-list order, that positions a pair supplies the value; a lookup that does not position the
/// pair is skipped. Lookup flags are not interpreted because the engine applies kerning to a glyph
/// pair without shaping, and the language system's required feature is not consulted. Values are in
/// font units.
/// </para>
/// <para>
/// The instance is immutable after construction and safe to read from any thread. Lookups allocate
/// nothing and take O(log coverage) time plus O(log pairs) or O(log classes) per candidate lookup.
/// </para>
/// </remarks>
internal sealed class GposKerning {
    private const int LOOKUP_TYPE_PAIR_POSITIONING = 2;
    private const int LOOKUP_TYPE_EXTENSION_POSITIONING = 9;
    private const int EXTENSION_FORMAT = 1;
    private const int PAIR_POS_FORMAT_1 = 1;
    private const int PAIR_POS_FORMAT_2 = 2;
    private const int COVERAGE_FORMAT_1 = 1;
    private const int COVERAGE_FORMAT_2 = 2;
    private const int CLASS_DEF_FORMAT_1 = 1;
    private const int CLASS_DEF_FORMAT_2 = 2;
    private const int VALUE_FORMAT_MASK = 0x00FF;
    private const int X_PLACEMENT = 0x0001;
    private const int Y_PLACEMENT = 0x0002;
    private const int X_ADVANCE = 0x0004;
    private const int USE_MARK_FILTERING_SET = 0x0010;
    private const int BASE_HEADER_LENGTH = 10;
    private const int VERSION_1_1_HEADER_LENGTH = 14;
    private const string CONTEXT = "the 'GPOS' table";

    private readonly ReadOnlyMemory<byte> _data;
    private readonly PairPositioning[] _lookups;

    private GposKerning(ReadOnlyMemory<byte> data, PairPositioning[] lookups) {
        _data = data;
        _lookups = lookups;
    }

    /// <summary>Parses the pair-positioning lookups of the face's <c>kern</c> feature.</summary>
    /// <param name="gpos">The complete <c>GPOS</c> table bytes.</param>
    /// <param name="glyphCount">The face's <c>numGlyphs</c>.</param>
    /// <returns>
    /// The pair-positioning lookup, or <see langword="null"/> when the table has no supported
    /// <c>kern</c> pair positioning or an unsupported major/minor version.
    /// </returns>
    /// <exception cref="FontFormatException">The <c>GPOS</c> structure or a pair lookup is malformed.</exception>
    internal static GposKerning? TryParse(ReadOnlyMemory<byte> gpos, int glyphCount) {
        SfntReader reader = new(gpos.Span, CONTEXT);
        if (reader.Length < BASE_HEADER_LENGTH) {
            throw new FontFormatException(
                $"the 'GPOS' table is {reader.Length} byte(s) long; the 10-byte header is required");
        }

        int major = reader.ReadUInt16(0);
        int minor = reader.ReadUInt16(2);
        if (major != 1 || minor > 1) {
            return null;
        }

        int headerLength = minor == 1 ? VERSION_1_1_HEADER_LENGTH : BASE_HEADER_LENGTH;
        reader.Require(0, headerLength);
        int scriptListOffset = reader.ReadUInt16(4);
        int featureListOffset = reader.ReadUInt16(6);
        int lookupListOffset = reader.ReadUInt16(8);

        RequireList(reader, lookupListOffset, headerLength, "lookup list");
        int lookupCount = reader.ReadUInt16(lookupListOffset);
        reader.Require(lookupListOffset + 2, (long)lookupCount * 2);

        RequireList(reader, featureListOffset, headerLength, "feature list");
        int featureCount = reader.ReadUInt16(featureListOffset);
        reader.Require(featureListOffset + 2, (long)featureCount * 6);

        RequireList(reader, scriptListOffset, headerLength, "script list");
        int scriptCount = reader.ReadUInt16(scriptListOffset);
        reader.Require(scriptListOffset + 2, (long)scriptCount * 6);

        int scriptOffset = SelectScript(reader, scriptListOffset, scriptCount);
        if (scriptOffset < 0) {
            return null;
        }

        int langSysOffset = scriptOffset + reader.ReadUInt16(scriptOffset);
        if (langSysOffset == scriptOffset || !reader.Fits(langSysOffset, 6)) {
            return null;
        }

        int featureIndexCount = reader.ReadUInt16(langSysOffset + 4);
        reader.Require(langSysOffset + 6, (long)featureIndexCount * 2);
        if (featureIndexCount == 0) {
            return null;
        }

        bool[] seenLookups = new bool[lookupCount];
        List<int>? kernLookups = null;
        for (int index = 0; index < featureIndexCount; index++) {
            int featureIndex = reader.ReadUInt16(langSysOffset + 6 + (2 * index));
            if (featureIndex >= featureCount) {
                throw new FontFormatException(
                    $"the 'GPOS' default language system references feature {featureIndex}; the feature list has {featureCount}");
            }

            int featureRecord = featureListOffset + 2 + (6 * featureIndex);
            if (FontTableTag.FromAscii(reader.Slice(featureRecord, 4)) != FontTableTag.Kern) {
                continue;
            }

            int featureOffset = featureListOffset + reader.ReadUInt16(featureRecord + 4);
            reader.Require(featureOffset, 4);
            int lookupIndexCount = reader.ReadUInt16(featureOffset + 2);
            reader.Require(featureOffset + 4, (long)lookupIndexCount * 2);
            for (int lookup = 0; lookup < lookupIndexCount; lookup++) {
                int lookupIndex = reader.ReadUInt16(featureOffset + 4 + (2 * lookup));
                if (lookupIndex >= lookupCount) {
                    throw new FontFormatException(
                        $"the 'GPOS' kern feature references lookup {lookupIndex}; the lookup list has {lookupCount}");
                }

                if (seenLookups[lookupIndex]) {
                    continue;
                }

                seenLookups[lookupIndex] = true;
                kernLookups ??= [];
                kernLookups.Add(lookupIndex);
            }
        }

        if (kernLookups is null) {
            return null;
        }

        List<PairPositioning> positionings = [];
        for (int index = 0; index < kernLookups.Count; index++) {
            AppendLookup(reader, lookupListOffset, kernLookups[index], glyphCount, positionings);
        }

        return positionings.Count == 0 ? null : new GposKerning(gpos, positionings.ToArray());
    }

    /// <summary>Looks up one kerned pair.</summary>
    /// <param name="leftGlyphId">The left glyph identifier.</param>
    /// <param name="rightGlyphId">The right glyph identifier.</param>
    /// <param name="value">The advance adjustment in font units, or zero.</param>
    /// <returns><see langword="true"/> when a pair-positioning lookup positions the pair.</returns>
    internal bool TryGet(int leftGlyphId, int rightGlyphId, out int value) {
        value = 0;
        SfntReader reader = new(_data.Span, CONTEXT);
        for (int index = 0; index < _lookups.Length; index++) {
            PairPositioning lookup = _lookups[index];
            if (lookup.XAdvanceOffset < 0) {
                continue;
            }

            if (!TryGetCoverageIndex(reader, lookup.CoverageOffset, leftGlyphId, out int coverageIndex)) {
                continue;
            }

            if (lookup.Format == PAIR_POS_FORMAT_1) {
                int pairSetOffset = lookup.SubtableOffset
                    + reader.ReadUInt16(lookup.SubtableOffset + 10 + (2 * coverageIndex));
                int recordLength = 2 + lookup.ValueSize1 + lookup.ValueSize2;
                int pairCount = reader.ReadUInt16(pairSetOffset);
                int low = 0;
                int high = pairCount - 1;
                while (low <= high) {
                    int middle = low + ((high - low) / 2);
                    int record = pairSetOffset + 2 + (middle * recordLength);
                    int secondGlyph = reader.ReadUInt16(record);
                    if (secondGlyph < rightGlyphId) {
                        low = middle + 1;
                    } else if (secondGlyph > rightGlyphId) {
                        high = middle - 1;
                    } else {
                        value = reader.ReadInt16(record + 2 + lookup.XAdvanceOffset);
                        return true;
                    }
                }

                continue;
            }

            int class1 = GetClass(reader, lookup.ClassDef1Offset, leftGlyphId);
            int class2 = GetClass(reader, lookup.ClassDef2Offset, rightGlyphId);
            int classRecordLength = lookup.ValueSize1 + lookup.ValueSize2;
            long recordOffset = lookup.SubtableOffset
                + 16L
                + ((((long)class1 * lookup.Class2Count) + class2) * classRecordLength);
            value = reader.ReadInt16((int)recordOffset + lookup.XAdvanceOffset);
            return true;
        }

        return false;
    }

    private static int SelectScript(SfntReader reader, int scriptListOffset, int scriptCount) {
        int selected = -1;
        int bestRank = 3;
        for (int index = 0; index < scriptCount; index++) {
            int record = scriptListOffset + 2 + (6 * index);
            FontTableTag tag = FontTableTag.FromAscii(reader.Slice(record, 4));
            int rank = tag == FontTableTag.Latn ? 0 : tag == FontTableTag.Dflt ? 1 : 2;
            if (rank >= bestRank) {
                continue;
            }

            int scriptOffset = scriptListOffset + reader.ReadUInt16(record + 4);
            if (!reader.Fits(scriptOffset, 4)) {
                throw new FontFormatException(
                    $"the 'GPOS' script '{tag}' at offset {scriptOffset} is outside the table");
            }

            selected = scriptOffset;
            bestRank = rank;
        }

        return selected;
    }

    private static void AppendLookup(
        SfntReader reader,
        int lookupListOffset,
        int lookupIndex,
        int glyphCount,
        List<PairPositioning> positionings) {
        int lookupOffset = lookupListOffset + reader.ReadUInt16(lookupListOffset + 2 + (2 * lookupIndex));
        reader.Require(lookupOffset, 6);
        int lookupType = reader.ReadUInt16(lookupOffset);
        int lookupFlag = reader.ReadUInt16(lookupOffset + 2);
        int subtableCount = reader.ReadUInt16(lookupOffset + 4);
        if (lookupType != LOOKUP_TYPE_PAIR_POSITIONING
            && lookupType != LOOKUP_TYPE_EXTENSION_POSITIONING) {
            return;
        }

        int subtablesOffset = lookupOffset + 6;
        long subtablesLength = (long)subtableCount * 2;
        if ((lookupFlag & USE_MARK_FILTERING_SET) != 0) {
            subtablesLength += 2;
        }

        reader.Require(subtablesOffset, subtablesLength);
        for (int index = 0; index < subtableCount; index++) {
            int subtableOffset = lookupOffset + reader.ReadUInt16(subtablesOffset + (2 * index));
            if (lookupType == LOOKUP_TYPE_EXTENSION_POSITIONING) {
                reader.Require(subtableOffset, 8);
                int extensionFormat = reader.ReadUInt16(subtableOffset);
                if (extensionFormat != EXTENSION_FORMAT) {
                    throw new FontFormatException(
                        $"the 'GPOS' extension positioning subtable {index} uses format {extensionFormat}; format 1 is required");
                }

                if (reader.ReadUInt16(subtableOffset + 2) != LOOKUP_TYPE_PAIR_POSITIONING) {
                    continue;
                }

                uint extensionOffset = reader.ReadUInt32(subtableOffset + 4);
                long extended = (long)subtableOffset + extensionOffset;
                if (extended > int.MaxValue) {
                    throw new FontFormatException(
                        $"the 'GPOS' extension positioning subtable {index} has an invalid offset {extensionOffset}");
                }

                subtableOffset = (int)extended;
            }

            positionings.Add(ParsePairPositioning(reader, subtableOffset, glyphCount));
        }
    }

    private static PairPositioning ParsePairPositioning(
        SfntReader reader,
        int subtableOffset,
        int glyphCount) {
        reader.Require(subtableOffset, 8);
        int format = reader.ReadUInt16(subtableOffset);
        if (format != PAIR_POS_FORMAT_1 && format != PAIR_POS_FORMAT_2) {
            throw new FontFormatException(
                $"the 'GPOS' pair positioning subtable at offset {subtableOffset} uses format {format}; formats 1 and 2 are supported");
        }

        int coverageOffset = subtableOffset + reader.ReadUInt16(subtableOffset + 2);
        int valueFormat1 = reader.ReadUInt16(subtableOffset + 4);
        int valueFormat2 = reader.ReadUInt16(subtableOffset + 6);
        ValidateValueFormat(valueFormat1);
        ValidateValueFormat(valueFormat2);
        int valueSize1 = ValueRecordSize(valueFormat1);
        int valueSize2 = ValueRecordSize(valueFormat2);
        int xAdvanceOffset = (valueFormat1 & X_ADVANCE) == 0
            ? -1
            : ((valueFormat1 & X_PLACEMENT) != 0 ? 2 : 0)
                + ((valueFormat1 & Y_PLACEMENT) != 0 ? 2 : 0);
        int coverageCount = ValidateCoverage(reader, coverageOffset, glyphCount);
        if (format == PAIR_POS_FORMAT_1) {
            reader.Require(subtableOffset + 8, 2);
            int pairSetCount = reader.ReadUInt16(subtableOffset + 8);
            if (pairSetCount != coverageCount) {
                throw new FontFormatException(
                    $"the 'GPOS' pair positioning subtable at offset {subtableOffset} covers {coverageCount} glyph(s) but declares {pairSetCount} pair set(s)");
            }

            reader.Require(subtableOffset + 10, (long)pairSetCount * 2);
            int recordLength = 2 + valueSize1 + valueSize2;
            for (int index = 0; index < pairSetCount; index++) {
                int pairSetOffset = subtableOffset + reader.ReadUInt16(subtableOffset + 10 + (2 * index));
                reader.Require(pairSetOffset, 2);
                int pairCount = reader.ReadUInt16(pairSetOffset);
                reader.Require(pairSetOffset + 2, (long)pairCount * recordLength);
                int previous = -1;
                for (int pair = 0; pair < pairCount; pair++) {
                    int record = pairSetOffset + 2 + (pair * recordLength);
                    int secondGlyph = reader.ReadUInt16(record);
                    if (secondGlyph <= previous) {
                        throw new FontFormatException(
                            $"the 'GPOS' pair set at offset {pairSetOffset} is not sorted by second glyph");
                    }

                    if (secondGlyph >= glyphCount) {
                        throw new FontFormatException(
                            $"the 'GPOS' pair set at offset {pairSetOffset} references glyph {secondGlyph}; the face has {glyphCount} glyph(s)");
                    }

                    previous = secondGlyph;
                }
            }

            return new PairPositioning(
                format,
                subtableOffset,
                coverageOffset,
                valueSize1,
                valueSize2,
                xAdvanceOffset,
                0,
                0,
                0);
        }

        reader.Require(subtableOffset + 8, 8);
        int classDef1Offset = subtableOffset + reader.ReadUInt16(subtableOffset + 8);
        int classDef2Offset = subtableOffset + reader.ReadUInt16(subtableOffset + 10);
        int class1Count = reader.ReadUInt16(subtableOffset + 12);
        int class2Count = reader.ReadUInt16(subtableOffset + 14);
        if (class1Count == 0 || class2Count == 0) {
            throw new FontFormatException(
                $"the 'GPOS' class pair subtable at offset {subtableOffset} declares {class1Count} first classes and {class2Count} second classes");
        }

        int classRecordLength = valueSize1 + valueSize2;
        reader.Require(subtableOffset + 16, (long)class1Count * class2Count * classRecordLength);
        ValidateClassDef(reader, classDef1Offset, class1Count, glyphCount, "first");
        ValidateClassDef(reader, classDef2Offset, class2Count, glyphCount, "second");
        return new PairPositioning(
            format,
            subtableOffset,
            coverageOffset,
            valueSize1,
            valueSize2,
            xAdvanceOffset,
            classDef1Offset,
            classDef2Offset,
            class2Count);
    }

    private static int ValidateCoverage(SfntReader reader, int coverageOffset, int glyphCount) {
        reader.Require(coverageOffset, 4);
        int format = reader.ReadUInt16(coverageOffset);
        if (format == COVERAGE_FORMAT_1) {
            int glyphCountInCoverage = reader.ReadUInt16(coverageOffset + 2);
            reader.Require(coverageOffset + 4, (long)glyphCountInCoverage * 2);
            int previous = -1;
            for (int index = 0; index < glyphCountInCoverage; index++) {
                int glyph = reader.ReadUInt16(coverageOffset + 4 + (2 * index));
                if (glyph <= previous) {
                    throw new FontFormatException(
                        $"the 'GPOS' coverage at offset {coverageOffset} is not sorted by glyph");
                }

                if (glyph >= glyphCount) {
                    throw new FontFormatException(
                        $"the 'GPOS' coverage at offset {coverageOffset} references glyph {glyph}; the face has {glyphCount} glyph(s)");
                }

                previous = glyph;
            }

            return glyphCountInCoverage;
        }

        if (format != COVERAGE_FORMAT_2) {
            throw new FontFormatException(
                $"the 'GPOS' coverage at offset {coverageOffset} uses format {format}; formats 1 and 2 are supported");
        }

        int rangeCount = reader.ReadUInt16(coverageOffset + 2);
        reader.Require(coverageOffset + 4, (long)rangeCount * 6);
        int previousEnd = -1;
        int coverageCount = 0;
        for (int index = 0; index < rangeCount; index++) {
            int range = coverageOffset + 4 + (6 * index);
            int start = reader.ReadUInt16(range);
            int end = reader.ReadUInt16(range + 2);
            int startCoverageIndex = reader.ReadUInt16(range + 4);
            if (end < start || start <= previousEnd) {
                throw new FontFormatException(
                    $"the 'GPOS' coverage at offset {coverageOffset} has overlapping or unsorted ranges");
            }

            if (end >= glyphCount) {
                throw new FontFormatException(
                    $"the 'GPOS' coverage at offset {coverageOffset} references glyph {end}; the face has {glyphCount} glyph(s)");
            }

            if (startCoverageIndex != coverageCount) {
                throw new FontFormatException(
                    $"the 'GPOS' coverage at offset {coverageOffset} has a non-contiguous coverage index {startCoverageIndex}");
            }

            coverageCount += end - start + 1;
            previousEnd = end;
        }

        return coverageCount;
    }

    private static void ValidateClassDef(
        SfntReader reader,
        int classDefOffset,
        int classCount,
        int glyphCount,
        string which) {
        reader.Require(classDefOffset, 4);
        int format = reader.ReadUInt16(classDefOffset);
        if (format == CLASS_DEF_FORMAT_1) {
            reader.Require(classDefOffset + 2, 4);
            int startGlyph = reader.ReadUInt16(classDefOffset + 2);
            int glyphCountInClassDef = reader.ReadUInt16(classDefOffset + 4);
            reader.Require(classDefOffset + 6, (long)glyphCountInClassDef * 2);
            if (startGlyph + glyphCountInClassDef > glyphCount) {
                throw new FontFormatException(
                    $"the 'GPOS' {which} class definition at offset {classDefOffset} covers glyphs past the face's {glyphCount}");
            }

            for (int index = 0; index < glyphCountInClassDef; index++) {
                if (reader.ReadUInt16(classDefOffset + 6 + (2 * index)) >= classCount) {
                    throw new FontFormatException(
                        $"the 'GPOS' {which} class definition at offset {classDefOffset} declares a class outside {classCount}");
                }
            }

            return;
        }

        if (format != CLASS_DEF_FORMAT_2) {
            throw new FontFormatException(
                $"the 'GPOS' {which} class definition at offset {classDefOffset} uses format {format}; formats 1 and 2 are supported");
        }

        int rangeCount = reader.ReadUInt16(classDefOffset + 2);
        reader.Require(classDefOffset + 4, (long)rangeCount * 6);
        int previousEnd = -1;
        for (int index = 0; index < rangeCount; index++) {
            int range = classDefOffset + 4 + (6 * index);
            int start = reader.ReadUInt16(range);
            int end = reader.ReadUInt16(range + 2);
            int classValue = reader.ReadUInt16(range + 4);
            if (end < start || start <= previousEnd) {
                throw new FontFormatException(
                    $"the 'GPOS' {which} class definition at offset {classDefOffset} has overlapping or unsorted ranges");
            }

            if (end >= glyphCount) {
                throw new FontFormatException(
                    $"the 'GPOS' {which} class definition at offset {classDefOffset} covers glyphs past the face's {glyphCount}");
            }

            if (classValue >= classCount) {
                throw new FontFormatException(
                    $"the 'GPOS' {which} class definition at offset {classDefOffset} declares a class outside {classCount}");
            }

            previousEnd = end;
        }
    }

    private static bool TryGetCoverageIndex(
        SfntReader reader,
        int coverageOffset,
        int glyphId,
        out int coverageIndex) {
        int format = reader.ReadUInt16(coverageOffset);
        if (format == COVERAGE_FORMAT_1) {
            int glyphCount = reader.ReadUInt16(coverageOffset + 2);
            int low = 0;
            int high = glyphCount - 1;
            while (low <= high) {
                int middle = low + ((high - low) / 2);
                int glyph = reader.ReadUInt16(coverageOffset + 4 + (2 * middle));
                if (glyph < glyphId) {
                    low = middle + 1;
                } else if (glyph > glyphId) {
                    high = middle - 1;
                } else {
                    coverageIndex = middle;
                    return true;
                }
            }
        } else {
            int rangeCount = reader.ReadUInt16(coverageOffset + 2);
            int low = 0;
            int high = rangeCount - 1;
            while (low <= high) {
                int middle = low + ((high - low) / 2);
                int range = coverageOffset + 4 + (6 * middle);
                int start = reader.ReadUInt16(range);
                int end = reader.ReadUInt16(range + 2);
                if (glyphId < start) {
                    high = middle - 1;
                } else if (glyphId > end) {
                    low = middle + 1;
                } else {
                    coverageIndex = reader.ReadUInt16(range + 4) + (glyphId - start);
                    return true;
                }
            }
        }

        coverageIndex = 0;
        return false;
    }

    private static int GetClass(SfntReader reader, int classDefOffset, int glyphId) {
        int format = reader.ReadUInt16(classDefOffset);
        if (format == CLASS_DEF_FORMAT_1) {
            int startGlyph = reader.ReadUInt16(classDefOffset + 2);
            int glyphCount = reader.ReadUInt16(classDefOffset + 4);
            int index = glyphId - startGlyph;
            return (uint)index < (uint)glyphCount
                ? reader.ReadUInt16(classDefOffset + 6 + (2 * index))
                : 0;
        }

        int rangeCount = reader.ReadUInt16(classDefOffset + 2);
        int low = 0;
        int high = rangeCount - 1;
        while (low <= high) {
            int middle = low + ((high - low) / 2);
            int range = classDefOffset + 4 + (6 * middle);
            int start = reader.ReadUInt16(range);
            int end = reader.ReadUInt16(range + 2);
            if (glyphId < start) {
                high = middle - 1;
            } else if (glyphId > end) {
                low = middle + 1;
            } else {
                return reader.ReadUInt16(range + 4);
            }
        }

        return 0;
    }

    private static void ValidateValueFormat(int valueFormat) {
        if ((valueFormat & ~VALUE_FORMAT_MASK) != 0) {
            throw new FontFormatException(
                $"the 'GPOS' value format 0x{valueFormat:X4} uses unsupported bits; only placement, advance, and device offsets are defined");
        }
    }

    private static int ValueRecordSize(int valueFormat) => BitOperations.PopCount((uint)valueFormat) * 2;

    private static void RequireList(
        SfntReader reader,
        int offset,
        int headerLength,
        string name) {
        if (offset < headerLength) {
            throw new FontFormatException(
                $"the 'GPOS' {name} offset {offset} overlaps the {headerLength}-byte header");
        }

        if (!reader.Fits(offset, 2)) {
            throw new FontFormatException(
                $"the 'GPOS' {name} offset {offset} is outside the {reader.Length}-byte table");
        }
    }

    private readonly struct PairPositioning {
        internal PairPositioning(
            int format,
            int subtableOffset,
            int coverageOffset,
            int valueSize1,
            int valueSize2,
            int xAdvanceOffset,
            int classDef1Offset,
            int classDef2Offset,
            int class2Count) {
            Format = format;
            SubtableOffset = subtableOffset;
            CoverageOffset = coverageOffset;
            ValueSize1 = valueSize1;
            ValueSize2 = valueSize2;
            XAdvanceOffset = xAdvanceOffset;
            ClassDef1Offset = classDef1Offset;
            ClassDef2Offset = classDef2Offset;
            Class2Count = class2Count;
        }

        internal int Format { get; }

        internal int SubtableOffset { get; }

        internal int CoverageOffset { get; }

        internal int ValueSize1 { get; }

        internal int ValueSize2 { get; }

        internal int XAdvanceOffset { get; }

        internal int ClassDef1Offset { get; }

        internal int ClassDef2Offset { get; }

        internal int Class2Count { get; }
    }
}
