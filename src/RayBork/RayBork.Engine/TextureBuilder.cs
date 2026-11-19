using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Builds one immutable procedural <see cref="Texture"/> from mutable CPU pixels.
/// </summary>
/// <remarks>
/// Create it with <see cref="EngineContext.CreateTextureBuilder"/>; it copies the CPU pixels and
/// may build multiple textures, and supports only <see cref="TextureFormat.Rgba8Unorm"/>,
/// <see cref="TextureFormat.Rgba8Srgb"/>, and <see cref="TextureFormat.R8Unorm"/>.
/// An instance is not thread-safe, but its members may run on any thread; after the app stops
/// they throw <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class TextureBuilder
{
    /// <summary>
    /// The largest accepted base-level width or height in pixels; matches the bake limit.
    /// </summary>
    private const int MAX_DIMENSION = 16384;

    private const float SRGB_LINEAR_THRESHOLD = 0.0031308f;
    private const float SRGB_SLOPE = 12.92f;
    private const float SRGB_SCALE = 1.055f;
    private const float SRGB_OFFSET = 0.055f;
    private const float SRGB_EXPONENT = 1f / 2.4f;
    private const float UNORM_MAX = 255f;

    private readonly ProceduralAssetService _service;
    private readonly int _width;
    private readonly int _height;
    private readonly TextureFormat _format;
    private readonly bool _generateMips;
    private readonly SamplerState _sampler;

    private byte[]? _basePixels;
    private byte[][]? _mipChain;

    /// <summary>
    /// Initializes a builder; game code uses <see cref="EngineContext.CreateTextureBuilder"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or exceeds <c>16384</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The format is not procedural, or the sampler state is invalid.
    /// </exception>
    internal TextureBuilder(
        ProceduralAssetService service,
        int width,
        int height,
        TextureFormat format,
        bool generateMips,
        SamplerState? sampler
    )
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MAX_DIMENSION);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MAX_DIMENSION);
        if (!IsSupportedFormat(format))
        {
            throw new ArgumentException(
                $"The texture format '{format}' is not a procedural texture format; use Rgba8Unorm, "
                    + "Rgba8Srgb, or R8Unorm.",
                nameof(format)
            );
        }

        if (sampler.HasValue)
        {
            ValidateSampler(sampler.Value);
        }

        _service = service;
        _width = width;
        _height = height;
        _format = format;
        _generateMips = generateMips;
        _sampler = sampler ?? new SamplerState(TextureFilter.Linear, TextureWrap.Repeat, 1f);
    }

    /// <summary>Gets the base-level width in pixels.</summary>
    public int Width
    {
        get
        {
            _service.ThrowIfStopped();
            return _width;
        }
    }

    /// <summary>Gets the base-level height in pixels.</summary>
    public int Height
    {
        get
        {
            _service.ThrowIfStopped();
            return _height;
        }
    }

    /// <summary>Gets the uncompressed storage format.</summary>
    public TextureFormat Format
    {
        get
        {
            _service.ThrowIfStopped();
            return _format;
        }
    }

    /// <summary>Gets the sampler state the built texture uses.</summary>
    public SamplerState Sampler
    {
        get
        {
            _service.ThrowIfStopped();
            return _sampler;
        }
    }

    /// <summary>
    /// Gets whether <see cref="BuildAsync"/> generates a full mip chain when none exists.
    /// </summary>
    public bool GenerateMipsEnabled
    {
        get
        {
            _service.ThrowIfStopped();
            return _generateMips;
        }
    }

    /// <summary>
    /// Sets the base-level pixels from packed bytes; the length must equal <c>width × height × 4</c>
    /// for RGBA8 or <c>width × height</c> for <see cref="TextureFormat.R8Unorm"/>.
    /// </summary>
    /// <param name="pixels">
    /// The base-level pixels; sRGB bytes are already encoded and alpha is linear. The builder copies
    /// them.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The buffer length does not match the size and format.
    /// </exception>
    public void SetPixels(ReadOnlySpan<byte> pixels)
    {
        _service.ThrowIfStopped();
        int bytesPerPixel = BytesPerPixel(_format);
        long expected = (long)_width * _height * bytesPerPixel;
        if (pixels.Length != expected)
        {
            throw new ArgumentException(
                $"The pixel buffer is {pixels.Length} bytes; a {_width}x{_height} {_format} base level "
                    + $"requires exactly {expected} bytes (width × height × {bytesPerPixel}).",
                nameof(pixels)
            );
        }

        _basePixels = pixels.ToArray();
        _mipChain = null;
    }

    /// <summary>Sets the base-level pixels from one linear <see cref="Color"/> per pixel.</summary>
    /// <param name="colors">
    /// The base-level colors; components are clamped to <c>[0, 1]</c>, sRGB RGB is encoded, and
    /// alpha stays linear. The builder keeps a converted copy.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The color count is wrong, or a component is not finite.
    /// </exception>
    public void SetPixels(ReadOnlySpan<Color> colors)
    {
        _service.ThrowIfStopped();
        long expected = (long)_width * _height;
        if (colors.Length != expected)
        {
            throw new ArgumentException(
                $"The color buffer has {colors.Length} entries; a {_width}x{_height} texture requires "
                    + $"exactly {expected}.",
                nameof(colors)
            );
        }

        for (int index = 0; index < colors.Length; index++)
        {
            Color color = colors[index];
            if (!IsFinite(color))
            {
                throw new ArgumentException(
                    $"The color at index {index} has a non-finite component.",
                    nameof(colors)
                );
            }
        }

        int bytesPerPixel = BytesPerPixel(_format);
        byte[] pixels = new byte[checked(colors.Length * bytesPerPixel)];
        for (int pixel = 0; pixel < colors.Length; pixel++)
        {
            Color color = colors[pixel];
            int offset = pixel * bytesPerPixel;
            if (bytesPerPixel == 1)
            {
                pixels[offset] = ToUnormByte(color.R);
                continue;
            }

            pixels[offset] =
                _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.R) : ToUnormByte(color.R);
            pixels[offset + 1] =
                _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.G) : ToUnormByte(color.G);
            pixels[offset + 2] =
                _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.B) : ToUnormByte(color.B);
            pixels[offset + 3] = ToUnormByte(color.A);
        }

        _basePixels = pixels;
        _mipChain = null;
    }

    /// <summary>Regenerates the full mip chain from the current base level.</summary>
    /// <exception cref="InvalidOperationException">The base-level pixels were not set.</exception>
    /// <remarks>
    /// Each level uses the bake's deterministic box filter; sRGB bytes are averaged without decoding.
    /// </remarks>
    public void GenerateMips()
    {
        _service.ThrowIfStopped();
        if (_basePixels is null)
        {
            throw new InvalidOperationException(
                "Set the base-level pixels before generating mipmaps."
            );
        }

        _mipChain = BuildMipChain(_basePixels, _width, _height, BytesPerPixel(_format));
    }

    /// <summary>
    /// Uploads the texture on the render thread and returns it when the GPU resource is ready.
    /// </summary>
    /// <returns>The texture; the caller owns one reference and must dispose it once.</returns>
    /// <exception cref="InvalidOperationException">The base-level pixels were not set.</exception>
    /// <exception cref="OperationCanceledException">Canceled before the upload completed.</exception>
    /// <exception cref="AssetLoadException">The GPU upload failed.</exception>
    /// <remarks>
    /// With mip generation enabled, the chain is generated when <see cref="GenerateMips"/> was not
    /// called; the result is procedural with a synthetic <see cref="Texture.Path"/>.
    /// </remarks>
    public async Task<Texture> BuildAsync(CancellationToken cancellationToken = default)
    {
        _service.ThrowIfStopped();
        if (_basePixels is null)
        {
            throw new InvalidOperationException(
                "Set the base-level pixels before building the texture."
            );
        }

        if (_generateMips && _mipChain is null)
        {
            GenerateMips();
        }

        byte[][] levels = _mipChain ?? [_basePixels];
        byte[] pixelData = ConcatenateLevels(levels, out int[] levelByteLengths);
        int id = ProceduralAssetIds.NextTextureId();
        string canonicalPath = ProceduralAssetIds.TexturePath(id);
        TextureAssetDescriptor descriptor = new()
        {
            Payload = canonicalPath,
            Width = checked((uint)_width),
            Height = checked((uint)_height),
            MipLevels = checked((uint)levels.Length),
            Format = _format,
            IsSrgb = _format == TextureFormat.Rgba8Srgb,
            Filter = _sampler.Filter,
            Wrap = _sampler.Wrap,
            MaxAnisotropy = _sampler.MaxAnisotropy,
        };
        ProceduralTextureGpuUpload upload = new(
            canonicalPath,
            _width,
            _height,
            _format,
            _sampler.Filter,
            _sampler.Wrap,
            _sampler.MaxAnisotropy,
            pixelData,
            levelByteLengths
        );
        GpuAssetResource resource = await _service
            .UploadAsync(upload, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EngineAsset lifetime = new(
                canonicalPath,
                resource,
                _service.QueueDestruction,
                static () => { }
            );
            lifetime.AddOwnerReference();
            return new Texture(lifetime, descriptor, isProcedural: true);
        }
        catch
        {
            _service.QueueDestruction(resource);
            throw;
        }
    }

    private static void ValidateSampler(SamplerState sampler)
    {
        if (!Enum.IsDefined(sampler.Filter))
        {
            throw new ArgumentException(
                "The sampler filter is not a defined value.",
                nameof(sampler)
            );
        }

        if (!Enum.IsDefined(sampler.Wrap))
        {
            throw new ArgumentException(
                "The sampler wrap mode is not a defined value.",
                nameof(sampler)
            );
        }

        if (!float.IsFinite(sampler.MaxAnisotropy) || sampler.MaxAnisotropy < 1f)
        {
            throw new ArgumentException(
                "The sampler maximum anisotropy must be finite and at least 1.",
                nameof(sampler)
            );
        }
    }

    private static bool IsSupportedFormat(TextureFormat format) =>
        format is TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.R8Unorm;

    private static int BytesPerPixel(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => 4,
            TextureFormat.R8Unorm => 1,
            _ => throw new ArgumentException(
                $"The texture format '{format}' is not a procedural texture format.",
                nameof(format)
            ),
        };

    private static byte[] ConcatenateLevels(byte[][] levels, out int[] levelByteLengths)
    {
        long totalLength = 0;
        foreach (byte[] level in levels)
        {
            totalLength += level.Length;
        }

        if (totalLength > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"The {levels.Length}-level mip chain is {totalLength} bytes, which exceeds the maximum "
                    + "upload size."
            );
        }

        byte[] pixelData = new byte[totalLength];
        levelByteLengths = new int[levels.Length];
        int offset = 0;
        for (int level = 0; level < levels.Length; level++)
        {
            byte[] pixels = levels[level];
            pixels.CopyTo(pixelData, offset);
            levelByteLengths[level] = pixels.Length;
            offset += pixels.Length;
        }

        return pixelData;
    }

    private static byte[][] BuildMipChain(
        byte[] basePixels,
        int width,
        int height,
        int bytesPerPixel
    )
    {
        int levelCount = LevelCount(width, height);
        byte[][] levels = new byte[levelCount][];
        levels[0] = basePixels;
        int currentWidth = width;
        int currentHeight = height;
        for (int level = 1; level < levelCount; level++)
        {
            byte[] next = Downsample(
                levels[level - 1],
                currentWidth,
                currentHeight,
                bytesPerPixel,
                out int nextWidth,
                out int nextHeight
            );
            levels[level] = next;
            currentWidth = nextWidth;
            currentHeight = nextHeight;
        }

        return levels;
    }

    private static byte[] Downsample(
        byte[] source,
        int width,
        int height,
        int bytesPerPixel,
        out int nextWidth,
        out int nextHeight
    )
    {
        nextWidth = Math.Max(1, width / 2);
        nextHeight = Math.Max(1, height / 2);
        byte[] destination = new byte[nextWidth * nextHeight * bytesPerPixel];
        for (int y = 0; y < nextHeight; y++)
        {
            int sourceY0 = y * height / nextHeight;
            int sourceY1 = (y + 1) * height / nextHeight;
            for (int x = 0; x < nextWidth; x++)
            {
                int sourceX0 = x * width / nextWidth;
                int sourceX1 = (x + 1) * width / nextWidth;
                int count = (sourceX1 - sourceX0) * (sourceY1 - sourceY0);
                int destinationOffset = ((y * nextWidth) + x) * bytesPerPixel;
                for (int channel = 0; channel < bytesPerPixel; channel++)
                {
                    int sum = 0;
                    for (int sourceY = sourceY0; sourceY < sourceY1; sourceY++)
                    {
                        int rowOffset = sourceY * width * bytesPerPixel;
                        for (int sourceX = sourceX0; sourceX < sourceX1; sourceX++)
                        {
                            sum += source[rowOffset + (sourceX * bytesPerPixel) + channel];
                        }
                    }

                    destination[destinationOffset + channel] = (byte)((sum + (count / 2)) / count);
                }
            }
        }

        return destination;
    }

    private static int LevelCount(int width, int height)
    {
        int levels = 1;
        int levelWidth = width;
        int levelHeight = height;
        while (levelWidth > 1 || levelHeight > 1)
        {
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
            levels++;
        }

        return levels;
    }

    private static byte ToUnormByte(float linear)
    {
        float clamped = Math.Clamp(linear, 0f, 1f);
        return (byte)((int)((clamped * UNORM_MAX) + 0.5f));
    }

    private static byte ToSrgbByte(float linear)
    {
        float clamped = Math.Clamp(linear, 0f, 1f);
        float encoded =
            clamped <= SRGB_LINEAR_THRESHOLD
                ? SRGB_SLOPE * clamped
                : (SRGB_SCALE * MathF.Pow(clamped, SRGB_EXPONENT)) - SRGB_OFFSET;
        return ToUnormByte(encoded);
    }

    private static bool IsFinite(Color color) =>
        float.IsFinite(color.R)
        && float.IsFinite(color.G)
        && float.IsFinite(color.B)
        && float.IsFinite(color.A);
}
