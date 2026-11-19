namespace RayBork;

/// <summary>The primitive topology of a mesh.</summary>
public enum PrimitiveTopology
{
    /// <summary>Independent triangles, three indices each.</summary>
    TriangleList = 0,

    /// <summary>A triangle strip, one shared index per vertex.</summary>
    TriangleStrip = 1,

    /// <summary>Independent line segments, two indices each.</summary>
    LineList = 2,

    /// <summary>A line strip, one index per vertex.</summary>
    LineStrip = 3,

    /// <summary>Independent points, one index each.</summary>
    PointList = 4,
}
