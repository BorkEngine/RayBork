namespace RayBork.Tests.Fonts;

/// <summary>
/// Builds Type 2 charstring byte sequences for the synthetic CFF tests.
/// </summary>
/// <remarks>
/// Operands are encoded with the smallest applicable Type 2 form: one byte for -107..107, two bytes
/// for +/-108..1131, the 16-bit shortint form beyond that, and the 16.16 fixed form only through
/// <see cref="Fixed"/>. The helpers cover the operators the tests exercise; raw <see cref="Bytes"/>
/// and <see cref="EscapeOperator"/> cover the reserved and unknown-opcode cases.
/// </remarks>
internal static class SyntheticCffCharStrings
{
    internal const byte HSTEM = 1;
    internal const byte VSTEM = 3;
    internal const byte VMOVETO = 4;
    internal const byte RLINETO = 5;
    internal const byte HLINETO = 6;
    internal const byte VLINETO = 7;
    internal const byte RRCURVETO = 8;
    internal const byte CALLSUBR = 10;
    internal const byte RETURN = 11;
    internal const byte ESCAPE = 12;
    internal const byte ENDCHAR = 14;
    internal const byte HSTEMHM = 18;
    internal const byte HINTMASK = 19;
    internal const byte CNTRMASK = 20;
    internal const byte RMOVETO = 21;
    internal const byte HMOVETO = 22;
    internal const byte VSTEMHM = 23;
    internal const byte RCURVELINE = 24;
    internal const byte RLINECURVE = 25;
    internal const byte VVCURVETO = 26;
    internal const byte HHCURVETO = 27;
    internal const byte CALLGSUBR = 29;
    internal const byte VHCURVETO = 30;
    internal const byte HVCURVETO = 31;

    internal const int DOTSECTION = 0;
    internal const int AND = 3;
    internal const int OR = 4;
    internal const int NOT = 5;
    internal const int ABS = 9;
    internal const int ADD = 10;
    internal const int SUB = 11;
    internal const int DIV = 12;
    internal const int NEG = 14;
    internal const int EQ = 15;
    internal const int DROP = 18;
    internal const int PUT = 20;
    internal const int GET = 21;
    internal const int IFELSE = 22;
    internal const int RANDOM = 23;
    internal const int MUL = 24;
    internal const int SQRT = 26;
    internal const int DUP = 27;
    internal const int EXCH = 28;
    internal const int INDEX = 29;
    internal const int ROLL = 30;
    internal const int HFLEX = 34;
    internal const int FLEX = 35;
    internal const int HFLEX1 = 36;
    internal const int FLEX1 = 37;

    /// <summary>Concatenates charstring fragments.</summary>
    internal static byte[] Concat(params byte[][] fragments)
    {
        int length = 0;
        foreach (byte[] fragment in fragments)
        {
            length += fragment.Length;
        }

        byte[] result = new byte[length];
        int position = 0;
        foreach (byte[] fragment in fragments)
        {
            fragment.CopyTo(result, position);
            position += fragment.Length;
        }

        return result;
    }

    /// <summary>Encodes raw byte values.</summary>
    internal static byte[] Bytes(params int[] values)
    {
        byte[] result = new byte[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            result[index] = (byte)values[index];
        }

        return result;
    }

    /// <summary>Encodes one integer operand.</summary>
    internal static byte[] Number(int value)
    {
        if (value is >= -107 and <= 107)
        {
            return [(byte)(value + 139)];
        }

        if (value is >= 108 and <= 1131)
        {
            int adjusted = value - 108;
            return [(byte)(247 + (adjusted >> 8)), (byte)adjusted];
        }

        if (value is >= -1131 and <= -108)
        {
            int adjusted = -value - 108;
            return [(byte)(251 + (adjusted >> 8)), (byte)adjusted];
        }

        if (value is >= short.MinValue and <= short.MaxValue)
        {
            return [28, (byte)(value >> 8), (byte)value];
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "A Type 2 charstring integer operand must fit a 16-bit shortint."
        );
    }

