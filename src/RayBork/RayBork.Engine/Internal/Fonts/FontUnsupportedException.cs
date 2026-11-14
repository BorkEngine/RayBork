namespace RayBork.Internal;

/// <summary>
/// A controlled failure for a valid font feature outside the parser's supported subset.
/// </summary>
/// <remarks>
/// <para>
/// This is separate from <see cref="FontFormatException"/>: the face is not corrupt. It covers valid
/// features the runtime cannot express (point-matched composite component arguments, CFF/CFF2
/// outlines) and valid faces that exceed a documented defensive parser limit (composite
/// component-resolution fanout, kern pair count, and the GPOS work caps).
/// </para>
/// <para>
/// The font loader maps this to <see cref="AssetLoadException"/> with the <c>RB1011</c> code and an
/// "unsupported feature" message; <see cref="FontFormatException"/> keeps the "invalid face" message
/// for the same code.
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
