namespace RayBork;

/// <summary>The address mode for texture coordinates outside <c>[0, 1]</c>.</summary>
public enum TextureWrap
{
    /// <summary>Repeats the texture.</summary>
    Repeat = 0,

    /// <summary>Clamps to the edge texel.</summary>
    Clamp = 1,

    /// <summary>Mirrors the texture at each integer boundary.</summary>
    Mirror = 2,
}
