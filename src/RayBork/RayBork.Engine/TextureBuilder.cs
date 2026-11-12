using RayBork.Internal;

namespace RayBork;

/// <summary>
/// Builds one immutable procedural <see cref="Texture"/> from mutable CPU pixels.
/// </summary>
/// <remarks>
/// <para>
/// Create the builder with <see cref="EngineContext.CreateTextureBuilder"/>, set the base level with
/// one of the <c>SetPixels</c> overloads, and call <see cref="BuildAsync"/>. The texture is one-shot:
/// the builder owns its CPU pixels and the asset exposes only GPU metadata, so the pixel data is not
/// readable from the built texture. The builder can be reused to build another texture.
/// </para>
/// <para>
/// A builder instance is not thread-safe; use one per thread. The factory, the CPU members, and
/// <see cref="BuildAsync"/> do not touch tick-affine engine state, so they may be called from any
/// thread, including a worker thread. Awaiting a build on the tick thread suspends that tick's
/// continuation exactly like an asset load. After the app stops, every member that touches the
/// builder throws <see cref="ObjectDisposedException"/>.
/// </para>
/// <para>
/// Procedural textures use the uncompressed <see cref="TextureFormat.Rgba8Unorm"/>,
/// <see cref="TextureFormat.Rgba8Srgb"/>, and <see cref="TextureFormat.R8Unorm"/> formats. There is no
/// runtime block encoder and no raw KTX2 upload: block-compressed textures come from the bake.
/// </para>
/// </remarks>
public sealed class TextureBuilder {
    /// <summary>The largest accepted base-level width or height in pixels. It matches the bake limit.</summary>
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
    /// Initializes a builder. Game code uses <see cref="EngineContext.CreateTextureBuilder"/> so the
    /// builder always has the app's GPU upload service.
    /// </summary>
    /// <param name="service">The app's procedural asset service.</param>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="format">The uncompressed storage format.</param>
    /// <param name="generateMips">Whether a build generates the full mip chain when none exists.</param>
    /// <param name="sampler">The sampler state, or the default linear repeat sampler when omitted.</param>
    /// <exception cref="ArgumentNullException"><paramref name="service"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive or exceeds 16384.</exception>
    /// <exception cref="ArgumentException">
    /// The format is not an uncompressed procedural format, or the sampler state is not valid.
    /// </exception>
    internal TextureBuilder(
        ProceduralAssetService service,
        int width,
        int height,
        TextureFormat format,
        bool generateMips,
        SamplerState? sampler) {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MAX_DIMENSION);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MAX_DIMENSION);
        if (!IsSupportedFormat(format)) {
            throw new ArgumentException(
                $"The texture format '{format}' is not a procedural texture format; use Rgba8Unorm, "
                    + "Rgba8Srgb, or R8Unorm.",
                nameof(format));
        }

        if (sampler.HasValue) {
            ValidateSampler(sampler.Value);
        }

        _service = service;
        _width = width;
        _height = height;
        _format = format;
        _generateMips = generateMips;
        _sampler = sampler ?? new SamplerState(TextureFilter.Linear, TextureWrap.Repeat, 1f);
    }

    /// <summary>
    /// Gets the base-level width in pixels.
    /// </summary>
    public int Width {
        get {
            _service.ThrowIfStopped();
            return _width;
        }
    }

    /// <summary>
    /// Gets the base-level height in pixels.
    /// </summary>
    public int Height {
        get {
            _service.ThrowIfStopped();
            return _height;
        }
    }

    /// <summary>
    /// Gets the uncompressed storage format.
    /// </summary>
    public TextureFormat Format {
        get {
            _service.ThrowIfStopped();
            return _format;
        }
    }

    /// <summary>
    /// Gets the sampler state the built texture uses.
    /// </summary>
    public SamplerState Sampler {
        get {
            _service.ThrowIfStopped();
            return _sampler;
        }
    }

    /// <summary>
    /// Gets whether <see cref="BuildAsync"/> generates the full mip chain when no chain exists.
    /// </summary>
    public bool GenerateMipsEnabled {
        get {
            _service.ThrowIfStopped();
            return _generateMips;
        }
    }

    /// <summary>
    /// Sets the base-level pixels from a tightly packed byte buffer. The buffer length must be exactly
    /// <c>width × height × 4</c> for the RGBA8 formats or <c>width × height</c> for
    /// <see cref="TextureFormat.R8Unorm"/>.
    /// </summary>
    /// <param name="pixels">
    /// The base-level pixels. For the sRGB format the bytes are already sRGB-encoded; the alpha
    /// channel of every format is linear. The builder keeps a copy.
    /// </param>
    /// <exception cref="ArgumentException">The buffer length does not match the size and format.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public void SetPixels(ReadOnlySpan<byte> pixels) {
        _service.ThrowIfStopped();
        int bytesPerPixel = BytesPerPixel(_format);
        long expected = (long)_width * _height * bytesPerPixel;
        if (pixels.Length != expected) {
            throw new ArgumentException(
                $"The pixel buffer is {pixels.Length} bytes; a {_width}x{_height} {_format} base level "
                    + $"requires exactly {expected} bytes (width × height × {bytesPerPixel}).",
                nameof(pixels));
        }

        _basePixels = pixels.ToArray();
        _mipChain = null;
    }

    /// <summary>
    /// Sets the base-level pixels from one linear <see cref="Color"/> per pixel.
    /// </summary>
    /// <param name="colors">
    /// The base-level colors. For <see cref="TextureFormat.Rgba8Srgb"/> the RGB channels are converted
    /// to 8-bit sRGB and alpha stays linear. For <see cref="TextureFormat.Rgba8Unorm"/> every channel
    /// is stored as linear unorm. For <see cref="TextureFormat.R8Unorm"/> only the red channel is
    /// stored. Components are clamped to <c>[0, 1]</c>. The builder keeps a converted copy.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The color count does not match the size, or a component is not finite.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    public void SetPixels(ReadOnlySpan<Color> colors) {
        _service.ThrowIfStopped();
        long expected = (long)_width * _height;
        if (colors.Length != expected) {
            throw new ArgumentException(
                $"The color buffer has {colors.Length} entries; a {_width}x{_height} texture requires "
                    + $"exactly {expected}.",
                nameof(colors));
        }

        for (int index = 0; index < colors.Length; index++) {
            Color color = colors[index];
            if (!IsFinite(color)) {
                throw new ArgumentException(
                    $"The color at index {index} has a non-finite component.",
                    nameof(colors));
            }
        }

        int bytesPerPixel = BytesPerPixel(_format);
        byte[] pixels = new byte[checked(colors.Length * bytesPerPixel)];
        for (int pixel = 0; pixel < colors.Length; pixel++) {
            Color color = colors[pixel];
            int offset = pixel * bytesPerPixel;
            if (bytesPerPixel == 1) {
                pixels[offset] = ToUnormByte(color.R);
                continue;
            }

            pixels[offset] = _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.R) : ToUnormByte(color.R);
            pixels[offset + 1] = _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.G) : ToUnormByte(color.G);
            pixels[offset + 2] = _format == TextureFormat.Rgba8Srgb ? ToSrgbByte(color.B) : ToUnormByte(color.B);
            pixels[offset + 3] = ToUnormByte(color.A);
        }

        _basePixels = pixels;
        _mipChain = null;
    }

    /// <summary>
    /// Regenerates the full mip chain from the current base level.
    /// </summary>
    /// <exception cref="InvalidOperationException">The base-level pixels were not set.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <remarks>
    /// Every level is produced by the bake's deterministic box filter: the destination level has
    /// <c>max(1, width / 2)</c> by <c>max(1, height / 2)</c> pixels, and each destination pixel
    /// averages the source rectangle it covers with half-up integer rounding. The filter averages the
    /// stored bytes; for the sRGB format it does not decode the transfer function first. A build with
    /// mip generation enabled calls this automatically when no chain exists.
    /// </remarks>
    public void GenerateMips() {
        _service.ThrowIfStopped();
        if (_basePixels is null) {
            throw new InvalidOperationException("Set the base-level pixels before generating mipmaps.");
        }

        _mipChain = BuildMipChain(_basePixels, _width, _height, BytesPerPixel(_format));
    }

    /// <summary>
    /// Uploads the texture on the render thread and returns the immutable asset after the GPU resource
    /// is ready.
    /// </summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The created texture. The caller owns one reference and must dispose it exactly once.</returns>
    /// <exception cref="InvalidOperationException">The base-level pixels were not set.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled before the upload completed.</exception>
    /// <exception cref="ObjectDisposedException">The app has stopped.</exception>
    /// <exception cref="AssetLoadException">The GPU upload failed.</exception>
    /// <remarks>
    /// With mip generation enabled, the build generates the full chain when <see cref="GenerateMips"/>
    /// was not called. The raw levels are uploaded through the non-blocking GPU bridge without a KTX2
    /// container. The returned texture has <see cref="Texture.IsProcedural"/> set and a reserved
    /// synthetic <see cref="Texture.Path"/>.
    /// </remarks>
    public async Task<Texture> BuildAsync(CancellationToken cancellationToken = default) {
        _service.ThrowIfStopped();
        if (_basePixels is null) {
            throw new InvalidOperationException("Set the base-level pixels before building the texture.");
        }

        if (_generateMips && _mipChain is null) {
            GenerateMips();
        }

        byte[][] levels = _mipChain ?? [_basePixels];
        byte[] pixelData = ConcatenateLevels(levels, out int[] levelByteLengths);
        int id = ProceduralAssetIds.NextTextureId();
        string canonicalPath = ProceduralAssetIds.TexturePath(id);
        TextureAssetDescriptor descriptor = new() {
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
            levelByteLengths);
        GpuAssetResource resource = await _service.UploadAsync(upload, cancellationToken).ConfigureAwait(false);
        try {
            EngineAsset lifetime = new(canonicalPath, resource, _service.QueueDestruction, static () => { });
            lifetime.AddOwnerReference();
            return new Texture(lifetime, descriptor, isProcedural: true);
        } catch {
            _service.QueueDestruction(resource);
            throw;
        }
    }

    private static void ValidateSampler(SamplerState sampler) {
        if (!Enum.IsDefined(sampler.Filter)) {
            throw new ArgumentException("The sampler filter is not a defined value.", nameof(sampler));
        }

        if (!Enum.IsDefined(sampler.Wrap)) {
            throw new ArgumentException("The sampler wrap mode is not a defined value.", nameof(sampler));
        }

        if (!float.IsFinite(sampler.MaxAnisotropy) || sampler.MaxAnisotropy < 1f) {
            throw new ArgumentException(
                "The sampler maximum anisotropy must be finite and at least 1.",
                nameof(sampler));
        }
    }

    private static bool IsSupportedFormat(TextureFormat format) => format is
        TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.R8Unorm;

    private static int BytesPerPixel(TextureFormat format) => format switch {
        TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb => 4,
        TextureFormat.R8Unorm => 1,
        _ => throw new ArgumentException(
            $"The texture format '{format}' is not a procedural texture format.",
            nameof(format)),
    };

    private static byte[] ConcatenateLevels(byte[][] levels, out int[] levelByteLengths) {
        long totalLength = 0;
        foreach (byte[] level in levels) {
            totalLength += level.Length;
        }

        if (totalLength > int.MaxValue) {
            throw new InvalidOperationException(
                $"The {levels.Length}-level mip chain is {totalLength} bytes, which exceeds the maximum "
                    + "upload size.");
        }

        byte[] pixelData = new byte[totalLength];
        levelByteLengths = new int[levels.Length];
        int offset = 0;
        for (int level = 0; level < levels.Length; level++) {
            byte[] pixels = levels[level];
            pixels.CopyTo(pixelData, offset);
            levelByteLengths[level] = pixels.Length;
            offset += pixels.Length;
        }

        return pixelData;
    }

    private static byte[][] BuildMipChain(byte[] basePixels, int width, int height, int bytesPerPixel) {
        int levelCount = LevelCount(width, height);
        byte[][] levels = new byte[levelCount][];
        levels[0] = basePixels;
        int currentWidth = width;
        int currentHeight = height;
        for (int level = 1; level < levelCount; level++) {
            byte[] next = Downsample(levels[level - 1], currentWidth, currentHeight, bytesPerPixel, out int nextWidth, out int nextHeight);
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
        out int nextHeight) {
        nextWidth = Math.Max(1, width / 2);
        nextHeight = Math.Max(1, height / 2);
        byte[] destination = new byte[nextWidth * nextHeight * bytesPerPixel];
        for (int y = 0; y < nextHeight; y++) {
            int sourceY0 = y * height / nextHeight;
            int sourceY1 = (y + 1) * height / nextHeight;
            for (int x = 0; x < nextWidth; x++) {
                int sourceX0 = x * width / nextWidth;
                int sourceX1 = (x + 1) * width / nextWidth;
                int count = (sourceX1 - sourceX0) * (sourceY1 - sourceY0);
                int destinationOffset = ((y * nextWidth) + x) * bytesPerPixel;
                for (int channel = 0; channel < bytesPerPixel; channel++) {
                    int sum = 0;
                    for (int sourceY = sourceY0; sourceY < sourceY1; sourceY++) {
                        int rowOffset = sourceY * width * bytesPerPixel;
                        for (int sourceX = sourceX0; sourceX < sourceX1; sourceX++) {
                            sum += source[rowOffset + (sourceX * bytesPerPixel) + channel];
                        }
                    }

                    destination[destinationOffset + channel] = (byte)((sum + (count / 2)) / count);
                }
            }
        }

        return destination;
    }

    private static int LevelCount(int width, int height) {
        int levels = 1;
        int levelWidth = width;
        int levelHeight = height;
        while (levelWidth > 1 || levelHeight > 1) {
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
            levels++;
        }

        return levels;
    }

    private static byte ToUnormByte(float linear) {
        float clamped = Math.Clamp(linear, 0f, 1f);
        return (byte)((int)((clamped * UNORM_MAX) + 0.5f));
    }

    private static byte ToSrgbByte(float linear) {
        float clamped = Math.Clamp(linear, 0f, 1f);
        float encoded = clamped <= SRGB_LINEAR_THRESHOLD
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
