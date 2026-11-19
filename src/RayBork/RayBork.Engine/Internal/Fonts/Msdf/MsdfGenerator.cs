using System.Buffers;

namespace RayBork.Internal;

/// <summary>
/// Deterministically computes the MSDF layout and RGBA8 distance field of one glyph.
/// </summary>
/// <remarks>
/// Output is byte-identical for the same input, with no randomness or culture dependence. The field
/// follows the built-in shader: <c>0.5</c> is the glyph edge, values above are inside, alpha is 255,
/// and row zero is the quad's top edge.
/// </remarks>
internal static class MsdfGenerator
{
    /// <summary>The default full padded texture side, in pixels.</summary>
    internal const int DEFAULT_PIXEL_SIZE = 32;

    /// <summary>The default distance range, in pixels.</summary>
    internal const int DEFAULT_RANGE = 4;

    /// <summary>The default padding around the glyph box, in pixels.</summary>
    internal const int DEFAULT_PADDING = 2;

    /// <summary>The maximum number of line edges that one outline may flatten to.</summary>
    internal const int MAX_FLATTENED_EDGES = 65_536;

    /// <summary>The maximum number of texels in one glyph texture (2048 by 2048).</summary>
    internal const int MAX_TEXELS = 4_194_304;

    /// <summary>The maximum number of edge-texel pairs in one generation (8,388,608).</summary>
    internal const int MAX_EDGE_TEXEL_PAIRS = 8_388_608;

