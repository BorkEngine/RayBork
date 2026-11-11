namespace RayBork;

/// <summary>
/// The minification, magnification, and mipmap filtering mode of a texture sampler.
/// </summary>
public enum TextureFilter {
    /// <summary>Nearest-neighbor filtering.</summary>
    Nearest = 0,

    /// <summary>Linear filtering.</summary>
    Linear = 1,

    /// <summary>
    /// Linear filtering with anisotropic filtering. The device's maximum anisotropy is used; a device
    /// without anisotropic filtering falls back to linear and logs a warning.
    /// </summary>
    Anisotropic = 2,
}
