using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Maps packed <see cref="TextureFormat"/> values to Vulkan formats, mip level layout, and KTX2
/// descriptor metadata.
/// </summary>
/// <remarks>
/// Only uncompressed and BC formats are packed; other formats fail with <c>RB1008</c>.
/// </remarks>
internal static class VulkanTextureFormat
{
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

    /// <summary>Determines whether a format may appear in a packed texture payload.</summary>
    internal static bool IsPacked(TextureFormat format) =>
        format is not (TextureFormat.Rgba16Float or TextureFormat.Depth32Float);

    /// <summary>Gets the Vulkan format of a packed texture.</summary>
    /// <exception cref="AssetLoadException">Not a packed format (<c>RB1008</c>).</exception>
    internal static Format ToVkFormat(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            TextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
            TextureFormat.Bc7Unorm => Format.BC7UnormBlock,
            TextureFormat.Bc7Srgb => Format.BC7SrgbBlock,
            TextureFormat.Bc5Unorm => Format.BC5UnormBlock,
            TextureFormat.R8Unorm => Format.R8Unorm,
            _ => throw CreateUnsupportedFormatException(format),
        };

    /// <summary>Gets whether a packed format samples through the sRGB transfer function.</summary>
    internal static bool IsSrgb(TextureFormat format) =>
        format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb;

    /// <summary>Gets whether a packed format stores 4x4 texel blocks.</summary>
    internal static bool IsBlockCompressed(TextureFormat format) =>
        format is TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb or TextureFormat.Bc5Unorm;

    /// <summary>Gets the stored bytes of one texel block or uncompressed pixel.</summary>
    internal static int BytesPerBlock(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => 4,
            TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb or TextureFormat.Bc5Unorm => 16,
            TextureFormat.R8Unorm => 1,
            _ => throw CreateUnsupportedFormatException(format),
        };

    /// <summary>Gets the Khronos data format color model of a packed format.</summary>
    internal static byte DfdColorModel(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.R8Unorm =>
                COLOR_MODEL_RGBSDA,
            TextureFormat.Bc7Unorm or TextureFormat.Bc7Srgb => COLOR_MODEL_BC7,
            TextureFormat.Bc5Unorm => COLOR_MODEL_BC5,
            _ => throw CreateUnsupportedFormatException(format),
        };

    /// <summary>Gets the data format transfer function a packed format requires.</summary>
    internal static byte DfdTransferFunction(TextureFormat format) =>
        IsPacked(format)
            ? (IsSrgb(format) ? TRANSFER_SRGB : TRANSFER_LINEAR)
            : throw CreateUnsupportedFormatException(format);

    /// <summary>Gets the stored byte length of one mip level, without KTX2 padding.</summary>
    /// <exception cref="AssetLoadException">Bad format or extent (<c>RB1008</c>).</exception>
    internal static int GetLevelByteLength(TextureFormat format, int width, int height)
    {
        int bytesPerBlock = BytesPerBlock(format);
        if (width <= 0 || height <= 0)
        {
            throw new AssetLoadException(
                $"The packed texture format '{format}' has an invalid level extent {width}x{height}.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                null
            );
        }

        long blocksWide = IsBlockCompressed(format) ? ((long)width + 3) / 4 : width;
        long blocksHigh = IsBlockCompressed(format) ? ((long)height + 3) / 4 : height;
        long byteLength = blocksWide * blocksHigh * bytesPerBlock;
        if (byteLength > int.MaxValue)
        {
            throw new AssetLoadException(
                $"The packed texture level {width}x{height} of '{format}' exceeds the maximum supported level size.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                null
            );
        }

        return (int)byteLength;
    }

    /// <summary>Gets the KTX2 alignment of a level's first byte: <c>lcm(texelBlockSize, 4)</c>.</summary>
    internal static int GetLevelAlignment(TextureFormat format) =>
        LeastCommonMultiple(BytesPerBlock(format), BLOCK_ALIGNMENT);

    /// <summary>Gets the mip count of a complete chain from the base extent to 1x1.</summary>
    internal static uint FullMipCount(uint width, uint height)
    {
        uint largest = Math.Max(width, height);
        uint count = 1;
        while (largest > 1)
        {
            largest >>= 1;
            count++;
        }

        return count;
    }

    /// <summary>Gets one mip level's pixel extent, never less than one.</summary>
    internal static int LevelExtent(uint baseExtent, int level) =>
        Math.Max(1, (int)(baseExtent >> level));

    private static int LeastCommonMultiple(int left, int right)
    {
        int a = left;
        int b = right;
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return (left / a) * right;
    }

    private static AssetLoadException CreateUnsupportedFormatException(TextureFormat format)
    {
        string reason = format is TextureFormat.Rgba16Float or TextureFormat.Depth32Float
            ? $"the texture format '{format}' is a render-target format and cannot appear in a packed texture payload"
            : $"the texture format '{format}' is not a packed texture format";
        return new AssetLoadException(
            $"The texture descriptor is invalid: {reason}.",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            null
        );
    }
}
