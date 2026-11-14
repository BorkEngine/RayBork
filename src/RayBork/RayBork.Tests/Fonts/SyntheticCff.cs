using System.Globalization;
using System.Text;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Builds deterministic, structurally valid CFF 1.0 tables and whole OTF faces for the CFF tests.
/// </summary>
/// <remarks>
/// The builder writes the CFF header, Name INDEX, Top DICT INDEX, String INDEX, Global Subr INDEX,
/// CharStrings INDEX, the optional charset, the optional Private DICT and local subroutine INDEX, and
/// — for a CID-keyed face — the FDArray, per-FD private dictionaries, and FDSelect. All INDEXes use a
/// four-byte offset size so a block's length is known before its offsets are, and every DICT offset
/// operand is written as a five-byte longint for the same reason. Tests mutate the returned table
/// through <see cref="Build"/> or the whole face through <see cref="SyntheticFontBuilder.FindTable"/>
/// for corrupt-input cases; the offsets captured by <see cref="Build"/> locate the structures.
/// </remarks>
internal sealed class SyntheticCff {
    private const int CFF_MAJOR_VERSION = 1;
    private const int CFF_MINOR_VERSION = 0;
    private const int CFF_HEADER_SIZE = 4;
    private const int INDEX_OFFSET_SIZE = 4;
    private const int LONG_INT_OPERAND_LENGTH = 5;
    private const int MAX_INDEX_COUNT = 0xFFFF;

    private const int CHARSTRINGS_OPERATOR = 17;
    private const int CHARSET_OPERATOR = 15;
    private const int ENCODING_OPERATOR = 16;
    private const int PRIVATE_OPERATOR = 18;
    private const int FONT_MATRIX_ESCAPE = 7;
    private const int ROS_ESCAPE = 30;
    private const int FDARRAY_ESCAPE = 36;
    private const int FDSELECT_ESCAPE = 37;
    private const int SUBRS_OPERATOR = 19;
    private const int DEFAULT_WIDTH_OPERATOR = 20;
    private const int NOMINAL_WIDTH_OPERATOR = 21;
    private const int CHARSET_FORMAT_0 = 0;
    private const int CHARSET_FORMAT_1 = 1;
    private const int CHARSET_FORMAT_2 = 2;
    private const int FD_SELECT_FORMAT_0 = 0;
    private const int FD_SELECT_FORMAT_3 = 3;
    private const int FONT_MATRIX_OPERAND_COUNT = 6;
    private const int FONT_DICT_PRIVATE_LENGTH = (2 * LONG_INT_OPERAND_LENGTH) + 1;
    private const int MAX_FORMAT_1_RUN = 0x100;

    private readonly List<byte[]> _charStrings = [];
    private readonly List<byte[]> _globalSubrs = [];
    private readonly List<byte[]> _localSubrs = [];
    private readonly List<SyntheticCffFontDict> _fontDicts = [];
    private readonly List<SyntheticFdRange> _fdRanges = [];
    private byte[]? _charset;
    private byte[]? _fdSelect;
    private byte[]? _topDictExtra;
    private byte[]? _privateDictExtra;
    private double[]? _fontMatrix;
    private int? _encoding;
    private int? _predefinedCharset;
    private int? _defaultWidthX;
    private int? _nominalWidthX;
    private bool _isCid;

    private SyntheticCff() {
    }

    /// <summary>Gets the number of charstrings added so far.</summary>
    internal int GlyphCount => _charStrings.Count;

    /// <summary>Gets the number of Font DICTs added so far.</summary>
    internal int FontDictCount => _fontDicts.Count;

    /// <summary>Gets the CharStrings INDEX offset of the last <see cref="Build"/>.</summary>
    internal int CharStringsOffset { get; private set; }

    /// <summary>Gets the charset offset of the last <see cref="Build"/>, or zero when predefined.</summary>
    internal int CharsetOffset { get; private set; }

    /// <summary>Gets the top-level Private DICT offset of the last <see cref="Build"/>, or zero.</summary>
    internal int PrivateOffset { get; private set; }

    /// <summary>Gets the FDArray INDEX offset of the last <see cref="Build"/>, or zero for a non-CID table.</summary>
    internal int FdArrayOffset { get; private set; }

    /// <summary>Gets the FDSelect offset of the last <see cref="Build"/>, or zero for a non-CID table.</summary>
    internal int FdSelectOffset { get; private set; }

