using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// One flattened outline edge in glyph texture pixel space with its MSDF colour pair.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Start"/> and <see cref="End"/> are in the top-down pixel space of the glyph texture
/// (row zero is the quad's upper edge). <see cref="ColorMask"/> is a two-bit mask over the red,
/// green, and blue distance channels: one of <see cref="MsdfOutlineFlattener.YELLOW"/>,
/// <see cref="MsdfOutlineFlattener.CYAN"/>, and <see cref="MsdfOutlineFlattener.MAGENTA"/>. Every
/// edge carries two of the three channels, and any two different masks share exactly one channel.
/// </para>
/// <para>
/// The bounds and the squared length are precomputed because the rasterizer evaluates the distance
/// from every texel centre to every edge and rejects edges whose bounding box cannot improve a
/// channel.
/// </para>
/// </remarks>
internal readonly struct MsdfEdge {
    /// <summary>Initializes a flattened edge.</summary>
    /// <param name="start">The start point in texture pixels.</param>
    /// <param name="end">The end point in texture pixels.</param>
    /// <param name="colorMask">The two-channel colour mask.</param>
    internal MsdfEdge(Vector2 start, Vector2 end, int colorMask) {
        Start = start;
        End = end;
        ColorMask = colorMask;
        MinX = MathF.Min(start.X, end.X);
        MinY = MathF.Min(start.Y, end.Y);
        MaxX = MathF.Max(start.X, end.X);
        MaxY = MathF.Max(start.Y, end.Y);
        LengthSquared = Vector2.DistanceSquared(start, end);
    }

    /// <summary>Gets the start point in texture pixels.</summary>
    internal Vector2 Start { get; }

    /// <summary>Gets the end point in texture pixels.</summary>
    internal Vector2 End { get; }

    /// <summary>Gets the two-channel colour mask of the edge.</summary>
    internal int ColorMask { get; }

    /// <summary>Gets the minimum x coordinate of the edge bounds.</summary>
    internal float MinX { get; }

    /// <summary>Gets the minimum y coordinate of the edge bounds.</summary>
    internal float MinY { get; }

    /// <summary>Gets the maximum x coordinate of the edge bounds.</summary>
    internal float MaxX { get; }

    /// <summary>Gets the maximum y coordinate of the edge bounds.</summary>
    internal float MaxY { get; }

    /// <summary>Gets the squared length of the edge; zero for a degenerate edge.</summary>
    internal float LengthSquared { get; }
}
