using System.Text;

namespace RayBork.Internal;

/// <summary>
/// A parsed sfnt (TTF or OTF) face: its table directory, metrics, Unicode cmap, TrueType outlines,
/// and kerning data.
/// </summary>
/// <remarks>
/// <para>
/// The instance retains the packed face bytes for its lifetime because glyphs, kerning values, and
/// table spans are read on demand; the <c>loca</c>/<c>glyf</c> tables, cmap, and kerning lookups are
/// parsed once and never copied. A face is immutable after <see cref="Parse"/> and safe to read from
/// any thread without locks. Every read that returns a value is allocation-free except
/// <see cref="GetGlyph"/>, which allocates the resolved contour and segment arrays.
/// </para>
/// <para>
/// The container accepts the <c>0x00010000</c> and <c>true</c> TrueType magic and the <c>OTTO</c>
/// CFF magic; a TrueType Collection (<c>ttcf</c>) is rejected because one pack entry stores one face.
/// A TrueType face must carry <c>head</c>, <c>maxp</c>, <c>hhea</c>, <c>hmtx</c>, <c>cmap</c>,
/// <c>loca</c>, and <c>glyf</c>. A CFF face carries <c>CFF </c> or <c>CFF2</c> instead of
/// <c>glyf</c>/<c>loca</c>; it parses structurally and exposes the CFF table span, but
/// <see cref="GetGlyph"/> resolves only TrueType outlines. The 16-bit <c>numGlyphs</c> field caps a
/// face at 65,535 glyphs.
/// </para>
/// <para>
/// The container, the required tables, the selected cmap subtable, and the optional kerning tables are
/// validated by <see cref="Parse"/>. Glyph data is resolved and validated by <see cref="GetGlyph"/> on
/// demand, so a face with a corrupt unused glyph still parses; resolving that glyph throws the same
/// <see cref="FontFormatException"/>, including composite depth or reference-cycle failures.
/// </para>
/// <para>
/// Every structural failure, including truncation, an out-of-bounds table record, a bad metric, or a
/// malformed glyph, cmap, kern, or GPOS structure, throws <see cref="FontFormatException"/> with a
/// precise reason. Framework exceptions are never thrown for corrupt input.
/// </para>
/// </remarks>
internal sealed class SfntFace {
    /// <summary>The <c>.notdef</c> glyph identifier.</summary>
    internal const int NOTDEF_GLYPH_ID = 0;

    /// <summary>The minimum supported <c>unitsPerEm</c>.</summary>
    internal const int MIN_UNITS_PER_EM = 16;

    /// <summary>The maximum supported <c>unitsPerEm</c>.</summary>
    internal const int MAX_UNITS_PER_EM = 16384;

    private const uint TRUETYPE_MAGIC = 0x0001_0000;
    private const uint TRUE_MAGIC = 0x7472_7565;
    private const uint OTTO_MAGIC = 0x4F54_544F;
    private const uint TTC_MAGIC = 0x7474_6366;
    private const int OFFSET_TABLE_LENGTH = 12;
    private const int TABLE_RECORD_LENGTH = 16;
    private const uint HEAD_VERSION = 0x0001_0000;
    private const int HEAD_LENGTH = 54;
    private const int UNITS_PER_EM_OFFSET = 18;
    private const int INDEX_TO_LOC_FORMAT_OFFSET = 50;
    private const uint MAXP_VERSION_0_5 = 0x0000_5000;
    private const uint MAXP_VERSION_1_0 = 0x0001_0000;
    private const int MAXP_LENGTH = 6;
    private const int MAXP_VERSION_1_0_LENGTH = 32;
    private const int NUM_GLYPHS_OFFSET = 4;
    private const int HHEA_LENGTH = 36;
    private const int ASCENDER_OFFSET = 4;
    private const int DESCENDER_OFFSET = 6;
    private const int LINE_GAP_OFFSET = 8;
    private const int NUMBER_OF_HMETRICS_OFFSET = 34;
    private const int LONG_METRIC_LENGTH = 4;
    private const int TRAILING_BEARING_LENGTH = 2;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly ReadOnlyMemory<FontTable> _tables;
    private readonly ReadOnlyMemory<byte> _hmtx;
    private readonly ReadOnlyMemory<byte> _cff;
    private readonly ReadOnlyMemory<byte> _cff2;
    private readonly Metrics _metrics;
    private readonly SfntOutlineKind _outlineKind;
    private readonly CmapTable _cmap;
    private readonly GlyphOutlineReader? _outlines;
    private readonly KerningReader _kerning;

