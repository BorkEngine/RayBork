using System.Text;
using PolyType;
using RayBork.Bake;
using RayBork.Bake.Textures;
using RayBork.Internal;

namespace RayBork.BuiltInAssets;

/// <summary>
/// The tool-only processor that bakes the built-in white and error placeholder textures into the
/// embedded pack.
/// </summary>
/// <remarks>
/// <para>
/// A <c>.pixel</c> source is a small UTF-8 text marker whose trimmed content names the placeholder, so
/// the committed source stays self-describing instead of encoding pixels. The processor generates the
/// marker's image in memory and calls <see cref="TextureBaker.Bake"/> with the uncompressed RGBA
/// channel type, exactly like a generated game texture. It emits the
/// <see cref="TextureAssetDescriptor"/> at the source's base path and its KTX2 payload at
/// <c>&lt;base&gt;-ktx2</c>.
/// </para>
/// <para>
/// Both placeholders are authored color images, so they are stored with the sRGB transfer function
/// (<c>Rgba8Srgb</c>) and the descriptor reports <c>IsSrgb = true</c>; their payload and descriptor
/// therefore agree with the ordinary sRGB texture path. The white texture uses linear filtering and
/// clamp wrapping so it samples as a constant; the error texture uses nearest filtering so its
/// checkerboard stays visible at any scale, and clamp wrapping as well.
/// </para>
/// <para>
/// The processor is registered only by <c>RayBork.BuiltInAssets</c>; <c>.pixel</c> is not a
/// user-facing source type. It is synchronous, keeps no per-source state, and reports every failure as
/// <c>RB4007</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInTextureProcessor : IAssetProcessor {
    private const string PAYLOAD_SUFFIX = "-ktx2";
    private const string WHITE_MARKER = "white";
    private const string ERROR_MARKER = "error";
    private const int WHITE_TEXTURE_SIZE = 1;
    private const int ERROR_TEXTURE_SIZE = 4;
    private const float DEFAULT_ANISOTROPY = 1f;
    private const uint GPU_PAYLOAD_ALIGNMENT = 16;

    private static readonly string[] _extensions = ["pixel"];

    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <inheritdoc/>
    public string Name => "builtin-texture";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);

        if (!TryReadMarker(context, out string marker)) {
            return;
        }

        string name = context.SourceFileNameWithoutExtension;
        if (!string.Equals(marker, name, StringComparison.Ordinal)) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in texture marker '{context.RelativePath}' contains '{marker}'; the "
                    + $"marker text must match the file name '{name}'.");
            return;
        }

        int size;
        byte[] pixels;
        TextureFilter filter;
        switch (name) {
            case WHITE_MARKER:
                size = WHITE_TEXTURE_SIZE;
                pixels = [255, 255, 255, 255];
                filter = TextureFilter.Linear;
                break;

            case ERROR_MARKER:
                size = ERROR_TEXTURE_SIZE;
                pixels = CreateErrorCheckerboard();
                filter = TextureFilter.Nearest;
                break;

            default:
                context.Diagnostics.Error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"The built-in texture source '{context.RelativePath}' has the unsupported "
                        + $"placeholder name '{name}'; expected '{WHITE_MARKER}' or '{ERROR_MARKER}'.");
                return;
        }

        BakedTexture baked;
        try {
            baked = TextureBaker.Bake(
                pixels,
                size,
                size,
                TextureChannelType.Rgba,
                TextureCompression.None,
                isSrgb: true);
        } catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in texture '{context.RelativePath}' could not be baked: "
                    + exception.Message);
            return;
        }

        string payloadPath = context.BaseAssetPath + PAYLOAD_SUFFIX;
        var descriptor = new TextureAssetDescriptor {
            Payload = payloadPath,
            Width = (uint)size,
            Height = (uint)size,
            MipLevels = baked.MipLevels,
            Format = baked.Format,
            IsSrgb = baked.Format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb,
            Filter = filter,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = DEFAULT_ANISOTROPY,
        };

        context.Outputs.AddAsset(context.BaseAssetPath, descriptor);
        context.Outputs.AddData(payloadPath, baked.Payload.Span, GPU_PAYLOAD_ALIGNMENT);
    }

    /// <summary>
    /// Creates the error placeholder's magenta/opaque-black checkerboard pixels, row-major with a
    /// top-left origin. Cell <c>(x, y)</c> is magenta when <c>(x + y)</c> is even and opaque black
    /// otherwise.
    /// </summary>
    /// <returns>The straight-alpha RGBA8 pixels, exactly <c>size × size × 4</c> bytes.</returns>
    private static byte[] CreateErrorCheckerboard() {
        var pixels = new byte[ERROR_TEXTURE_SIZE * ERROR_TEXTURE_SIZE * 4];
        for (int y = 0; y < ERROR_TEXTURE_SIZE; y++) {
            for (int x = 0; x < ERROR_TEXTURE_SIZE; x++) {
                bool magenta = ((x + y) & 1) == 0;
                int offset = ((y * ERROR_TEXTURE_SIZE) + x) * 4;
                pixels[offset] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static bool TryReadMarker(AssetProcessorContext context, out string marker) {
        marker = string.Empty;

        byte[] bytes;
        try {
            bytes = File.ReadAllBytes(context.SourcePath);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Failed to read the built-in texture marker '{context.RelativePath}': "
                    + exception.Message);
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF) {
            content = content[3..];
        }

        string text;
        try {
            text = _strictUtf8.GetString(content);
        } catch (DecoderFallbackException) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in texture marker '{context.RelativePath}' is not valid UTF-8.");
            return false;
        }

        marker = text.Trim();
        if (marker.Length == 0) {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in texture marker '{context.RelativePath}' is empty; it must contain "
                    + "the placeholder name.");
            return false;
        }

        return true;
    }
}
