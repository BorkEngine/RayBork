namespace RayBork.Bake;

/// <summary>The channel content of a texture produced by <see cref="TextureBaker"/>.</summary>
/// <remarks>
/// The channel type selects which straight-alpha RGBA8 source channels the payload stores.
/// <see cref="Rgba"/> is stored as sRGB; <see cref="Rgb"/>, <see cref="Rg"/>, and
/// <see cref="R"/> are linear.
/// </remarks>
public enum TextureChannelType
{
    /// <summary>Red, green, blue, and alpha; stored as an sRGB color texture.</summary>
    Rgba = 0,

    /// <summary>Red, green, and blue; alpha is forced to opaque and storage is linear.</summary>
    Rgb = 1,

    /// <summary>Red and green only; storage is linear and always block compressed as BC5.</summary>
    Rg = 2,

    /// <summary>Red only; storage is linear as R8.</summary>
    R = 3,
}
