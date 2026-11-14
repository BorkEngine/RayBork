using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Interprets one CFF Type 2 charstring into raw charstring-space contours.
/// </summary>
/// <remarks>
/// <para>
/// The interpreter is created per glyph resolution and owns that resolution's operand stack,
/// transient array, contour list, and hint state; <see cref="CffFont"/> applies the FontMatrix to the
/// returned points. Operations follow the Type 2 specification: a leading width operand is consumed
/// on the first stack-clearing operator and ignored (advances come from <c>hmtx</c>), every contour is
/// closed with a straight line when its last point is not its first, the optional trailing argument of
/// <c>hvcurveto</c>/<c>vhcurveto</c> overrides the final on-axis delta, the flex operators expand to two
/// cubic segments, and <c>seac</c> composes the base glyph at the origin with the accent glyph
/// translated by <c>(adx, ady)</c>. Zero-length and other degenerate contours with no segments are
/// dropped, so a glyph without drawing operators resolves to no contours.
/// </para>
/// <para>
/// Defensive limits are checked before the outline or the operand stack grows, and all raise
/// <see cref="FontUnsupportedException"/> with the limit named: operand stack depth
/// <see cref="MAX_OPERAND_STACK"/>, subroutine nesting <see cref="MAX_SUBROUTINE_NESTING"/>, total
/// subroutine calls <see cref="MAX_SUBROUTINE_CALLS"/>, executed charstring tokens
/// <see cref="MAX_EXECUTED_TOKENS"/>, hint zones <see cref="MAX_HINT_COUNT"/>, points per glyph
/// <see cref="MAX_POINTS_PER_GLYPH"/>, and contours per glyph <see cref="MAX_CONTOURS_PER_GLYPH"/>.
/// The executed-token budget counts every number and operator decoded, including arithmetic-only
/// subroutines that add no outline data, so a runaway charstring cannot burn CPU while leaving the
/// point and contour counters untouched. The counters are shared with <c>seac</c> components, so a
/// composite cannot multiply them. Malformed instructions — unknown operators, stack underflow, a
/// transient index outside its fixed <see cref="TRANSIENT_ARRAY_SIZE"/>-entry array, a subroutine
/// index outside its INDEX, a <c>hintmask</c> whose mask bytes are past the end, a drawing operator
/// before the first <c>moveto</c>, and unsupported operand counts — raise
/// <see cref="FontFormatException"/>.
/// </para>
/// <para>
/// <c>index</c> clamps a negative depth to the top of the stack and rejects a depth past the stack;
/// <c>roll</c> reduces its shift modulo the element count and rotates toward the top for a positive
/// shift, matching PostScript. <c>random</c> is deterministic: it always pushes
/// <see cref="DETERMINISTIC_RANDOM_VALUE"/> so parsing the same face twice produces identical
/// outlines. <c>div</c> by zero and the square root of a negative value produce zero, the defined
/// fallbacks for the specification's undefined results. The transient array is zero-initialized, so
/// reading an unwritten entry is deterministic. The deprecated no-argument <c>dotsection</c> operator
/// is accepted and treated as a stack-clearing no-op. <c>endchar</c> ends the glyph even when it
/// appears in a called subroutine, because some real fonts terminate from a shared subroutine; bytes
/// after it are ignored. The instance is mutable and single-use; it is never shared across threads.
/// Its operand-stack and transient scratch arrays are rented from the shared array pool and returned
/// when <see cref="Run"/> completes.
/// </para>
/// </remarks>
internal sealed class CffType2Interpreter {
    /// <summary>The maximum Type 2 operand stack depth.</summary>
    internal const int MAX_OPERAND_STACK = 48;

    /// <summary>The number of entries in the <c>put</c>/<c>get</c> transient array.</summary>
    internal const int TRANSIENT_ARRAY_SIZE = 32;

    /// <summary>The maximum subroutine call nesting depth.</summary>
    internal const int MAX_SUBROUTINE_NESTING = 10;

    /// <summary>The maximum number of subroutine calls one glyph resolution may execute.</summary>
    internal const int MAX_SUBROUTINE_CALLS = 65536;

    /// <summary>The maximum number of outline points one glyph resolution may produce.</summary>
    internal const int MAX_POINTS_PER_GLYPH = 20000;

    /// <summary>The maximum number of contours one glyph resolution may start.</summary>
    internal const int MAX_CONTOURS_PER_GLYPH = 4096;

