namespace RayBork.Internal;

/// <summary>The defensive counters shared by one glyph's charstring interpretations.</summary>
/// <remarks>
/// A glyph and its <c>seac</c> components share one budget, so components cannot multiply the
/// per-glyph point or contour limits. The counters are mutable and single-resolution only.
/// </remarks>
internal sealed class CffGlyphBudget
{
    /// <summary>Gets or sets the number of outline points resolved so far.</summary>
    internal int Points { get; set; }

    /// <summary>Gets or sets the number of contours started so far.</summary>
    internal int Contours { get; set; }

    /// <summary>Gets or sets the number of charstring tokens decoded so far.</summary>
    internal int ExecutedTokens { get; set; }

    /// <summary>Gets or sets the number of subroutine calls executed so far.</summary>
    internal int SubroutineCalls { get; set; }
}
