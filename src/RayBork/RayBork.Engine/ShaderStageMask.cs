namespace RayBork;

/// <summary>
/// The shader stages present in a baked shader program.
/// </summary>
[Flags]
public enum ShaderStageMask {
    /// <summary>No stage.</summary>
    None = 0,

    /// <summary>The vertex stage.</summary>
    Vertex = 1 << 0,

    /// <summary>The fragment stage.</summary>
    Fragment = 1 << 1,
}
