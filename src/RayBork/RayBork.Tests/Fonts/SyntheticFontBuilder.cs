using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>The container and outline kind of a synthetic face.</summary>
internal enum SyntheticOutline {
    TrueType,
    Cff,
    Cff2,
}

/// <summary>One table directory record of a built synthetic face.</summary>
internal readonly struct SyntheticTableRecord {
    internal SyntheticTableRecord(int recordOffset, int offset, int length) {
        RecordOffset = recordOffset;
        Offset = offset;
        Length = length;
    }

    internal int RecordOffset { get; }

    internal int Offset { get; }

    internal int Length { get; }
}

/// <summary>
/// Builds deterministic, structurally valid sfnt faces for the font parser tests.
/// </summary>
/// <remarks>
/// The builder writes the offset table and a sorted table directory, the required
/// <c>head</c>/<c>maxp</c>/<c>hhea</c>/<c>hmtx</c>/<c>cmap</c> tables, <c>loca</c>/<c>glyf</c> for
/// TrueType faces, and optional <c>kern</c> and <c>GPOS</c> tables. Checksums are zero because the
/// parser does not validate them. Tests mutate the returned bytes through
/// <see cref="FindTable"/> for the corrupt-input cases.
/// </remarks>
internal sealed class SyntheticFontBuilder {
    private const int HEAD_LENGTH = 54;
    private const int HHEA_LENGTH = 36;
    private const int MAXP_VERSION_1_0_LENGTH = 32;
    private const int TABLE_RECORD_LENGTH = 16;
    private const int TABLE_ALIGNMENT = 4;
    private const int SIMPLE_HEADER_LENGTH = 10;

    private readonly List<SyntheticGlyph> _glyphs = [];
    private readonly List<SyntheticCmapRecord> _cmapRecords = [];
    private readonly List<SyntheticKernPair> _kernPairs = [];
    private readonly HashSet<string> _omittedTables = [];
    private int _unitsPerEm = 1000;
    private int _ascender = 800;
    private int _descender = -200;
    private int _lineGap;
    private byte[]? _rawCmap;
    private byte[]? _rawKern;
    private byte[]? _rawGpos;
    private SyntheticGpos? _gpos;
    private SyntheticOutline _outline = SyntheticOutline.TrueType;
    private byte[] _cffData = [];
    private byte[] _cff2Data = [];
    private int _numberOfHMetrics = -1;
    private int _locaFormat;
    private int? _headIndexToLocFormat;

    private SyntheticFontBuilder() {
    }

    internal int GlyphCount => _glyphs.Count;

    /// <summary>Creates a builder whose glyph 0 is a small <c>.notdef</c> triangle.</summary>
    internal static SyntheticFontBuilder Create() {
        var builder = new SyntheticFontBuilder();
        builder.AddSimple(
            500,
            0,
            new SyntheticPoint(0, 0),
            new SyntheticPoint(300, 0),
            new SyntheticPoint(150, 400));
        return builder;
    }

    /// <summary>Adds a single-contour simple glyph.</summary>
    internal int AddSimple(int advanceWidth, int leftSideBearing, params SyntheticPoint[] points) =>
        Add(SyntheticGlyph.Simple(advanceWidth, leftSideBearing, [points]));

    /// <summary>Adds a multi-contour simple glyph.</summary>
    internal int AddSimpleContours(
        int advanceWidth,
        int leftSideBearing,
        params SyntheticPoint[][] contours) =>
        Add(SyntheticGlyph.Simple(advanceWidth, leftSideBearing, contours));

    /// <summary>Adds a glyph that occupies no bytes in <c>glyf</c>.</summary>
    internal int AddEmpty(int advanceWidth) => Add(SyntheticGlyph.Empty(advanceWidth));

    /// <summary>Adds a composite glyph whose header bounds are zero.</summary>
    internal int AddComposite(
        int advanceWidth,
        int leftSideBearing,
        params SyntheticComponent[] components) =>
        Add(SyntheticGlyph.Composite(advanceWidth, leftSideBearing, null, components));

    /// <summary>Adds a composite glyph with explicit header bounds.</summary>
    internal int AddComposite(
        int advanceWidth,
        int leftSideBearing,
        FontBounds bounds,
        params SyntheticComponent[] components) =>
        Add(SyntheticGlyph.Composite(advanceWidth, leftSideBearing, bounds, components));

