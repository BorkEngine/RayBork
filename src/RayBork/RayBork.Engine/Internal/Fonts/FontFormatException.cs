namespace RayBork.Internal;

/// <summary>
/// A controlled failure raised by the managed sfnt face parser.
/// </summary>
/// <remarks>
/// <para>
/// Every malformed, truncated, or inconsistent face is reported through this type; the parser never
/// lets a framework exception escape for corrupt input. A structurally valid face outside the
/// supported subset, or one that exceeds a documented defensive parser limit, is reported separately
/// as <see cref="FontUnsupportedException"/>.
/// </para>
/// <para>
/// This parser guarantees only that classification and <see cref="Reason"/>; how the runtime surfaces
/// it as an asset diagnostic is owned by the future font loader task. The exception is not
/// serializable and carries no state beyond the reason.
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
