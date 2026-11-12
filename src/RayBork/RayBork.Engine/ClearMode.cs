namespace RayBork;

/// <summary>
/// Selects the buffers a <see cref="Camera"/> pass clears before it draws.
/// </summary>
[Flags]
public enum ClearMode {
    /// <summary>Clears nothing; the pass is an overlay over the previous contents.</summary>
    None = 0,

    /// <summary>Clears the color attachment to the camera's <see cref="Camera.ClearColor"/>.</summary>
    Color = 1 << 0,

    /// <summary>Clears depth to the reverse-Z far value <c>0.0</c>.</summary>
    Depth = 1 << 1,

    /// <summary>Clears stencil to <c>0</c>.</summary>
    Stencil = 1 << 2,

    /// <summary>Clears color and depth; the default for a camera.</summary>
    ColorAndDepth = Color | Depth,

    /// <summary>Clears color, depth, and stencil.</summary>
    All = Color | Depth | Stencil,
}
