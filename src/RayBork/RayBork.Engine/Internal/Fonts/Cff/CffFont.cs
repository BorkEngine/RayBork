using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// A parsed CFF 1.0 (Compact Font Format, Type 2 charstring) face.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Parse"/> validates the CFF header, Name INDEX, Top DICT INDEX, String INDEX, Global
/// Subr INDEX, Top DICT, charset, CharStrings INDEX, the top-level Private DICT, and — for a
/// CID-keyed face — the FDArray Font DICTs and the FDSelect. Every offset and length is bounded by
/// the <c>CFF </c> table. A charstring is interpreted only by <see cref="ReadGlyph"/>, so a face with
/// a corrupt unused glyph still parses and the failure occurs when that glyph is read.
/// </para>
/// <para>
/// A CFF2 table is reported as <see cref="FontUnsupportedException"/>; CFF2 is not supported. Valid
/// CFF features outside the subset are also unsupported: a predefined Expert or ExpertSubset charset,
/// an FDSelect format 4 table, a <c>seac</c> in a CID-keyed face, a <c>seac</c> whose base or accent
/// cannot be resolved, a nested <c>seac</c>, more than <see cref="MAX_FONT_DICTS"/> Font DICTs, more
/// than <see cref="MAX_LOCAL_SUBR_ENTRIES"/> total local-subroutine entries, more than
/// <see cref="MAX_PRIVATE_DICT_BYTES"/> total private-DICT bytes, a <c>CharstringType</c> other than
/// 2, a <c>PaintType</c> other than 0, a non-zero <c>StrokeWidth</c>, <c>SyntheticBase</c>, and the
/// interpreter limits documented on <see cref="CffType2Interpreter"/>. All other structural failures
/// are <see cref="FontFormatException"/>. Shared Private DICTs and Local Subr INDEXes are parsed once
/// per (offset, size) or offset even when many Font DICTs reference them; the private-DICT byte cap
/// counts each distinct (offset, size) once and includes the Top DICT Private DICT.
/// </para>
/// <para>
/// Charstring coordinates are mapped to font units by the Top DICT FontMatrix and the face's
/// <c>unitsPerEm</c>: the default <c>[0.001 0 0 0.001 0 0]</c> with 1,000 units per em is the
/// identity, and a non-default matrix is applied as
/// <c>(a*x + c*y + e, b*x + d*y + f) * unitsPerEm</c> to every point after interpretation, including
/// the composed <c>seac</c> components. A Font DICT FontMatrix is not applied; CID faces use the Top
/// DICT matrix.
/// </para>
/// <para>
/// <see cref="ReadGlyph"/> reports bounds computed from the returned outline, not a stored glyph box:
/// the box is the outward-rounded integer hull of every on-curve point and every cubic control
/// point, so it contains the whole outline because a Bézier lies inside its control hull. A
/// coordinate outside the 32-bit integer range saturates the corresponding bound. A glyph without a
/// contour has an all-zero box and <see cref="OutlinePath.Empty"/>.
/// </para>
/// <para>
/// The instance retains the face bytes and is immutable after <see cref="Parse"/>, so reads are safe
/// from any thread without locks. Each <see cref="ReadGlyph"/> call allocates its own outline arrays;
/// the interpreter's operand-stack and transient scratch arrays are pooled, and an identity FontMatrix
/// reuses the raw contours instead of copying them. Concurrent reads share no mutable state.
/// </para>
/// </remarks>
internal sealed class CffFont
{
    private const int HEADER_LENGTH = 4;
    private const int MIN_HEADER_SIZE = 4;
    private const int MIN_OFF_SIZE = 1;
    private const int MAX_OFF_SIZE = 4;
    private const int CFF_MAJOR_VERSION = 1;
    private const int CFF2_MAJOR_VERSION = 2;
    private const int CHARSTRINGS_OPERATOR = 17;
    private const int CHARSET_OPERATOR = 15;
    private const int ENCODING_OPERATOR = 16;
    private const int PRIVATE_OPERATOR = 18;
    private const int CHARSTRING_TYPE_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 6;
    private const int PAINT_TYPE_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 5;
    private const int SYNTHETIC_BASE_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 20;
    private const int STROKE_WIDTH_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 8;
    private const int FONT_MATRIX_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 7;
    private const int ROS_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 30;
    private const int FD_ARRAY_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 36;
    private const int FD_SELECT_OPERATOR = CffDictReader.ESCAPE_OPERATOR_FLAG | 37;
    private const int SUBRS_OPERATOR = 19;
    private const int DEFAULT_WIDTH_OPERATOR = 20;
    private const int NOMINAL_WIDTH_OPERATOR = 21;
    private const int FONT_MATRIX_OPERAND_COUNT = 6;
    private const int PRIVATE_OPERAND_COUNT = 2;
    private const int ROS_OPERAND_COUNT = 3;
    private const int TYPE_2_CHARSTRING_TYPE = 2;
    private const int FILLED_PAINT_TYPE = 0;

