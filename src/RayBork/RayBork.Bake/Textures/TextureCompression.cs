namespace RayBork.Bake;

/// <summary>The block-compression choice of a texture.</summary>
/// <remarks>
/// <see cref="None"/> stores RGBA8 or R8 when an uncompressed format exists, while
/// <see cref="TextureChannelType.Rg"/> is always BC5. <see cref="Auto"/> selects BC7 for RGBA
/// and RGB and BC5 for RG.
/// </remarks>
public enum TextureCompression
{
    /// <summary>Uncompressed storage when the channel type has an uncompressed format.</summary>
    None = 0,

    /// <summary>The block format that matches the channel type.</summary>
    Auto = 1,
}
