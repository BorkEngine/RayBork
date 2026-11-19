using System.Numerics;

namespace RayBork.Internal;

/// <summary>One flattened outline edge in pixel space with its MSDF colour pair.</summary>
/// <remarks>
/// Endpoints and bounds are top-down texture pixels; <see cref="ColorMask"/> is two of the three
/// distance channels, and any two different masks share exactly one channel.
/// </remarks>
internal readonly struct MsdfEdge
{
    /// <summary>Initializes a flattened edge and precomputes its bounds and squared length.</summary>
    internal MsdfEdge(Vector2 start, Vector2 end, int colorMask)
    {
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