    /// <summary>The maximum number of CID-keyed FDArray Font DICTs the parser accepts.</summary>
    internal const int MAX_FONT_DICTS = 256;

    /// <summary>The maximum total number of local-subroutine entries the parser accepts in one face.</summary>
    internal const int MAX_LOCAL_SUBR_ENTRIES = 1048576;

    /// <summary>The maximum total number of private-DICT bytes the parser accepts in one face.</summary>
    internal const int MAX_PRIVATE_DICT_BYTES = 8 * 1024 * 1024;

    private readonly int _glyphCount;
    private readonly int _unitsPerEm;
    private readonly CffIndex _globalSubrs;
    private readonly CffIndex _charStrings;
    private readonly CffCharset _charset;
    private readonly CffFontMatrix _fontMatrix;
    private readonly CffPrivateDict _privateDict;
    private readonly CffPrivateDict[]? _fontDicts;
    private readonly CffFdSelect? _fdSelect;
    private readonly bool _isCidKeyed;

    private CffFont(
        int glyphCount,
        int unitsPerEm,
        CffIndex globalSubrs,
        CffIndex charStrings,
        CffCharset charset,
        CffFontMatrix fontMatrix,
        CffPrivateDict privateDict,
        CffPrivateDict[]? fontDicts,
        CffFdSelect? fdSelect,
        bool isCidKeyed
    )
    {
        _glyphCount = glyphCount;
        _unitsPerEm = unitsPerEm;
        _globalSubrs = globalSubrs;
        _charStrings = charStrings;
        _charset = charset;
        _fontMatrix = fontMatrix;
        _privateDict = privateDict;
        _fontDicts = fontDicts;
        _fdSelect = fdSelect;
        _isCidKeyed = isCidKeyed;
    }

    /// <summary>Gets the Global Subr INDEX; the interpreter reads it through the face.</summary>
    internal CffIndex GlobalSubrs => _globalSubrs;

    /// <summary>Gets a value indicating whether the face is CID-keyed (has a ROS key).</summary>
    internal bool IsCidKeyed => _isCidKeyed;