    internal SyntheticFontBuilder WithUnitsPerEm(int unitsPerEm) {
        _unitsPerEm = unitsPerEm;
        return this;
    }

    internal SyntheticFontBuilder WithVerticalMetrics(int ascender, int descender, int lineGap) {
        _ascender = ascender;
        _descender = descender;
        _lineGap = lineGap;
        return this;
    }

    /// <summary>Overrides how many glyphs carry a full metric record in <c>hmtx</c>.</summary>
    internal SyntheticFontBuilder WithNumberOfHMetrics(int numberOfHMetrics) {
        _numberOfHMetrics = numberOfHMetrics;
        return this;
    }

    /// <summary>Selects the <c>loca</c> entry format: 0 for short, 1 for long.</summary>
    internal SyntheticFontBuilder WithLocaFormat(int indexToLocFormat) {
        _locaFormat = indexToLocFormat;
        return this;
    }

    /// <summary>Writes a <c>head</c> <c>indexToLocFormat</c> independently of the encoded <c>loca</c>.</summary>
    internal SyntheticFontBuilder WithHeadIndexToLocFormat(int indexToLocFormat) {
        _headIndexToLocFormat = indexToLocFormat;
        return this;
    }

    /// <summary>Adds a cmap encoding record with the default platform/encoding for the format.</summary>
    internal SyntheticFontBuilder WithCmap(
        int format,
        params (int Codepoint, int GlyphId)[] mappings) {
        (int platformId, int encodingId) = format switch {
            0 => (1, 0),
            12 => (3, 10),
            _ => (3, 1),
        };
        return WithCmapRecord(platformId, encodingId, format, mappings);
    }

    /// <summary>Adds a cmap encoding record.</summary>
    internal SyntheticFontBuilder WithCmapRecord(
        int platformId,
        int encodingId,
        int format,
        params (int Codepoint, int GlyphId)[] mappings) {
        _cmapRecords.Add(new SyntheticCmapRecord(platformId, encodingId, format, mappings));
        return this;
    }

    /// <summary>Uses raw cmap table bytes instead of the generated record list.</summary>
    internal SyntheticFontBuilder WithRawCmap(byte[] cmap) {
        _rawCmap = cmap;
        return this;
    }

    /// <summary>Adds a format 4 cmap whose segments address a glyph-id array.</summary>
    internal SyntheticFontBuilder WithCmapIndirectGlyphArray(
        params (int Codepoint, int GlyphId)[] mappings) {
        _cmapRecords.Add(new SyntheticCmapRecord(3, 1, 4, mappings, indirectGlyphArray: true));
        return this;
    }

    internal SyntheticFontBuilder WithKernPairs(params (int Left, int Right, int Value)[] pairs) {
        foreach ((int left, int right, int value) in pairs) {
            _kernPairs.Add(new SyntheticKernPair(left, right, value));
        }

        return this;
    }

    /// <summary>Uses raw <c>kern</c> table bytes instead of the generated format 0 table.</summary>
    internal SyntheticFontBuilder WithRawKern(byte[] kern) {
        _rawKern = kern;
        return this;
    }

    /// <summary>Uses raw <c>GPOS</c> table bytes instead of the generated table.</summary>
    internal SyntheticFontBuilder WithRawGpos(byte[] gpos) {
        _rawGpos = gpos;
        return this;
    }

    internal SyntheticFontBuilder WithGpos(SyntheticGpos gpos) {
        _gpos = gpos;
        return this;
    }

    internal SyntheticFontBuilder UseCff(byte[] tableData) {
        _outline = SyntheticOutline.Cff;
        _cffData = tableData;
        return this;
    }

    internal SyntheticFontBuilder UseCff2(byte[] tableData) {
        _outline = SyntheticOutline.Cff2;
        _cff2Data = tableData;
        return this;
    }

    /// <summary>Omits one table from the built face.</summary>
    internal SyntheticFontBuilder WithoutTable(string tag) {
        _omittedTables.Add(tag);
        return this;
    }