    /// <summary>Computes the layout of one glyph texture from its stored bounds.</summary>
    /// <param name="bounds">The stored bounds in font units, y-up; they must not be inverted.</param>
    /// <param name="unitsPerEm">The face's font units per em; it must be positive.</param>
    /// <param name="pixelSize">
    /// The complete padded texture side in pixels; it must exceed twice <paramref name="padding"/>.
    /// </param>
    /// <param name="range">
    /// The distance range in pixels; distances saturate at <c>±range / 2</c>.
    /// </param>
    /// <param name="padding">
    /// The empty border around the glyph box in pixels; at least <c>range / 2</c> stores the complete
    /// outside field. A smaller padding clips it at the texture edge.
    /// </param>
    /// <returns>The layout; an all-zero box yields <see cref="MsdfGlyphLayout.Empty"/>.</returns>
    /// <remarks>
    /// The longest bounds side is fitted into <c>pixelSize - 2 * padding</c> pixels, with the outline
    /// left- and bottom-aligned. Quad metrics are in em units, and row zero is the quad's top edge.
    /// </remarks>
    internal static MsdfGlyphLayout ComputeLayout(
        in FontBounds bounds,
        int unitsPerEm,
        int pixelSize = DEFAULT_PIXEL_SIZE,
        int range = DEFAULT_RANGE,
        int padding = DEFAULT_PADDING
    )
    {
        if (pixelSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelSize),
                pixelSize,
                "The pixel size must be positive."
            );
        }

        if (range <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(range),
                range,
                "The distance range must be positive."
            );
        }

        if (padding < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(padding),
                padding,
                "The padding must not be negative."
            );
        }

        if (unitsPerEm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerEm),
                unitsPerEm,
                "The font units per em must be positive."
            );
        }

        if (pixelSize <= (2L * padding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelSize),
                pixelSize,
                "The pixel size must be greater than twice the padding so that a glyph box remains."
            );
        }

        long boundsWidth = (long)bounds.XMax - bounds.XMin;
        long boundsHeight = (long)bounds.YMax - bounds.YMin;
        if (boundsWidth < 0 || boundsHeight < 0)
        {
            throw new ArgumentException(
                $"The bounds are inverted: ({bounds.XMin}, {bounds.YMin})-({bounds.XMax}, {bounds.YMax}).",
                nameof(bounds)
            );
        }

        if (boundsWidth == 0 && boundsHeight == 0 && bounds.XMin == 0 && bounds.YMin == 0)
        {
            return MsdfGlyphLayout.Empty;
        }

        long longest = Math.Max(boundsWidth, boundsHeight);
        int glyphBox = pixelSize - (2 * padding);
        double scale;
        long textureWidth;
        long textureHeight;
        if (longest == 0)
        {
            // Both extents are zero but the box is not the parser's empty-glyph marker. Treat the
            // point as a one-font-unit extent so the scale is defined and use the full texture square.
            scale = glyphBox;
            textureWidth = pixelSize;
            textureHeight = pixelSize;
        }
        else
        {
            scale = (double)glyphBox / longest;
            long contentWidth = CeilDiv(boundsWidth * glyphBox, longest);
            long contentHeight = CeilDiv(boundsHeight * glyphBox, longest);
            textureWidth = Math.Max(1L, contentWidth + (2L * padding));
            textureHeight = Math.Max(1L, contentHeight + (2L * padding));
        }

        float scaleValue = (float)scale;
        float paddingInFontUnits = padding / scaleValue;
        float bearingXEm = (bounds.XMin - paddingInFontUnits) / unitsPerEm;
        float bearingYEm = (bounds.YMin - paddingInFontUnits) / unitsPerEm;
        float pixelsPerEm = scaleValue * unitsPerEm;

        // The fitted content is left- and bottom-aligned. The ceil remainder stays in the right and top
        // padding, so the content's minimum y sits exactly padding above the texture's bottom edge and
        // the quad's lower-left derived from BearingYEm is exact.
        long verticalSpan = textureHeight - (2L * padding);
        return new MsdfGlyphLayout(
            (int)textureWidth,
            (int)textureHeight,
            scaleValue,
            range,
            padding - (bounds.XMin * scaleValue),
            padding + verticalSpan + (bounds.YMin * scaleValue),
            bearingXEm,
            bearingYEm,
            textureWidth / pixelsPerEm,
            textureHeight / pixelsPerEm
        );
    }

    /// <summary>Generates the RGBA8 MSDF texture of one glyph outline.</summary>
    /// <param name="outline">The closed, non-zero-filled outline in font units.</param>
    /// <param name="layout">The layout from <see cref="ComputeLayout"/> for the same glyph.</param>
    /// <returns>
    /// Row-major RGBA8 texels at exactly the layout's texture size; empty input yields an empty array.
    /// </returns>
    /// <exception cref="MsdfLimitException">
    /// The texture, flattened-edge, or edge-texel budget is exceeded.
    /// </exception>
    /// <remarks>
    /// An outline with no edges produces the constant outside field. The caller owns the returned
    /// buffer; the edge array is pooled.
    /// </remarks>
    internal static byte[] Generate(OutlinePath outline, in MsdfGlyphLayout layout)
    {
        ArgumentNullException.ThrowIfNull(outline);
        if (layout.TextureWidth < 0 || layout.TextureHeight < 0)
        {
            throw new ArgumentException(
                "The layout must not have a negative texture size.",
                nameof(layout)
            );
        }

        if (layout.IsEmpty)
        {
            if (outline.IsEmpty)
            {
                return [];
            }

            throw new ArgumentException(
                "The layout has no texture but the outline has contours.",
                nameof(layout)
            );
        }

        if (
            !(layout.Scale > 0f)
            || !float.IsFinite(layout.Scale)
            || !float.IsFinite(layout.OriginX)
            || !float.IsFinite(layout.OriginY)
            || layout.Range <= 0
        )
        {
            throw new ArgumentException(
                "The layout must have a positive finite scale, finite origins, and a positive distance range.",
                nameof(layout)
            );
        }

        long texelCount = (long)layout.TextureWidth * layout.TextureHeight;
        if (texelCount > MAX_TEXELS)
        {
            throw new MsdfLimitException(
                $"the {layout.TextureWidth} by {layout.TextureHeight} texture has {texelCount} texels; the limit is {MAX_TEXELS}"
            );
        }

        int edgeCount = outline.IsEmpty ? 0 : MsdfOutlineFlattener.CountEdges(outline, layout);
        if ((texelCount * edgeCount) > MAX_EDGE_TEXEL_PAIRS)
        {
            throw new MsdfLimitException(
                $"{edgeCount} edges over {texelCount} texels is {texelCount * edgeCount} edge-texel pairs; the limit is {MAX_EDGE_TEXEL_PAIRS}"
            );
        }

        byte[] bitmap = new byte[(int)texelCount * 4];
        if (edgeCount == 0)
        {
            FillOutside(bitmap);
            return bitmap;
        }

        MsdfEdge[] edges = ArrayPool<MsdfEdge>.Shared.Rent(edgeCount);
        try
        {
            MsdfOutlineFlattener.Flatten(outline, layout, edges.AsSpan(0, edgeCount));
            MsdfRasterizer.Rasterize(
                bitmap,
                layout.TextureWidth,
                layout.TextureHeight,
                layout.Range,
                edges.AsSpan(0, edgeCount)
            );
            return bitmap;
        }
        finally
        {
            ArrayPool<MsdfEdge>.Shared.Return(edges);
        }
    }

    private static long CeilDiv(long value, long divisor)
    {
        long quotient = value / divisor;
        return (value % divisor) == 0 ? quotient : quotient + 1;
    }

    private static void FillOutside(byte[] bitmap)
    {
        for (int index = 3; index < bitmap.Length; index += 4)
        {
            bitmap[index] = 255;
        }
    }
}
