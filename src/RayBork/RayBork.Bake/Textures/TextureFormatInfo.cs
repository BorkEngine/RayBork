namespace RayBork.Bake.Textures;

/// <summary>
/// The KTX2 and Data Format Descriptor metadata of one <see cref="TextureFormat"/>.
/// </summary>
/// <remarks>
/// The values mirror the Vulkan format table plus the narrow KTX2 profile this bake writes: one 2D
/// image, one layer, one face, no supercompression, and a complete mip chain. The DFD color model,
/// transfer function, texel block extent, and sample list are the fields a KTX2 reader compares
/// against the descriptor.
/// </remarks>
internal sealed class TextureFormatInfo {
    private const byte COLOR_MODEL_RGBSDA = 1;
    private const byte COLOR_MODEL_BC5 = 132;
    private const byte COLOR_MODEL_BC7 = 134;
    private const byte TRANSFER_LINEAR = 1;
    private const byte TRANSFER_SRGB = 2;
    private const byte CHANNEL_RED = 0;
    private const byte CHANNEL_GREEN = 1;
    private const byte CHANNEL_BLUE = 2;
    private const byte CHANNEL_ALPHA = 15;

    private static readonly TextureFormatInfo _rgba8Unorm = new(
        TextureFormat.Rgba8Unorm,
        vkFormat: 37,
        blockWidth: 1,
        blockHeight: 1,
        bytesPerBlock: 4,
        COLOR_MODEL_RGBSDA,
        TRANSFER_LINEAR,
        RgbaSamples());

    private static readonly TextureFormatInfo _rgba8Srgb = new(
        TextureFormat.Rgba8Srgb,
        vkFormat: 43,
        blockWidth: 1,
        blockHeight: 1,
        bytesPerBlock: 4,
        COLOR_MODEL_RGBSDA,
        TRANSFER_SRGB,
        RgbaSamples());

    private static readonly TextureFormatInfo _bc7Unorm = new(
        TextureFormat.Bc7Unorm,
        vkFormat: 145,
        blockWidth: 4,
        blockHeight: 4,
        bytesPerBlock: 16,
        COLOR_MODEL_BC7,
        TRANSFER_LINEAR,
        [BlockSample(0, 128, CHANNEL_RED)]);

    private static readonly TextureFormatInfo _bc7Srgb = new(
        TextureFormat.Bc7Srgb,
        vkFormat: 146,
        blockWidth: 4,
        blockHeight: 4,
        bytesPerBlock: 16,
        COLOR_MODEL_BC7,
        TRANSFER_SRGB,
        [BlockSample(0, 128, CHANNEL_RED)]);

    private static readonly TextureFormatInfo _bc5Unorm = new(
        TextureFormat.Bc5Unorm,
        vkFormat: 141,
        blockWidth: 4,
        blockHeight: 4,
        bytesPerBlock: 16,
        COLOR_MODEL_BC5,
        TRANSFER_LINEAR,
        [BlockSample(0, 64, CHANNEL_RED), BlockSample(64, 64, CHANNEL_GREEN)]);

    private static readonly TextureFormatInfo _r8Unorm = new(
        TextureFormat.R8Unorm,
        vkFormat: 9,
        blockWidth: 1,
        blockHeight: 1,
        bytesPerBlock: 1,
        COLOR_MODEL_RGBSDA,
        TRANSFER_LINEAR,
        [UnormSample(0, CHANNEL_RED)]);

    private readonly DfdSample[] _samples;

    private TextureFormatInfo(
        TextureFormat format,
        uint vkFormat,
        int blockWidth,
        int blockHeight,
        int bytesPerBlock,
        byte colorModel,
        byte transferFunction,
        DfdSample[] samples) {
        Format = format;
        VkFormat = vkFormat;
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        ColorModel = colorModel;
        TransferFunction = transferFunction;
        _samples = samples;
    }

    /// <summary>Gets the descriptor format this metadata describes.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets the Vulkan format value written to the KTX2 header.</summary>
    internal uint VkFormat { get; }

    /// <summary>Gets the texel block width in pixels.</summary>
    internal int BlockWidth { get; }

    /// <summary>Gets the texel block height in pixels.</summary>
    internal int BlockHeight { get; }

    /// <summary>Gets the stored bytes per texel block, or per pixel when uncompressed.</summary>
    internal int BytesPerBlock { get; }

    /// <summary>Gets the Khronos Data Format color model.</summary>
    internal byte ColorModel { get; }

    /// <summary>Gets the Khronos Data Format transfer function.</summary>
    internal byte TransferFunction { get; }

    /// <summary>Gets whether the format stores sRGB-encoded samples.</summary>
    internal bool IsSrgb => TransferFunction == TRANSFER_SRGB;

    /// <summary>Gets the ordered DFD samples.</summary>
    internal IReadOnlyList<DfdSample> Samples => _samples;

    /// <summary>Gets whether the format stores 4x4 block-compressed data.</summary>
    internal bool IsBlockCompressed => BlockWidth != 1 || BlockHeight != 1;

    /// <summary>
    /// Gets the alignment KTX2 requires for the first byte of every mip level:
    /// <c>lcm(texelBlockSize, 4)</c>.
    /// </summary>
    internal int RequiredAlignment => LeastCommonMultiple(BytesPerBlock, 4);

    /// <summary>Gets the metadata for a descriptor format.</summary>
    /// <param name="format">The descriptor format.</param>
    /// <returns>The format metadata.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a supported format.</exception>
    internal static TextureFormatInfo For(TextureFormat format)
        => format switch {
            TextureFormat.Rgba8Unorm => _rgba8Unorm,
            TextureFormat.Rgba8Srgb => _rgba8Srgb,
            TextureFormat.Bc7Unorm => _bc7Unorm,
            TextureFormat.Bc7Srgb => _bc7Srgb,
            TextureFormat.Bc5Unorm => _bc5Unorm,
            TextureFormat.R8Unorm => _r8Unorm,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The texture format has no KTX2 metadata mapping."),
        };

    /// <summary>Gets the stored byte length of one mip level.</summary>
    /// <param name="width">The level's pixel width.</param>
    /// <param name="height">The level's pixel height.</param>
    /// <returns>The stored byte length, without padding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    internal int GetLevelByteLength(int width, int height) {
        if (width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        long blocksWide = IsBlockCompressed ? (width + 3) / 4 : width;
        long blocksHigh = IsBlockCompressed ? (height + 3) / 4 : height;
        return checked((int)(blocksWide * blocksHigh * BytesPerBlock));
    }

    private static DfdSample[] RgbaSamples() =>
    [
        UnormSample(0, CHANNEL_RED),
        UnormSample(8, CHANNEL_GREEN),
        UnormSample(16, CHANNEL_BLUE),
        UnormSample(24, CHANNEL_ALPHA),
    ];

    private static DfdSample UnormSample(ushort bitOffset, byte channel)
        => new(bitOffset, 8, channel, 0, 255);

    private static DfdSample BlockSample(ushort bitOffset, byte bitLength, byte channel)
        => new(bitOffset, bitLength, channel, 0, uint.MaxValue);

    private static int LeastCommonMultiple(int left, int right) {
        int a = left;
        int b = right;
        while (b != 0) {
            (a, b) = (b, a % b);
        }

        return (left / a) * right;
    }
}
