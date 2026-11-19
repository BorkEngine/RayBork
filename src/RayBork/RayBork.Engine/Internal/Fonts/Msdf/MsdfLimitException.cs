namespace RayBork.Internal;

/// <summary>An MSDF request that exceeds a defensive generation limit.</summary>
/// <remarks>
/// The outline is valid but too large for the bounded generator. <see cref="FontGlyphPipeline"/>
/// treats it as a font-data failure and falls back to face metrics without generating a glyph.
/// </remarks>
internal sealed class MsdfLimitException : Exception
{
    /// <summary>Initializes an over-limit failure.</summary>
    /// <param name="reason">The actionable cause, without a sentence prefix.</param>
    internal MsdfLimitException(string reason)
        : base($"The MSDF glyph request exceeds a generator limit: {reason}.")
    {
        Reason = reason;
    }

    /// <summary>Gets the actionable cause, without the message prefix.</summary>
    internal string Reason { get; }
}
