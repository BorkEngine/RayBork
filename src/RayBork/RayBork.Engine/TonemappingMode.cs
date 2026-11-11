namespace RayBork;

/// <summary>
/// Selects how the present pass maps linear HDR color to the output surface.
/// </summary>
public enum TonemappingMode {
    /// <summary>
    /// Applies the AgX curve, which compresses the highlight range while preserving hue. The
    /// default.
    /// </summary>
    AgX,

    /// <summary>
    /// Writes linear values through the swapchain transfer function without a tone curve. Intended
    /// for custom presentation paths.
    /// </summary>
    None,
}