    /// <summary>Creates an empty CFF table builder.</summary>
    internal static SyntheticCff Create() => new();

    /// <summary>Encodes one raw Top DICT or Private DICT key with longint operands.</summary>
    /// <param name="operatorCode">The one-byte operator code.</param>
    /// <param name="operands">The integer operands in order.</param>
    internal static byte[] DictKey(int operatorCode, params int[] operands) =>
        [.. EncodeDictKey(operatorCode, escapeCode: null, operands)];

    /// <summary>Encodes one raw escaped Top DICT key with longint operands.</summary>
    /// <param name="escapeCode">The second operator byte after <c>12</c>.</param>
    /// <param name="operands">The integer operands in order.</param>
    internal static byte[] DictEscapeKey(int escapeCode, params int[] operands) =>
        [.. EncodeDictKey(operatorCode: 12, escapeCode, operands)];

    /// <summary>Adds one glyph charstring.</summary>
    /// <param name="charString">The complete Type 2 charstring.</param>
    /// <returns>The glyph identifier.</returns>
    internal int AddGlyph(byte[] charString) {
        _charStrings.Add(charString);
        return _charStrings.Count - 1;
    }

    /// <summary>Adds one glyph charstring from concatenated fragments.</summary>
    /// <param name="fragments">The charstring fragments.</param>
    /// <returns>The glyph identifier.</returns>
    internal int AddGlyph(params byte[][] fragments) =>
        AddGlyph(SyntheticCffCharStrings.Concat(fragments));

    /// <summary>Sets the Global Subr INDEX entries.</summary>
    internal SyntheticCff WithGlobalSubrs(params byte[][] subrs) {
        _globalSubrs.Clear();
        _globalSubrs.AddRange(subrs);
        return this;
    }

    /// <summary>Sets the top-level Private DICT local subroutine entries.</summary>
    internal SyntheticCff WithLocalSubrs(params byte[][] subrs) {
        _localSubrs.Clear();
        _localSubrs.AddRange(subrs);
        return this;
    }

    /// <summary>Sets a charset of format 0 with one SID per glyph after <c>.notdef</c>.</summary>
    internal SyntheticCff WithCharset(params int[] sids) => WithCharsetFormat(CHARSET_FORMAT_0, sids);

    /// <summary>Sets a charset with one SID per glyph after <c>.notdef</c>.</summary>
    /// <param name="format">The charset format: 0, 1, or 2.</param>
    /// <param name="sids">The SIDs for glyphs 1..n-1.</param>
    internal SyntheticCff WithCharsetFormat(int format, params int[] sids) {
        _predefinedCharset = null;
        _charset = [.. EncodeCharset(format, sids)];
        return this;
    }

    /// <summary>Uses raw charset bytes, including the format byte.</summary>
    internal SyntheticCff WithRawCharset(byte[] bytes) {
        _predefinedCharset = null;
        _charset = bytes;
        return this;
    }

    /// <summary>Uses a predefined charset offset instead of a charset table.</summary>
    /// <param name="offset">The predefined charset offset: 0, 1, or 2.</param>
    internal SyntheticCff WithPredefinedCharset(int offset) {
        _charset = null;
        _predefinedCharset = offset;
        return this;
    }

    /// <summary>Sets the Top DICT FontMatrix.</summary>
    internal SyntheticCff WithFontMatrix(params double[] values) {
        _fontMatrix = values;
        return this;
    }

    /// <summary>Sets the Top DICT Encoding operator value.</summary>
    internal SyntheticCff WithEncoding(int value) {
        _encoding = value;
        return this;
    }

    /// <summary>Appends raw bytes to the Top DICT after the generated keys.</summary>
    internal SyntheticCff WithTopDictExtra(byte[] bytes) {
        _topDictExtra = _topDictExtra is null ? bytes : [.. _topDictExtra, .. bytes];
        return this;
    }

    /// <summary>Appends raw bytes to the top-level Private DICT before the generated keys.</summary>
    internal SyntheticCff WithPrivateDictExtra(byte[] bytes) {
        _privateDictExtra = _privateDictExtra is null ? bytes : [.. _privateDictExtra, .. bytes];
        return this;
    }

    /// <summary>Adds the default and nominal widths to the top-level Private DICT.</summary>
    internal SyntheticCff WithPrivateWidths(int defaultWidthX, int nominalWidthX) {
        _defaultWidthX = defaultWidthX;
        _nominalWidthX = nominalWidthX;
        return this;
    }

