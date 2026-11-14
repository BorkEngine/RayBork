namespace RayBork.Internal;

/// <summary>
/// The outline source of a parsed face.
/// </summary>
/// <remarks>
/// A TrueType face stores quadratic outlines in <c>glyf</c>; an OpenType CFF face stores its outlines
/// in <c>CFF </c> or <c>CFF2</c>. The runtime resolves only TrueType outlines, so <see cref="Cff"/>
/// and <see cref="Cff2"/> faces expose their table span but cannot draw glyphs until the CFF outline
/// reader lands.
/// </remarks>
internal enum SfntOutlineKind {
    /// <summary>TrueType quadratic outlines in the <c>glyf</c> table.</summary>
    TrueType,

    /// <summary>Compact Font Format outlines in the <c>CFF </c> table.</summary>
    Cff,

    /// <summary>Compact Font Format 2 outlines in the <c>CFF2</c> table.</summary>
    Cff2,
}