    /// <summary>Parses a CFF 1.0 table.</summary>
    /// <param name="table">The complete <c>CFF </c> table bytes; they are retained for the face's lifetime.</param>
    /// <param name="glyphCount">The face's <c>maxp</c> <c>numGlyphs</c>.</param>
    /// <param name="unitsPerEm">The face's <c>head</c> units per em.</param>
    /// <returns>The parsed CFF face.</returns>
    /// <exception cref="FontFormatException">
    /// The header, an INDEX, a DICT, the charset, the charstrings count, or a CID-keyed FDArray or
    /// FDSelect is malformed, truncated, or inconsistent with the face.
    /// </exception>
    /// <exception cref="FontUnsupportedException">
    /// The table is CFF2; a charset is a predefined Expert or ExpertSubset charset; an FDSelect uses
    /// format 4; the Top DICT declares a supported-subset violation (<c>CharstringType</c> other than
    /// 2, <c>PaintType</c> other than 0, a non-zero <c>StrokeWidth</c>, or <c>SyntheticBase</c>); or
    /// the face exceeds a parse limit (256 Font DICTs, 1,048,576 local-subroutine entries, or
    /// 8,388,608 private-DICT bytes).
    /// </exception>
    internal static CffFont Parse(ReadOnlyMemory<byte> table, int glyphCount, int unitsPerEm)
    {
        SfntReader reader = new(table.Span, "the 'CFF ' table");
        if (table.Length < HEADER_LENGTH)
        {
            throw new FontFormatException(
                $"the 'CFF ' table is {table.Length} byte(s) long; the {HEADER_LENGTH}-byte CFF header is required"
            );
        }

        int major = reader.ReadByte(0);
        int minor = reader.ReadByte(1);
        if (major != CFF_MAJOR_VERSION)
        {
            if (major == CFF2_MAJOR_VERSION)
            {
                throw new FontUnsupportedException(
                    $"the table uses CFF version {major}.{minor}; only CFF 1.0 outlines are supported"
                );
            }

            throw new FontFormatException(
                $"the CFF version is {major}.{minor}; version 1.0 is supported"
            );
        }

        int headerSize = reader.ReadByte(2);
        if (headerSize < MIN_HEADER_SIZE)
        {
            throw new FontFormatException(
                $"the CFF header declares the header size {headerSize}; at least {MIN_HEADER_SIZE} bytes are required"
            );
        }

        if (headerSize > table.Length)
        {
            throw new FontFormatException(
                $"the CFF header declares the header size {headerSize}; the table is {table.Length} byte(s) long"
            );
        }

        int offSize = reader.ReadByte(3);
        if (offSize is < MIN_OFF_SIZE or > MAX_OFF_SIZE)
        {
            throw new FontFormatException(
                $"the CFF header declares the offset size {offSize}; the supported range is {MIN_OFF_SIZE} to {MAX_OFF_SIZE}"
            );
        }

        int position = headerSize;
        CffIndex names = CffIndex.Parse(table, position, "the CFF Name INDEX", out position);
        if (names.Count != 1)
        {
            throw new FontFormatException(
                $"the CFF Name INDEX has {names.Count} name(s); exactly one is required"
            );
        }

        CffIndex topDicts = CffIndex.Parse(table, position, "the CFF Top DICT INDEX", out position);
        if (topDicts.Count != 1)
        {
            throw new FontFormatException(
                $"the CFF Top DICT INDEX has {topDicts.Count} DICT(s); exactly one is required"
            );
        }

        _ = CffIndex.Parse(table, position, "the CFF String INDEX", out position);
        CffIndex globalSubrs = CffIndex.Parse(table, position, "the CFF Global Subr INDEX", out _);

        int? charStringsOffset = null;
        int? charsetOffset = null;
        int? encodingOffset = null;
        int? privateSize = null;
        int? privateOffset = null;
        int? fdArrayOffset = null;
        int? fdSelectOffset = null;
        double[]? fontMatrixOperands = null;
        bool isCidKeyed = false;

        CffDictReader topDict = new(topDicts.Get(0), "the CFF Top DICT");
        while (topDict.TryRead(out int operatorCode, out int operandCount))
        {
            switch (operatorCode)
            {
                case CHARSTRINGS_OPERATOR:
                    charStringsOffset = ReadIntegerOperand(
                        topDict,
                        operandCount,
                        1,
                        0,
                        "CharStrings"
                    );
                    break;
                case CHARSET_OPERATOR:
                    charsetOffset = ReadIntegerOperand(topDict, operandCount, 1, 0, "charset");
                    break;
                case ENCODING_OPERATOR:
                    encodingOffset = ReadIntegerOperand(topDict, operandCount, 1, 0, "Encoding");
                    break;
                case PRIVATE_OPERATOR:
                    RequireOperandCount(operandCount, PRIVATE_OPERAND_COUNT, "Private");
                    privateSize = ToInteger(topDict.Operand(0), "the CFF Top DICT Private size");
                    privateOffset = ToInteger(
                        topDict.Operand(1),
                        "the CFF Top DICT Private offset"
                    );
                    break;
                case CHARSTRING_TYPE_OPERATOR:
                {
                    int charStringType = ReadIntegerOperand(
                        topDict,
                        operandCount,
                        1,
                        0,
                        "CharstringType"
                    );
                    if (charStringType != TYPE_2_CHARSTRING_TYPE)
                    {
                        throw new FontUnsupportedException(
                            $"the CFF Top DICT declares CharstringType {charStringType}; only Type 2 charstrings are supported"
                        );
                    }

                    break;
                }

                case PAINT_TYPE_OPERATOR:
                {
                    int paintType = ReadIntegerOperand(topDict, operandCount, 1, 0, "PaintType");
                    if (paintType != FILLED_PAINT_TYPE)
                    {
                        throw new FontUnsupportedException(
                            $"the CFF Top DICT declares PaintType {paintType}; only filled outlines (PaintType 0) are supported"
                        );
                    }

                    break;
                }

                case SYNTHETIC_BASE_OPERATOR:
                    RequireOperandCount(operandCount, 1, "SyntheticBase");
                    throw new FontUnsupportedException(
                        "the CFF Top DICT declares SyntheticBase; synthetic multiple-master faces are not supported"
                    );
                case STROKE_WIDTH_OPERATOR:
                {
                    RequireOperandCount(operandCount, 1, "StrokeWidth");
                    double strokeWidth = topDict.Operand(0);
                    if (strokeWidth != 0)
                    {
                        throw new FontUnsupportedException(
                            $"the CFF Top DICT declares StrokeWidth {strokeWidth}; stroked outlines are not supported"
                        );
                    }

                    break;
                }

                case FONT_MATRIX_OPERATOR:
                    RequireOperandCount(operandCount, FONT_MATRIX_OPERAND_COUNT, "FontMatrix");
                    fontMatrixOperands =
                    [
                        topDict.Operand(0),
                        topDict.Operand(1),
                        topDict.Operand(2),
                        topDict.Operand(3),
                        topDict.Operand(4),
                        topDict.Operand(5),
                    ];
                    break;
                case ROS_OPERATOR:
                    RequireOperandCount(operandCount, ROS_OPERAND_COUNT, "ROS");
                    isCidKeyed = true;
                    break;
                case FD_ARRAY_OPERATOR:
                    fdArrayOffset = ReadIntegerOperand(topDict, operandCount, 1, 0, "FDArray");
                    break;
                case FD_SELECT_OPERATOR:
                    fdSelectOffset = ReadIntegerOperand(topDict, operandCount, 1, 0, "FDSelect");
                    break;
                default:
                    break;
            }
        }

        if (charStringsOffset is null)
        {
            throw new FontFormatException(
                "the CFF Top DICT is missing the CharStrings operator (17)"
            );
        }

        if (encodingOffset is { } encoding && (encoding < 0 || encoding > table.Length))
        {
            throw new FontFormatException(
                $"the CFF Top DICT Encoding offset {encoding} is outside the {table.Length}-byte table"
            );
        }

        CffIndex charStrings = CffIndex.Parse(
            table,
            charStringsOffset.Value,
            "the CFF CharStrings INDEX",
            out _
        );
        if (charStrings.Count != glyphCount)
        {
            throw new FontFormatException(
                $"the CFF CharStrings INDEX has {charStrings.Count} entry/entries; the face has {glyphCount} glyph(s)"
            );
        }

        CffCharset charset = CffCharset.Parse(
            table,
            glyphCount,
            charsetOffset ?? 0,
            "the CFF charset"
        );

        CffFontMatrix fontMatrix = fontMatrixOperands is null
            ? CffFontMatrix.Default
            : new CffFontMatrix(
                fontMatrixOperands[0],
                fontMatrixOperands[1],
                fontMatrixOperands[2],
                fontMatrixOperands[3],
                fontMatrixOperands[4],
                fontMatrixOperands[5]
            );

        CffParseState state = new();
        CffPrivateDict privateDict = CffPrivateDict.Empty;
        if (privateSize is not null && privateOffset is not null)
        {
            if (privateSize < 0)
            {
                throw new FontFormatException(
                    $"the CFF Top DICT declares the Private DICT size {privateSize}; sizes are non-negative"
                );
            }

            if (privateOffset < 0)
            {
                throw new FontFormatException(
                    $"the CFF Top DICT declares the Private DICT offset {privateOffset}; offsets are non-negative"
                );
            }

            privateDict = ResolvePrivateDict(
                table,
                privateSize.Value,
                privateOffset.Value,
                "the CFF Private DICT",
                state
            );
        }
        CffPrivateDict[]? fontDicts = null;
        CffFdSelect? fdSelect = null;
        if (isCidKeyed)
        {
            if (fdArrayOffset is null)
            {
                throw new FontFormatException(
                    "the CID-keyed CFF Top DICT is missing the FDArray operator (12 36)"
                );
            }

            if (fdSelectOffset is null)
            {
                throw new FontFormatException(
                    "the CID-keyed CFF Top DICT is missing the FDSelect operator (12 37)"
                );
            }

            CffIndex fdArray = CffIndex.Parse(
                table,
                fdArrayOffset.Value,
                "the CFF FDArray INDEX",
                out _
            );
            if (fdArray.Count == 0)
            {
                throw new FontFormatException("the CFF FDArray INDEX is empty");
            }

            if (fdArray.Count > MAX_FONT_DICTS)
            {
                throw new FontUnsupportedException(
                    $"the CFF FDArray has {fdArray.Count} Font DICT(s); the limit is {MAX_FONT_DICTS}"
                );
            }

            fontDicts = new CffPrivateDict[fdArray.Count];
            for (int index = 0; index < fdArray.Count; index++)
            {
                fontDicts[index] = ParseFontDict(table, fdArray.Get(index), index, state);
            }

            fdSelect = CffFdSelect.Parse(
                table,
                glyphCount,
                fdArray.Count,
                fdSelectOffset.Value,
                "the CFF FDSelect"
            );
        }

        return new CffFont(
            glyphCount,
            unitsPerEm,
            globalSubrs,
            charStrings,
            charset,
            fontMatrix,
            privateDict,
            fontDicts,
            fdSelect,
            isCidKeyed
        );
    }

