using RayBork.Bake.Textures;

namespace RayBork.Bake.Processors;

/// <summary>
/// The built-in texture processor: decodes PNG, JPEG, TGA, and BMP sources to straight-alpha RGBA8
/// and emits a <see cref="RayBork.Internal.TextureAssetDescriptor"/> plus its KTX2 payload.
/// </summary>
/// <remarks>
/// <para>
/// Decoding rules live in <see cref="TextureSourceReader"/>: StbImageSharp to straight-alpha RGBA8
/// with no premultiplication, gamma, ICC, flipping, or resampling; a 16-bit PNG or a source outside
/// the bake's size policy is rejected before the decoder allocates pixels; and every decode failure
/// is an <c>RB4007</c> diagnostic. A failed decode, sidecar value, or bake emits no outputs.
/// </para>
/// <para>
/// The decoded pixels and the sidecar's format, transfer function, and sampler settings go through
/// <see cref="TextureBaker"/>, so the processor never writes KTX2 headers or block data itself. The
/// descriptor is added through <see cref="IAssetOutputSink.AddAsset{T}"/> and the payload through
/// <see cref="IAssetOutputSink.AddData"/> with the alignment the GPU upload requires. The processor
/// keeps no per-source state and reads the latest builder sampler defaults at process time.
/// </para>
/// </remarks>
internal sealed class TextureProcessor : AssetProcessor<TextureSidecar> {
    private const string PAYLOAD_SUFFIX = "-ktx2";
    private const float DEFAULT_ANISOTROPY = 1f;

    private static readonly string[] _extensions = ["png", "jpg", "jpeg", "tga", "bmp"];

    private readonly Func<TextureFilter> _defaultFilter;
    private readonly Func<TextureWrap> _defaultWrap;

    /// <summary>Initializes the built-in texture processor.</summary>
    /// <param name="defaultFilter">Reads the current builder default filter.</param>
    /// <param name="defaultWrap">Reads the current builder default wrap mode.</param>
    internal TextureProcessor(Func<TextureFilter> defaultFilter, Func<TextureWrap> defaultWrap) {
        ArgumentNullException.ThrowIfNull(defaultFilter);
        ArgumentNullException.ThrowIfNull(defaultWrap);

        _defaultFilter = defaultFilter;
        _defaultWrap = defaultWrap;
    }

    /// <inheritdoc/>
    public override string Name => "texture";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public override SidecarPolicy Policy => SidecarPolicy.Optional;

    /// <inheritdoc/>
    protected override TextureSidecar CreateDefaultSidecar(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return new TextureSidecar();
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, TextureSidecar sidecar) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (!TryResolveFormat(context, sidecar, out TextureChannelType channelType,
                out TextureCompression compression, out bool isSrgb)
            || !TryResolveFilter(context, sidecar, out TextureFilter filter)
            || !TryResolveWrap(context, sidecar, out TextureWrap wrap)
            || !TryResolveAnisotropy(context, sidecar, out float anisotropy)) {
            return;
        }

        if (!TextureSourceReader.TryDecode(
                context.SourcePath,
                context.RelativePath,
                (code, message) => context.Diagnostics.Error(code, message),
                out byte[] pixels,
                out int width,
                out int height)) {
            return;
        }

        BakedTexture baked;
        try {
            baked = TextureBaker.Bake(
                pixels,
                width,
                height,
                channelType,
                compression,
                isSrgb);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Texture source '{context.RelativePath}' could not be baked: {exception.Message}");
            return;
        }

        string payloadPath = context.BaseAssetPath + PAYLOAD_SUFFIX;
        var descriptor = new RayBork.Internal.TextureAssetDescriptor {
            Payload = payloadPath,
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = baked.MipLevels,
            Format = baked.Format,
            IsSrgb = baked.Format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb,
            Filter = filter,
            Wrap = wrap,
            MaxAnisotropy = anisotropy,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, baked.Payload.Span, alignment: 16);
    }

    private static bool TryResolveFormat(
        AssetProcessorContext context,
        TextureSidecar sidecar,
        out TextureChannelType channelType,
        out TextureCompression compression,
        out bool isSrgb) {
        string format = sidecar.Format ?? "rgba8";
        bool defaultSrgb;
        switch (format) {
            case "rgba8":
                channelType = TextureChannelType.Rgba;
                compression = TextureCompression.None;
                defaultSrgb = true;
                break;

            case "bc7":
                channelType = TextureChannelType.Rgba;
                compression = TextureCompression.Auto;
                defaultSrgb = true;
                break;

            case "bc5":
                channelType = TextureChannelType.Rg;
                compression = TextureCompression.Auto;
                defaultSrgb = false;
                break;

            case "r8":
                channelType = TextureChannelType.R;
                compression = TextureCompression.None;
                defaultSrgb = false;
                break;

            default:
                channelType = default;
                compression = default;
                isSrgb = false;
                context.Diagnostics.Error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Texture source '{context.RelativePath}' declares the unsupported format "
                        + $"'{format}'; expected one of rgba8, bc7, bc5, or r8.");
                return false;
        }

        isSrgb = sidecar.Srgb ?? defaultSrgb;
        if (isSrgb && !defaultSrgb) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Texture source '{context.RelativePath}' sets srgb = true, but the format "
                    + $"'{format}' is a linear data format; sRGB storage is supported only for "
                    + "rgba8 and bc7.");
            return false;
        }

        return true;
    }

    private bool TryResolveFilter(
        AssetProcessorContext context,
        TextureSidecar sidecar,
        out TextureFilter filter) {
        if (sidecar.Filter is null) {
            filter = _defaultFilter();
            return true;
        }

        switch (sidecar.Filter) {
            case "nearest":
                filter = TextureFilter.Nearest;
                return true;

            case "linear":
                filter = TextureFilter.Linear;
                return true;

            case "anisotropic":
                filter = TextureFilter.Anisotropic;
                return true;

            default:
                filter = default;
                context.Diagnostics.Error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Texture source '{context.RelativePath}' declares the unsupported filter "
                        + $"'{sidecar.Filter}'; expected one of nearest, linear, or anisotropic.");
                return false;
        }
    }

    private bool TryResolveWrap(
        AssetProcessorContext context,
        TextureSidecar sidecar,
        out TextureWrap wrap) {
        if (sidecar.Wrap is null) {
            wrap = _defaultWrap();
            return true;
        }

        switch (sidecar.Wrap) {
            case "repeat":
                wrap = TextureWrap.Repeat;
                return true;

            case "clamp":
                wrap = TextureWrap.Clamp;
                return true;

            case "mirror":
                wrap = TextureWrap.Mirror;
                return true;

            default:
                wrap = default;
                context.Diagnostics.Error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Texture source '{context.RelativePath}' declares the unsupported wrap mode "
                        + $"'{sidecar.Wrap}'; expected one of repeat, clamp, or mirror.");
                return false;
        }
    }

    private static bool TryResolveAnisotropy(
        AssetProcessorContext context,
        TextureSidecar sidecar,
        out float anisotropy) {
        anisotropy = sidecar.Anisotropy ?? DEFAULT_ANISOTROPY;
        if (!float.IsFinite(anisotropy) || anisotropy <= 0f) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Texture source '{context.RelativePath}' declares the anisotropy "
                    + $"'{anisotropy}'; it must be a positive finite number.");
            return false;
        }

        return true;
    }
}
