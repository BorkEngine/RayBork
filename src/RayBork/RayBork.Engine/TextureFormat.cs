namespace RayBork;

/// <summary>The GPU format of a packed texture asset.</summary>
/// <remarks>
/// Packed color textures are usually <see cref="Bc7Srgb"/>; uncompressed formats are the lossless
/// fallback. <see cref="Rgba16Float"/> and <see cref="Depth32Float"/> are render-target formats.
/// </remarks>
public enum TextureFormat
{
    /// <summary>Uncompressed linear RGBA8.</summary>
    Rgba8Unorm = 0,

    /// <summary>Uncompressed sRGB RGBA8.</summary>
    Rgba8Srgb = 1,

    /// <summary>BC7-compressed linear RGBA.</summary>
    Bc7Unorm = 2,

    /// <summary>BC7-compressed sRGB RGBA.</summary>
    Bc7Srgb = 3,

    /// <summary>BC5-compressed linear RG.</summary>
    Bc5Unorm = 4,

    /// <summary>Uncompressed linear R8.</summary>
    R8Unorm = 5,

    /// <summary>Linear half-float RGBA, used by HDR render targets.</summary>
    Rgba16Float = 6,

    /// <summary>32-bit float depth, used by render targets.</summary>
    Depth32Float = 7,
}