    /// <summary>Resolves one glyph's bounds and font-unit outline.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, glyph count)</c>.</param>
    /// <returns>The resolved glyph in font units; an empty result has zero bounds and no contours.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="FontFormatException">The charstring, its subroutines, or its components are malformed.</exception>
    /// <exception cref="FontUnsupportedException">
    /// The glyph uses an unsupported CFF feature or exceeds an interpreter limit.
    /// </exception>
    internal FontGlyph ReadGlyph(int glyphId)
    {
        ThrowIfGlyphOutOfRange(glyphId);
        CffGlyphBudget budget = new();
        OutlineContour[] raw = ReadRawContours(glyphId, budget, allowSeac: true);
        if (raw.Length == 0)
        {
            return new FontGlyph(glyphId, default, OutlinePath.Empty);
        }

        // The overwhelmingly common default matrix with 1,000 units per em is the identity, so the
        // raw contours are returned as-is instead of copying every contour and segment. The raw
        // points are still checked so a charstring cannot produce non-finite bounds.
        OutlineContour[] contours;
        if (_fontMatrix.IsIdentity(_unitsPerEm))
        {
            ValidateFinitePoints(raw, glyphId);
            contours = raw;
        }
        else
        {
            contours = Transform(raw, glyphId);
        }

        return new FontGlyph(glyphId, ComputeBounds(contours), new OutlinePath(contours));
    }

