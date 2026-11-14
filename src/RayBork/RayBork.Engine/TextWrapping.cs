namespace RayBork;

/// <summary>
/// Selects where a text block may break lines when a maximum width is supplied.
/// </summary>
/// <remarks>
/// Every mode starts a new line at <c>'\n'</c> and ignores <c>'\r'</c>. <see cref="Word"/> and
/// <see cref="Character"/> require a positive maximum width, in world units at the requested size.
/// </remarks>
public enum TextWrapping {
    /// <summary>
    /// Lines break only at <c>'\n'</c>, and lines may overflow the maximum width.
    /// </summary>
    None,

    /// <summary>
    /// Lines break at whitespace boundaries. The whitespace at a break is consumed, and a word wider
    /// than the maximum width overflows on a line of its own.
    /// </summary>
    Word,

    /// <summary>
    /// Lines break between any two runes. A rune that does not fit starts the next line and overflows
    /// when it is wider than the maximum width on its own.
    /// </summary>
    Character,
}
