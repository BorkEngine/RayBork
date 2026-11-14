using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// A bounds-checked reader over one CFF DICT: encoded operands followed by an operator.
/// </summary>
/// <remarks>
/// <para>
/// A DICT stores numbers and operators, and every operator consumes the operands that precede it.
/// <see cref="TryRead"/> yields one operator code together with its operand count; the operands are
/// read through <see cref="Operand"/> before the next call. The reader validates every byte, rejects
/// the reserved bytes and nibbles, rejects a trailing operand run that no operator consumes, and
/// bounds the operand stack at <see cref="MAX_OPERANDS"/>. All failures are
/// <see cref="FontFormatException"/>, never a framework exception.
/// </para>
/// <para>
/// Operator codes are the single-byte value for a one-byte operator and
/// <c>0x0C00 | secondByte</c> for an escape operator. The reader is a short-lived parse-time helper;
/// it is not thread-safe and retains the sliced table bytes.
/// </para>
/// </remarks>
internal sealed class CffDictReader {
    /// <summary>The maximum number of operands one DICT operator may carry.</summary>
    internal const int MAX_OPERANDS = 48;

    /// <summary>The flag that marks an escape (<c>12 xx</c>) operator code.</summary>
    internal const int ESCAPE_OPERATOR_FLAG = 0x0C00;

    private const byte SHORT_INT = 28;
    private const byte LONG_INT = 29;
    private const byte REAL = 30;
    private const byte ONE_BYTE_INT_MAX = 246;
    private const byte TWO_BYTE_POSITIVE_MAX = 250;
    private const byte TWO_BYTE_NEGATIVE_MAX = 254;
    private const int REAL_NIBBLE_END = 0x0F;
    private const int REAL_NIBBLE_RESERVED = 0x0D;
    private const int MAX_REAL_EXPONENT = 10000;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly string _context;
    private readonly double[] _operands = new double[MAX_OPERANDS];
    private int _operandCount;
    private int _availableOperandCount;
    private int _position;

    /// <summary>Initializes a DICT reader over one slice with a diagnostic context.</summary>
    /// <param name="data">The DICT bytes.</param>
    /// <param name="context">A description such as <c>the CFF Top DICT</c> used in failure reasons.</param>
    internal CffDictReader(ReadOnlyMemory<byte> data, string context) {
        _data = data;
        _context = context;
    }

    /// <summary>Reads the next operator and its operand count.</summary>
    /// <param name="operatorCode">The one-byte or escaped operator code.</param>
    /// <param name="operandCount">The number of operands that precede the operator.</param>
    /// <returns><see langword="false"/> when the DICT ends.</returns>
    /// <exception cref="FontFormatException">
    /// The DICT is truncated, uses a reserved byte, has a malformed real operand, overflows the
    /// operand stack, or ends with operands that no operator consumes.
    /// </exception>
    internal bool TryRead(out int operatorCode, out int operandCount) {
        ReadOnlySpan<byte> data = _data.Span;
        while (true) {
            if (_position >= data.Length) {
                if (_operandCount != 0) {
                    throw new FontFormatException(
                        $"{_context} ends with {_operandCount} operand(s) that no operator consumes");
                }

                operatorCode = 0;
                operandCount = 0;
                return false;
            }

            byte value = data[_position++];
            if (value >= 32) {
                ReadNumber(data, value);
                continue;
            }

            switch (value) {
                case SHORT_INT:
                    RequireBytes(data, sizeof(short));
                    Push(BinaryPrimitives.ReadInt16BigEndian(data[_position..]));
                    _position += sizeof(short);
                    continue;
                case LONG_INT:
                    RequireBytes(data, sizeof(int));
                    Push(BinaryPrimitives.ReadInt32BigEndian(data[_position..]));
                    _position += sizeof(int);
                    continue;
                case REAL:
                    Push(ReadReal(data));
                    continue;
                default:
                    break;
            }

            if (value > 21) {
                throw new FontFormatException(
                    $"{_context} contains the reserved byte 0x{value:X2}");
            }

            if (value == 12) {
                if (_position >= data.Length) {
                    throw new FontFormatException($"{_context} ends after the escape byte 12");
                }

                operatorCode = ESCAPE_OPERATOR_FLAG | data[_position++];
            } else {
                operatorCode = value;
            }

            operandCount = _operandCount;
            _availableOperandCount = _operandCount;
            _operandCount = 0;
            return true;
        }
    }

