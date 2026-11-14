using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The resolved outline of one glyph as font-unit, y-up contours.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are in font units with +X to the right and +Y up, unscaled and relative to the glyph
/// origin. The on-curve points, direct control points, and component offsets come from the
/// <c>glyf</c> or CFF table; implied on-curve midpoints between consecutive off-curve points, the
/// implied start point of an all-off-curve contour, and transformed composite points are derived.
/// Multiply by <c>size / unitsPerEm</c> to place a glyph in world units.
/// </para>
/// <para>
/// Contours keep their source order and direction; the fill rule is non-zero winding. Every contour
/// is closed: <see cref="OutlineContour.Start"/> is its first on-curve point and the last segment of
/// the contour ends exactly at that point, so a consumer walks <see cref="OutlineContour.Segments"/>
/// in order without adding a closing edge. A degenerate contour may have no segments.
/// </para>
/// <para>
/// The representation is allocation-light for the MSDF generator: a path exposes its contours and
/// each contour exposes its segments as contiguous read-only memory of small value types, so a
/// generator flattens a glyph in a single pass without per-segment objects. An empty glyph, such as
/// the space glyph, resolves to <see cref="Empty"/>.
/// </para>
/// </remarks>
internal sealed class OutlinePath {
    /// <summary>The shared path of a glyph without an outline.</summary>
    internal static OutlinePath Empty { get; } = new(ReadOnlyMemory<OutlineContour>.Empty);

    /// <summary>Initializes a path over the already-resolved contours.</summary>
    /// <param name="contours">The closed contours in drawing order.</param>
    internal OutlinePath(ReadOnlyMemory<OutlineContour> contours) {
        Contours = contours;
    }

    /// <summary>Gets the closed contours in drawing order; use <c>Contours.Span</c> to iterate.</summary>
    internal ReadOnlyMemory<OutlineContour> Contours { get; }

    /// <summary>Gets a value indicating whether the glyph has no contours.</summary>
    internal bool IsEmpty => Contours.IsEmpty;
}

/// <summary>
/// One closed contour of an <see cref="OutlinePath"/>.
/// </summary>
/// <remarks>
/// Segments are in drawing order. The first one starts at <see cref="Start"/>, each later one starts
/// where the previous one ended, and the last one ends at <see cref="Start"/>.
/// </remarks>
internal sealed class OutlineContour {
    /// <summary>Initializes a contour.</summary>
    /// <param name="start">The first on-curve point, in font units.</param>
    /// <param name="segments">The segments that close the contour.</param>
    internal OutlineContour(Vector2 start, ReadOnlyMemory<OutlineSegment> segments) {
        Start = start;
        Segments = segments;
    }

    /// <summary>Gets the first on-curve point, in font units.</summary>
    internal Vector2 Start { get; }

    /// <summary>Gets the segments in drawing order; use <c>Segments.Span</c> to iterate.</summary>
    internal ReadOnlyMemory<OutlineSegment> Segments { get; }
}

/// <summary>
/// One line, quadratic, or cubic edge of an <see cref="OutlineContour"/>.
/// </summary>
/// <remarks>
/// A line uses only <see cref="End"/>; its control points are zero. A quadratic uses
/// <see cref="Control1"/> and <see cref="End"/>. A cubic uses <see cref="Control1"/>,
/// <see cref="Control2"/>, and <see cref="End"/>. Points are in font units.
/// </remarks>
internal readonly struct OutlineSegment {
    private OutlineSegment(OutlineSegmentKind kind, Vector2 control1, Vector2 control2, Vector2 end) {
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

    /// <summary>Creates a straight line.</summary>
    /// <param name="end">The end point in font units.</param>
    /// <returns>The line segment.</returns>
    internal static OutlineSegment Line(Vector2 end) => new(OutlineSegmentKind.Line, default, default, end);

    /// <summary>Creates a quadratic Bézier segment.</summary>
    /// <param name="control">The single control point in font units.</param>
    /// <param name="end">The end point in font units.</param>
    /// <returns>The quadratic segment.</returns>
    internal static OutlineSegment Quadratic(Vector2 control, Vector2 end) =>
        new(OutlineSegmentKind.Quadratic, control, default, end);

    /// <summary>Creates a cubic Bézier segment.</summary>
    /// <param name="control1">The first control point in font units.</param>
    /// <param name="control2">The second control point in font units.</param>
    /// <param name="end">The end point in font units.</param>
    /// <returns>The cubic segment.</returns>
    internal static OutlineSegment Cubic(Vector2 control1, Vector2 control2, Vector2 end) =>
        new(OutlineSegmentKind.Cubic, control1, control2, end);
}

/// <summary>The kind of one <see cref="OutlineSegment"/>.</summary>
internal enum OutlineSegmentKind {
    /// <summary>A straight line to <see cref="OutlineSegment.End"/>.</summary>
    Line,

    /// <summary>A quadratic Bézier curve with one control point.</summary>
    Quadratic,

    /// <summary>A cubic Bézier curve with two control points.</summary>
    Cubic,
}
