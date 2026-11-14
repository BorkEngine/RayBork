namespace RayBork.Internal;

/// <summary>
/// A controlled failure for a valid font feature outside the parser's supported subset.
/// </summary>
/// <remarks>
/// <para>
/// This is separate from <see cref="FontFormatException"/>: the face is not corrupt. It covers valid
/// features the runtime cannot express (point-matched composite component arguments, CFF2 outlines,
/// predefined Expert charsets, an FDSelect format 4 table, an unresolvable or nested <c>seac</c>, and
/// CFF Top DICT features outside the subset such as a non-Type-2 <c>CharstringType</c>, a stroked
/// <c>PaintType</c>, a non-zero <c>StrokeWidth</c>, or <c>SyntheticBase</c>) and valid faces that
/// exceed a documented defensive parser or interpreter limit. The reason always names the limit. The
/// limits are: 16 composite levels, 20,000 points per glyph, 4,096 component resolutions per glyph, a
/// 48-value CFF operand stack, 1,000,000 executed CFF charstring tokens per glyph, 65,536 CFF hint
/// zones per glyph, 10 CFF subroutine levels, 65,536 CFF subroutine calls per glyph, 4,096 CFF
/// contours per glyph, 256 CFF Font DICTs, 1,048,576 parsed CFF local-subroutine entries per face,
/// 1,000,000 legacy kern pairs, 64 referenced GPOS lookups, 65,536 GPOS feature lookup references,
/// 32,768 GPOS subtable records, 256 GPOS pair-positioning subtables, 65,536 validated GPOS pair
/// sets, 1,000,000 validated GPOS pair records, and 262,144 validated coverage or class definition
/// entries.
/// </para>
/// <para>
/// This parser guarantees only that classification and <see cref="Reason"/>; the future font loader
/// task defines how it is surfaced as an asset diagnostic. The exception is not serializable and
/// carries no state beyond the reason.
/// </para>
/// </remarks>
internal sealed class FontUnsupportedException : Exception {
    /// <summary>Initializes an unsupported-feature failure.</summary>
    /// <param name="reason">The actionable cause, without a sentence prefix.</param>
    internal FontUnsupportedException(string reason)
        : base($"The font face uses an unsupported feature: {reason}.") {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
