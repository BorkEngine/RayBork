namespace RayBork.Internal;

/// <summary>
/// A controlled failure for an MSDF generation request that exceeds a documented defensive limit.
/// </summary>
/// <remarks>
/// <para>
/// The outline is not corrupt and the request is well formed; it is simply too large to generate in
/// bounded time and memory. The reason always names the limit: <see cref="MsdfGenerator"/> caps the
/// flattened edge count, the texture texel count, and the number of edge-texel pairs. The flattener's
/// per-curve subdivision cap is a clamp rather than a failure, so it never raises this exception.
/// </para>
/// <para>
/// The exception carries no state beyond <see cref="Reason"/> and is not serializable. The future font
/// loader decides how to surface it as an asset diagnostic.
/// </para>
/// </remarks>
internal sealed class MsdfLimitException : Exception {
    /// <summary>Initializes an over-limit failure.</summary>
    /// <param name="reason">The actionable cause, without a sentence prefix.</param>
    internal MsdfLimitException(string reason)
        : base($"The MSDF glyph request exceeds a generator limit: {reason}.") {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