    /// <summary>Builds the complete face bytes.</summary>
    internal byte[] Build() {
        if (_glyphs.Count == 0) {
            throw new InvalidOperationException("A synthetic face needs at least one glyph.");
        }

        int glyphCount = _glyphs.Count;
        if (glyphCount > 0xFFFF) {
            throw new InvalidOperationException($"Synthetic glyph count {glyphCount} exceeds 65535.");
        }

        int hmtxGlyphCount = _glyphs.Count;
        int numberOfHMetrics = _numberOfHMetrics < 0 ? hmtxGlyphCount : _numberOfHMetrics;
        if (numberOfHMetrics < 1 || numberOfHMetrics > hmtxGlyphCount) {
            throw new InvalidOperationException(
                $"Synthetic numberOfHMetrics {numberOfHMetrics} is outside 1..{hmtxGlyphCount}.");
        }

        if (_locaFormat is not 0 and not 1) {
            throw new InvalidOperationException($"Synthetic loca format {_locaFormat} is not 0 or 1.");
        }

        if (_rawGpos is not null && _gpos is not null) {
            throw new InvalidOperationException("A synthetic face cannot use raw and generated GPOS bytes.");
        }

        if (_rawKern is null) {
            foreach (SyntheticKernPair pair in _kernPairs) {
                if (pair.Left < 0 || pair.Left >= glyphCount || pair.Right < 0 || pair.Right >= glyphCount) {
                    throw new InvalidOperationException(
                        $"Synthetic kern pair ({pair.Left}, {pair.Right}) is outside the face.");
                }
            }
        }

        List<(string Tag, byte[] Data)> tables = [];
        if (!_omittedTables.Contains("head")) {
            tables.Add(("head", BuildHead(_headIndexToLocFormat ?? _locaFormat)));
        }

        if (!_omittedTables.Contains("maxp")) {
            tables.Add(("maxp", BuildMaxp(glyphCount)));
        }

        if (!_omittedTables.Contains("hhea")) {
            tables.Add(("hhea", BuildHhea(numberOfHMetrics)));
        }

        if (!_omittedTables.Contains("hmtx")) {
            tables.Add(("hmtx", BuildHmtx(numberOfHMetrics)));
        }

        if (!_omittedTables.Contains("cmap")) {
            tables.Add(("cmap", BuildCmap()));
        }

        if (_outline == SyntheticOutline.TrueType) {
            (byte[] glyf, int[] locaOffsets) = BuildGlyf();
            if (!_omittedTables.Contains("loca")) {
                tables.Add(("loca", BuildLoca(locaOffsets)));
            }

            if (!_omittedTables.Contains("glyf")) {
                tables.Add(("glyf", glyf));
            }
        } else if (_outline == SyntheticOutline.Cff) {
            if (!_omittedTables.Contains("CFF ")) {
                tables.Add(("CFF ", _cffData));
            }
        } else if (!_omittedTables.Contains("CFF2")) {
            tables.Add(("CFF2", _cff2Data));
        }

        if (!_omittedTables.Contains("kern")) {
            if (_rawKern is not null) {
                tables.Add(("kern", _rawKern));
            } else if (_kernPairs.Count > 0) {
                tables.Add(("kern", BuildKern()));
            }
        }

        if (!_omittedTables.Contains("GPOS")) {
            if (_rawGpos is not null) {
                tables.Add(("GPOS", _rawGpos));
            } else if (_gpos is not null) {
                tables.Add(("GPOS", _gpos.Build(glyphCount)));
            }
        }

        uint magic = _outline == SyntheticOutline.TrueType ? 0x0001_0000u : 0x4F54_544Fu;
        return Assemble(magic, tables);
    }

    /// <summary>Finds one table directory record in a built or mutated face.</summary>
    internal static SyntheticTableRecord FindTable(ReadOnlySpan<byte> face, string tag) {
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(face[4..]);
        for (int index = 0; index < tableCount; index++) {
            int record = 12 + (index * TABLE_RECORD_LENGTH);
            string recordTag = Encoding.ASCII.GetString(face.Slice(record, 4));
            if (string.Equals(recordTag, tag, StringComparison.Ordinal)) {
                return new SyntheticTableRecord(
                    record,
                    (int)BinaryPrimitives.ReadUInt32BigEndian(face[(record + 8)..]),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(face[(record + 12)..]));
            }
        }

        throw new ArgumentException($"The synthetic face has no '{tag}' table.", nameof(tag));
    }

    private int Add(SyntheticGlyph glyph) {
        _glyphs.Add(glyph);
        return _glyphs.Count - 1;
    }

