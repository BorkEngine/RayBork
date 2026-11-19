namespace RayBork;

/// <summary>The filtering mode of a texture sampler.</summary>
public enum TextureFilter
{
    /// <summary>Nearest-neighbor filtering.</summary>
    Nearest = 0,

    /// <summary>Linear filtering.</summary>
    Linear = 1,

    /// <summary>Linear filtering with anisotropy, falling back to linear when unsupported.</summary>
    Anisotropic = 2,
}
