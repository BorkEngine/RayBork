using RayBork.Bake.Textures;
using RayBork.Internal;

namespace RayBork.Bake;

/// <summary>
/// Builds a complete KTX2 2.0 texture payload from straight-alpha RGBA8 pixels.
/// </summary>
/// <remarks>
/// <para>
/// The helper is the one KTX2 producer for the bake: the built-in texture processor calls it for
/// decoded image sources, and a custom processor (for example one that generates a texture) calls it
/// directly so it never hand-writes KTX2 headers or block data. It generates the full mip chain with
/// a deterministic box filter, block-encodes every level when compression is requested, and returns
/// the payload together with the descriptor format and mip count.
/// </para>
/// <para>
/// Input pixels are straight-alpha RGBA8, row-major with a top-left origin. The helper applies no
/// gamma conversion, premultiplication, ICC handling, flipping, or resampling beyond the mip box
/// filter. <see cref="TextureChannelType.Rgb"/> forces alpha to <c>255</c>;
/// <see cref="TextureChannelType.Rg"/> and <see cref="TextureChannelType.R"/> keep only the channels
/// they name.
/// </para>
/// <para>
/// <see cref="TextureChannelType.Rgba"/> is authored color and is stored in the sRGB transfer
/// function by <see cref="Bake(ReadOnlySpan{byte}, int, int, TextureChannelType, TextureCompression)"/>;
/// the other channel types are linear. The built-in texture processor selects the transfer function
/// explicitly from its sidecar.
/// </para>
/// </remarks>
public static class TextureBaker {
    private const TextureFilter DEFAULT_FILTER = TextureFilter.Linear;
    private const TextureWrap DEFAULT_WRAP = TextureWrap.Repeat;

