using System.Globalization;

namespace RayBork.Bake.Fonts;

/// <summary>
/// Validates the font sidecar's preload ranges without parsing glyphs.
/// </summary>
/// <remarks>
/// A range is either one codepoint, <c>U+XXXX</c>, or an inclusive pair,
/// <c>U+XXXX-U+YYYY</c>. The documented compact form <c>U+XXXX-YYYY</c> is accepted for the upper
/// bound, matching the sidecar example. The hex value is four to six digits and must not exceed
/// <c>U+10FFFF</c>. M6 consumes the stored ranges to generate MSDF glyphs.
/// </remarks>
internal static class FontRangeValidator {
    private const string PREFIX = "U+";
    private const int MAX_CODEPOINT = 0x10FFFF;

    /// <summary>Validates one range string.</summary>
    /// <param name="range">The authored range.</param>
    /// <param name="reason">The failure reason when invalid.</param>
    /// <returns><see langword="true"/> when the syntax and bounds are valid.</returns>
    internal static bool TryValidate(string range, out string? reason) {
        if (string.IsNullOrEmpty(range)) {
            reason = "the range is empty";
            return false;
        }

        int separator = range.IndexOf('-', StringComparison.Ordinal);
        string firstText;
        string secondText;
        if (separator < 0) {
            firstText = range;
            secondText = range;
        } else {
            if (range.IndexOf('-', separator + 1) >= 0) {
                reason = "the range contains more than one '-'";
                return false;
            }

            firstText = range[..separator];
            secondText = range[(separator + 1)..];
        }

        if (!TryParseCodepoint(firstText, requirePrefix: true, out int first, out reason)) {
            return false;
        }

        if (!TryParseCodepoint(secondText, requirePrefix: false, out int second, out reason)) {
            return false;
        }

        if (first > second) {
            reason = $"the range start U+{first:X4} is greater than its end U+{second:X4}";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TryParseCodepoint(
        string text,
        bool requirePrefix,
        out int codepoint,
        out string? reason) {
        codepoint = 0;
        string digits;
        if (text.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase)) {
            digits = text[PREFIX.Length..];
        } else if (requirePrefix) {
            reason = $"the codepoint '{text}' does not start with 'U+'";
            return false;
        } else {
            digits = text;
        }

        if (digits.Length is < 4 or > 6) {
            reason = $"the codepoint '{text}' must contain four to six hex digits";
            return false;
        }

        foreach (char digit in digits) {
            if (!char.IsAsciiHexDigit(digit)) {
                reason = $"the codepoint '{text}' contains the non-hex digit '{digit}'";
                return false;
            }
        }

        codepoint = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (codepoint > MAX_CODEPOINT) {
            reason = $"the codepoint U+{codepoint:X} exceeds U+10FFFF";
            return false;
        }

        reason = null;
        return true;
    }
}
