using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>
/// The inputs for one engine asset decode: the canonical path, the descriptor bytes, the visible pack
/// stack used to resolve sibling payload entries, and the app-lifetime font services.
/// </summary>
/// <remarks>
/// <para>
/// The context is immutable and only valid for the duration of one decode on the catalog loader
/// thread. The visible stack is the catalog's mounted snapshot; it is published atomically on start
/// and never mutated by a loader, so a decode may read it without locking.
/// </para>
/// <para>
/// The glyph generation service and the glyph texture factory are provided for
/// <see cref="FontAssetFormat"/>, which creates one <see cref="FontGlyphPipeline"/> per decoded face.
/// The factory uploads a generated MSDF bitmap as a procedural texture; the service runs the
/// generation on the app-lifetime font thread. Other formats ignore them.
/// </para>
/// </remarks>
internal sealed class AssetFormatContext
{
    private readonly Dictionary<string, VisibleAsset> _visibleStack;

    /// <summary>
    /// Initializes a decode context.
    /// </summary>
    /// <param name="canonicalPath">The canonical descriptor path, without a leading slash.</param>
    /// <param name="descriptorPayload">The complete descriptor payload bytes.</param>
    /// <param name="visibleStack">The visible pack stack that resolves sibling payload entries.</param>
    /// <param name="glyphService">The app-lifetime glyph generation service.</param>
    /// <param name="glyphTextureFactory">The factory that uploads a generated glyph bitmap.</param>
    /// <param name="fontLogger">The font logger category for per-glyph diagnostics.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
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

    /// <summary>
    /// Gets the canonical descriptor path, without a leading slash.
    /// </summary>
    internal string CanonicalPath { get; }

    /// <summary>
    /// Gets the complete descriptor payload bytes. A format must treat the array as read-only.
    /// </summary>
    internal byte[] DescriptorPayload { get; }

    /// <summary>
    /// Gets the app-lifetime glyph generation service. <see cref="FontAssetFormat"/> passes it to the
    /// per-font pipeline it creates.
    /// </summary>
    internal GlyphGenerationService GlyphService { get; }

    /// <summary>
    /// Gets the factory that uploads a generated MSDF glyph bitmap as a procedural texture.
    /// </summary>
    internal IGlyphTextureFactory GlyphTextureFactory { get; }

    /// <summary>
    /// Gets the <c>RayBork.Font</c> logger that receives the per-glyph pipeline diagnostics.
    /// </summary>
    internal ILogger FontLogger { get; }

    /// <summary>
    /// Reads one sibling payload entry from the visible pack stack.
    /// </summary>
    /// <remarks>
    /// The path must be canonical, resolve in the visible stack, and name a raw entry. Every rejection
    /// is an <see cref="AssetLoadException"/> naming the descriptor path and the payload kind.
    /// </remarks>
    /// <param name="payloadPath">The payload path named by the descriptor.</param>
    /// <param name="payloadKind">The payload kind used in diagnostics, such as <c>mesh</c>.</param>
    /// <returns>The decompressed payload bytes.</returns>
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
