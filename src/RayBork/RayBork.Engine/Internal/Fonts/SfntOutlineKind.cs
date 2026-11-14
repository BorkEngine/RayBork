namespace RayBork.Internal;

/// <summary>
/// The outline source of a parsed face.
/// </summary>
/// <remarks>
/// A TrueType face stores quadratic outlines in <c>glyf</c>; an OpenType CFF face stores its outlines
/// in <c>CFF </c> (CFF 1.0, resolved by <see cref="CffFont"/>) or <c>CFF2</c> (parsed structurally and
/// exposed, with glyph reads raising <see cref="FontUnsupportedException"/> because CFF2 is not
/// supported).
/// </remarks>
internal enum SfntOutlineKind {
    /// <summary>TrueType quadratic outlines in the <c>glyf</c> table.</summary>
    TrueType,

    /// <summary>Compact Font Format outlines in the <c>CFF </c> table.</summary>
    Cff,

    /// <summary>Compact Font Format 2 outlines in the <c>CFF2</c> table.</summary>
    Cff2,
}