    /// <summary>Interprets one glyph's charstring in raw charstring coordinates.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, glyph count)</c>.</param>
    /// <param name="budget">The per-resolution counters shared with any <c>seac</c> component.</param>
    /// <param name="allowSeac">
    /// <see langword="true"/> for the requested glyph; <see langword="false"/> for a <c>seac</c>
    /// component, which forbids nesting.
    /// </param>
    /// <returns>The resolved contours before the FontMatrix is applied.</returns>
    /// <remarks>
    /// This is the component-resolution seam for <see cref="CffType2Interpreter"/>; the returned
    /// contours are not transformed. The caller validates <paramref name="glyphId"/>.
    /// </remarks>
    internal OutlineContour[] ReadRawContours(int glyphId, CffGlyphBudget budget, bool allowSeac)
    {
        CffPrivateDict privateDict = GetPrivateDict(glyphId);
        CffType2Interpreter interpreter = new(this, budget, privateDict, glyphId, allowSeac);
        return interpreter.Run(_charStrings.Get(glyphId));
    }

    /// <summary>Resolves a Standard Encoding character code to a glyph identifier.</summary>
    /// <param name="code">The character code, in <c>[0, 256)</c>.</param>
    /// <param name="glyphId">The resolved glyph identifier, or zero when the code does not resolve.</param>
    /// <returns><see langword="true"/> when the code resolves through the charset.</returns>
    /// <remarks>This is the <c>seac</c> resolution seam for <see cref="CffType2Interpreter"/>.</remarks>
    internal bool TryGetStandardEncodingGlyphId(int code, out int glyphId)
    {
        glyphId = 0;
        if ((uint)code >= (uint)CffStandardEncoding.CODE_COUNT)
        {
            return false;
        }

        int sid = CffStandardEncoding.GetSid(code);
        return sid != 0 && _charset.TryGetGlyphId(sid, out glyphId);
    }

