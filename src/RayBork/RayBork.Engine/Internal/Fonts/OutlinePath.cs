using System.Numerics;

namespace RayBork.Internal;

/// <summary>The resolved outline of one glyph as font-unit, y-up contours.</summary>
/// <remarks>
/// Coordinates are unscaled font units relative to the glyph origin; multiply by
/// <c>size / unitsPerEm</c>. Contours are closed, keep source order and direction, and use the
/// non-zero winding rule.
/// </remarks>
internal sealed class OutlinePath
{
    /// <summary>The shared path of a glyph without an outline.</summary>
    internal static OutlinePath Empty { get; } = new(ReadOnlyMemory<OutlineContour>.Empty);

    /// <summary>Initializes a path over its closed contours.</summary>
    internal OutlinePath(ReadOnlyMemory<OutlineContour> contours)
    {
        Contours = contours;
    }

    /// <summary>Gets the closed contours in drawing order; use <c>Contours.Span</c> to iterate.</summary>
    internal ReadOnlyMemory<OutlineContour> Contours { get; }

    /// <summary>Gets a value indicating whether the glyph has no contours.</summary>
    internal bool IsEmpty => Contours.IsEmpty;
}

/// <summary>One closed contour of an <see cref="OutlinePath"/>.</summary>
/// <remarks>
/// Segments are in drawing order: the first starts at <see cref="Start"/>, each later one starts where
/// the previous ended, and the last ends at <see cref="Start"/>.
/// </remarks>
internal sealed class OutlineContour
{
    /// <summary>Initializes a contour from its first on-curve point and closing segments.</summary>
    internal OutlineContour(Vector2 start, ReadOnlyMemory<OutlineSegment> segments)
    {
        Start = start;
        Segments = segments;
    }

    /// <summary>Gets the first on-curve point, in font units.</summary>
    internal Vector2 Start { get; }

    /// <summary>Gets the segments in drawing order; use <c>Segments.Span</c> to iterate.</summary>
    internal ReadOnlyMemory<OutlineSegment> Segments { get; }
}

/// <summary>One line, quadratic, or cubic edge of an <see cref="OutlineContour"/>.</summary>
/// <remarks>
/// A line uses only <see cref="End"/>; a quadratic also uses <see cref="Control1"/>, and a cubic both
/// control points. Points are in font units and an unused control point is zero.
/// </remarks>
internal readonly struct OutlineSegment
{
    private OutlineSegment(OutlineSegmentKind kind, Vector2 control1, Vector2 control2, Vector2 end)
    {
        Kind = kind;
        Control1 = control1;
        Control2 = control2;
        End = end;
    }

    /// <summary>Gets the segment kind.</summary>
    internal OutlineSegmentKind Kind { get; }

    /// <summary>Gets the quadratic control point or the first cubic control point.</summary>
    internal Vector2 Control1 { get; }

    /// <summary>Gets the second cubic control point; zero for a line or a quadratic.</summary>
    internal Vector2 Control2 { get; }

    /// <summary>Gets the end point of the segment.</summary>
    internal Vector2 End { get; }

    /// <summary>Creates a line segment to the given end point, in font units.</summary>
    internal static OutlineSegment Line(Vector2 end) =>
        new(OutlineSegmentKind.Line, default, default, end);

    /// <summary>Creates a quadratic Bézier segment.</summary>
    internal static OutlineSegment Quadratic(Vector2 control, Vector2 end) =>
        new(OutlineSegmentKind.Quadratic, control, default, end);

    /// <summary>Creates a cubic Bézier segment.</summary>
    internal static OutlineSegment Cubic(Vector2 control1, Vector2 control2, Vector2 end) =>
        new(OutlineSegmentKind.Cubic, control1, control2, end);
}

/// <summary>The kind of one <see cref="OutlineSegment"/>.</summary>
internal enum OutlineSegmentKind
{
    /// <summary>A straight line to <see cref="OutlineSegment.End"/>.</summary>
    Line,

    /// <summary>A quadratic Bézier curve with one control point.</summary>
    Quadratic,

    /// <summary>A cubic Bézier curve with two control points.</summary>
    Cubic,
}
