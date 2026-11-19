using System.Numerics;

namespace RayBork.Internal;

/// <summary>Flattens a glyph outline into deterministic pixel-space line edges.</summary>
/// <remarks>
/// Uniform curve subdivision keeps each chord within <see cref="FLATTEN_TOLERANCE_PX"/> pixels,
/// capped at <see cref="MAXIMUM_SEGMENTS_PER_CURVE"/> pieces per segment. Edges cycle yellow, cyan,
/// and magenta so consecutive edges share exactly one channel, and each contour restarts the cycle.
/// </remarks>
internal static class MsdfOutlineFlattener
{
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
    internal static int CountEdges(OutlinePath outline, in MsdfGlyphLayout layout)
    {
        long count = 0;
        ReadOnlySpan<OutlineContour> contours = outline.Contours.Span;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
        {
            OutlineContour contour = contours[contourIndex];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            Vector2 from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                count += GetDivisions(from, segment, end, layout);
                if (count > MsdfGenerator.MAX_FLATTENED_EDGES)
                {
                    throw new MsdfLimitException(
                        $"the outline flattens to more than {MsdfGenerator.MAX_FLATTENED_EDGES} edges"
                    );
                }

                from = end;
            }
        }

        return (int)count;
    }

    /// <summary>Fills a correctly sized edge buffer with the flattened outline.</summary>
    internal static void Flatten(
        OutlinePath outline,
        in MsdfGlyphLayout layout,
        Span<MsdfEdge> edges
    )
    {
        int index = 0;
        ReadOnlySpan<OutlineContour> contours = outline.Contours.Span;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
        {
            OutlineContour contour = contours[contourIndex];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            // The closing edge's colour depends on the contour's flattened edge count, so count the
            // contour before emitting it. The count costs one square root per quadratic segment and two
            // per cubic segment.
            int contourEdgeCount = 0;
            Vector2 from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                contourEdgeCount += GetDivisions(from, segment, end, layout);
                from = end;
            }

            int contourStart = index;
            from = layout.ToPixel(contour.Start);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                Vector2 control1 = layout.ToPixel(segment.Control1);
                Vector2 control2 = layout.ToPixel(segment.Control2);
                int divisions = GetDivisions(from, segment, end, layout);
                Vector2 segmentStart = from;
                for (int step = 1; step <= divisions; step++)
                {
                    Vector2 to =
                        segment.Kind == OutlineSegmentKind.Line
                            ? end
                            : Evaluate(
                                segmentStart,
                                control1,
                                control2,
                                end,
                                segment.Kind,
                                (float)step / divisions
                            );
                    edges[index] = new MsdfEdge(
                        from,
                        to,
                        GetColorMask(index - contourStart, contourEdgeCount)
                    );
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
        in MsdfGlyphLayout layout
    )
    {
        int divisions;
        switch (segment.Kind)
        {
            case OutlineSegmentKind.Quadratic:
            {
                Vector2 control = layout.ToPixel(segment.Control1);
                Vector2 secondDifference = start - (2f * control) + end;
                divisions = GetDivisionCount(secondDifference.Length(), QUADRATIC_ERROR_FACTOR);
                break;
            }

            case OutlineSegmentKind.Cubic:
            {
                Vector2 control1 = layout.ToPixel(segment.Control1);
                Vector2 control2 = layout.ToPixel(segment.Control2);
                float first = (start - (2f * control1) + control2).Length();
                float second = (control1 - (2f * control2) + end).Length();
                divisions = GetDivisionCount(MathF.Max(first, second), CUBIC_ERROR_FACTOR);
                break;
            }

            default:
                divisions = 1;
                break;
        }

        // A segment that starts and ends at the same pixel and flattens to a single chord has no
        // extent; it would emit one zero-length edge, which contributes no boundary and no winding.
        return (start == end && divisions <= 1) ? 0 : divisions;
    }

    private static int GetDivisionCount(float deviation, double errorFactor)
    {
        if (!(deviation > 0f))
        {
            return 1;
        }

        double required = Math.Sqrt((deviation * errorFactor) / FLATTEN_TOLERANCE_PX);
        if (required >= MAXIMUM_SEGMENTS_PER_CURVE)
        {
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
        float t
    )
    {
        float remaining = 1f - t;
        if (kind == OutlineSegmentKind.Quadratic)
        {
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

    private static int GetColorMask(int edgeIndex, int contourEdgeCount)
    {
        int color = edgeIndex % COLOR_COUNT;
        // A contour with 3n + 1 edges lands the closing edge on the first edge's yellow pair. Cyan is
        // the remaining pair, and it differs from both the first edge and the previous magenta edge.
        if (contourEdgeCount > 1 && edgeIndex == contourEdgeCount - 1 && color == 0)
        {
            color = 1;
        }

        return color switch
        {
            0 => YELLOW,
            1 => CYAN,
            _ => MAGENTA,
        };
    }
}
