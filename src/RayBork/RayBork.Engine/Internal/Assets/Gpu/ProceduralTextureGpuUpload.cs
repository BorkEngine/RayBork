namespace RayBork.Internal;

/// <summary>
/// The GPU upload for one procedural texture: tightly packed uncompressed mip levels plus the format
/// and sampler state.
/// </summary>
/// <remarks>
/// The payload is not KTX2: one contiguous mip chain, base level first, with per-level lengths.
/// Level data is retained for device recovery.
/// </remarks>
internal sealed class ProceduralTextureGpuUpload : AssetGpuUpload
{
    /// <summary>Initializes a procedural texture upload.</summary>
    internal ProceduralTextureGpuUpload(
        string path,
        int width,
        int height,
        TextureFormat format,
        TextureFilter filter,
        TextureWrap wrap,
        float maxAnisotropy,
        ReadOnlyMemory<byte> pixelData,
        ReadOnlyMemory<int> levelByteLengths
    )
        : base(AssetGpuUploadKind.ProceduralTexture, path)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (
            !Enum.IsDefined(format)
            || format
                is not (
                    TextureFormat.Rgba8Unorm
                    or TextureFormat.Rgba8Srgb
                    or TextureFormat.R8Unorm
                )
        )
        {
            throw new ArgumentException(
                $"The texture format '{format}' is not a procedural texture format.",
                nameof(format)
            );
        }

        if (levelByteLengths.Length == 0)
        {
            throw new ArgumentException(
                "The upload must carry at least one mip level.",
                nameof(levelByteLengths)
            );
        }

        long totalLength = 0;
        for (int level = 0; level < levelByteLengths.Length; level++)
        {
            int length = levelByteLengths.Span[level];
            if (length <= 0)
            {
                throw new ArgumentException(
                    $"Mip level {level} is empty.",
                    nameof(levelByteLengths)
                );
            }

            totalLength += length;
        }

        if (totalLength != pixelData.Length)
        {
            throw new ArgumentException(
                $"The mip levels describe {totalLength} bytes, but the pixel buffer is {pixelData.Length}.",
                nameof(levelByteLengths)
            );
        }

        Width = width;
        Height = height;
        Format = format;
        Filter = filter;
        Wrap = wrap;
        MaxAnisotropy = maxAnisotropy;
        PixelData = pixelData;
        LevelByteLengths = levelByteLengths.ToArray();
    }

    /// <summary>Gets the base-level width in pixels.</summary>
    internal int Width { get; }

    /// <summary>Gets the base-level height in pixels.</summary>
    internal int Height { get; }

    /// <summary>Gets the uncompressed storage format.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets the sampler filter.</summary>
    internal TextureFilter Filter { get; }

    /// <summary>Gets the sampler address mode.</summary>
    internal TextureWrap Wrap { get; }

    /// <summary>Gets the requested maximum anisotropy.</summary>
    internal float MaxAnisotropy { get; }

    /// <summary>Gets every mip level, base level first and tightly packed.</summary>
    internal ReadOnlyMemory<byte> PixelData { get; }

    /// <summary>Gets the stored byte length of each level, base level first.</summary>
    internal ReadOnlyMemory<int> LevelByteLengths { get; }

    /// <summary>Gets the number of mip levels.</summary>
    internal int MipLevels => LevelByteLengths.Length;

    /// <inheritdoc/>
    internal override long EstimatedBytes => PixelData.Length;
}