    private SfntFace(
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<FontTable> tables,
        ReadOnlyMemory<byte> hmtx,
        Metrics metrics,
        SfntOutlineKind outlineKind,
        ReadOnlyMemory<byte> cff,
        ReadOnlyMemory<byte> cff2,
        CmapTable cmap,
        GlyphOutlineReader? outlines,
        KerningReader kerning) {
        _data = data;
        _tables = tables;
        _hmtx = hmtx;
        _metrics = metrics;
        _outlineKind = outlineKind;
        _cff = cff;
        _cff2 = cff2;
        _cmap = cmap;
        _outlines = outlines;
        _kerning = kerning;
    }

    /// <summary>Gets the retained face bytes.</summary>
    internal ReadOnlyMemory<byte> Data => _data;

    /// <summary>Gets the validated table directory in file order; use <c>Tables.Span</c> to iterate.</summary>
    /// <remarks>
    /// A corrupt face that repeats a tag keeps the first record; the sfnt specification requires
    /// unique tags, so this only affects malformed input.
    /// </remarks>
    internal ReadOnlyMemory<FontTable> Tables => _tables;

    /// <summary>Gets the outline source of the face's glyphs.</summary>
    internal SfntOutlineKind OutlineKind => _outlineKind;

    /// <summary>Gets the <c>CFF </c> table bytes, or empty when the face has no CFF table.</summary>
    internal ReadOnlyMemory<byte> CffTable => _cff;

    /// <summary>Gets the <c>CFF2</c> table bytes, or empty when the face has no CFF2 table.</summary>
    internal ReadOnlyMemory<byte> Cff2Table => _cff2;

    /// <summary>Gets the <c>head</c> <c>unitsPerEm</c> value.</summary>
    internal int UnitsPerEm => _metrics.UnitsPerEm;

    /// <summary>Gets the <c>hhea</c> ascender in font units.</summary>
    internal int Ascender => _metrics.Ascender;

    /// <summary>Gets the <c>hhea</c> descender in font units.</summary>
    internal int Descender => _metrics.Descender;

    /// <summary>Gets the <c>hhea</c> line gap in font units.</summary>
    internal int LineGap => _metrics.LineGap;

    /// <summary>Gets the <c>maxp</c> <c>numGlyphs</c> value.</summary>
    internal int GlyphCount => _metrics.GlyphCount;

    /// <summary>Gets the <c>hhea</c> <c>numberOfHMetrics</c> value.</summary>
    internal int NumberOfHMetrics => _metrics.NumberOfHMetrics;

    /// <summary>Gets the number of codepoints that the selected cmap subtable maps to a real glyph.</summary>
    internal int MappedGlyphCount => _cmap.MappedGlyphCount;

    /// <summary>Gets the format of the selected cmap subtable: 0, 4, 6, or 12.</summary>
    internal int CmapFormat => _cmap.Format;

