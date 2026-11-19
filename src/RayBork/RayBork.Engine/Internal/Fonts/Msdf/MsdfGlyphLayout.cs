using System.Numerics;

namespace RayBork.Internal;

/// <summary>The pixel and em-unit metrics of one glyph's MSDF layout.</summary>
/// <remarks>
/// <see cref="Scale"/> is texture pixels per font unit, texture sizes are pixels, and bearings and
/// sizes are em units at that scale. The padded glyph box fits the authored pixel size, row zero
/// is the quad's upper edge, and a glyph without an outline has the all-zero empty layout.
/// </remarks>
internal readonly struct MsdfGlyphLayout
{
    /// <summary>Initializes a layout from its pixel texture and em-unit metrics.</summary>
    internal MsdfGlyphLayout(
        int textureWidth,
        int textureHeight,
        float scale,
        int range,
        float originX,
        float originY,
        float bearingXEm,
        float bearingYEm,
        float widthEm,
        float heightEm
    )
    {
        TextureWidth = textureWidth;
        TextureHeight = textureHeight;
        Scale = scale;
        Range = range;
        OriginX = originX;
        OriginY = originY;
        BearingXEm = bearingXEm;
        BearingYEm = bearingYEm;
        WidthEm = widthEm;
        HeightEm = heightEm;
    }

    /// <summary>Gets the empty layout of a glyph without an outline.</summary>
    internal static MsdfGlyphLayout Empty => default;

    /// <summary>Gets the texture width in pixels, or zero for the empty layout.</summary>
    internal int TextureWidth { get; }

    /// <summary>Gets the texture height in pixels, or zero for the empty layout.</summary>
    internal int TextureHeight { get; }

    /// <summary>Gets the raster scale in pixels per font unit, or zero for the empty layout.</summary>
    internal float Scale { get; }

    /// <summary>Gets the distance range in pixels; <c>range / 2</c> saturates a channel.</summary>
    internal int Range { get; }

    /// <summary>Gets the pixel x coordinate that the font-unit origin maps to.</summary>
    internal float OriginX { get; }

    /// <summary>Gets the pixel y of the font origin; pixel y points down.</summary>
    internal float OriginY { get; }

    /// <summary>Gets the quad's left edge in em units relative to the pen position.</summary>
    internal float BearingXEm { get; }

    /// <summary>Gets the quad's bottom edge in em units relative to the baseline.</summary>
    internal float BearingYEm { get; }

    /// <summary>Gets the quad width in em units, including the padding.</summary>
    internal float WidthEm { get; }

    /// <summary>Gets the quad height in em units, including the padding.</summary>
    internal float HeightEm { get; }

    /// <summary>Gets a value indicating whether the layout has no texture.</summary>
    internal bool IsEmpty => TextureWidth == 0 || TextureHeight == 0;

    /// <summary>Maps a font-unit point into top-down texture pixel space.</summary>
    internal Vector2 ToPixel(Vector2 fontPoint) =>
        new((fontPoint.X * Scale) + OriginX, OriginY - (fontPoint.Y * Scale));
}
