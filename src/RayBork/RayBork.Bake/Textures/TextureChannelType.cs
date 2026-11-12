namespace RayBork.Bake;

/// <summary>
/// The channel content of a texture produced by <see cref="TextureBaker"/>.
/// </summary>
/// <remarks>
/// <para>
/// The channel type selects which straight-alpha RGBA8 source channels the payload stores.
/// <see cref="Rgba"/> keeps all four channels and is stored in the sRGB transfer function by
/// <see cref="TextureBaker.Bake"/>; <see cref="Rgb"/>, <see cref="Rg"/>, and <see cref="R"/> are
/// linear data layouts. <see cref="Rgb"/> forces alpha to opaque and <see cref="Rg"/> and
/// <see cref="R"/> keep only the channels they name.
/// </para>
/// <para>
/// The channel type is authoring metadata, not a payload format: the bake maps it together with
/// <see cref="TextureCompression"/> to a concrete <see cref="TextureFormat"/>.
/// </para>
/// </remarks>
public enum TextureChannelType {
    /// <summary>Red, green, blue, and alpha; stored as an sRGB color texture.</summary>
    Rgba = 0,

    /// <summary>Red, green, and blue; alpha is forced to opaque and storage is linear.</summary>
    Rgb = 1,

    /// <summary>Red and green only; storage is linear and always block compressed as BC5.</summary>
    Rg = 2,

    /// <summary>Red only; storage is linear as R8.</summary>
    R = 3,
}
