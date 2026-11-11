namespace RayBork.Bake;

/// <summary>
/// The block-compression choice of a texture produced by <see cref="TextureBaker"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="None"/> stores an uncompressed format when one exists: RGBA8 for
/// <see cref="TextureChannelType.Rgba"/> and <see cref="TextureChannelType.Rgb"/>, and R8 for
/// <see cref="TextureChannelType.R"/>. RayBork has no uncompressed two-channel format, so
/// <see cref="TextureChannelType.Rg"/> is always stored as BC5 in both modes.
/// </para>
/// <para>
/// <see cref="Auto"/> selects the block format that matches the channel type: BC7 for
/// <see cref="TextureChannelType.Rgba"/> and <see cref="TextureChannelType.Rgb"/>, and BC5 for
/// <see cref="TextureChannelType.Rg"/>.
/// </para>
/// </remarks>
public enum TextureCompression {
    /// <summary>Uncompressed storage when the channel type has an uncompressed format.</summary>
    None = 0,

    /// <summary>The block format that matches the channel type.</summary>
    Auto = 1,
}
