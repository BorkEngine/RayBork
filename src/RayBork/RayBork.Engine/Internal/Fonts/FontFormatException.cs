namespace RayBork.Internal;

/// <summary>
/// A controlled failure raised by the managed sfnt face parser.
/// </summary>
/// <remarks>
/// <para>
/// Every malformed, truncated, inconsistent, or unsupported face is reported through this one type;
/// the parser never lets a framework exception escape for corrupt input. The runtime font loader
/// wraps it as an <see cref="AssetLoadException"/> with the <c>RB1011</c> diagnostic code and uses
/// <see cref="Reason"/> for the actionable cause.
/// </para>
/// <para>
/// The exception is not serializable and carries no state beyond the reason; it never escapes the
/// font loader.
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
