namespace RayBork;

/// <summary>
/// Selects how the lines of a text block are aligned horizontally inside the block's layout box.
/// </summary>
public enum TextAlignment {
    /// <summary>
    /// Each line starts at the left edge of the layout box.
    /// </summary>
    Left,

    /// <summary>
    /// Each line is centered horizontally inside the layout box.
    /// </summary>
    Center,

    /// <summary>
    /// Each line ends at the right edge of the layout box.
    /// </summary>
    Right,
}