    /// <summary>The maximum number of charstring tokens one glyph resolution may decode.</summary>
    internal const int MAX_EXECUTED_TOKENS = 1000000;

    /// <summary>The maximum number of hint zones one glyph resolution may declare.</summary>
    internal const int MAX_HINT_COUNT = 65536;

    /// <summary>The fixed value that <c>random</c> pushes, keeping glyph generation reproducible.</summary>
    internal const double DETERMINISTIC_RANDOM_VALUE = 0.5;

    private const byte SHORT_INT = 28;
    private const byte ESCAPE = 12;
    private const byte HSTEM = 1;
    private const byte VSTEM = 3;
    private const byte VMOVETO = 4;
    private const byte RLINETO = 5;
    private const byte HLINETO = 6;
    private const byte VLINETO = 7;
    private const byte RRCURVETO = 8;
    private const byte CALLSUBR = 10;
    private const byte RETURN = 11;
    private const byte ENDCHAR = 14;
    private const byte HSTEMHM = 18;
    private const byte HINTMASK = 19;
    private const byte CNTRMASK = 20;
    private const byte RMOVETO = 21;
    private const byte HMOVETO = 22;
    private const byte VSTEMHM = 23;
    private const byte RCURVELINE = 24;
    private const byte RLINECURVE = 25;
    private const byte VVCURVETO = 26;
    private const byte HHCURVETO = 27;
    private const byte CALLGSUBR = 29;
    private const byte VHCURVETO = 30;
    private const byte HVCURVETO = 31;

    private const int DOTSECTION = 0;
    private const int AND = 3;
    private const int OR = 4;
    private const int NOT = 5;
    private const int ABS = 9;
    private const int ADD = 10;
    private const int SUB = 11;
    private const int DIV = 12;
    private const int NEG = 14;
    private const int EQ = 15;
    private const int DROP = 18;
    private const int PUT = 20;
    private const int GET = 21;
    private const int IFELSE = 22;
    private const int RANDOM = 23;
    private const int MUL = 24;
    private const int SQRT = 26;
    private const int DUP = 27;
    private const int EXCH = 28;
    private const int INDEX = 29;
    private const int ROLL = 30;
    private const int HFLEX = 34;
    private const int FLEX = 35;
    private const int HFLEX1 = 36;
    private const int FLEX1 = 37;

    private readonly CffFont _font;
    private readonly CffGlyphBudget _budget;
    private readonly CffIndex _localSubrs;
    private readonly bool _allowSeac;
    private readonly string _context;
    private readonly double[] _stack;
    private readonly double[] _transient;
    private bool _arraysReturned;
    private readonly List<OutlineContour> _contours = [];
    private List<OutlineSegment>? _segments;
    private Vector2 _current;
    private Vector2 _start;
    private bool _hasContour;
    private bool _widthParsed;
    private bool _glyphEnded;
    private int _stackCount;
    private long _hintCount;
    private int _subroutineNesting;

    /// <summary>Initializes one glyph-resolution interpreter.</summary>
    /// <param name="font">The face that owns the charstrings, global subrs, and charset.</param>
    /// <param name="budget">The counters shared with any <c>seac</c> component.</param>
    /// <param name="privateDict">The private dictionary selected for the glyph.</param>
    /// <param name="glyphId">The glyph identifier, used only in failure reasons.</param>
    /// <param name="allowSeac">
    /// <see langword="false"/> when the interpreter is resolving a <c>seac</c> component, which the
    /// specification forbids from nesting.
    /// </param>
    internal CffType2Interpreter(
        CffFont font,
        CffGlyphBudget budget,
        CffPrivateDict privateDict,
        int glyphId,
        bool allowSeac) {
        _font = font;
        _budget = budget;
        _localSubrs = privateDict.LocalSubrs;
        _allowSeac = allowSeac;
        _context = $"the CFF charstring of glyph {glyphId}";
        _stack = ArrayPool<double>.Shared.Rent(MAX_OPERAND_STACK);
        _transient = ArrayPool<double>.Shared.Rent(TRANSIENT_ARRAY_SIZE);
        Array.Clear(_transient, 0, TRANSIENT_ARRAY_SIZE);
    }