    /// <summary>Makes the table CID-keyed and clears the predefined charset.</summary>
    internal SyntheticCff AsCid() {
        _isCid = true;
        _predefinedCharset = null;
        return this;
    }

    /// <summary>Adds one Font DICT with its own local subroutine INDEX.</summary>
    /// <param name="localSubrs">The Font DICT local subroutine entries.</param>
    /// <returns>The Font DICT index.</returns>
    internal int AddFontDict(params byte[][] localSubrs) => AddFontDictWithPrivate([], localSubrs);

    /// <summary>Adds one Font DICT with a private DICT prefix and local subroutine INDEX.</summary>
    /// <param name="privateDictExtra">Raw bytes written before the generated Private DICT keys.</param>
    /// <param name="localSubrs">The Font DICT local subroutine entries.</param>
    /// <returns>The Font DICT index.</returns>
    internal int AddFontDictWithPrivate(byte[] privateDictExtra, params byte[][] localSubrs) {
        _fontDicts.Add(new SyntheticCffFontDict(privateDictExtra, [.. localSubrs]));
        return _fontDicts.Count - 1;
    }

    /// <summary>Adds a Font DICT that reuses another Font DICT's Private DICT and local subroutines.</summary>
    /// <param name="fontDictIndex">The Font DICT to share.</param>
    /// <returns>The new Font DICT index.</returns>
    internal int AddSharedFontDict(int fontDictIndex) {
        if ((uint)fontDictIndex >= (uint)_fontDicts.Count) {
            throw new ArgumentOutOfRangeException(
                nameof(fontDictIndex),
                fontDictIndex,
                $"The table has {_fontDicts.Count} Font DICT(s).");
        }

        _fontDicts.Add(_fontDicts[fontDictIndex]);
        return _fontDicts.Count - 1;
    }

    /// <summary>Builds an FDSelect format 0 table from one Font DICT index per glyph.</summary>
    internal SyntheticCff WithFdSelectFormat0(params byte[] fontDictIndices) {
        _fdSelect = [(byte)FD_SELECT_FORMAT_0, .. fontDictIndices];
        _fdRanges.Clear();
        return this;
    }

    /// <summary>Builds an FDSelect format 3 table from ranges; the sentinel is appended.</summary>
    internal SyntheticCff WithFdSelectFormat3(params (int First, int FontDict)[] ranges) {
        _fdSelect = null;
        _fdRanges.Clear();
        foreach ((int first, int fontDict) in ranges) {
            _fdRanges.Add(new SyntheticFdRange(first, (byte)fontDict));
        }

        return this;
    }

    /// <summary>Uses raw FDSelect bytes.</summary>
    internal SyntheticCff WithRawFdSelect(byte[] bytes) {
        _fdSelect = bytes;
        _fdRanges.Clear();
        return this;
    }

