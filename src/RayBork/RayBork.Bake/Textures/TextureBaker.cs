using RayBork.Bake.Textures;
using RayBork.Internal;

namespace RayBork.Bake;

/// <summary>Builds a complete KTX2 2.0 texture payload from straight-alpha RGBA8 pixels.</summary>
/// <remarks>
/// Input is straight-alpha RGBA8, row-major with a top-left origin, and the only resampling is the
/// deterministic mip box filter. <see cref="TextureChannelType.Rgba"/> is stored as sRGB; the other
/// channel types are linear.
/// </remarks>
public static class TextureBaker
{
    private const TextureFilter DEFAULT_FILTER = TextureFilter.Linear;
    private const TextureWrap DEFAULT_WRAP = TextureWrap.Repeat;

    /// <summary>Bakes one texture and emits its descriptor and KTX2 payload.</summary>
    /// <remarks>
    /// The descriptor is written at the context's base asset path and the payload at
    /// <c>&lt;base&gt;-ktx2</c>. Null filter or wrap means Linear or Repeat.
    /// <paramref name="srgb"/> applies only to RGBA; null stores RGBA as sRGB.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The context is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is invalid, an enum value is undefined, anisotropy is not positive and finite,
    /// or the alignment is not 1, 16, 64, or 256.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Encoding or writing failed.</exception>
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
        uint alignment = 16
    )
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!TextureFormatMapping.IsDefined(channelType, compression))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values."
            );
        }

        TextureFilter resolvedFilter = filter ?? DEFAULT_FILTER;
        if (!Enum.IsDefined(resolvedFilter))
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter),
                filter,
                "The filter must be a defined enum value."
            );
        }

        TextureWrap resolvedWrap = wrap ?? DEFAULT_WRAP;
        if (!Enum.IsDefined(resolvedWrap))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wrap),
                wrap,
                "The wrap mode must be a defined enum value."
            );
        }

        if (!float.IsFinite(maxAnisotropy) || maxAnisotropy <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAnisotropy),
                maxAnisotropy,
                "The maximum anisotropy must be a positive finite number."
            );
        }

        if (alignment is not 1u and not 16u and not 64u and not 256u)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                "The alignment must be 1, 16, 64, or 256 bytes."
            );
        }

        bool isSrgb = srgb ?? channelType == TextureChannelType.Rgba;
        BakedTexture baked = Bake(rgba8Pixels, width, height, channelType, compression, isSrgb);

        string payloadPath = context.BaseAssetPath + "-ktx2";
        var descriptor = new TextureAssetDescriptor
        {
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
    /// <returns>The KTX2 payload with its descriptor format and mip count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is invalid or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Encoding or writing failed.</exception>
    public static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression
    ) =>
        BakeCore(
            rgba8Pixels,
            width,
            height,
            channelType,
            compression,
            isSrgb: channelType == TextureChannelType.Rgba
        );

    /// <summary>Bakes one texture with an explicit transfer function for RGBA.</summary>
    /// <remarks>The flag is ignored for the linear channel types.</remarks>
    /// <returns>The KTX2 payload with its descriptor format and mip count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is invalid or an enum value is undefined.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel buffer length is not <c>width × height × 4</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Encoding or writing failed.</exception>
    internal static BakedTexture Bake(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb
    ) => BakeCore(rgba8Pixels, width, height, channelType, compression, isSrgb);

    private static BakedTexture BakeCore(
        ReadOnlySpan<byte> rgba8Pixels,
        int width,
        int height,
        TextureChannelType channelType,
        TextureCompression compression,
        bool isSrgb
    )
    {
        if (!TextureFormatMapping.IsDefined(channelType, compression))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelType),
                channelType,
                "The channel type and compression mode must be defined enum values."
            );
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The width must be positive."
            );
        }

        if (width > TextureLimits.MAX_DIMENSION)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"The width must be at most {TextureLimits.MAX_DIMENSION} pixels."
            );
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "The height must be positive."
            );
        }

        if (height > TextureLimits.MAX_DIMENSION)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"The height must be at most {TextureLimits.MAX_DIMENSION} pixels."
            );
        }

        long expectedLength = (long)width * height * 4;
        if (rgba8Pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"The RGBA8 pixel buffer is {rgba8Pixels.Length} bytes; expected {expectedLength} "
                    + "(width × height × 4).",
                nameof(rgba8Pixels)
            );
        }

        TextureFormat format = TextureFormatMapping.Map(channelType, compression, isSrgb);
        TextureFormatInfo formatInfo = TextureFormatInfo.For(format);
        TextureLevel[] levels = TextureMipChain.Generate(rgba8Pixels, width, height, channelType);
        var storedLevels = new byte[levels.Length][];
        for (int level = 0; level < levels.Length; level++)
        {
            storedLevels[level] = formatInfo.IsBlockCompressed
                ? BcTextureEncoder.Encode(levels[level], format)
                : PackUncompressed(levels[level], format);
        }

        byte[] payload = Ktx2Writer.Write(formatInfo, width, height, storedLevels);
        return new BakedTexture(payload, format, (uint)levels.Length);
    }

    private static byte[] PackUncompressed(TextureLevel level, TextureFormat format)
    {
        switch (format)
        {
            case TextureFormat.Rgba8Unorm:
            case TextureFormat.Rgba8Srgb:
                return level.Pixels;

            case TextureFormat.R8Unorm:
                var red = new byte[level.Width * level.Height];
                for (int pixel = 0; pixel < red.Length; pixel++)
                {
                    red[pixel] = level.Pixels[pixel * 4];
                }

                return red;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(format),
                    format,
                    "The format is not an uncompressed texture format."
                );
        }
    }
}