    /// <summary>Encodes one 16.16 fixed operand.</summary>
    internal static byte[] Fixed(double value)
    {
        int fixedValue = (int)Math.Round(value * 65536.0);
        return
        [
            255,
            (byte)(fixedValue >> 24),
            (byte)(fixedValue >> 16),
            (byte)(fixedValue >> 8),
            (byte)fixedValue,
        ];
    }

    /// <summary>Encodes a sequence of integer operands.</summary>
    internal static byte[] Numbers(params int[] values)
    {
        List<byte> result = [];
        foreach (int value in values)
        {
            result.AddRange(Number(value));
        }

        return [.. result];
    }

    /// <summary>Encodes one operator with integer operands.</summary>
    internal static byte[] Operator(byte operatorCode, params int[] operands) =>
        Concat(Numbers(operands), [operatorCode]);

    /// <summary>Encodes one escaped operator with integer operands.</summary>
    internal static byte[] EscapeOperator(int escapeCode, params int[] operands) =>
        Concat(Numbers(operands), [ESCAPE, (byte)escapeCode]);

    /// <summary>Prepends an optional width operand.</summary>
    internal static byte[] WithWidth(int width, byte[] charString) =>
        Concat(Number(width), charString);

    internal static byte[] RMoveTo(int dx, int dy) => Operator(RMOVETO, dx, dy);

    internal static byte[] HMoveTo(int dx) => Operator(HMOVETO, dx);

    internal static byte[] VMoveTo(int dy) => Operator(VMOVETO, dy);

    internal static byte[] RLineTo(params int[] deltas) => Operator(RLINETO, deltas);

    internal static byte[] HLineTo(params int[] deltas) => Operator(HLINETO, deltas);

    internal static byte[] VLineTo(params int[] deltas) => Operator(VLINETO, deltas);

    internal static byte[] RRCurveTo(params int[] deltas) => Operator(RRCURVETO, deltas);

    internal static byte[] HHCurveTo(params int[] deltas) => Operator(HHCURVETO, deltas);

    internal static byte[] VVCurveTo(params int[] deltas) => Operator(VVCURVETO, deltas);

    internal static byte[] HVCurveTo(params int[] deltas) => Operator(HVCURVETO, deltas);

    internal static byte[] VHCurveTo(params int[] deltas) => Operator(VHCURVETO, deltas);

    internal static byte[] RCurveLine(params int[] deltas) => Operator(RCURVELINE, deltas);

    internal static byte[] RLineCurve(params int[] deltas) => Operator(RLINECURVE, deltas);

    internal static byte[] HStem(params int[] deltas) => Operator(HSTEM, deltas);

    internal static byte[] VStem(params int[] deltas) => Operator(VSTEM, deltas);

    internal static byte[] HStemHm(params int[] deltas) => Operator(HSTEMHM, deltas);

    internal static byte[] VStemHm(params int[] deltas) => Operator(VSTEMHM, deltas);

    internal static byte[] HintMask(params byte[] mask) => Concat([HINTMASK], mask);

    internal static byte[] CntrMask(params byte[] mask) => Concat([CNTRMASK], mask);

    internal static byte[] CallSubr(int operand) => Operator(CALLSUBR, operand);

    internal static byte[] CallGSubr(int operand) => Operator(CALLGSUBR, operand);

    internal static byte[] Return() => [RETURN];

    internal static byte[] EndChar() => [ENDCHAR];

    internal static byte[] EndCharSeac(int adx, int ady, int baseCode, int accentCode) =>
        Operator(ENDCHAR, adx, ady, baseCode, accentCode);

    internal static byte[] Flex(params int[] operands) => EscapeOperator(FLEX, operands);

    internal static byte[] HFlex(params int[] operands) => EscapeOperator(HFLEX, operands);

    internal static byte[] HFlex1(params int[] operands) => EscapeOperator(HFLEX1, operands);

    internal static byte[] Flex1(params int[] operands) => EscapeOperator(FLEX1, operands);
}