    /// <summary>Builds the complete <c>CFF </c> table bytes.</summary>
    /// <exception cref="InvalidOperationException">The configured structures are inconsistent.</exception>
    internal byte[] Build() {
        int glyphCount = _charStrings.Count;
        if (glyphCount == 0) {
            throw new InvalidOperationException("A synthetic CFF table needs at least one charstring.");
        }

        if (glyphCount > MAX_INDEX_COUNT) {
            throw new InvalidOperationException($"Synthetic glyph count {glyphCount} exceeds {MAX_INDEX_COUNT}.");
        }

        if (_isCid && _fontDicts.Count == 0) {
            throw new InvalidOperationException("A CID-keyed synthetic CFF table needs at least one Font DICT.");
        }

        if (!_isCid && (_fontDicts.Count > 0 || _fdSelect is not null || _fdRanges.Count > 0)) {
            throw new InvalidOperationException("Font DICTs and FDSelect are only valid for a CID-keyed table.");
        }

        if (_fontMatrix is not null && _fontMatrix.Length != FONT_MATRIX_OPERAND_COUNT) {
            throw new InvalidOperationException("A synthetic FontMatrix needs six operands.");
        }

        byte[]? charsetData = null;
        if (_charset is not null) {
            charsetData = _charset;
        }

        byte[]? fdSelectData = null;
        if (_isCid) {
            if (_fdRanges.Count > 0) {
                fdSelectData = EncodeFdSelectFormat3(glyphCount);
            } else {
                fdSelectData = _fdSelect ?? [(byte)FD_SELECT_FORMAT_0, .. new byte[glyphCount]];
            }
        }

        byte[] nameIndex = EncodeIndex([Encoding.ASCII.GetBytes("SyntheticCff")]);
        byte[] stringIndex = EncodeIndex([]);
        byte[] globalSubrIndex = EncodeIndex(_globalSubrs);
        byte[] charStringsIndex = EncodeIndex(_charStrings);

        bool hasTopPrivate = !_isCid
            && (_localSubrs.Count > 0
                || _privateDictExtra is { Length: > 0 }
                || _defaultWidthX is not null
                || _nominalWidthX is not null);
        int topPrivateLength = hasTopPrivate
            ? PrivateDictLength(_privateDictExtra, _defaultWidthX, _nominalWidthX, _localSubrs.Count > 0)
            : 0;
        byte[] localSubrsIndex = hasTopPrivate && _localSubrs.Count > 0
            ? EncodeIndex(_localSubrs)
            : [];

        Dictionary<SyntheticCffFontDict, int> uniqueFontDictIndices = new(ReferenceEqualityComparer.Instance);
        List<SyntheticCffFontDict> uniqueFontDicts = [];
        List<int> uniquePrivateLengths = [];
        List<int> uniqueSubrLengths = [];
        int[] fontDictUnique = new int[_fontDicts.Count];
        int[] fontDictLengths = new int[_fontDicts.Count];
        int fontDictDataLength = 0;
        for (int index = 0; index < _fontDicts.Count; index++) {
            SyntheticCffFontDict fontDict = _fontDicts[index];
            if (!uniqueFontDictIndices.TryGetValue(fontDict, out int unique)) {
                unique = uniqueFontDicts.Count;
                uniqueFontDictIndices.Add(fontDict, unique);
                uniqueFontDicts.Add(fontDict);
                uniquePrivateLengths.Add(PrivateDictLength(
                    fontDict.PrivateDictExtra,
                    null,
                    null,
                    fontDict.LocalSubrs.Count > 0));
                uniqueSubrLengths.Add(fontDict.LocalSubrs.Count > 0
                    ? EncodeIndex(fontDict.LocalSubrs).Length
                    : 0);
            }

            fontDictUnique[index] = unique;
            int length = uniquePrivateLengths[unique] == 0 ? 0 : FONT_DICT_PRIVATE_LENGTH;
            fontDictLengths[index] = length;
            fontDictDataLength += length;
        }

        byte[] probeTopDict = EncodeTopDict(
            charStringsOffset: 0,
            charsetOffset: 0,
            privateLength: topPrivateLength,
            privateOffset: 0,
            fdArrayOffset: 0,
            fdSelectOffset: 0);
        int topDictIndexLength = IndexLength(1, probeTopDict.Length);

        int offset = CFF_HEADER_SIZE;
        offset += nameIndex.Length;
        int topDictIndexOffset = offset;
        offset += topDictIndexLength;
        offset += stringIndex.Length;
        offset += globalSubrIndex.Length;
        int charStringsOffset = offset;
        offset += charStringsIndex.Length;
        int charsetOffset = 0;
        if (charsetData is not null) {
            charsetOffset = offset;
            offset += charsetData.Length;
        }

        int privateOffset = 0;
        if (hasTopPrivate) {
            privateOffset = offset;
            offset += topPrivateLength + localSubrsIndex.Length;
        }

        int fdArrayOffset = 0;
        int fdSelectOffset = 0;
        int[] uniquePrivateOffsets = new int[uniqueFontDicts.Count];
        if (_isCid) {
            fdArrayOffset = offset;
            offset += IndexLength(_fontDicts.Count, fontDictDataLength);
            for (int unique = 0; unique < uniqueFontDicts.Count; unique++) {
                if (uniquePrivateLengths[unique] == 0) {
                    continue;
                }

                uniquePrivateOffsets[unique] = offset;
                offset += uniquePrivateLengths[unique] + uniqueSubrLengths[unique];
            }

            fdSelectOffset = offset;
            offset += fdSelectData!.Length;
        }

        byte[] topDict = EncodeTopDict(
            charStringsOffset,
            charsetOffset,
            topPrivateLength,
            privateOffset,
            fdArrayOffset,
            fdSelectOffset);
        if (topDict.Length != probeTopDict.Length) {
            throw new InvalidOperationException("The synthetic Top DICT length changed between passes.");
        }

        byte[] topDictIndex = EncodeIndex([topDict]);
        if (topDictIndex.Length != topDictIndexLength) {
            throw new InvalidOperationException("The synthetic Top DICT INDEX length changed between passes.");
        }

        byte[] table = new byte[offset];
        table[0] = CFF_MAJOR_VERSION;
        table[1] = CFF_MINOR_VERSION;
        table[2] = CFF_HEADER_SIZE;
        table[3] = INDEX_OFFSET_SIZE;
        WriteBlock(table, CFF_HEADER_SIZE, nameIndex);
        WriteBlock(table, topDictIndexOffset, topDictIndex);
        WriteBlock(table, topDictIndexOffset + topDictIndex.Length, stringIndex);
        WriteBlock(
            table,
            topDictIndexOffset + topDictIndex.Length + stringIndex.Length,
            globalSubrIndex);
        WriteBlock(table, charStringsOffset, charStringsIndex);
        if (charsetData is not null) {
            WriteBlock(table, charsetOffset, charsetData);
        }

        if (hasTopPrivate) {
            byte[] privateDict = EncodePrivateDict(
                _privateDictExtra,
                _defaultWidthX,
                _nominalWidthX,
                _localSubrs.Count > 0,
                topPrivateLength);
            WriteBlock(table, privateOffset, privateDict);
            WriteBlock(table, privateOffset + topPrivateLength, localSubrsIndex);
        }

        if (_isCid) {
            byte[][] fontDicts = new byte[_fontDicts.Count][];
            for (int index = 0; index < _fontDicts.Count; index++) {
                int unique = fontDictUnique[index];
                fontDicts[index] = fontDictLengths[index] == 0
                    ? []
                    : EncodeFontDict(uniquePrivateLengths[unique], uniquePrivateOffsets[unique]);
            }

            WriteBlock(table, fdArrayOffset, EncodeIndex(fontDicts));
            for (int unique = 0; unique < uniqueFontDicts.Count; unique++) {
                if (uniquePrivateLengths[unique] == 0) {
                    continue;
                }

                SyntheticCffFontDict fontDict = uniqueFontDicts[unique];
                byte[] privateDict = EncodePrivateDict(
                    fontDict.PrivateDictExtra,
                    null,
                    null,
                    fontDict.LocalSubrs.Count > 0,
                    uniquePrivateLengths[unique]);
                WriteBlock(table, uniquePrivateOffsets[unique], privateDict);
                if (fontDict.LocalSubrs.Count > 0) {
                    WriteBlock(
                        table,
                        uniquePrivateOffsets[unique] + uniquePrivateLengths[unique],
                        EncodeIndex(fontDict.LocalSubrs));
                }
            }

            WriteBlock(table, fdSelectOffset, fdSelectData!);
        }

        CharStringsOffset = charStringsOffset;
        CharsetOffset = charsetOffset;
        PrivateOffset = privateOffset;
        FdArrayOffset = fdArrayOffset;
        FdSelectOffset = fdSelectOffset;
        return table;
    }

