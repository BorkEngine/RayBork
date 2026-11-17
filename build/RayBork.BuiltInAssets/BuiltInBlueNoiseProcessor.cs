using PolyType;
using RayBork.Bake;
using RayBork.Bake.Textures;
using RayBork.Internal;

namespace RayBork.BuiltInAssets;

/// <summary>
/// The tool-only processor that bakes the N8AO blue-noise image into the built-in pack.
/// </summary>
/// <remarks>
/// <para>
/// The source is the raw 128x128 straight-alpha RGBA8 image, byte-for-byte the <c>BlueNoise.js</c>
/// payload shipped by N8AO 2.0. The processor validates the byte count, bakes a linear
/// <see cref="TextureFormat.Rgba8Unorm"/> texture with nearest filtering and repeat wrapping, and
/// emits it at <see cref="SsaoShaderContract.BLUE_NOISE_PATH"/>.
/// </para>
/// <para>
/// The processor is synchronous and keeps no per-source state. It reports every failure as
/// <c>RB4007</c>.
/// </para>
/// </remarks>
internal sealed class BuiltInBlueNoiseProcessor : IAssetProcessor
{
    private const int SIZE = 128;
    private const int BYTE_COUNT = SIZE * SIZE * 4;
    private const string SOURCE_FILE_NAME = "blue-noise.rgba8";

    private static readonly string[] _extensions = ["rgba8"];

    /// <inheritdoc/>
    public string Name => "builtin-blue-noise";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (
            !string.Equals(
                context.SourceFileNameWithoutExtension,
                Path.GetFileNameWithoutExtension(SOURCE_FILE_NAME),
                StringComparison.Ordinal
            )
        )
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in raw image '{context.RelativePath}' is not the supported "
                    + $"'{SOURCE_FILE_NAME}' source."
            );
            return;
        }

        byte[] pixels;
        try
        {
            pixels = File.ReadAllBytes(context.SourcePath);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Failed to read the built-in blue-noise source '{context.RelativePath}': "
                    + exception.Message
            );
            return;
        }

        if (pixels.Length != BYTE_COUNT)
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in blue-noise source '{context.RelativePath}' is {pixels.Length} bytes; "
                    + $"it must be exactly {BYTE_COUNT} bytes for a {SIZE}x{SIZE} RGBA8 image."
            );
            return;
        }

        try
        {
            TextureBaker.AddTo(
                context,
                pixels,
                SIZE,
                SIZE,
                TextureChannelType.Rgba,
                TextureCompression.None,
                TextureFilter.Nearest,
                TextureWrap.Repeat,
                srgb: false,
                maxAnisotropy: 1f
            );
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            context.Diagnostics.Error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"The built-in blue-noise texture '{context.RelativePath}' could not be baked: "
                    + exception.Message
            );
        }
    }
}
