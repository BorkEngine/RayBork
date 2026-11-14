using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The texture size, raster scale, and em-unit quad metrics of one glyph's MSDF layout.
/// </summary>
/// <remarks>
/// <para>
/// The layout is produced by <see cref="MsdfGenerator.ComputeLayout"/> and is everything
/// <see cref="MsdfGenerator.Generate"/> needs besides the outline itself. It spans three spaces:
/// <see cref="Scale"/> is in texture pixels per font unit, the texture size is in pixels, and the
/// bearing, width, and height metrics are in em units measured at that scale. The authored pixel size
/// is the complete padded texture side: the glyph box is fitted into the pixel size minus twice the
/// padding and the padding completes the side, so the texture's longest side is exactly the authored
/// pixel size.
/// </para>
/// <para>
/// The glyph quad's lower-left corner sits at (<see cref="BearingXEm"/>, <see cref="BearingYEm"/>)
/// relative to the pen position on the baseline, and the quad is <see cref="WidthEm"/> by
/// <see cref="HeightEm"/> em. It samples the whole texture with <c>u</c> and <c>v</c> in <c>[0, 1]</c>.
/// Texture row zero is the quad's upper edge, i.e. the top padding row, and the image is stored
/// top-down to match the engine's top-left UV convention. The fitted content is left- and
/// bottom-aligned, so for a non-degenerate glyph the outline's maximum y sits <c>padding</c> to
/// <c>padding + 1</c> texels below row zero; <c>v = 0</c> samples the quad's upper edge either way.
/// Texel <c>(x, y)</c> has its centre at <c>(x + 0.5, y + 0.5)</c> in the top-down pixel space that
/// <see cref="ToPixel"/> maps font units into.
/// </para>
/// <para>
/// A glyph without an outline has the empty layout: a zero texture size, a zero scale, and all-zero
/// metrics. The default instance is the empty layout.
/// </para>
/// </remarks>
internal readonly struct MsdfGlyphLayout {
    /// <summary>Initializes a layout.</summary>
    /// <param name="textureWidth">The texture width in pixels.</param>
    /// <param name="textureHeight">The texture height in pixels.</param>
    /// <param name="scale">The raster scale in pixels per font unit.</param>
    /// <param name="range">The distance range in pixels.</param>
    /// <param name="originX">The pixel x coordinate of the font origin.</param>
    /// <param name="originY">The pixel y coordinate of the font origin.</param>
    /// <param name="bearingXEm">The quad's left edge in em units relative to the pen.</param>
    /// <param name="bearingYEm">The quad's bottom edge in em units relative to the baseline.</param>
    /// <param name="widthEm">The quad width in em units.</param>
    /// <param name="heightEm">The quad height in em units.</param>
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
        float heightEm) {
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

    /// <summary>Gets the distance range in pixels: a distance of <c>range / 2</c> saturates a channel.</summary>
    internal int Range { get; }

    /// <summary>Gets the pixel x coordinate that the font-unit origin maps to.</summary>
    internal float OriginX { get; }

    /// <summary>Gets the pixel y coordinate that the font-unit origin maps to; pixel y points down.</summary>
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

    /// <summary>Maps a font-unit point into the texture pixel space.</summary>
    /// <param name="fontPoint">The point in font units.</param>
    /// <returns>
    /// The point in texture pixels, x to the right and y downward, with row zero at the outline's
    /// maximum y.
    /// </returns>
    internal Vector2 ToPixel(Vector2 fontPoint) => new(
        (fontPoint.X * Scale) + OriginX,
        OriginY - (fontPoint.Y * Scale));
}
