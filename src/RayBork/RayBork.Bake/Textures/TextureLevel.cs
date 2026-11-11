namespace RayBork.Bake.Textures;

/// <summary>
/// One straight-alpha RGBA8 level of a generated mip chain.
/// </summary>
/// <remarks>
/// The baker generates every level once as RGBA8 and then packs or block-encodes each level for the
/// selected format. The pixel array is owned by the level and is never shared with a caller.
/// </remarks>
internal sealed class TextureLevel {
    /// <summary>Initializes a mip level.</summary>
    /// <param name="width">The level's pixel width.</param>
    /// <param name="height">The level's pixel height.</param>
    /// <param name="pixels">The straight-alpha RGBA8 pixels, <c>width × height × 4</c> bytes.</param>
    internal TextureLevel(int width, int height, byte[] pixels) {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        if (pixels.Length != (long)width * height * 4) {
            throw new ArgumentException(
                $"The RGBA8 pixels are {pixels.Length} bytes; expected {width * height * 4}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Gets the level's pixel width.</summary>
    internal int Width { get; }

    /// <summary>Gets the level's pixel height.</summary>
    internal int Height { get; }

    /// <summary>Gets the level's straight-alpha RGBA8 pixels.</summary>
    internal byte[] Pixels { get; }
}