    private static CffPrivateDict ResolvePrivateDict(
        ReadOnlyMemory<byte> table,
        int size,
        int offset,
        string context,
        CffParseState state
    )
    {
        if (size == 0)
        {
            if (offset > table.Length)
            {
                throw new FontFormatException(
                    $"{context} is empty but its offset {offset} is past the {table.Length}-byte table"
                );
            }

            return CffPrivateDict.Empty;
        }

        (int Offset, int Size) key = (offset, size);
        if (state.PrivateDicts.TryGetValue(key, out CffPrivateDict cached))
        {
            return cached;
        }

        SfntReader tableReader = new(table.Span, context);
        tableReader.Require(offset, size);
        if (size > MAX_PRIVATE_DICT_BYTES - state.PrivateDictBytes)
        {
            throw new FontUnsupportedException(
                $"the CFF face parses more than {MAX_PRIVATE_DICT_BYTES} private-DICT bytes"
            );
        }

        state.PrivateDictBytes += size;
        CffDictReader dict = new(table.Slice(offset, size), context);
        int? subrsRelativeOffset = null;
        while (dict.TryRead(out int operatorCode, out int operandCount))
        {
            switch (operatorCode)
            {
                case SUBRS_OPERATOR:
                    subrsRelativeOffset = ReadIntegerOperand(dict, operandCount, 1, 0, "Subrs");
                    break;
                case DEFAULT_WIDTH_OPERATOR:
                    RequireOperandCount(operandCount, 1, "defaultWidthX");
                    break;
                case NOMINAL_WIDTH_OPERATOR:
                    RequireOperandCount(operandCount, 1, "nominalWidthX");
                    break;
                default:
                    break;
            }
        }

        CffPrivateDict privateDict = CffPrivateDict.Empty;
        if (subrsRelativeOffset is int relative)
        {
            long absoluteOffset = (long)offset + relative;
            if (relative < 0 || absoluteOffset > table.Length)
            {
                throw new FontFormatException(
                    $"{context} declares the Subrs offset {relative}; the offset from the Private DICT start resolves to {absoluteOffset} in the {table.Length}-byte table"
                );
            }

            int subrsOffset = (int)absoluteOffset;
            if (!state.LocalSubrs.TryGetValue(subrsOffset, out CffIndex? index))
            {
                index = CffIndex.Parse(table, subrsOffset, "the CFF Local Subr INDEX", out _);
                state.LocalSubrEntries += index.Count;
                if (state.LocalSubrEntries > MAX_LOCAL_SUBR_ENTRIES)
                {
                    throw new FontUnsupportedException(
                        $"the CFF face parses more than {MAX_LOCAL_SUBR_ENTRIES} local subroutine entries"
                    );
                }

                state.LocalSubrs.Add(subrsOffset, index);
            }

            privateDict = new CffPrivateDict(index);
        }

        state.PrivateDicts.Add(key, privateDict);
        return privateDict;
    }

