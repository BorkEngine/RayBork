using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Maps the packed <see cref="TextureFormat"/> values to their Vulkan formats, KTX2 level layout, and
/// KTX2 data format descriptor metadata.
/// </summary>
/// <remarks>
/// <para>
/// M3 packed textures are the uncompressed RGBA8/R8 formats and the BC7/BC5 block formats.
/// <see cref="TextureFormat.Rgba16Float"/> and <see cref="TextureFormat.Depth32Float"/> are
/// render-target formats and have no packed representation; a texture upload that carries one is a
/// decode failure.
/// </para>
/// <para>
/// The data format descriptor values mirror the narrow KTX2 2.0 profile the bake writes: the Khronos
/// data format color model, a linear or sRGB transfer function, a 1x1 or 4x4 texel block, and the
/// stored block size. The reader compares these fields against the descriptor so a payload of the
/// wrong format or transfer cannot be uploaded behind a mismatched public format.
/// </para>
/// </remarks>
internal static class VulkanTextureFormat {
    /// <summary>The Khronos data format color model for RGBSDA (uncompressed) formats.</summary>
    internal const byte COLOR_MODEL_RGBSDA = 1;

    /// <summary>The Khronos data format color model for BC5 block-compressed formats.</summary>
    internal const byte COLOR_MODEL_BC5 = 132;

    /// <summary>The Khronos data format color model for BC7 block-compressed formats.</summary>
    internal const byte COLOR_MODEL_BC7 = 134;

    /// <summary>The linear transfer function of the Khronos data format descriptor.</summary>
    internal const byte TRANSFER_LINEAR = 1;

    /// <summary>The sRGB transfer function of the Khronos data format descriptor.</summary>
    internal const byte TRANSFER_SRGB = 2;

    private const int BLOCK_ALIGNMENT = 4;

    /// <summary>
    /// Determines whether a format may appear in a packed texture payload.
    /// </summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns><see langword="true"/> for a packed texture format.</returns>
    internal static bool IsPacked(TextureFormat format) => format is not (
        TextureFormat.Rgba16Float or TextureFormat.Depth32Float);

    /// <summary>
    /// Gets the Vulkan format of a packed texture.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns>The matching Vulkan format.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is a render-target format or not a defined packed format. The diagnostic code is
    /// <c>RB1008</c>.
    /// </exception>
    internal static Format ToVkFormat(TextureFormat format) => format switch {
        TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        TextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
        TextureFormat.Bc7Unorm => Format.BC7UnormBlock,
        TextureFormat.Bc7Srgb => Format.BC7SrgbBlock,
        TextureFormat.Bc5Unorm => Format.BC5UnormBlock,
        TextureFormat.R8Unorm => Format.R8Unorm,
        _ => throw CreateUnsupportedFormatException(format),
    };

    /// <summary>
    /// Gets whether a packed format samples through the sRGB transfer function.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns><see langword="true"/> for <see cref="TextureFormat.Rgba8Srgb"/> and
    /// <see cref="TextureFormat.Bc7Srgb"/>.</returns>
    internal static bool IsSrgb(TextureFormat format) => format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb;

    /// <summary>
    /// Gets whether a packed format stores 4x4 texel blocks.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns><see langword="true"/> for the BC formats.</returns>
    internal static bool IsBlockCompressed(TextureFormat format) => format is
        TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb or TextureFormat.Bc5Unorm;

    /// <summary>
    /// Gets the stored bytes of one texel block, or of one pixel for an uncompressed format.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns>The stored block size in bytes.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is not a packed format. The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static int BytesPerBlock(TextureFormat format) => format switch {
        TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => 4,
        TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb or TextureFormat.Bc5Unorm => 16,
        TextureFormat.R8Unorm => 1,
        _ => throw CreateUnsupportedFormatException(format),
    };

    /// <summary>
    /// Gets the Khronos data format color model of a packed format.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns>The data format color model.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is not a packed format. The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static byte DfdColorModel(TextureFormat format) => format switch {
        TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.R8Unorm => COLOR_MODEL_RGBSDA,
        TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb => COLOR_MODEL_BC7,
        TextureFormat.Bc5Unorm => COLOR_MODEL_BC5,
        _ => throw CreateUnsupportedFormatException(format),
    };

    /// <summary>
    /// Gets the data format transfer function a packed format requires.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns>The linear or sRGB transfer function.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is not a packed format. The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static byte DfdTransferFunction(TextureFormat format) => IsPacked(format)
        ? (IsSrgb(format) ? TRANSFER_SRGB : TRANSFER_LINEAR)
        : throw CreateUnsupportedFormatException(format);

    /// <summary>
    /// Gets the stored byte length of one mip level, without KTX2 alignment padding.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <param name="width">The level's pixel width.</param>
    /// <param name="height">The level's pixel height.</param>
    /// <returns>The level's stored byte length.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is not a packed format. The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static int GetLevelByteLength(TextureFormat format, int width, int height) {
        int bytesPerBlock = BytesPerBlock(format);
        if (width <= 0 || height <= 0) {
            throw new AssetLoadException(
                $"The packed texture format '{format}' has an invalid level extent {width}x{height}.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                null);
        }

        long blocksWide = IsBlockCompressed(format) ? ((long)width + 3) / 4 : width;
        long blocksHigh = IsBlockCompressed(format) ? ((long)height + 3) / 4 : height;
        long byteLength = blocksWide * blocksHigh * bytesPerBlock;
        if (byteLength > int.MaxValue) {
            throw new AssetLoadException(
                $"The packed texture level {width}x{height} of '{format}' exceeds the maximum supported level size.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                null);
        }

        return (int)byteLength;
    }

    /// <summary>
    /// Gets the alignment KTX2 requires for the first byte of every mip level:
    /// <c>lcm(texelBlockSize, 4)</c>.
    /// </summary>
    /// <param name="format">The packed texture format.</param>
    /// <returns>The level alignment in bytes.</returns>
    /// <exception cref="AssetLoadException">
    /// The format is not a packed format. The diagnostic code is <c>RB1008</c>.
    /// </exception>
    internal static int GetLevelAlignment(TextureFormat format) => LeastCommonMultiple(BytesPerBlock(format), BLOCK_ALIGNMENT);

    /// <summary>
    /// Gets the largest mip level count of a base extent down to 1x1.
    /// </summary>
    /// <param name="width">The base width in pixels.</param>
    /// <param name="height">The base height in pixels.</param>
    /// <returns>The number of levels in a complete mip chain.</returns>
    internal static uint FullMipCount(uint width, uint height) {
        uint largest = Math.Max(width, height);
        uint count = 1;
        while (largest > 1) {
            largest >>= 1;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Gets one mip level's pixel extent.
    /// </summary>
    /// <param name="baseExtent">The base-level width or height.</param>
    /// <param name="level">The zero-based level index.</param>
    /// <returns>The level extent, never less than one.</returns>
    internal static int LevelExtent(uint baseExtent, int level) => Math.Max(1, (int)(baseExtent >> level));

    private static int LeastCommonMultiple(int left, int right) {
        int a = left;
        int b = right;
        while (b != 0) {
            (a, b) = (b, a % b);
        }

        return (left / a) * right;
    }

    private static AssetLoadException CreateUnsupportedFormatException(TextureFormat format) {
        string reason = format is TextureFormat.Rgba16Float or TextureFormat.Depth32Float
            ? $"the texture format '{format}' is a render-target format and cannot appear in a packed texture payload"
            : $"the texture format '{format}' is not a packed texture format";
        return new AssetLoadException(
            $"The texture descriptor is invalid: {reason}.",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            null);
    }
}
