using System.Buffers.Binary;

namespace RayBork.Tests.Bake.Textures;

/// <summary>Deterministic texture inputs and independently computed level geometry for tests.</summary>
internal static class TextureTestData {
    /// <summary>A 2x2 RGBA8 PNG: red, green, blue, and semi-transparent white.</summary>
    internal static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x72, 0xB6, 0x0D, 0x24, 0x00, 0x00, 0x00,
        0x14, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0xF8, 0xCF, 0xC0, 0xF0,
        0x1F, 0x04, 0x59, 0x18, 0xC1, 0x34, 0x43, 0x23, 0x00, 0x3F, 0x80, 0x06,
        0x81, 0xC4, 0x55, 0x98, 0x41, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    /// <summary>A valid 2x2 16-bit-per-channel grayscale PNG.</summary>
    internal static readonly byte[] SixteenBitPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02,
        0x10, 0x00, 0x00, 0x00, 0x00, 0x07, 0x4D, 0x8E, 0xBB, 0x00, 0x00, 0x00,
        0x12, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x60, 0x60, 0x60, 0x60,
        0x64, 0x60, 0x60, 0xFA, 0xFF, 0x1F, 0x00, 0x03, 0x13, 0x02, 0x02, 0x60,
        0xE7, 0xBE, 0xAF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>A truncated PNG signature and IHDR prefix.</summary>
    internal static readonly byte[] TruncatedPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00,
    ];

    /// <summary>Creates a deterministic RGBA8 gradient with a distinct alpha ramp.</summary>
    /// <param name="width">The pixel width.</param>
    /// <param name="height">The pixel height.</param>
    /// <returns>The row-major straight-alpha RGBA8 pixels.</returns>
    internal static byte[] CreatePixels(int width, int height) {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(17 + (x * 37));
                pixels[offset + 1] = (byte)(41 + (y * 53));
                pixels[offset + 2] = (byte)(101 + (x * y));
                pixels[offset + 3] = (byte)(255 - (x * 11) - (y * 7));
            }
        }

        return pixels;
    }

    /// <summary>
    /// Creates a PNG header whose declared extent exceeds the bake's size policy. The probe rejects
    /// it before any decoder reads the (deliberately incomplete) body.
    /// </summary>
    /// <param name="width">The declared width.</param>
    /// <param name="height">The declared height.</param>
    /// <returns>The crafted header bytes.</returns>
    internal static byte[] CreatePngHeader(int width, int height) {
        var header = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), 13);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), height);
        header[24] = 8;
        header[25] = 6;
        return header;
    }

    /// <summary>Gets the number of levels in a complete mip chain.</summary>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <returns>The complete level count.</returns>
    internal static int MipLevelCount(int width, int height) {
        int levels = 1;
        while (width > 1 || height > 1) {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            levels++;
        }

        return levels;
    }

    /// <summary>Gets an independent expected stored length of one mip level.</summary>
    /// <param name="format">The stored format.</param>
    /// <param name="width">The level width.</param>
    /// <param name="height">The level height.</param>
    /// <returns>The stored byte length without alignment padding.</returns>
    internal static int LevelByteLength(TextureFormat format, int width, int height) {
        int bytesPerBlock = format switch {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => 4,
            TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb or TextureFormat.Bc5Unorm => 16,
            TextureFormat.R8Unorm => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a packed format."),
        };
        bool blockCompressed = format is TextureFormat.Bc7Unorm
            or TextureFormat.Bc7Srgb
            or TextureFormat.Bc5Unorm;
        int blocksWide = blockCompressed ? (width + 3) / 4 : width;
        int blocksHigh = blockCompressed ? (height + 3) / 4 : height;
        return blocksWide * blocksHigh * bytesPerBlock;
    }

    /// <summary>Gets the level width for a complete chain.</summary>
    /// <param name="width">The base width.</param>
    /// <param name="level">The zero-based level.</param>
    /// <returns>The level width.</returns>
    internal static int LevelWidth(int width, int level) => Math.Max(1, width >> level);

    /// <summary>Gets the level height for a complete chain.</summary>
    /// <param name="height">The base height.</param>
    /// <param name="level">The zero-based level.</param>
    /// <returns>The level height.</returns>
    internal static int LevelHeight(int height, int level) => Math.Max(1, height >> level);
}