    /// <summary>Builds a complete OTF face whose CFF table is this builder's output.</summary>
    /// <param name="mappings">The cmap format 4 mappings; a default maps <c>A</c> when omitted.</param>
    /// <returns>The face bytes.</returns>
    internal byte[] BuildFace(params (int Codepoint, int GlyphId)[] mappings) =>
        BuildFace(1000, mappings);

    /// <summary>Builds a complete OTF face whose CFF table is this builder's output.</summary>
    /// <param name="unitsPerEm">The face <c>head</c> units per em.</param>
    /// <param name="mappings">The cmap format 4 mappings; a default maps <c>A</c> when omitted.</param>
    /// <returns>The face bytes.</returns>
    internal byte[] BuildFace(int unitsPerEm, params (int Codepoint, int GlyphId)[] mappings) {
        if (_charStrings.Count == 0) {
            throw new InvalidOperationException("A synthetic CFF face needs at least one charstring.");
        }

        SyntheticFontBuilder face = SyntheticFontBuilder.Create().WithUnitsPerEm(unitsPerEm);
        while (face.GlyphCount < _charStrings.Count) {
            face.AddEmpty(500);
        }

        (int Codepoint, int GlyphId)[] cmap = mappings.Length == 0
            ? [(0x41, _charStrings.Count > 1 ? 1 : 0)]
            : mappings;
        return face.UseCff(Build()).WithCmap(4, cmap).Build();
    }