    /// <summary>Parses and validates one packed sfnt face.</summary>
    /// <param name="data">The complete face bytes; they are retained for the face's lifetime.</param>
    /// <returns>The parsed face.</returns>
    /// <exception cref="FontFormatException">
    /// The container, a required table, the selected cmap subtable, the <c>loca</c> offsets, or a
    /// kerning table is malformed or unsupported.
    /// </exception>
    internal static SfntFace Parse(ReadOnlyMemory<byte> data) {
        SfntReader face = new(data.Span, "the face");
        if (face.Length < OFFSET_TABLE_LENGTH) {
            throw new FontFormatException(
                $"the face is {face.Length} byte(s) long; the 12-byte sfnt offset table is required");
        }

        uint magic = face.ReadUInt32(0);
        bool trueType = magic is TRUETYPE_MAGIC or TRUE_MAGIC;
        if (!trueType && magic != OTTO_MAGIC) {
            if (magic == TTC_MAGIC) {
                throw new FontFormatException(
                    "the face is a TrueType Collection (ttcf); a single sfnt face is required");
            }

            throw new FontFormatException(
                $"the face magic 0x{magic:X8} is not a supported TrueType (0x00010000 or true) or OpenType (OTTO) face");
        }

        int tableCount = face.ReadUInt16(4);
        long directoryLength = OFFSET_TABLE_LENGTH + ((long)tableCount * TABLE_RECORD_LENGTH);
        if (directoryLength > face.Length) {
            throw new FontFormatException(
                $"the {tableCount}-entry table directory extends past the {face.Length}-byte face");
        }

        FontTable[] tables = new FontTable[tableCount];
        for (int index = 0; index < tableCount; index++) {
            int recordOffset = OFFSET_TABLE_LENGTH + (index * TABLE_RECORD_LENGTH);
            FontTableTag tag = FontTableTag.FromAscii(face.Slice(recordOffset, 4));
            uint offset = face.ReadUInt32(recordOffset + 8);
            uint length = face.ReadUInt32(recordOffset + 12);
            if ((ulong)offset + length > (ulong)face.Length) {
                throw new FontFormatException(
                    $"the '{tag}' table at offset {offset} with length {length} extends past the {face.Length}-byte face");
            }

            tables[index] = new FontTable(tag, (int)offset, data.Slice((int)offset, (int)length));
        }

        ReadOnlySpan<FontTable> directory = tables;
        FontTable head = RequireTable(directory, FontTableTag.Head);
        FontTable maxp = RequireTable(directory, FontTableTag.Maxp);
        FontTable hhea = RequireTable(directory, FontTableTag.Hhea);
        FontTable hmtx = RequireTable(directory, FontTableTag.Hmtx);
        FontTable cmap = RequireTable(directory, FontTableTag.Cmap);

        Metrics metrics = ParseMetrics(head, maxp, hhea, hmtx);

        SfntOutlineKind outlineKind;
        ReadOnlyMemory<byte> cff = ReadOnlyMemory<byte>.Empty;
        ReadOnlyMemory<byte> cff2 = ReadOnlyMemory<byte>.Empty;
        GlyphOutlineReader? outlines = null;
        if (trueType) {
            FontTable loca = RequireTable(directory, FontTableTag.Loca);
            FontTable glyf = RequireTable(directory, FontTableTag.Glyf);
            outlineKind = SfntOutlineKind.TrueType;
            outlines = new GlyphOutlineReader(
                glyf.Data,
                loca.Data,
                metrics.GlyphCount,
                metrics.LongLoca);
        } else {
            FontTable? cffTable = FindTable(directory, FontTableTag.Cff);
            FontTable? cff2Table = FindTable(directory, FontTableTag.Cff2);
            if (cffTable is null && cff2Table is null) {
                throw new FontFormatException(
                    "the CFF face is missing the required 'CFF ' or 'CFF2' table");
            }

            outlineKind = cffTable is not null ? SfntOutlineKind.Cff : SfntOutlineKind.Cff2;
            cff = cffTable?.Data ?? ReadOnlyMemory<byte>.Empty;
            cff2 = cff2Table?.Data ?? ReadOnlyMemory<byte>.Empty;
        }

        CmapTable cmapTable = CmapTable.Parse(cmap.Data, metrics.GlyphCount);
        ReadOnlyMemory<byte> kern = FindTable(directory, FontTableTag.Kern)?.Data
            ?? ReadOnlyMemory<byte>.Empty;
        ReadOnlyMemory<byte> gpos = FindTable(directory, FontTableTag.Gpos)?.Data
            ?? ReadOnlyMemory<byte>.Empty;
        KerningReader kerning = KerningReader.Parse(kern, gpos, metrics.GlyphCount);

        return new SfntFace(
            data,
            tables,
            hmtx.Data,
            metrics,
            outlineKind,
            cff,
            cff2,
            cmapTable,
            outlines,
            kerning);
    }

    /// <summary>Finds one table record by tag.</summary>
    /// <param name="tag">The table tag.</param>
    /// <param name="table">The table record when it is present.</param>
    /// <returns><see langword="true"/> when the face carries the table.</returns>
    internal bool TryGetTable(FontTableTag tag, out FontTable table) {
        FontTable? found = FindTable(_tables.Span, tag);
        if (found is null) {
            table = default;
            return false;
        }

        table = found.Value;
        return true;
    }

    /// <summary>Gets a table's bytes, or empty when the face does not carry the table.</summary>
    /// <param name="tag">The table tag.</param>
    /// <returns>The table bytes.</returns>
    internal ReadOnlyMemory<byte> GetTableData(FontTableTag tag) =>
        FindTable(_tables.Span, tag)?.Data ?? ReadOnlyMemory<byte>.Empty;