    private static CffPrivateDict ParseFontDict(
        ReadOnlyMemory<byte> table,
        ReadOnlyMemory<byte> data,
        int fontDictIndex,
        CffParseState state
    )
    {
        string context = $"the CFF Font DICT {fontDictIndex}";
        CffDictReader dict = new(data, context);
        int? privateSize = null;
        int? privateOffset = null;
        while (dict.TryRead(out int operatorCode, out int operandCount))
        {
            if (operatorCode != PRIVATE_OPERATOR)
            {
                continue;
            }

            RequireOperandCount(operandCount, PRIVATE_OPERAND_COUNT, $"{context} Private");
            privateSize = ToInteger(dict.Operand(0), $"{context} Private size");
            privateOffset = ToInteger(dict.Operand(1), $"{context} Private offset");
        }

        if (privateSize is null || privateOffset is null)
        {
            return CffPrivateDict.Empty;
        }

        if (privateSize < 0)
        {
            throw new FontFormatException(
                $"{context} declares the Private DICT size {privateSize}; sizes are non-negative"
            );
        }

        if (privateOffset < 0)
        {
            throw new FontFormatException(
                $"{context} declares the Private DICT offset {privateOffset}; offsets are non-negative"
            );
        }

        return ResolvePrivateDict(
            table,
            privateSize.Value,
            privateOffset.Value,
            $"{context} Private DICT",
            state
        );
    }

    private static int ReadIntegerOperand(
        CffDictReader dict,
        int operandCount,
        int expectedCount,
        int operandIndex,
        string key
    )
    {
        RequireOperandCount(operandCount, expectedCount, key);
        return ToInteger(dict.Operand(operandIndex), $"the CFF Top DICT {key}");
    }

    private static int ToInteger(double value, string context)
    {
        if (value != Math.Floor(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw new FontFormatException($"{context} operand {value} is not a 32-bit integer");
        }

        return (int)value;
    }

    private static void RequireOperandCount(int actual, int expected, string key)
    {
        if (actual != expected)
        {
            throw new FontFormatException(
                $"the CFF DICT {key} operator has {actual} operand(s); {expected} are required"
            );
        }
    }

    private CffPrivateDict GetPrivateDict(int glyphId)
    {
        if (!_isCidKeyed)
        {
            return _privateDict;
        }

        int fontDictIndex = _fdSelect!.GetFontDictIndex(glyphId);
        return _fontDicts![fontDictIndex];
    }

    private OutlineContour[] Transform(OutlineContour[] contours, int glyphId)
    {
        OutlineContour[] transformed = new OutlineContour[contours.Length];
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
        {
            OutlineContour contour = contours[contourIndex];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            OutlineSegment[] transformedSegments = new OutlineSegment[segments.Length];
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                OutlineSegment segment = segments[segmentIndex];
                transformedSegments[segmentIndex] = segment.Kind switch
                {
                    OutlineSegmentKind.Line => OutlineSegment.Line(
                        TransformPoint(segment.End, glyphId)
                    ),
                    OutlineSegmentKind.Quadratic => OutlineSegment.Quadratic(
                        TransformPoint(segment.Control1, glyphId),
                        TransformPoint(segment.End, glyphId)
                    ),
                    _ => OutlineSegment.Cubic(
                        TransformPoint(segment.Control1, glyphId),
                        TransformPoint(segment.Control2, glyphId),
                        TransformPoint(segment.End, glyphId)
                    ),
                };
            }

            transformed[contourIndex] = new OutlineContour(
                TransformPoint(contour.Start, glyphId),
                transformedSegments
            );
        }

        return transformed;
    }

