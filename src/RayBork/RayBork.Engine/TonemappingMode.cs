namespace RayBork;

/// <summary>Selects how the present pass maps linear HDR color to the output surface.</summary>
/// <remarks>The present pass is frame-global, so every camera in a frame shares the mode.</remarks>
public enum TonemappingMode
{
    /// <summary>Applies the AgX curve: compresses highlights, preserves hue. Default.</summary>
    AgX,

    /// <summary>Writes linear values through the transfer function; no tone curve.</summary>
    None,
}
