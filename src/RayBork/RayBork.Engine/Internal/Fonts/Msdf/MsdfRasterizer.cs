using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Rasterizes flattened MSDF edges into the RGBA8 distance field of one glyph texture.
/// </summary>
/// <remarks>
/// <para>
/// For every texel centre and every distance channel the rasterizer keeps the squared distance to the
/// nearest edge that carries that channel. The outline's side comes from one non-zero winding test for
/// the whole texel, so a channel whose nearest edge is a hole wall still carries the correct side. The
/// stored value is <c>round(255 * (0.5 + clamp(signedDistance, -range/2, range/2) / range))</c>: 0.5 is
/// the glyph edge, the authored range maps to <c>[0, 1]</c>, a value above 0.5 is inside, and the alpha
/// channels are 255.
/// </para>
/// <para>
/// Distances are in texture pixels and measured from texel centres. Row zero is the quad's upper edge,
/// so the image is top-down and <c>v = 0</c> samples the quad's upper edge; this matches the engine's
/// top-left UV convention. A channel with no edge at all has a positive infinite distance and
/// saturates at the extreme for the texel's side. The median is unaffected: the nearest edge feeds its
/// two channels with the same value, and the third channel's nearest edge is at least as far away.
/// </para>
/// <para>
/// The field stores the exact distance to the nearest edge of each colour and does not extend an edge
/// past its endpoints (it has no pseudo-distance). A texel centre therefore reconstructs the exact
/// nearest-edge distance even at a corner, while bilinear filtering between texels behaves like an
/// exact single-channel distance field and can round a sharp corner at the texel scale.
/// </para>
/// </remarks>
internal static class MsdfRasterizer
{
    private const int RED_BIT = 0b001;
    private const int GREEN_BIT = 0b010;
    private const int BLUE_BIT = 0b100;

    /// <summary>Fills an RGBA8 bitmap with the distance field of the edges.</summary>
    /// <param name="bitmap">
    /// The destination buffer, exactly <paramref name="width"/> times <paramref name="height"/> RGBA8
    /// texels.
    /// </param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <param name="range">The distance range in pixels; it must be positive.</param>
    /// <param name="edges">The flattened edges in texture pixel space.</param>
    internal static void Rasterize(
        byte[] bitmap,
        int width,
        int height,
        int range,
        ReadOnlySpan<MsdfEdge> edges
    )
    {
        float halfRange = range * 0.5f;
        float inverseRange = 1f / range;
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            float texelY = y + 0.5f;
            for (int x = 0; x < width; x++)
            {
                float texelX = x + 0.5f;
                int winding = 0;
                float red = float.PositiveInfinity;
                float green = float.PositiveInfinity;
                float blue = float.PositiveInfinity;
                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    MsdfEdge edge = edges[edgeIndex];
                    Vector2 start = edge.Start;
                    Vector2 end = edge.End;
                    float side =
                        ((end.X - start.X) * (texelY - start.Y))
                        - ((end.Y - start.Y) * (texelX - start.X));
                    if (start.Y <= texelY)
                    {
                        if (end.Y > texelY && side > 0f)
                        {
                            winding++;
                        }
                    }
                    else if (end.Y <= texelY && side < 0f)
                    {
                        winding--;
                    }

                    float boundsX = MathF.Max(
                        MathF.Max(edge.MinX - texelX, texelX - edge.MaxX),
                        0f
                    );
                    float boundsY = MathF.Max(
                        MathF.Max(edge.MinY - texelY, texelY - edge.MaxY),
                        0f
                    );
                    float boundsSquared = (boundsX * boundsX) + (boundsY * boundsY);
                    float limit = edge.ColorMask switch
                    {
                        MsdfOutlineFlattener.YELLOW => MathF.Max(red, green),
                        MsdfOutlineFlattener.CYAN => MathF.Max(green, blue),
                        _ => MathF.Max(red, blue),
                    };
                    if (boundsSquared >= limit)
                    {
                        continue;
                    }

                    float distance = SquaredDistanceToSegment(edge, texelX, texelY);
                    if ((edge.ColorMask & RED_BIT) != 0 && distance < red)
                    {
                        red = distance;
                    }

                    if ((edge.ColorMask & GREEN_BIT) != 0 && distance < green)
                    {
                        green = distance;
                    }

                    if ((edge.ColorMask & BLUE_BIT) != 0 && distance < blue)
                    {
                        blue = distance;
                    }
                }

                float sign = winding != 0 ? 1f : -1f;
                bitmap[offset] = ToByte(sign * MathF.Sqrt(red), halfRange, inverseRange);
                bitmap[offset + 1] = ToByte(sign * MathF.Sqrt(green), halfRange, inverseRange);
                bitmap[offset + 2] = ToByte(sign * MathF.Sqrt(blue), halfRange, inverseRange);
                bitmap[offset + 3] = 255;
                offset += 4;
            }
        }
    }

    private static float SquaredDistanceToSegment(MsdfEdge edge, float x, float y)
    {
        float edgeX = edge.End.X - edge.Start.X;
        float edgeY = edge.End.Y - edge.Start.Y;
        float pointX = x - edge.Start.X;
        float pointY = y - edge.Start.Y;
        float parameter =
            edge.LengthSquared > 0f
                ? Math.Clamp(((pointX * edgeX) + (pointY * edgeY)) / edge.LengthSquared, 0f, 1f)
                : 0f;
        float closestX = pointX - (edgeX * parameter);
        float closestY = pointY - (edgeY * parameter);
        return (closestX * closestX) + (closestY * closestY);
    }

    private static byte ToByte(float distance, float halfRange, float inverseRange)
    {
        float clamped = Math.Clamp(distance, -halfRange, halfRange);
        int value = (int)MathF.Round(255f * (0.5f + (clamped * inverseRange)));
        return (byte)Math.Clamp(value, 0, 255);
    }
}