    private static byte[] EncodeCharset(int format, int[] sids) {
        if (format is not CHARSET_FORMAT_0 and not CHARSET_FORMAT_1 and not CHARSET_FORMAT_2) {
            throw new InvalidOperationException($"Synthetic charset format {format} is not 0, 1, or 2.");
        }

        List<byte> data = [(byte)format];
        if (format == CHARSET_FORMAT_0) {
            foreach (int sid in sids) {
                WriteUInt16(data, sid);
            }

            return [.. data];
        }

        int position = 0;
        while (position < sids.Length) {
            int first = sids[position];
            int run = 1;
            int maximum = format == CHARSET_FORMAT_1 ? MAX_FORMAT_1_RUN : 0x10000;
            while (position + run < sids.Length
                && run < maximum
                && sids[position + run] == first + run
                && first + run <= 0xFFFF) {
                run++;
            }

            WriteUInt16(data, first);
            if (format == CHARSET_FORMAT_1) {
                data.Add((byte)(run - 1));
            } else {
                WriteUInt16(data, run - 1);
            }

            position += run;
        }

        return [.. data];
    }

    private byte[] EncodeFdSelectFormat3(int glyphCount) {
        List<byte> data = [(byte)FD_SELECT_FORMAT_3];
        WriteUInt16(data, _fdRanges.Count);
        foreach (SyntheticFdRange range in _fdRanges) {
            WriteUInt16(data, range.First);
            data.Add(range.FontDict);
        }

        WriteUInt16(data, glyphCount);
        return [.. data];
    }

    private byte[] EncodeTopDict(
        int charStringsOffset,
        int charsetOffset,
        int privateLength,
        int privateOffset,
        int fdArrayOffset,
        int fdSelectOffset) {
        List<byte> dict = [];
        AppendKey(dict, CHARSTRINGS_OPERATOR, charStringsOffset);
        if (_charset is not null) {
            AppendKey(dict, CHARSET_OPERATOR, charsetOffset);
        } else if (_predefinedCharset is int predefined) {
            AppendKey(dict, CHARSET_OPERATOR, predefined);
        }

        if (_encoding is int encoding) {
            AppendKey(dict, ENCODING_OPERATOR, encoding);
        }

        if (!_isCid && privateLength != 0) {
            AppendKey(dict, PRIVATE_OPERATOR, privateLength, privateOffset);
        }

        if (_fontMatrix is not null) {
            foreach (double value in _fontMatrix) {
                AppendReal(dict, value);
            }

            dict.Add(12);
            dict.Add(FONT_MATRIX_ESCAPE);
        }

        if (_isCid) {
            // ROS: registry SID, ordering SID, supplement; the parser only records that it is CID.
            dict.Add(139);
            dict.Add(139);
            dict.Add(139);
            dict.Add(12);
            dict.Add(ROS_ESCAPE);
            AppendEscapeKey(dict, FDARRAY_ESCAPE, fdArrayOffset);
            AppendEscapeKey(dict, FDSELECT_ESCAPE, fdSelectOffset);
        }

        if (_topDictExtra is not null) {
            dict.AddRange(_topDictExtra);
        }

        return [.. dict];
    }

    private static byte[] EncodeFontDict(int privateLength, int privateOffset) {
        List<byte> dict = [];
        AppendKey(dict, PRIVATE_OPERATOR, privateLength, privateOffset);
        return [.. dict];
    }

    private static List<byte> EncodeDictKey(int operatorCode, int? escapeCode, int[] operands) {
        List<byte> dict = [];
        foreach (int operand in operands) {
            dict.Add(29);
            dict.Add((byte)(operand >> 24));
            dict.Add((byte)(operand >> 16));
            dict.Add((byte)(operand >> 8));
            dict.Add((byte)operand);
        }

        dict.Add((byte)operatorCode);
        if (escapeCode is int code) {
            dict.Add((byte)code);
        }

        return dict;
    }

    private static byte[] EncodePrivateDict(
        byte[]? extra,
        int? defaultWidthX,
        int? nominalWidthX,
        bool hasSubrs,
        int privateLength) {
        List<byte> dict = [];
        if (extra is not null) {
            dict.AddRange(extra);
        }

        if (defaultWidthX is int defaultWidth) {
            AppendKey(dict, DEFAULT_WIDTH_OPERATOR, defaultWidth);
        }

        if (nominalWidthX is int nominalWidth) {
            AppendKey(dict, NOMINAL_WIDTH_OPERATOR, nominalWidth);
        }

        if (hasSubrs) {
            AppendKey(dict, SUBRS_OPERATOR, privateLength);
        }

        return [.. dict];
    }