    /// <summary>Gets the Type 2 subroutine bias for one subroutine count.</summary>
    /// <param name="subroutineCount">The number of subroutines in the INDEX.</param>
    /// <returns>The bias: 107, 1,131, or 32,768.</returns>
    internal static int GetSubroutineBias(int subroutineCount) => subroutineCount switch {
        < 1240 => 107,
        < 33900 => 1131,
        _ => 32768,
    };

    /// <summary>Runs one glyph's charstring and closes its last contour.</summary>
    /// <param name="charString">The charstring bytes from the CharStrings INDEX.</param>
    /// <returns>The closed contours in raw charstring coordinates, in drawing order.</returns>
    /// <exception cref="FontFormatException">The charstring or a called subroutine is malformed.</exception>
    /// <exception cref="FontUnsupportedException">The glyph exceeds an interpreter limit or uses an unsupported feature.</exception>
    internal OutlineContour[] Run(ReadOnlyMemory<byte> charString) {
        try {
            Execute(charString.Span, isSubroutine: false);
            FinishContour();
            return _contours.Count == 0 ? [] : [.. _contours];
        } finally {
            if (!_arraysReturned) {
                _arraysReturned = true;
                ArrayPool<double>.Shared.Return(_stack);
                ArrayPool<double>.Shared.Return(_transient);
            }
        }
    }

    private void Execute(ReadOnlySpan<byte> charString, bool isSubroutine) {
        int position = 0;
        while (position < charString.Length && !_glyphEnded) {
            _budget.ExecutedTokens++;
            if (_budget.ExecutedTokens > MAX_EXECUTED_TOKENS) {
                throw new FontUnsupportedException(
                    $"{_context} exceeds the {MAX_EXECUTED_TOKENS} executed-token limit");
            }

            byte value = charString[position++];
            if (value >= 32) {
                Push(ReadNumber(charString, ref position, value));
                continue;
            }

            if (value == SHORT_INT) {
                RequireCharStringBytes(charString, position, sizeof(short));
                Push(BinaryPrimitives.ReadInt16BigEndian(charString[position..]));
                position += sizeof(short);
                continue;
            }

            switch (value) {
                case HSTEM:
                case VSTEM:
                case HSTEMHM:
                case VSTEMHM:
                    ConsumeStemOperands();
                    break;
                case HINTMASK:
                case CNTRMASK:
                    position += ConsumeHintMask(charString, position);
                    break;
                case RMOVETO:
                    ExecuteRMoveTo();
                    break;
                case HMOVETO:
                    ExecuteHMoveTo();
                    break;
                case VMOVETO:
                    ExecuteVMoveTo();
                    break;
                case RLINETO:
                    ExecuteRLineTo();
                    break;
                case HLINETO:
                    ExecuteAlternatingLineTo(horizontalFirst: true);
                    break;
                case VLINETO:
                    ExecuteAlternatingLineTo(horizontalFirst: false);
                    break;
                case RRCURVETO:
                    ExecuteRRCurveTo();
                    break;
                case HHCURVETO:
                    ExecuteHHCurveTo();
                    break;
                case VVCURVETO:
                    ExecuteVVCurveTo();
                    break;
                case HVCURVETO:
                    ExecuteAlternatingCurveTo(horizontalFirst: true);
                    break;
                case VHCURVETO:
                    ExecuteAlternatingCurveTo(horizontalFirst: false);
                    break;
                case RCURVELINE:
                    ExecuteRCurveLine();
                    break;
                case RLINECURVE:
                    ExecuteRLineCurve();
                    break;
                case CALLSUBR:
                    ExecuteCallSubroutine(_localSubrs, "local");
                    break;
                case CALLGSUBR:
                    ExecuteCallSubroutine(_font.GlobalSubrs, "global");
                    break;
                case RETURN:
                    if (!isSubroutine) {
                        throw new FontFormatException(
                            $"{_context} contains return outside a subroutine");
                    }

                    return;
                case ENDCHAR:
                    ExecuteEndChar();
                    _glyphEnded = true;
                    return;
                case ESCAPE:
                    ExecuteEscape(charString, ref position);
                    break;
                default:
                    throw new FontFormatException(
                        $"{_context} contains the unknown operator {value}");
            }
        }
    }

    private void ExecuteRMoveTo() {
        int count = TakeMoveOperands(2);
        if (count != 2) {
            throw InvalidOperandCount("rmoveto", count, "2 operands plus an optional width");
        }

        double dx = _stack[0];
        double dy = _stack[1];
        ClearStack();
        MoveTo(dx, dy);
    }