    private Vector2 TransformPoint(Vector2 point, int glyphId)
    {
        Vector2 transformed = _fontMatrix.Transform(point, _unitsPerEm);
        ValidateFinitePoint(transformed, glyphId);
        return transformed;
    }

    private static void ValidateFinitePoints(OutlineContour[] contours, int glyphId)
    {
        foreach (OutlineContour contour in contours)
        {
            ValidateFinitePoint(contour.Start, glyphId);
            foreach (OutlineSegment segment in contour.Segments.Span)
            {
                ValidateFinitePoint(segment.End, glyphId);
                if (segment.Kind != OutlineSegmentKind.Line)
                {
                    ValidateFinitePoint(segment.Control1, glyphId);
                }

                if (segment.Kind == OutlineSegmentKind.Cubic)
                {
                    ValidateFinitePoint(segment.Control2, glyphId);
                }
            }
        }
    }

    private static void ValidateFinitePoint(Vector2 point, int glyphId)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new FontFormatException(
                $"the outline of glyph {glyphId} has a coordinate that is not finite"
            );
        }
    }

    private static FontBounds ComputeBounds(OutlineContour[] contours)
    {
        double xMin = double.MaxValue;
        double yMin = double.MaxValue;
        double xMax = double.MinValue;
        double yMax = double.MinValue;

        void Include(Vector2 point)
        {
            xMin = Math.Min(xMin, point.X);
            yMin = Math.Min(yMin, point.Y);
            xMax = Math.Max(xMax, point.X);
            yMax = Math.Max(yMax, point.Y);
        }

        foreach (OutlineContour contour in contours)
        {
            Include(contour.Start);
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            foreach (OutlineSegment segment in segments)
            {
                Include(segment.End);
                if (segment.Kind != OutlineSegmentKind.Line)
                {
                    Include(segment.Control1);
                }

                if (segment.Kind == OutlineSegmentKind.Cubic)
                {
                    Include(segment.Control2);
                }
            }
        }

        return new FontBounds(
            FloorToInt(xMin),
            FloorToInt(yMin),
            CeilingToInt(xMax),
            CeilingToInt(yMax)
        );
    }

    private static int FloorToInt(double value)
    {
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Floor(value);
    }

    private static int CeilingToInt(double value)
    {
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Ceiling(value);
    }

    private void ThrowIfGlyphOutOfRange(int glyphId)
    {
        if ((uint)glyphId < (uint)_glyphCount)
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(glyphId),
            glyphId,
            $"The face has {_glyphCount} glyph(s)."
        );
    }

    /// <summary>
    /// The mutable parse-time dedupe and budget state of one face.
    /// </summary>
    /// <remarks>
    /// Shared by the Top DICT Private DICT and every Font DICT, so a Private DICT or Local Subr
    /// INDEX referenced by many Font DICTs is parsed and counted once.
    /// </remarks>
    private sealed class CffParseState
    {
        internal Dictionary<(int Offset, int Size), CffPrivateDict> PrivateDicts { get; } = [];

        internal Dictionary<int, CffIndex> LocalSubrs { get; } = [];

        internal int LocalSubrEntries { get; set; }

        internal int PrivateDictBytes { get; set; }
    }
}
