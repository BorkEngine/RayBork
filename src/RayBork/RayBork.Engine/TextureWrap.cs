namespace RayBork;

/// <summary>
/// The address mode applied to texture coordinates outside <c>[0, 1]</c>.
/// </summary>
public enum TextureWrap {
    /// <summary>Repeat the texture.</summary>
    Repeat = 0,

    /// <summary>Clamp to the edge texel.</summary>
    Clamp = 1,

    /// <summary>Mirror the texture at each integer boundary.</summary>
    Mirror = 2,
}