    private static int PrivateDictLength(
        byte[]? extra,
        int? defaultWidthX,
        int? nominalWidthX,
        bool hasSubrs) {
        int length = extra?.Length ?? 0;
        if (defaultWidthX is not null) {
            length += LONG_INT_OPERAND_LENGTH + 1;
        }

        if (nominalWidthX is not null) {
            length += LONG_INT_OPERAND_LENGTH + 1;
        }

        if (hasSubrs) {
            length += LONG_INT_OPERAND_LENGTH + 1;
        }

        return length;
    }

    private static int IndexLength(int count, int dataLength) => count == 0
        ? 2
        : 2 + 1 + (INDEX_OFFSET_SIZE * (count + 1)) + dataLength;

    private static byte[] EncodeIndex(IReadOnlyList<byte[]> objects) {
        if (objects.Count == 0) {
            return [0, 0];
        }

        if (objects.Count > MAX_INDEX_COUNT) {
            throw new InvalidOperationException(
                $"Synthetic INDEX count {objects.Count} exceeds {MAX_INDEX_COUNT}.");
        }

        List<byte> data = [];
        foreach (byte[] item in objects) {
            data.AddRange(item);
        }

        int offset = 1;
        List<byte> index = [];
        WriteUInt16(index, objects.Count);
        index.Add(INDEX_OFFSET_SIZE);
        foreach (byte[] item in objects) {
            WriteUInt32(index, offset);
            offset += item.Length;
        }

        WriteUInt32(index, offset);
        index.AddRange(data);
        return [.. index];
    }

    private static void AppendKey(List<byte> dict, int operatorCode, params int[] operands) {
        foreach (int operand in operands) {
            dict.Add(29);
            dict.Add((byte)(operand >> 24));
            dict.Add((byte)(operand >> 16));
            dict.Add((byte)(operand >> 8));
            dict.Add((byte)operand);
        }

        dict.Add((byte)operatorCode);
    }

    private static void AppendEscapeKey(List<byte> dict, int escapeCode, params int[] operands) {
        foreach (int operand in operands) {
            dict.Add(29);
            dict.Add((byte)(operand >> 24));
            dict.Add((byte)(operand >> 16));
            dict.Add((byte)(operand >> 8));
            dict.Add((byte)operand);
        }

        dict.Add(12);
        dict.Add((byte)escapeCode);
    }

    private static void AppendReal(List<byte> dict, double value) {
        string text = value.ToString("0.################", CultureInfo.InvariantCulture);
        List<int> nibbles = [];
        foreach (char character in text) {
            if (character == '-') {
                nibbles.Add(0x0E);
            } else if (character == '.') {
                nibbles.Add(0x0A);
            } else if (character is >= '0' and <= '9') {
                nibbles.Add(character - '0');
            } else {
                throw new InvalidOperationException($"Cannot encode the real value {text}.");
            }
        }

        nibbles.Add(0x0F);
        if ((nibbles.Count & 1) != 0) {
            nibbles.Add(0x0F);
        }

        dict.Add(30);
        for (int index = 0; index < nibbles.Count; index += 2) {
            dict.Add((byte)((nibbles[index] << 4) | nibbles[index + 1]));
        }
    }

    private static void WriteUInt16(List<byte> target, int value) {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void WriteUInt32(List<byte> target, int value) {
        target.Add((byte)(value >> 24));
        target.Add((byte)(value >> 16));
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void WriteBlock(byte[] table, int offset, ReadOnlySpan<byte> data) =>
        data.CopyTo(table.AsSpan(offset));

    private readonly struct SyntheticFdRange {
        internal SyntheticFdRange(int first, byte fontDict) {
            First = first;
            FontDict = fontDict;
        }

        internal int First { get; }

        internal byte FontDict { get; }
    }

    private sealed class SyntheticCffFontDict {
        internal SyntheticCffFontDict(byte[] privateDictExtra, List<byte[]> localSubrs) {
            PrivateDictExtra = privateDictExtra;
            LocalSubrs = localSubrs;
        }

        internal byte[] PrivateDictExtra { get; }

        internal List<byte[]> LocalSubrs { get; }
    }
}
