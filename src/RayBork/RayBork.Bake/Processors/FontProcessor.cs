using RayBork.Bake.Fonts;
using RayBork.Internal;

namespace RayBork.Bake.Processors;

/// <summary>The built-in font processor: validates the sfnt face and emits a
/// <see cref="FontAssetDescriptor"/> plus a <c>-face</c> payload.</summary>
/// <remarks>
/// The optional <c>[font]</c> sidecar sets <c>msdf_pixel_size</c> (32), <c>msdf_range</c> (4), and
/// <c>msdf_padding</c> (2); the pixel size must exceed twice the padding. An invalid container,
/// CFF2 outline, or setting is <c>RB4010</c> and emits no outputs.
/// </remarks>
internal sealed class FontProcessor : AssetProcessor<FontSidecar>
{
    private const int DEFAULT_MSDF_PIXEL_SIZE = 32;
    private const int DEFAULT_MSDF_RANGE = 4;
    private const int DEFAULT_MSDF_PADDING = 2;
    private const string FACE_PAYLOAD_SUFFIX = "-face";

    private static readonly string[] _extensions = ["ttf", "otf"];

    /// <inheritdoc/>
    public override string Name => "font";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public override SidecarPolicy Policy => SidecarPolicy.Optional;

    /// <inheritdoc/>
    protected override FontSidecar CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new FontSidecar();
    }

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, FontSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);

        if (
            !TryResolveSettings(
                context,
                sidecar,
                out int pixelSize,
                out int msdfRange,
                out int padding
            )
        )
        {
            return;
        }

        long length;
        byte[] face;
        try
        {
            length = new FileInfo(context.SourcePath).Length;
            if (length > uint.MaxValue)
            {
                context.Diagnostics.Error(
                    FontDiagnosticCodes.FONT_INVALID,
                    $"Font source '{context.RelativePath}' is {length} bytes; the packed face length "
                        + "must fit a 32-bit unsigned value."
                );
                return;
            }

            face = File.ReadAllBytes(context.SourcePath);
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
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' could not be read: {exception.Message}"
            );
            return;
        }

        if (!SfntValidator.TryValidate(face, out string format, out bool cff2, out string? reason))
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' is not a valid sfnt face: {reason}."
            );
            return;
        }

        if (cff2)
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' stores CFF2 outlines; only TrueType 'glyf' and "
                    + "CFF 1.0 outlines are supported."
            );
            return;
        }

        string payloadPath = context.BaseAssetPath + FACE_PAYLOAD_SUFFIX;
        context.Outputs.AddAsset(
            context.BaseAssetPath,
            new FontAssetDescriptor
            {
                FacePayload = payloadPath,
                Format = format,
                ByteLength = (uint)face.Length,
                MsdfPixelSize = pixelSize,
                MsdfRange = msdfRange,
                MsdfPadding = padding,
            }
        );
        context.Outputs.AddData(payloadPath, face, alignment: 16);
    }

    private static bool TryResolveSettings(
        AssetProcessorContext context,
        FontSidecar sidecar,
        out int pixelSize,
        out int msdfRange,
        out int padding
    )
    {
        pixelSize = sidecar.MsdfPixelSize ?? DEFAULT_MSDF_PIXEL_SIZE;
        msdfRange = sidecar.MsdfRange ?? DEFAULT_MSDF_RANGE;
        padding = sidecar.MsdfPadding ?? DEFAULT_MSDF_PADDING;

        if (pixelSize <= 0)
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' declares msdf_pixel_size {pixelSize}; it "
                    + "must be positive."
            );
            return false;
        }

        if (msdfRange <= 0)
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' declares msdf_range {msdfRange}; it must be "
                    + "positive."
            );
            return false;
        }

        if (padding < 0)
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' declares msdf_padding {padding}; it must be "
                    + "non-negative."
            );
            return false;
        }

        if (pixelSize <= (2L * padding))
        {
            context.Diagnostics.Error(
                FontDiagnosticCodes.FONT_INVALID,
                $"Font source '{context.RelativePath}' declares msdf_pixel_size {pixelSize} with "
                    + $"msdf_padding {padding}; the pixel size must be greater than twice the padding "
                    + "so that a glyph box remains."
            );
            return false;
        }

        return true;
    }
}
