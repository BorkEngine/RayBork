using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>
/// The built-in packed font format: it decodes the descriptor, reads the whole face, parses it, and
/// creates the per-font glyph pipeline.
/// </summary>
/// <remarks>
/// The format is stateless and runs on the loader thread; a font load is CPU-only and never
/// uploads to the GPU. Only TrueType <c>glyf</c> and CFF 1.0 outlines are supported, and a face,
/// payload, outline, or MSDF rejection is an <see cref="AssetLoadException"/> <c>RB1011</c>.
/// </remarks>
internal sealed class FontAssetFormat : IEngineAssetFormat<Font>
{
    /// <summary>The shared format instance.</summary>
    internal static readonly FontAssetFormat Instance = new();

    private FontAssetFormat() { }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Font;

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "Pipeline ownership transfers to the returned DecodedAsset and its release callback, which dispose it exactly once."
    )]
    public DecodedAsset<Font> Decode(AssetFormatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        FontAssetDescriptor descriptor = AssetSerialization.Deserialize<FontAssetDescriptor>(
            context.DescriptorPayload
        );
        byte[] payload = ReadFacePayload(descriptor, context);
        if (payload.Length != descriptor.ByteLength)
        {
            throw CreateFailure(
                context.CanonicalPath,
                $"the font face payload is {payload.Length} byte(s); the descriptor declares "
                    + $"{descriptor.ByteLength}"
            );
        }

        SfntFace face = ParseFace(payload, context.CanonicalPath);
        if (face.OutlineKind == SfntOutlineKind.Cff2)
        {
            throw CreateFailure(
                context.CanonicalPath,
                "unsupported font feature: the face stores CFF2 outlines; only TrueType 'glyf' and "
                    + "CFF 1.0 outlines are supported"
            );
        }

        FontGlyphSettings settings = CreateSettings(descriptor, face, context.CanonicalPath);
        FontGlyphPipeline pipeline = new(
            face,
            settings,
            context.GlyphTextureFactory,
            context.GlyphService,
            context.FontLogger
        );
        return new DecodedAsset<Font>(
            upload: null,
            lifetime => new Font(lifetime, face, pipeline),
            pipeline.Dispose
        );
    }

    private static byte[] ReadFacePayload(
        FontAssetDescriptor descriptor,
        AssetFormatContext context
    )
    {
        try
        {
            return context.ReadRawPayload(descriptor.FacePayload, "font face");
        }
        catch (AssetLoadException exception)
        {
            // ReadRawPayload already prefixes the asset path and names the payload kind and path; keep
            // that reason so the wrapped failure reports each fact once.
            string prefix =
                $"The asset '{AssetPath.ToPublic(context.CanonicalPath)}' could not be loaded: ";
            string reason = exception.Message.StartsWith(prefix, StringComparison.Ordinal)
                ? exception.Message[prefix.Length..]
                : exception.Message;
            throw CreateFailure(context.CanonicalPath, reason, exception);
        }
    }

    private static SfntFace ParseFace(byte[] payload, string canonical)
    {
        try
        {
            return SfntFace.Parse(payload);
        }
        catch (FontFormatException exception)
        {
            throw CreateFailure(canonical, $"invalid font face: {exception.Reason}", exception);
        }
        catch (FontUnsupportedException exception)
        {
            throw CreateFailure(
                canonical,
                $"unsupported font feature: {exception.Reason}",
                exception
            );
        }
    }

    private static FontGlyphSettings CreateSettings(
        FontAssetDescriptor descriptor,
        SfntFace face,
        string canonical
    )
    {
        try
        {
            return new FontGlyphSettings(
                face.UnitsPerEm,
                descriptor.MsdfPixelSize,
                descriptor.MsdfRange,
                descriptor.MsdfPadding
            );
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw CreateFailure(
                canonical,
                $"invalid MSDF settings: {exception.Message}",
                exception
            );
        }
    }

    private static AssetLoadException CreateFailure(
        string canonical,
        string reason,
        Exception? inner = null
    ) =>
        new(
            $"The asset '{AssetPath.ToPublic(canonical)}' could not be loaded: {reason}.",
            DiagnosticCodes.FONT_LOAD_FAILED,
            inner
        );
}
