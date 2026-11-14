using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Flattens a font-unit glyph outline into deterministic pixel-space line edges and assigns each edge
/// its MSDF colour pair.
/// </summary>
/// <remarks>
/// <para>
/// Quadratic and cubic segments are sampled uniformly. The sample count comes from the exact chord
/// error bound of a C2 parametric curve: a quadratic's maximum deviation is a quarter of its second
/// difference and a cubic's is three quarters of its largest second difference, and a uniform
/// subdivision into <c>n</c> pieces divides the deviation by <c>n²</c>. The count therefore keeps
/// every chord within <see cref="FLATTEN_TOLERANCE_PX"/> output pixels of the curve, and is capped at
/// <see cref="MAXIMUM_SEGMENTS_PER_CURVE"/> per segment.
/// </para>
/// <para>
/// Each flattened edge carries two of the three distance channels: yellow is red and green, cyan is
/// green and blue, and magenta is red and blue. Every edge must carry two channels because the median
/// reconstruction needs two channels to agree along an edge; with a single channel the median would
/// pick a far edge over the nearest one. The pairs cycle over the flattened edges of a contour in
/// order, so any two consecutive edges carry different pairs that share exactly one channel. When the
/// closing edge would repeat the first edge's pair (a contour with <c>3n + 1</c> edges so the cycle
/// lands on yellow again), it takes cyan, the pair that differs from both the first edge and its own
/// predecessor.
/// </para>
/// <para>
/// Colouring is per contour. A contour that flattens to a single edge keeps yellow; it is degenerate
/// and cannot differ from itself. Two contours that cross (which a valid font outline never does) are
/// coloured independently, so a texel whose three nearest edges all belong to different contours may
/// not carry its nearest edge in two channels and the median can deviate there.
/// </para>
/// </remarks>
internal static class MsdfOutlineFlattener {
    /// <summary>The colour pair that contributes to the red and green channels.</summary>
    internal const int YELLOW = 0b011;

    /// <summary>The colour pair that contributes to the green and blue channels.</summary>
    internal const int CYAN = 0b110;

    /// <summary>The colour pair that contributes to the red and blue channels.</summary>
    internal const int MAGENTA = 0b101;

    /// <summary>The maximum chord deviation of one flattened curve, in output pixels.</summary>
    internal const float FLATTEN_TOLERANCE_PX = 0.2f;

    /// <summary>The subdivision cap of one quadratic or cubic segment.</summary>
    internal const int MAXIMUM_SEGMENTS_PER_CURVE = 256;

    private const int COLOR_COUNT = 3;
    private const double QUADRATIC_ERROR_FACTOR = 0.25;
    private const double CUBIC_ERROR_FACTOR = 0.75;