    /// <summary>
    /// Bakes one texture and emits its <c>Texture</c> descriptor and KTX2 payload through a processor
    /// context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the convenience entry point for a custom processor that generates pixels. It is the
    /// same KTX2 producer as <see cref="Bake(ReadOnlySpan{byte}, int, int, TextureChannelType, TextureCompression)"/>:
    /// it generates the full mip chain, encodes every level, and writes one complete KTX2 2.0 file.
    /// The descriptor is emitted at <see cref="AssetProcessorContext.BaseAssetPath"/> and the payload
    /// at <c>&lt;base&gt;-ktx2</c>, matching the built-in texture processor's entry layout. A
    /// processor therefore never touches MessagePack or pack bytes directly.
    /// </para>
    /// <para>
    /// A <see langword="null"/> <paramref name="filter"/> or <paramref name="wrap"/> selects the
    /// bake's baseline sampler defaults, <see cref="TextureFilter.Linear"/> and
    /// <see cref="TextureWrap.Repeat"/>. The per-source context does not expose a customized
    /// <c>WithTextureDefaults</c> setting, so a processor that needs a non-default sampler passes one
    /// explicitly.
    /// </para>
    /// <para>
    /// <paramref name="srgb"/> applies only to <see cref="TextureChannelType.Rgba"/>; the linear
    /// channel types ignore it. A null value stores <see cref="TextureChannelType.Rgba"/> as sRGB and
    /// every linear channel type as linear, matching the public <see cref="Bake(ReadOnlySpan{byte}, int, int, TextureChannelType, TextureCompression)"/>
    /// helper.
    /// </para>
    /// </remarks>
    /// <param name="context">The per-source processing context.</param>
    /// <param name="rgba8Pixels">
    /// The base-level straight-alpha RGBA8 pixels, exactly <c>width × height × 4</c> bytes.
    /// </param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <param name="filter">
    /// The sampler filter, or <see langword="null"/> for <see cref="DEFAULT_FILTER"/>.
    /// </param>
    /// <param name="wrap">
    /// The sampler address mode, or <see langword="null"/> for <see cref="DEFAULT_WRAP"/>.
    /// </param>
    /// <param name="srgb">
    /// Whether an <see cref="TextureChannelType.Rgba"/> payload uses the sRGB transfer function, or
    /// <see langword="null"/> to derive it from <paramref name="channelType"/>.
    /// </param>
    /// <param name="maxAnisotropy">
    /// The requested maximum anisotropy; a positive finite number, usually <c>1</c>.
    /// </param>
    /// <param name="alignment">
    /// The payload alignment: <c>1</c>, <c>16</c>, <c>64</c>, or <c>256</c>. GPU payloads use
    /// <c>16</c> or greater.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds the bake's limit, an enum value is undefined,
    /// <paramref name="maxAnisotropy"/> is not positive and finite, or
    /// <paramref name="alignment"/> is not a supported alignment.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    public static void AddTo(
        AssetProcessorContext context,
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType = TextureChannelType.Rgba,
        TextureCompression compression = TextureCompression.None,
        TextureFilter? filter = null,
        TextureWrap? wrap = null,
        bool? srgb = null,
        float maxAnisotropy = 1f,
        uint alignment = 16) {
        ArgumentNullException.ThrowIfNull(context);

        if (!TextureFormatMapping.IsDefined(channelType, compression)) {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values.");
        }

        TextureFilter resolvedFilter = filter ?? DEFAULT_FILTER;
        if (!Enum.IsDefined(resolvedFilter)) {
            throw new ArgumentOutOfRangeException(
                nameof(filter),
                filter,
                "The filter must be a defined enum value.");
        }

        TextureWrap resolvedWrap = wrap ?? DEFAULT_WRAP;
        if (!Enum.IsDefined(resolvedWrap)) {
            throw new ArgumentOutOfRangeException(
                nameof(wrap),
                wrap,
                "The wrap mode must be a defined enum value.");
        }

        if (!float.IsFinite(maxAnisotropy) || maxAnisotropy <= 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(maxAnisotropy),
                maxAnisotropy,
                "The maximum anisotropy must be a positive finite number.");
        }

        if (alignment is not 1u and not 16u and not 64u and not 256u) {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                "The alignment must be 1, 16, 64, or 256 bytes.");
        }

        bool isSrgb = srgb ?? channelType == TextureChannelType.Rgba;
        BakedTexture baked = Bake(
            rgba8Pixels,
            width,
            height,
            channelType,
            compression,
            isSrgb);

        string payloadPath = context.BaseAssetPath + "-ktx2";
        var descriptor = new TextureAssetDescriptor {
            Payload = payloadPath,
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = baked.MipLevels,
            Format = baked.Format,
            IsSrgb = baked.Format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb,
            Filter = resolvedFilter,
            Wrap = resolvedWrap,
            MaxAnisotropy = maxAnisotropy,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, baked.Payload.Span, alignment);
    }

    /// <summary>Bakes one texture into a complete KTX2 payload.</summary>
    /// <param name="rgba8Pixels">
    /// The base-level straight-alpha RGBA8 pixels, exactly <c>width × height × 4</c> bytes.
    /// </param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <returns>
    /// The KTX2 payload and the descriptor format and mip count that describe it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds the bake's limit, or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    public static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression)
        => BakeCore(
            rgba8Pixels,
            width,
            height,
            channelType,
            compression,
            isSrgb: channelType == TextureChannelType.Rgba);

    /// <summary>
    /// Bakes one texture with an explicit transfer function for <see cref="TextureChannelType.Rgba"/>.
    /// </summary>
    /// <remarks>
    /// The built-in texture processor uses this overload so a sidecar's <c>srgb</c> key selects the
    /// transfer function; the public helper always stores <see cref="TextureChannelType.Rgba"/> as
    /// sRGB. The flag is ignored for the linear channel types.
    /// </remarks>
    /// <param name="rgba8Pixels">The base-level straight-alpha RGBA8 pixels.</param>
    /// <param name="width">The base-level pixel width; must be positive.</param>
    /// <param name="height">The base-level pixel height; must be positive.</param>
    /// <param name="channelType">The semantic channel type.</param>
    /// <param name="compression">The compression mode.</param>
    /// <param name="isSrgb">Whether an RGBA payload uses the sRGB transfer function.</param>
    /// <returns>The KTX2 payload and the descriptor format and mip count that describe it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds the bake's limit, or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Block encoding or KTX2 container writing failed.
    /// </exception>
    internal static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb)
        => BakeCore(rgba8Pixels, width, height, channelType, compression, isSrgb);

    private static BakedTexture BakeCore(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb) {
        if (!TextureFormatMapping.IsDefined(channelType, compression)) {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values.");
        }

        if (width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (width > TextureLimits.MAX_DIMENSION) {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"The width must be at most {TextureLimits.MAX_DIMENSION} pixels.");
        }

        if (height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        if (height > TextureLimits.MAX_DIMENSION) {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"The height must be at most {TextureLimits.MAX_DIMENSION} pixels.");
        }

        long expectedLength = (long)width * height * 4;
        if (rgba8Pixels.Length != expectedLength) {
            throw new ArgumentException(
                $"The RGBA8 pixel buffer is {rgba8Pixels.Length} bytes; expected {expectedLength} "
                    + "(width × height × 4).",
                nameof(rgba8Pixels));
        }

        TextureFormat format = TextureFormatMapping.Map(channelType, compression, isSrgb);
        TextureFormatInfo formatInfo = TextureFormatInfo.For(format);
        TextureLevel[] levels = TextureMipChain.Generate(rgba8Pixels, width, height, channelType);
        var storedLevels = new byte[levels.Length][];
        for (int level = 0; level < levels.Length; level++) {
            storedLevels[level] = formatInfo.IsBlockCompressed
                ? BcTextureEncoder.Encode(levels[level], format)
                : PackUncompressed(levels[level], format);
        }

        byte[] payload = Ktx2Writer.Write(formatInfo, width, height, storedLevels);
        return new BakedTexture(payload, format, (uint)levels.Length);
    }

    private static byte[] PackUncompressed(TextureLevel level, TextureFormat format) {
        switch (format) {
            case TextureFormat.Rgba8Unorm:
            case TextureFormat.Rgba8Srgb:
                return level.Pixels;

            case TextureFormat.R8Unorm:
                var red = new byte[level.Width * level.Height];
                for (int pixel = 0; pixel < red.Length; pixel++) {
                    red[pixel] = level.Pixels[pixel * 4];
                }

                return red;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "The format is not an uncompressed texture format.");
        }
    }
}