    /// <summary>Resolves a Unicode scalar value to a glyph identifier.</summary>
    /// <param name="rune">The Unicode scalar value.</param>
    /// <param name="glyphId">The glyph identifier, or zero when the codepoint is unmapped.</param>
    /// <returns><see langword="true"/> when the codepoint maps to a real glyph.</returns>
    internal bool TryGetGlyphId(Rune rune, out int glyphId) => _cmap.TryGetGlyphId(rune, out glyphId);

    /// <summary>Gets one glyph's advance width.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>The advance width in font units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    internal int GetAdvanceWidth(int glyphId) {
        ThrowIfGlyphOutOfRange(glyphId, nameof(glyphId));
        SfntReader hmtx = new(_hmtx.Span, "the 'hmtx' table");
        int metric = glyphId < _metrics.NumberOfHMetrics
            ? glyphId
            : _metrics.NumberOfHMetrics - 1;
        return hmtx.ReadUInt16(metric * LONG_METRIC_LENGTH);
    }

    /// <summary>Gets one glyph's left side bearing.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>The left side bearing in font units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    internal int GetLeftSideBearing(int glyphId) {
        ThrowIfGlyphOutOfRange(glyphId, nameof(glyphId));
        SfntReader hmtx = new(_hmtx.Span, "the 'hmtx' table");
        if (glyphId < _metrics.NumberOfHMetrics) {
            return hmtx.ReadInt16((glyphId * LONG_METRIC_LENGTH) + TRAILING_BEARING_LENGTH);
        }

        int trailing = glyphId - _metrics.NumberOfHMetrics;
        return hmtx.ReadInt16(
            (_metrics.NumberOfHMetrics * LONG_METRIC_LENGTH)
                + (trailing * TRAILING_BEARING_LENGTH));
    }

    /// <summary>Resolves one glyph's stored bounds and font-unit outline.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>The resolved glyph in font units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="NotSupportedException">The face stores CFF or CFF2 outlines.</exception>
    /// <exception cref="FontFormatException">
    /// The glyph data is malformed, nests composites too deeply, has a reference cycle, or exceeds the
    /// point limit.
    /// </exception>
    internal FontGlyph GetGlyph(int glyphId) {
        ThrowIfGlyphOutOfRange(glyphId, nameof(glyphId));
        if (_outlines is null) {
            throw new NotSupportedException(
                "This face stores CFF outlines; only TrueType 'glyf' outlines are resolved.");
        }

        return _outlines.ReadGlyph(glyphId);
    }

    /// <summary>Gets the kerning adjustment of one horizontal pair.</summary>
    /// <param name="leftGlyphId">The left glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <param name="rightGlyphId">The right glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>The adjustment in font units, or zero when the pair is not kerned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A glyph identifier is outside the face.</exception>
    internal int GetKerning(int leftGlyphId, int rightGlyphId) {
        ThrowIfGlyphOutOfRange(leftGlyphId, nameof(leftGlyphId));
        ThrowIfGlyphOutOfRange(rightGlyphId, nameof(rightGlyphId));
        return _kerning.GetKerning(leftGlyphId, rightGlyphId);
    }

    private static Metrics ParseMetrics(
        FontTable head,
        FontTable maxp,
        FontTable hhea,
        FontTable hmtx) {
        SfntReader headReader = new(head.Data.Span, "the 'head' table");
        if (headReader.Length < HEAD_LENGTH) {
            throw new FontFormatException(
                $"the 'head' table is {headReader.Length} byte(s) long; {HEAD_LENGTH} are required");
        }

        uint headVersion = headReader.ReadUInt32(0);
        if (headVersion != HEAD_VERSION) {
            throw new FontFormatException(
                $"the 'head' table version is 0x{headVersion:X8}; version 1.0 is required");
        }

        int unitsPerEm = headReader.ReadUInt16(UNITS_PER_EM_OFFSET);
        if (unitsPerEm < MIN_UNITS_PER_EM || unitsPerEm > MAX_UNITS_PER_EM) {
            throw new FontFormatException(
                $"the 'head' table declares unitsPerEm {unitsPerEm}; the supported range is {MIN_UNITS_PER_EM} to {MAX_UNITS_PER_EM}");
        }

        short indexToLocFormat = headReader.ReadInt16(INDEX_TO_LOC_FORMAT_OFFSET);
        if (indexToLocFormat is not 0 and not 1) {
            throw new FontFormatException(
                $"the 'head' table declares indexToLocFormat {indexToLocFormat}; 0 and 1 are supported");
        }

        SfntReader maxpReader = new(maxp.Data.Span, "the 'maxp' table");
        if (maxpReader.Length < MAXP_LENGTH) {
            throw new FontFormatException(
                $"the 'maxp' table is {maxpReader.Length} byte(s) long; {MAXP_LENGTH} are required");
        }

        uint maxpVersion = maxpReader.ReadUInt32(0);
        if (maxpVersion is not MAXP_VERSION_0_5 and not MAXP_VERSION_1_0) {
            throw new FontFormatException(
                $"the 'maxp' table version is 0x{maxpVersion:X8}; versions 0.5 and 1.0 are supported");
        }

        if (maxpVersion == MAXP_VERSION_1_0 && maxpReader.Length < MAXP_VERSION_1_0_LENGTH) {
            throw new FontFormatException(
                $"the 'maxp' version 1.0 table is {maxpReader.Length} byte(s) long; {MAXP_VERSION_1_0_LENGTH} are required");
        }

        int glyphCount = maxpReader.ReadUInt16(NUM_GLYPHS_OFFSET);
        if (glyphCount == 0) {
            throw new FontFormatException("the 'maxp' table declares zero glyphs");
        }

        SfntReader hheaReader = new(hhea.Data.Span, "the 'hhea' table");
        if (hheaReader.Length < HHEA_LENGTH) {
            throw new FontFormatException(
                $"the 'hhea' table is {hheaReader.Length} byte(s) long; {HHEA_LENGTH} are required");
        }

        int numberOfHMetrics = hheaReader.ReadUInt16(NUMBER_OF_HMETRICS_OFFSET);
        if (numberOfHMetrics == 0) {
            throw new FontFormatException("the 'hhea' table declares zero long horizontal metrics");
        }

        if (numberOfHMetrics > glyphCount) {
            throw new FontFormatException(
                $"the 'hhea' table declares {numberOfHMetrics} long horizontal metrics but 'maxp' declares {glyphCount} glyph(s)");
        }

        long required = (LONG_METRIC_LENGTH * (long)numberOfHMetrics)
            + (TRAILING_BEARING_LENGTH * (long)(glyphCount - numberOfHMetrics));
        if (hmtx.Length < required) {
            throw new FontFormatException(
                $"the 'hmtx' table is {hmtx.Length} byte(s); {glyphCount} glyph(s) with {numberOfHMetrics} long metric(s) need {required}");
        }

        return new Metrics(
            unitsPerEm,
            hheaReader.ReadInt16(ASCENDER_OFFSET),
            hheaReader.ReadInt16(DESCENDER_OFFSET),
            hheaReader.ReadInt16(LINE_GAP_OFFSET),
            glyphCount,
            numberOfHMetrics,
            indexToLocFormat == 1);
    }

    private static FontTable RequireTable(ReadOnlySpan<FontTable> tables, FontTableTag tag) {
        FontTable? table = FindTable(tables, tag);
        if (table is null) {
            throw new FontFormatException($"the face is missing the required '{tag}' table");
        }

        return table.Value;
    }

    private static FontTable? FindTable(ReadOnlySpan<FontTable> tables, FontTableTag tag) {
        for (int index = 0; index < tables.Length; index++) {
            if (tables[index].Tag == tag) {
                return tables[index];
            }
        }

        return null;
    }

    private void ThrowIfGlyphOutOfRange(int glyphId, string parameterName) {
        if ((uint)glyphId < (uint)_metrics.GlyphCount) {
            return;
        }

        throw new ArgumentOutOfRangeException(
            parameterName,
            glyphId,
            $"The face has {_metrics.GlyphCount} glyph(s).");
    }

    private readonly struct Metrics {
        internal Metrics(
            int unitsPerEm,
            int ascender,
            int descender,
            int lineGap,
            int glyphCount,
            int numberOfHMetrics,
            bool longLoca) {
            UnitsPerEm = unitsPerEm;
            Ascender = ascender;
            Descender = descender;
            LineGap = lineGap;
            GlyphCount = glyphCount;
            NumberOfHMetrics = numberOfHMetrics;
            LongLoca = longLoca;
        }

        internal int UnitsPerEm { get; }

        internal int Ascender { get; }

        internal int Descender { get; }

        internal int LineGap { get; }

        internal int GlyphCount { get; }

        internal int NumberOfHMetrics { get; }

        internal bool LongLoca { get; }
    }
}