    /// <summary>Counts the line edges that flattening an outline produces.</summary>
    /// <param name="outline">The closed font-unit outline.</param>
    /// <param name="layout">The layout that supplies the pixel transform.</param>
    /// <returns>The number of flattened line edges.</returns>
    /// <exception cref="MsdfLimitException">
    /// The outline flattens to more than <see cref="MsdfGenerator.MAX_FLATTENED_EDGES"/> edges.
    /// </exception>
    internal static int CountEdges(OutlinePath outline, in MsdfGlyphLayout layout) {
        long count = 0;
        ReadOnlySpan<OutlineContour> contours = outline.Contours.Span;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++) {
            OutlineContour contour = contours[contourIndex];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            Vector2 from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                count += GetDivisions(from, segment, end, layout);
                if (count > MsdfGenerator.MAX_FLATTENED_EDGES) {
                    throw new MsdfLimitException(
                        $"the outline flattens to more than {MsdfGenerator.MAX_FLATTENED_EDGES} edges");
                }

                from = end;
            }
        }

        return (int)count;
    }

    /// <summary>Flattens an outline into the supplied edge buffer.</summary>
    /// <param name="outline">The closed font-unit outline.</param>
    /// <param name="layout">The layout that supplies the pixel transform.</param>
    /// <param name="edges">
    /// The destination span. It must hold exactly <see cref="CountEdges"/> edges.
    /// </param>
    internal static void Flatten(OutlinePath outline, in MsdfGlyphLayout layout, Span<MsdfEdge> edges) {
        int index = 0;
        ReadOnlySpan<OutlineContour> contours = outline.Contours.Span;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++) {
            OutlineContour contour = contours[contourIndex];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            // The closing edge's colour depends on the contour's flattened edge count, so count the
            // contour before emitting it. The count costs one square root per curved segment.
            int contourEdgeCount = 0;
            Vector2 from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                contourEdgeCount += GetDivisions(from, segment, end, layout);
                from = end;
            }

            int contourStart = index;
            from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                Vector2 control1 = layout.ToPixel(segment.Control1);
                Vector2 control2 = layout.ToPixel(segment.Control2);
                int divisions = GetDivisions(from, segment, end, layout);
                Vector2 segmentStart = from;
                for (int step = 1; step <= divisions; step++) {
                    Vector2 to = segment.Kind == OutlineSegmentKind.Line
                        ? end
                        : Evaluate(
                            segmentStart,
                            control1,
                            control2,
                            end,
                            segment.Kind,
                            (float)step / divisions);
                    edges[index] = new MsdfEdge(from, to, GetColorMask(index - contourStart, contourEdgeCount));
                    index++;
                    from = to;
                }
            }
        }
    }

    private static int GetDivisions(
        Vector2 start,
        in OutlineSegment segment,
        Vector2 end,
        in MsdfGlyphLayout layout) {
        switch (segment.Kind) {
            case OutlineSegmentKind.Quadratic: {
                Vector2 control = layout.ToPixel(segment.Control1);
                Vector2 secondDifference = start - (2f * control) + end;
                return GetDivisionCount(secondDifference.Length(), QUADRATIC_ERROR_FACTOR);
            }

            case OutlineSegmentKind.Cubic: {
                Vector2 control1 = layout.ToPixel(segment.Control1);
                Vector2 control2 = layout.ToPixel(segment.Control2);
                float first = (start - (2f * control1) + control2).Length();
                float second = (control1 - (2f * control2) + end).Length();
                return GetDivisionCount(MathF.Max(first, second), CUBIC_ERROR_FACTOR);
            }

            default:
                return 1;
        }
    }

    private static int GetDivisionCount(float deviation, double errorFactor) {
        if (!(deviation > 0f)) {
            return 1;
        }

        double required = Math.Sqrt((deviation * errorFactor) / FLATTEN_TOLERANCE_PX);
        if (required >= MAXIMUM_SEGMENTS_PER_CURVE) {
            return MAXIMUM_SEGMENTS_PER_CURVE;
        }

        return Math.Max(1, (int)Math.Ceiling(required));
    }

    private static Vector2 Evaluate(
        Vector2 start,
        Vector2 control1,
        Vector2 control2,
        Vector2 end,
        OutlineSegmentKind kind,
        float t) {
        float remaining = 1f - t;
        if (kind == OutlineSegmentKind.Quadratic) {
            return (remaining * remaining * start)
                + (2f * remaining * t * control1)
                + (t * t * end);
        }

        float remainingSquared = remaining * remaining;
        float tSquared = t * t;
        return (remainingSquared * remaining * start)
            + (3f * remainingSquared * t * control1)
            + (3f * remaining * tSquared * control2)
            + (tSquared * t * end);
    }

    private static int GetColorMask(int edgeIndex, int contourEdgeCount) {
        int color = edgeIndex % COLOR_COUNT;
        // A contour with 3n + 1 edges lands the closing edge on the first edge's yellow pair. Cyan is
        // the remaining pair, and it differs from both the first edge and the previous magenta edge.
        if (contourEdgeCount > 1 && edgeIndex == contourEdgeCount - 1 && color == 0) {
            color = 1;
        }

        return color switch {
            0 => YELLOW,
            1 => CYAN,
            _ => MAGENTA,
        };
    }
}
