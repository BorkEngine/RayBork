namespace RayBork;

/// <summary>
/// The primitive topology of a mesh.
/// </summary>
public enum PrimitiveTopology {
    /// <summary>Independent triangles, with three indices per triangle.</summary>
    TriangleList = 0,

    /// <summary>A triangle strip, with one index per vertex.</summary>
    TriangleStrip = 1,

    /// <summary>Independent line segments, with two indices per segment.</summary>
    LineList = 2,

    /// <summary>A line strip, with one index per vertex.</summary>
    LineStrip = 3,

    /// <summary>Independent points, with one index per point.</summary>
    PointList = 4,
}
