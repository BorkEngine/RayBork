using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The packed descriptor of a font asset. Its payload is the original TTF or OTF face bytes.
/// </summary>
/// <remarks>
/// The whole face is packed; there is no preload range. The runtime parses the face and generates an
/// MSDF glyph texture on demand for any mapped glyph. A pack baked before M6 carries the removed
/// <c>Ranges</c> member and must be rebaked.
/// </remarks>
[GenerateShape]
internal sealed partial class FontAssetDescriptor {
    /// <summary>Gets the canonical path of the face payload entry.</summary>
    public required string FacePayload { get; init; }

    /// <summary>Gets the face container format, <c>ttf</c> or <c>otf</c>.</summary>
    public required string Format { get; init; }

    /// <summary>Gets the face payload length in bytes.</summary>
    public required uint ByteLength { get; init; }

    /// <summary>Gets the authored MSDF pixel size.</summary>
    public required int MsdfPixelSize { get; init; }

    /// <summary>Gets the authored MSDF distance range.</summary>
    public required int MsdfRange { get; init; }

    /// <summary>Gets the authored MSDF padding.</summary>
    public required int MsdfPadding { get; init; }
}
