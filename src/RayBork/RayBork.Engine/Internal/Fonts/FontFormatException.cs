namespace RayBork.Internal;

/// <summary>
/// A controlled failure raised by the managed sfnt face parser.
/// </summary>
/// <remarks>
/// <para>
/// Every malformed, truncated, or inconsistent face is reported through this type; the parser never
/// lets a framework exception escape for corrupt input. Valid faces outside the supported subset or
/// past a defensive parser limit are reported separately as <see cref="FontUnsupportedException"/>.
/// </para>
/// <para>
/// The font loader maps this to <see cref="AssetLoadException"/> with the <c>RB1011</c> code and an
/// "invalid face" message, reading <see cref="Reason"/> for the actionable cause.
/// </para>
/// <para>
/// The exception is not serializable and carries no state beyond the reason.
/// </para>
/// </remarks>
internal sealed class FontFormatException : Exception {
    /// <summary>Initializes a font format failure.</summary>
    /// <param name="reason">The actionable cause, without a sentence prefix.</param>
    internal FontFormatException(string reason)
        : base($"The font face is invalid: {reason}.") {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