    private void ExecuteHMoveTo() {
        int count = TakeMoveOperands(1);
        if (count != 1) {
            throw InvalidOperandCount("hmoveto", count, "1 operand plus an optional width");
        }

        double dx = _stack[0];
        ClearStack();
        MoveTo(dx, 0);
    }

    private void ExecuteVMoveTo() {
        int count = TakeMoveOperands(1);
        if (count != 1) {
            throw InvalidOperandCount("vmoveto", count, "1 operand plus an optional width");
        }

        double dy = _stack[0];
        ClearStack();
        MoveTo(0, dy);
    }

    private void ExecuteRLineTo() {
        int count = _stackCount;
        if ((count & 1) != 0) {
            throw new FontFormatException(
                $"{_context} passes an odd number of operands to rlineto");
        }

        for (int index = 0; index < count; index += 2) {
            AddLine(_stack[index], _stack[index + 1]);
        }

        ClearStack();
    }

    private void ExecuteAlternatingLineTo(bool horizontalFirst) {
        int count = _stackCount;
        bool horizontal = horizontalFirst;
        for (int index = 0; index < count; index++) {
            if (horizontal) {
                AddLine(_stack[index], 0);
            } else {
                AddLine(0, _stack[index]);
            }

            horizontal = !horizontal;
        }

        ClearStack();
    }

    private void ExecuteRRCurveTo() {
        int count = _stackCount;
        if (count % 6 != 0) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to rrcurveto; the count must be a multiple of 6");
        }

        for (int index = 0; index < count; index += 6) {
            AddCurve(
                _stack[index],
                _stack[index + 1],
                _stack[index + 2],
                _stack[index + 3],
                _stack[index + 4],
                _stack[index + 5]);
        }

