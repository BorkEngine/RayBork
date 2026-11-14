namespace RayBork.Internal;

/// <summary>
/// A controlled failure for a valid font feature that the runtime cannot express yet.
/// </summary>
/// <remarks>
/// This is separate from <see cref="FontFormatException"/>: the face is not corrupt, the feature is
/// outside the supported subset. Point-matched composite component arguments are the current case. A
/// consumer that surfaces this as an asset error reports it as an unsupported feature rather than a
/// corrupt face.
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
