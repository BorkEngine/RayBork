namespace RayBork;

/// <summary>Selects how a text block breaks lines when a maximum width is supplied.</summary>
/// <remarks>
/// Every mode starts a new line at <c>'\n'</c> and ignores <c>'\r'</c>; <see cref="Word"/> and
/// <see cref="Character"/> require a positive maximum width.
/// </remarks>
public enum TextWrapping
{
    /// <summary>Breaks only at <c>'\n'</c>; lines may overflow the maximum width.</summary>
    None,

    /// <summary>Breaks at whitespace; an overlong word overflows on its own line.</summary>
    Word,

    /// <summary>Breaks between any two runes; an overlong rune overflows on its own line.</summary>
    Character,
}
