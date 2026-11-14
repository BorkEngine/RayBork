namespace RayBork.Internal;

/// <summary>
/// The defensive counters shared by all charstring interpretations of one glyph resolution.
/// </summary>
/// <remarks>
/// A glyph and its <c>seac</c> components share one budget, so component charstrings cannot multiply
/// the per-glyph point or contour limits. The counters are checked before the outline grows, and the
/// budget is mutable and single-resolution only; it is not shared across threads.
/// </remarks>
internal sealed class CffGlyphBudget {
    /// <summary>Gets or sets the number of outline points resolved so far.</summary>
    internal int Points { get; set; }

    /// <summary>Gets or sets the number of contours started so far.</summary>
    internal int Contours { get; set; }

    /// <summary>Gets or sets the number of subroutine calls executed so far.</summary>
    internal int SubroutineCalls { get; set; }
}
