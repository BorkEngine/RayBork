using PolyType;

namespace RayBork.Internal;

/// <summary>The packed descriptor of a font asset; its payload is the whole face.</summary>
/// <remarks>
/// The whole TTF or OTF face is packed and glyph MSDF textures are generated on demand. An unknown
/// descriptor member is ignored, so an older pack still loads.
/// </remarks>
[GenerateShape]
internal sealed partial class FontAssetDescriptor
{
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