    private byte[] BuildHead(int indexToLocFormat) {
        byte[] head = new byte[HEAD_LENGTH];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x0001_0000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(4), 0x0001_0000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F_3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), (ushort)_unitsPerEm);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(46), 8);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(48), 2);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)indexToLocFormat);
        return head;
    }

    private byte[] BuildMaxp(int glyphCount) {
        int length = _outline == SyntheticOutline.TrueType ? MAXP_VERSION_1_0_LENGTH : 6;
        byte[] maxp = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(
            maxp,
            _outline == SyntheticOutline.TrueType ? 0x0001_0000u : 0x0000_5000u);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphCount);
        return maxp;
    }

    private byte[] BuildHhea(int numberOfHMetrics) {
        byte[] hhea = new byte[HHEA_LENGTH];
        BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x0001_0000);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(4), (short)_ascender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(6), (short)_descender);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(8), (short)_lineGap);
        BinaryPrimitives.WriteInt16BigEndian(hhea.AsSpan(18), 1);
        BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), (ushort)numberOfHMetrics);
        return hhea;
    }

    private byte[] BuildHmtx(int numberOfHMetrics) {
        int glyphCount = _glyphs.Count;
        byte[] hmtx = new byte[(4 * numberOfHMetrics) + (2 * (glyphCount - numberOfHMetrics))];
        for (int glyph = 0; glyph < numberOfHMetrics; glyph++) {
            BinaryPrimitives.WriteUInt16BigEndian(
                hmtx.AsSpan(4 * glyph),
                (ushort)_glyphs[glyph].AdvanceWidth);
            BinaryPrimitives.WriteInt16BigEndian(
                hmtx.AsSpan((4 * glyph) + 2),
                (short)_glyphs[glyph].LeftSideBearing);
        }

        for (int glyph = numberOfHMetrics; glyph < glyphCount; glyph++) {
            int offset = (4 * numberOfHMetrics) + (2 * (glyph - numberOfHMetrics));
            BinaryPrimitives.WriteInt16BigEndian(
                hmtx.AsSpan(offset),
                (short)_glyphs[glyph].LeftSideBearing);
        }

        return hmtx;
    }

    private byte[] BuildCmap() {
        if (_rawCmap is not null) {
            return _rawCmap;
        }

        if (_cmapRecords.Count == 0) {
            throw new InvalidOperationException(
                "A synthetic face needs at least one cmap record or raw cmap bytes.");
        }

        return SyntheticCmap.Build(_cmapRecords);
    }

    private (byte[] Glyf, int[] Offsets) BuildGlyf() {
        List<byte> glyf = [];
        int[] offsets = new int[_glyphs.Count + 1];
        for (int index = 0; index < _glyphs.Count; index++) {
            offsets[index] = glyf.Count;
            AppendGlyph(glyf, _glyphs[index]);
            while (glyf.Count % TABLE_ALIGNMENT != 0) {
                glyf.Add(0);
            }
        }

        offsets[^1] = glyf.Count;
        return (glyf.ToArray(), offsets);
    }

    private byte[] BuildLoca(int[] offsets) {
        int entryLength = _locaFormat == 0 ? 2 : 4;
        byte[] loca = new byte[offsets.Length * entryLength];
        for (int index = 0; index < offsets.Length; index++) {
            if (_locaFormat == 0) {
                if (offsets[index] % 2 != 0) {
                    throw new InvalidOperationException("Short loca entries need even glyf offsets.");
                }

                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(2 * index), (ushort)(offsets[index] / 2));
            } else {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4 * index), (uint)offsets[index]);
            }
        }

        return loca;
    }

    private byte[] BuildKern() {
        List<SyntheticKernPair> pairs = [.. _kernPairs];
        pairs.Sort(static (first, second) => {
            int byLeft = first.Left.CompareTo(second.Left);
            return byLeft != 0 ? byLeft : first.Right.CompareTo(second.Right);
        });

        int subtableLength = 14 + (6 * pairs.Count);
        byte[] kern = new byte[4 + subtableLength];
        BinaryPrimitives.WriteUInt16BigEndian(kern, 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(6), (ushort)subtableLength);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(8), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(10), (ushort)pairs.Count);
        int searchRange = 6;
        int entrySelector = 0;
        while ((searchRange * 2) <= (pairs.Count * 6)) {
            searchRange *= 2;
            entrySelector++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(12), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(14), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(
            kern.AsSpan(16),
            (ushort)((pairs.Count * 6) - searchRange));
        for (int index = 0; index < pairs.Count; index++) {
            int offset = 18 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(offset), (ushort)pairs[index].Left);
            BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(offset + 2), (ushort)pairs[index].Right);
            BinaryPrimitives.WriteInt16BigEndian(kern.AsSpan(offset + 4), (short)pairs[index].Value);
        }

        return kern;
    }

    private static void AppendGlyph(List<byte> glyf, SyntheticGlyph glyph) {
        switch (glyph.Kind) {
            case SyntheticGlyphKind.Empty:
                return;
            case SyntheticGlyphKind.Simple:
                AppendSimpleGlyph(glyf, glyph);
                return;
            default:
                AppendCompositeGlyph(glyf, glyph);
                return;
        }
    }

    private static void AppendSimpleGlyph(List<byte> glyf, SyntheticGlyph glyph) {
        int pointCount = 0;
        foreach (SyntheticPoint[] contour in glyph.Contours) {
            pointCount += contour.Length;
        }

        int xMin = int.MaxValue;
        int yMin = int.MaxValue;
        int xMax = int.MinValue;
        int yMax = int.MinValue;
        foreach (SyntheticPoint[] contour in glyph.Contours) {
            foreach (SyntheticPoint point in contour) {
                xMin = Math.Min(xMin, point.X);
                yMin = Math.Min(yMin, point.Y);
                xMax = Math.Max(xMax, point.X);
                yMax = Math.Max(yMax, point.Y);
            }
        }

        WriteInt16(glyf, glyph.Contours.Length);
        WriteInt16(glyf, xMin);
        WriteInt16(glyf, yMin);
        WriteInt16(glyf, xMax);
        WriteInt16(glyf, yMax);

        int lastPoint = -1;
        foreach (SyntheticPoint[] contour in glyph.Contours) {
            lastPoint += contour.Length;
            WriteUInt16(glyf, lastPoint);
        }

        WriteUInt16(glyf, 0);

        SyntheticPoint[] points = new SyntheticPoint[pointCount];
        int pointIndex = 0;
        foreach (SyntheticPoint[] contour in glyph.Contours) {
            foreach (SyntheticPoint point in contour) {
                points[pointIndex++] = point;
            }
        }

        int[] deltasX = new int[pointCount];
        int[] deltasY = new int[pointCount];
        byte[] flags = new byte[pointCount];
        int previousX = 0;
        int previousY = 0;
        for (int index = 0; index < pointCount; index++) {
            deltasX[index] = points[index].X - previousX;
            deltasY[index] = points[index].Y - previousY;
            previousX = points[index].X;
            previousY = points[index].Y;
            byte flag = points[index].OnCurve ? (byte)0x01 : (byte)0x00;
            flag |= DeltaFlag(deltasX[index], 0x02, 0x10);
            flag |= DeltaFlag(deltasY[index], 0x04, 0x20);
            flags[index] = flag;
        }

        WriteFlags(glyf, flags);
        foreach (int delta in deltasX) {
            WriteDelta(glyf, delta);
        }

        foreach (int delta in deltasY) {
            WriteDelta(glyf, delta);
        }
    }

    private static void AppendCompositeGlyph(List<byte> glyf, SyntheticGlyph glyph) {
        WriteInt16(glyf, -1);
        FontBounds bounds = glyph.Bounds ?? new FontBounds(0, 0, 0, 0);
        WriteInt16(glyf, bounds.XMin);
        WriteInt16(glyf, bounds.YMin);
        WriteInt16(glyf, bounds.XMax);
        WriteInt16(glyf, bounds.YMax);
        for (int index = 0; index < glyph.Components.Length; index++) {
            SyntheticComponent component = glyph.Components[index];
            int flags = 0;
            if (component.WordArguments) {
                flags |= 0x0001;
            }

            if (!component.PointMatched) {
                flags |= 0x0002;
            }

            flags |= component.Transform switch {
                SyntheticComponentTransform.Scale => 0x0008,
                SyntheticComponentTransform.XAndYScale => 0x0040,
                SyntheticComponentTransform.TwoByTwo => 0x0080,
                _ => 0,
            };
            if (component.ScaledOffset) {
                flags |= 0x0800;
            }

            if (index < glyph.Components.Length - 1) {
                flags |= 0x0020;
            }

            WriteUInt16(glyf, flags);
            WriteUInt16(glyf, component.GlyphId);
            if (component.WordArguments) {
                WriteInt16(glyf, component.OffsetX);
                WriteInt16(glyf, component.OffsetY);
            } else {
                glyf.Add(unchecked((byte)(sbyte)component.OffsetX));
                glyf.Add(unchecked((byte)(sbyte)component.OffsetY));
            }

            switch (component.Transform) {
                case SyntheticComponentTransform.Scale:
                    WriteF2Dot14(glyf, component.A);
                    break;
                case SyntheticComponentTransform.XAndYScale:
                    WriteF2Dot14(glyf, component.A);
                    WriteF2Dot14(glyf, component.D);
                    break;
                case SyntheticComponentTransform.TwoByTwo:
                    WriteF2Dot14(glyf, component.A);
                    WriteF2Dot14(glyf, component.B);
                    WriteF2Dot14(glyf, component.C);
                    WriteF2Dot14(glyf, component.D);
                    break;
                default:
                    break;
            }
        }
    }

    private static byte DeltaFlag(int delta, int shortFlag, int sameOrPositiveFlag) {
        if (delta == 0) {
            return (byte)sameOrPositiveFlag;
        }

        if (Math.Abs(delta) <= 0xFF) {
            return (byte)(shortFlag | (delta > 0 ? sameOrPositiveFlag : 0));
        }

        return 0;
    }

    private static void WriteDelta(List<byte> target, int delta) {
        if (delta == 0) {
            return;
        }

        if (Math.Abs(delta) <= 0xFF) {
            target.Add((byte)Math.Abs(delta));
            return;
        }

        WriteInt16(target, delta);
    }

    private static void WriteFlags(List<byte> target, byte[] flags) {
        int index = 0;
        while (index < flags.Length) {
            byte flag = flags[index];
            int run = 1;
            while (index + run < flags.Length && flags[index + run] == flag && run < 0x100) {
                run++;
            }

            if (run >= 2) {
                target.Add((byte)(flag | 0x08));
                target.Add((byte)(run - 1));
                index += run;
            } else {
                target.Add(flag);
                index++;
            }
        }
    }

    private static void WriteF2Dot14(List<byte> target, float value) {
        short fixedValue = (short)MathF.Round(value * 16384f);
        WriteInt16(target, fixedValue);
    }

    private static void WriteUInt16(List<byte> target, int value) {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void WriteInt16(List<byte> target, int value) {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static byte[] Assemble(uint magic, List<(string Tag, byte[] Data)> tables) {
        tables.Sort(static (first, second) => string.CompareOrdinal(first.Tag, second.Tag));
        int count = tables.Count;
        int offset = 12 + (TABLE_RECORD_LENGTH * count);
        int[] offsets = new int[count];
        for (int index = 0; index < count; index++) {
            offsets[index] = offset;
            offset += tables[index].Data.Length;
            offset = (offset + 3) & ~3;
        }

        byte[] face = new byte[offset];
        BinaryPrimitives.WriteUInt32BigEndian(face, magic);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(4), (ushort)count);
        int searchRange = TABLE_RECORD_LENGTH;
        int entrySelector = 0;
        while ((searchRange * 2) <= (count * TABLE_RECORD_LENGTH)) {
            searchRange *= 2;
            entrySelector++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(
            face.AsSpan(10),
            (ushort)((count * TABLE_RECORD_LENGTH) - searchRange));
        for (int index = 0; index < count; index++) {
            int record = 12 + (TABLE_RECORD_LENGTH * index);
            Encoding.ASCII.GetBytes(tables[index].Tag).CopyTo(face, record);
            BinaryPrimitives.WriteUInt32BigEndian(face.AsSpan(record + 8), (uint)offsets[index]);
            BinaryPrimitives.WriteUInt32BigEndian(
                face.AsSpan(record + 12),
                (uint)tables[index].Data.Length);
            tables[index].Data.CopyTo(face, offsets[index]);
        }

        return face;
    }

    private readonly struct SyntheticKernPair {
        internal SyntheticKernPair(int left, int right, int value) {
            Left = left;
            Right = right;
            Value = value;
        }

        internal int Left { get; }

        internal int Right { get; }

        internal int Value { get; }
    }
}