        ClearStack();
    }

    private void ExecuteRCurveLine() {
        int count = _stackCount;
        if (count < 8 || (count - 2) % 6 != 0) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to rcurveline; one or more curve sextets and one line pair are required");
        }

        int curveCount = (count - 2) / 6;
        for (int index = 0; index < curveCount; index++) {
            int offset = index * 6;
            AddCurve(
                _stack[offset],
                _stack[offset + 1],
                _stack[offset + 2],
                _stack[offset + 3],
                _stack[offset + 4],
                _stack[offset + 5]);
        }

        int lineOffset = curveCount * 6;
        AddLine(_stack[lineOffset], _stack[lineOffset + 1]);
        ClearStack();
    }

    private void ExecuteRLineCurve() {
        int count = _stackCount;
        if (count < 8 || (count - 6) % 2 != 0) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to rlinecurve; one or more line pairs and one curve sextet are required");
        }

        int lineCount = (count - 6) / 2;
        for (int index = 0; index < lineCount; index++) {
            int offset = index * 2;
            AddLine(_stack[offset], _stack[offset + 1]);
        }

        int curveOffset = lineCount * 2;
        AddCurve(
            _stack[curveOffset],
            _stack[curveOffset + 1],
            _stack[curveOffset + 2],
            _stack[curveOffset + 3],
            _stack[curveOffset + 4],
            _stack[curveOffset + 5]);
        ClearStack();
    }

    private void ExecuteHHCurveTo() {
        int count = _stackCount;
        int index = 0;
        double dy1 = 0;
        if ((count & 1) != 0) {
            dy1 = _stack[index++];
        }

        int remaining = count - index;
        if (remaining < 4 || remaining % 4 != 0) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to hhcurveto; 4n or 4n+1 operands are required");
        }

        while (index < count) {
            AddCurve(_stack[index], dy1, _stack[index + 1], _stack[index + 2], _stack[index + 3], 0);
            index += 4;
            dy1 = 0;
        }

        ClearStack();
    }

    private void ExecuteVVCurveTo() {
        int count = _stackCount;
        int index = 0;
        double dx1 = 0;
        if ((count & 1) != 0) {
            dx1 = _stack[index++];
        }

        int remaining = count - index;
        if (remaining < 4 || remaining % 4 != 0) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to vvcurveto; 4n or 4n+1 operands are required");
        }

        while (index < count) {
            AddCurve(dx1, _stack[index], _stack[index + 1], _stack[index + 2], 0, _stack[index + 3]);
            index += 4;
            dx1 = 0;
        }

        ClearStack();
    }

    private void ExecuteAlternatingCurveTo(bool horizontalFirst) {
        int count = _stackCount;
        if (count < 4 || (count % 4 is not 0 and not 1)) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to an alternating curveto; 4n or 4n+1 operands are required");
        }

        int index = 0;
        bool horizontal = horizontalFirst;
        while (index < count) {
            bool hasExtra = (count - index) == 5;
            if (horizontal) {
                AddCurve(
                    _stack[index],
                    0,
                    _stack[index + 1],
                    _stack[index + 2],
                    hasExtra ? _stack[index + 4] : 0,
                    _stack[index + 3]);
            } else {
                AddCurve(
                    0,
                    _stack[index],
                    _stack[index + 1],
                    _stack[index + 2],
                    _stack[index + 3],
                    hasExtra ? _stack[index + 4] : 0);
            }

            index += hasExtra ? 5 : 4;
            horizontal = !horizontal;
        }

        ClearStack();
    }

    private void ExecuteCallSubroutine(CffIndex subrs, string kind) {
        double rawIndex = Pop();
        if (rawIndex != Math.Floor(rawIndex) || rawIndex < int.MinValue || rawIndex > int.MaxValue) {
            throw new FontFormatException(
                $"{_context} calls a {kind} subroutine with the non-integer index {rawIndex}");
        }

        int index = (int)rawIndex + GetSubroutineBias(subrs.Count);
        if ((uint)index >= (uint)subrs.Count) {
            throw new FontFormatException(
                $"{_context} calls the {kind} subroutine index {index}; the INDEX has {subrs.Count} object(s)");
        }

        _budget.SubroutineCalls++;
        if (_budget.SubroutineCalls > MAX_SUBROUTINE_CALLS) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_SUBROUTINE_CALLS} total subroutine call limit");
        }

        if (_subroutineNesting >= MAX_SUBROUTINE_NESTING) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_SUBROUTINE_NESTING}-level subroutine nesting limit");
        }

        _subroutineNesting++;
        Execute(subrs.Get(index).Span, isSubroutine: true);
        _subroutineNesting--;
    }

    private void ExecuteEndChar() {
        int count = TakeEndCharOperands();
        if (count == 0) {
            return;
        }

        if (count != 4) {
            throw new FontFormatException(
                $"{_context} passes {count} operand(s) to endchar; 0 or 4 plus an optional width are required");
        }

        double adx = _stack[0];
        double ady = _stack[1];
        double baseCode = _stack[2];
        double accentCode = _stack[3];
        ClearStack();
        ComposeSeac(adx, ady, baseCode, accentCode);
    }

    private void ExecuteEscape(ReadOnlySpan<byte> charString, ref int position) {
        RequireCharStringBytes(charString, position, 1);
        int code = charString[position++];
        switch (code) {
            case DOTSECTION:
                ClearStack();
                break;
            case AND: {
                double second = Pop();
                double first = Pop();
                Push(first != 0 && second != 0 ? 1 : 0);
                break;
            }

            case OR: {
                double second = Pop();
                double first = Pop();
                Push(first != 0 || second != 0 ? 1 : 0);
                break;
            }

            case NOT:
                Push(Pop() == 0 ? 1 : 0);
                break;
            case ABS:
                Push(Math.Abs(Pop()));
                break;
            case ADD: {
                double second = Pop();
                double first = Pop();
                Push(first + second);
                break;
            }

            case SUB: {
                double second = Pop();
                double first = Pop();
                Push(first - second);
                break;
            }

            case DIV: {
                double second = Pop();
                double first = Pop();
                Push(second == 0 ? 0 : first / second);
                break;
            }

            case NEG:
                Push(-Pop());
                break;
            case EQ: {
                double second = Pop();
                double first = Pop();
                Push(first == second ? 1 : 0);
                break;
            }

            case DROP:
                Pop();
                break;
            case PUT: {
                int index = ToTransientIndex(Pop());
                _transient[index] = Pop();
                break;
            }

            case GET: {
                int index = ToTransientIndex(Pop());
                Push(_transient[index]);
                break;
            }

            case IFELSE: {
                double secondValue = Pop();
                double firstValue = Pop();
                double secondResult = Pop();
                double firstResult = Pop();
                Push(firstValue <= secondValue ? firstResult : secondResult);
                break;
            }

            case RANDOM:
                Push(DETERMINISTIC_RANDOM_VALUE);
                break;
            case MUL: {
                double second = Pop();
                double first = Pop();
                Push(first * second);
                break;
            }

            case SQRT: {
                double value = Pop();
                Push(value < 0 ? 0 : Math.Sqrt(value));
                break;
            }

            case DUP:
                if (_stackCount == 0) {
                    throw Underflow();
                }

                Push(_stack[_stackCount - 1]);
                break;
            case EXCH:
                if (_stackCount < 2) {
                    throw Underflow();
                }

                (_stack[_stackCount - 1], _stack[_stackCount - 2]) =
                    (_stack[_stackCount - 2], _stack[_stackCount - 1]);
                break;
            case INDEX: {
                int index = ToIntegerOperand(Pop(), "index");
                if (_stackCount == 0) {
                    throw Underflow();
                }

                if (index < 0) {
                    index = 0;
                }

                if (index >= _stackCount) {
                    throw new FontFormatException(
                        $"{_context} indexes {index} value(s) below the stack top but only {_stackCount} value(s) remain");
                }

                Push(_stack[_stackCount - 1 - index]);
                break;
            }

            case ROLL:
                ExecuteRoll();
                break;
            case HFLEX:
                ExecuteHFlex();
                break;
            case FLEX:
                ExecuteFlex();
                break;
            case HFLEX1:
                ExecuteHFlex1();
                break;
            case FLEX1:
                ExecuteFlex1();
                break;
            default:
                throw new FontFormatException(
                    $"{_context} contains the unknown escape operator 12 {code}");
        }
    }

    private void ExecuteRoll() {
        int shift = ToIntegerOperand(Pop(), "roll shift");
        int count = ToIntegerOperand(Pop(), "roll count");
        if (count < 0) {
            throw new FontFormatException($"{_context} rolls the negative element count {count}");
        }

        if (count > _stackCount) {
            throw new FontFormatException(
                $"{_context} rolls {count} element(s) with only {_stackCount} on the operand stack");
        }

        if (count <= 1) {
            return;
        }

        shift %= count;
        if (shift < 0) {
            shift += count;
        }

        int start = _stackCount - count;
        for (int step = 0; step < shift; step++) {
            double last = _stack[_stackCount - 1];
            for (int index = _stackCount - 1; index > start; index--) {
                _stack[index] = _stack[index - 1];
            }

            _stack[start] = last;
        }
    }

    private void ExecuteHFlex() {
        RequireExactOperands(7, "hflex");
        double dy2 = _stack[2];
        AddCurve(_stack[0], 0, _stack[1], dy2, _stack[3], 0);
        AddCurve(_stack[4], 0, _stack[5], -dy2, _stack[6], 0);
        ClearStack();
    }

    private void ExecuteFlex() {
        RequireExactOperands(13, "flex");
        AddCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
        AddCurve(_stack[6], _stack[7], _stack[8], _stack[9], _stack[10], _stack[11]);
        ClearStack();
    }

    private void ExecuteHFlex1() {
        RequireExactOperands(9, "hflex1");
        double dy1 = _stack[1];
        double dy2 = _stack[3];
        double dy5 = _stack[7];
        AddCurve(_stack[0], dy1, _stack[2], dy2, _stack[4], 0);
        AddCurve(_stack[5], 0, _stack[6], dy5, _stack[8], -(dy1 + dy2 + dy5));
        ClearStack();
    }

    private void ExecuteFlex1() {
        RequireExactOperands(11, "flex1");
        double dx = _stack[0] + _stack[2] + _stack[4] + _stack[6] + _stack[8];
        double dy = _stack[1] + _stack[3] + _stack[5] + _stack[7] + _stack[9];
        double dx6;
        double dy6;
        if (Math.Abs(dx) > Math.Abs(dy)) {
            dx6 = _stack[10];
            dy6 = -dy;
        } else {
            dx6 = -dx;
            dy6 = _stack[10];
        }

        AddCurve(_stack[0], _stack[1], _stack[2], _stack[3], _stack[4], _stack[5]);
        AddCurve(_stack[6], _stack[7], _stack[8], _stack[9], dx6, dy6);
        ClearStack();
    }

    private void ComposeSeac(double adx, double ady, double baseCode, double accentCode) {
        if (!_allowSeac) {
            throw new FontUnsupportedException(
                $"{_context} composes a seac glyph while resolving a seac component; nested seac is not supported");
        }

        if (_font.IsCidKeyed) {
            throw new FontUnsupportedException(
                $"{_context} uses seac in a CID-keyed face");
        }

        int baseGlyphId = ResolveSeacComponent(baseCode, "base");
        int accentGlyphId = ResolveSeacComponent(accentCode, "accent");
        FinishContour();
        OutlineContour[] baseContours = _font.ReadRawContours(baseGlyphId, _budget, allowSeac: false);
        OutlineContour[] accentContours = _font.ReadRawContours(accentGlyphId, _budget, allowSeac: false);
        Vector2 accentOffset = new((float)adx, (float)ady);
        foreach (OutlineContour contour in baseContours) {
            _contours.Add(contour);
        }

        foreach (OutlineContour contour in accentContours) {
            _contours.Add(Translate(contour, accentOffset));
        }
    }

    private int ResolveSeacComponent(double rawCode, string role) {
        if (rawCode != Math.Floor(rawCode) || rawCode < 0 || rawCode >= CffStandardEncoding.CODE_COUNT) {
            throw new FontUnsupportedException(
                $"{_context} cannot resolve the seac {role} character code {rawCode}; Standard Encoding codes are integers in 0..255");
        }

        int code = (int)rawCode;
        if (!_font.TryGetStandardEncodingGlyphId(code, out int glyphId)) {
            throw new FontUnsupportedException(
                $"{_context} cannot resolve the seac {role} character code {code} through the CFF Standard Encoding and charset");
        }

        return glyphId;
    }

    private void MoveTo(double dx, double dy) {
        Vector2 next = _current + new Vector2((float)dx, (float)dy);
        FinishContour();
        _budget.Contours++;
        if (_budget.Contours > MAX_CONTOURS_PER_GLYPH) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_CONTOURS_PER_GLYPH} contour limit");
        }

        _segments = [];
        _current = next;
        _start = next;
        _hasContour = true;
        ReservePoints(1);
    }

    private void AddLine(double dx, double dy) {
        RequireContour("line");
        ReservePoints(1);
        _current += new Vector2((float)dx, (float)dy);
        _segments!.Add(OutlineSegment.Line(_current));
    }

    private void AddCurve(double dx1, double dy1, double dx2, double dy2, double dx3, double dy3) {
        RequireContour("curve");
        ReservePoints(3);
        Vector2 control1 = _current + new Vector2((float)dx1, (float)dy1);
        Vector2 control2 = control1 + new Vector2((float)dx2, (float)dy2);
        Vector2 end = control2 + new Vector2((float)dx3, (float)dy3);
        _current = end;
        _segments!.Add(OutlineSegment.Cubic(control1, control2, end));
    }

    private void FinishContour() {
        if (!_hasContour) {
            return;
        }

        if (_segments!.Count > 0) {
            if (_current != _start) {
                _segments.Add(OutlineSegment.Line(_start));
            }

            _contours.Add(new OutlineContour(_start, _segments.ToArray()));
        }

        _segments = null;
        _hasContour = false;
    }

    private void RequireContour(string operatorName) {
        if (_hasContour) {
            return;
        }

        throw new FontFormatException(
            $"{_context} draws a {operatorName} before its first moveto");
    }

    private void ReservePoints(int count) {
        _budget.Points += count;
        if (_budget.Points > MAX_POINTS_PER_GLYPH) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_POINTS_PER_GLYPH} point limit");
        }
    }

    private void ConsumeStemOperands() {
        AddHints(TakeStemOperands());
        ClearStack();
    }

    private int ConsumeHintMask(ReadOnlySpan<byte> charString, int position) {
        AddHints(TakeStemOperands());
        ClearStack();
        long maskBytes = (_hintCount + 7) / 8;
        if (maskBytes < 0) {
            throw new FontFormatException(
                $"{_context} computes the negative hint mask length {maskBytes}");
        }

        if (maskBytes > charString.Length - position) {
            throw new FontFormatException(
                $"{_context} ends before its {maskBytes}-byte hint mask");
        }

        return (int)maskBytes;
    }

    private void AddHints(int operandCount) {
        _hintCount += operandCount / 2;
        if (_hintCount > MAX_HINT_COUNT) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_HINT_COUNT} hint limit");
        }
    }

    private int TakeStemOperands() {
        int count = _stackCount;
        if (!_widthParsed) {
            _widthParsed = true;
            if ((count & 1) != 0) {
                RemoveFirstOperand();
                count--;
            }
        } else if ((count & 1) != 0) {
            throw new FontFormatException(
                $"{_context} passes an odd number of operands to a stem operator");
        }

        return count;
    }

    private int TakeMoveOperands(int expected) {
        int count = _stackCount;
        if (!_widthParsed) {
            _widthParsed = true;
            if (count == expected + 1) {
                RemoveFirstOperand();
                count--;
            }
        }

        return count;
    }

    private int TakeEndCharOperands() {
        int count = _stackCount;
        if (!_widthParsed) {
            _widthParsed = true;
            if (count is 1 or 5) {
                RemoveFirstOperand();
                count--;
            }
        }

        return count;
    }

    private void RemoveFirstOperand() {
        Array.Copy(_stack, 1, _stack, 0, _stackCount - 1);
        _stackCount--;
    }

    private void Push(double value) {
        if (_stackCount >= MAX_OPERAND_STACK) {
            throw new FontUnsupportedException(
                $"{_context} exceeds the {MAX_OPERAND_STACK}-value operand stack limit");
        }

        _stack[_stackCount++] = value;
    }

    private double Pop() {
        if (_stackCount == 0) {
            throw Underflow();
        }

        return _stack[--_stackCount];
    }

    private void ClearStack() => _stackCount = 0;

    private FontFormatException Underflow() =>
        new($"{_context} underflows its operand stack");

    private FontFormatException InvalidOperandCount(string operatorName, int count, string expected) =>
        new($"{_context} passes {count} operand(s) to {operatorName}; {expected} required");

    private void RequireExactOperands(int expected, string operatorName) {
        if (_stackCount != expected) {
            throw InvalidOperandCount(operatorName, _stackCount, $"{expected} operands");
        }
    }

    private int ToTransientIndex(double value) {
        if (value != Math.Floor(value) || value < 0 || value >= TRANSIENT_ARRAY_SIZE) {
            throw new FontFormatException(
                $"{_context} uses the transient array index {value}; the array has {TRANSIENT_ARRAY_SIZE} entries");
        }

        return (int)value;
    }

    private int ToIntegerOperand(double value, string name) {
        if (value != Math.Floor(value) || value < int.MinValue || value > int.MaxValue) {
            throw new FontFormatException(
                $"{_context} passes the non-integer {name} operand {value}");
        }

        return (int)value;
    }

    private double ReadNumber(ReadOnlySpan<byte> charString, ref int position, byte value) {
        if (value <= 246) {
            return value - 139;
        }

        if (value <= 250) {
            RequireCharStringBytes(charString, position, 1);
            int number = ((value - 247) * 256) + charString[position] + 108;
            position++;
            return number;
        }

        if (value <= 254) {
            RequireCharStringBytes(charString, position, 1);
            int number = -((value - 251) * 256) - charString[position] - 108;
            position++;
            return number;
        }

        RequireCharStringBytes(charString, position, sizeof(int));
        int fixedValue = BinaryPrimitives.ReadInt32BigEndian(charString[position..]);
        position += sizeof(int);
        return fixedValue / 65536.0;
    }

    private void RequireCharStringBytes(ReadOnlySpan<byte> charString, int position, int count) {
        if (count > charString.Length - position) {
            throw new FontFormatException(
                $"{_context} is truncated: the {count}-byte operand at offset {position} is outside the {charString.Length}-byte charstring");
        }
    }

    private static OutlineContour Translate(OutlineContour contour, Vector2 offset) {
        ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
        OutlineSegment[] translated = new OutlineSegment[segments.Length];
        for (int index = 0; index < segments.Length; index++) {
            OutlineSegment segment = segments[index];
            translated[index] = segment.Kind switch {
                OutlineSegmentKind.Line => OutlineSegment.Line(segment.End + offset),
                OutlineSegmentKind.Quadratic => OutlineSegment.Quadratic(
                    segment.Control1 + offset,
                    segment.End + offset),
                _ => OutlineSegment.Cubic(
                    segment.Control1 + offset,
                    segment.Control2 + offset,
                    segment.End + offset),
            };
        }

        return new OutlineContour(contour.Start + offset, translated);
    }
}
