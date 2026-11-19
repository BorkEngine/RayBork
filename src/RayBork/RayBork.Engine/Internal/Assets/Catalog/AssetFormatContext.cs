using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>The inputs for one engine asset decode.</summary>
/// <remarks>
/// A context carries the canonical descriptor path, its payload bytes, the visible pack stack, and
/// the app-lifetime font services. It is immutable and valid only for one decode on the loader
/// thread; the glyph services are used only by <see cref="FontAssetFormat"/>.
/// </remarks>
internal sealed class AssetFormatContext
{
    private readonly Dictionary<string, VisibleAsset> _visibleStack;

    /// <summary>Initializes a decode context.</summary>
    internal AssetFormatContext(
        string canonicalPath,
        byte[] descriptorPayload,
        Dictionary<string, VisibleAsset> visibleStack,
        GlyphGenerationService glyphService,
        IGlyphTextureFactory glyphTextureFactory,
        ILogger fontLogger
    )
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(descriptorPayload);
        ArgumentNullException.ThrowIfNull(visibleStack);
        ArgumentNullException.ThrowIfNull(glyphService);
        ArgumentNullException.ThrowIfNull(glyphTextureFactory);
        ArgumentNullException.ThrowIfNull(fontLogger);

        CanonicalPath = canonicalPath;
        DescriptorPayload = descriptorPayload;
        GlyphService = glyphService;
        GlyphTextureFactory = glyphTextureFactory;
        FontLogger = fontLogger;
        _visibleStack = visibleStack;
    }

    /// <summary>Gets the canonical descriptor path, without a leading slash.</summary>
    internal string CanonicalPath { get; }

    /// <summary>Gets the descriptor payload bytes; formats must not modify them.</summary>
    internal byte[] DescriptorPayload { get; }

    /// <summary>Gets the app-lifetime glyph generation service.</summary>
    internal GlyphGenerationService GlyphService { get; }

    /// <summary>Gets the factory that uploads a generated glyph bitmap.</summary>
    internal IGlyphTextureFactory GlyphTextureFactory { get; }

    /// <summary>Gets the font logger category for per-glyph diagnostics.</summary>
    internal ILogger FontLogger { get; }

    /// <summary>Reads one sibling raw payload entry.</summary>
    /// <param name="payloadPath">The payload path named by the descriptor.</param>
    /// <param name="payloadKind">The payload kind used in diagnostics, such as <c>mesh</c>.</param>
    /// <exception cref="AssetLoadException">The payload is invalid or missing.</exception>
    internal byte[] ReadRawPayload(string payloadPath, string payloadKind)
    {
        if (!AssetPath.TryValidateCanonical(payloadPath, out string? reason))
        {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload path '{payloadPath}' is not canonical: {reason}"
            );
        }

        if (!_visibleStack.TryGetValue(payloadPath, out VisibleAsset payload))
        {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload '{payloadPath}' is not in the visible pack stack"
            );
        }

        if (payload.Entry.Type != AssetEntryType.Raw)
        {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload '{payloadPath}' is a {payload.Entry.Type} entry, not raw"
            );
        }

        return payload.Archive.ReadPayload(payload.Entry);
    }
}