    /// <summary>Gets one operand of the operator returned by the last <see cref="TryRead"/> call.</summary>
    /// <param name="index">The zero-based operand index.</param>
    /// <returns>The operand value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the operator's operands.</exception>
    internal double Operand(int index) {
        if ((uint)index >= (uint)_availableOperandCount) {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The operator has {_availableOperandCount} operand(s).");
        }

        return _operands[index];
    }

    private void ReadNumber(ReadOnlySpan<byte> data, byte value) {
        if (value <= ONE_BYTE_INT_MAX) {
            Push(value - 139);
            return;
        }

        if (value <= TWO_BYTE_POSITIVE_MAX) {
            RequireBytes(data, 1);
            Push(((value - 247) * 256) + data[_position] + 108);
            _position++;
            return;
        }

        if (value <= TWO_BYTE_NEGATIVE_MAX) {
            RequireBytes(data, 1);
            Push(-((value - 251) * 256) - data[_position] - 108);
            _position++;
            return;
        }

        throw new FontFormatException($"{_context} contains the reserved byte 0xFF");
    }

    private double ReadReal(ReadOnlySpan<byte> data) {
        bool negative = false;
        bool seenDigit = false;
        bool seenDot = false;
        bool seenExponent = false;
        bool exponentNegative = false;
        bool exponentDigit = false;
        double mantissa = 0;
        int fractionalDigits = 0;
        int exponent = 0;
        while (true) {
            if (_position >= data.Length) {
                throw new FontFormatException($"{_context} ends inside a real operand");
            }

            byte packed = data[_position++];
            for (int half = 0; half < 2; half++) {
                int nibble = half == 0 ? (packed >> 4) & 0x0F : packed & 0x0F;
                switch (nibble) {
                    case <= 9:
                        if (seenExponent) {
                            exponentDigit = true;
                            exponent = Math.Min((exponent * 10) + nibble, MAX_REAL_EXPONENT);
                        } else {
                            seenDigit = true;
                            mantissa = (mantissa * 10) + nibble;
                            if (seenDot) {
                                fractionalDigits++;
                            }
                        }

                        break;
                    case 0x0A:
                        if (seenDot || seenExponent) {
                            throw MalformedReal();
                        }

                        seenDot = true;
                        break;
                    case 0x0B:
                        if (seenExponent || (!seenDigit && !seenDot)) {
                            throw MalformedReal();
                        }

                        seenExponent = true;
                        break;
                    case 0x0C:
                        if (seenExponent || (!seenDigit && !seenDot)) {
                            throw MalformedReal();
                        }

                        seenExponent = true;
                        exponentNegative = true;
                        break;
                    case REAL_NIBBLE_RESERVED:
                        throw new FontFormatException(
                            $"{_context} contains the reserved nibble 0x{nibble:X1} in a real operand");
                    case 0x0E:
                        if (seenExponent || negative || seenDigit || seenDot) {
                            throw MalformedReal();
                        }

                        negative = true;
                        break;
                    default:
                        if (seenExponent && !exponentDigit) {
                            throw MalformedReal();
                        }

                        if (!seenDigit && !seenDot && (negative || seenExponent)) {
                            throw MalformedReal();
                        }

                        double value = seenDigit ? mantissa : 0;
                        if (fractionalDigits > 0) {
                            value /= Math.Pow(10, fractionalDigits);
                        }

                        if (exponent != 0) {
                            value *= Math.Pow(10, exponentNegative ? -exponent : exponent);
                        }

                        if (negative) {
                            value = -value;
                        }

                        if (!double.IsFinite(value)) {
                            throw new FontFormatException(
                                $"{_context} has a real operand outside the supported double range");
                        }

                        return value;
                }
            }
        }
    }

    private FontFormatException MalformedReal() =>
        new($"{_context} has a malformed real operand at offset {_position - 1}");

    private void Push(double value) {
        if (_operandCount >= MAX_OPERANDS) {
            throw new FontFormatException(
                $"{_context} pushes more than {MAX_OPERANDS} operands for one operator");
        }

        _operands[_operandCount++] = value;
    }

    private void RequireBytes(ReadOnlySpan<byte> data, int count) {
        if (count > data.Length - _position) {
            throw new FontFormatException(
                $"{_context} is truncated: the {count}-byte operand at offset {_position} is outside the {data.Length}-byte DICT");
        }
    }
}
