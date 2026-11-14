using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>
/// Builds the closed outline paths and provides the analytic distances used by the MSDF tests.
/// </summary>
internal static class MsdfTestShapes {
    /// <summary>Creates a path over the supplied contours.</summary>
    internal static OutlinePath CreatePath(params OutlineContour[] contours) => new(contours);

    /// <summary>Creates an axis-aligned rectangle as one counter-clockwise contour.</summary>
    internal static OutlinePath Rectangle(float xMin, float yMin, float xMax, float yMax) => CreatePath(
        new OutlineContour(
            new Vector2(xMin, yMin),
            new OutlineSegment[] {
                OutlineSegment.Line(new Vector2(xMax, yMin)),
                OutlineSegment.Line(new Vector2(xMax, yMax)),
                OutlineSegment.Line(new Vector2(xMin, yMax)),
                OutlineSegment.Line(new Vector2(xMin, yMin)),
            }));

    /// <summary>Creates a counter-clockwise rectangle with a clockwise rectangular hole.</summary>
    internal static OutlinePath RectangleWithHole(
        float outerMinX,
        float outerMinY,
        float outerMaxX,
        float outerMaxY,
        float innerMinX,
        float innerMinY,
        float innerMaxX,
        float innerMaxY) => CreatePath(
            new OutlineContour(
                new Vector2(outerMinX, outerMinY),
                new OutlineSegment[] {
                    OutlineSegment.Line(new Vector2(outerMaxX, outerMinY)),
                    OutlineSegment.Line(new Vector2(outerMaxX, outerMaxY)),
                    OutlineSegment.Line(new Vector2(outerMinX, outerMaxY)),
                    OutlineSegment.Line(new Vector2(outerMinX, outerMinY)),
                }),
            new OutlineContour(
                new Vector2(innerMinX, innerMinY),
                new OutlineSegment[] {
                    OutlineSegment.Line(new Vector2(innerMinX, innerMaxY)),
                    OutlineSegment.Line(new Vector2(innerMaxX, innerMaxY)),
                    OutlineSegment.Line(new Vector2(innerMaxX, innerMinY)),
                    OutlineSegment.Line(new Vector2(innerMinX, innerMinY)),
                }));

    /// <summary>Creates a counter-clockwise right triangle with the right angle at the origin.</summary>
    internal static OutlinePath RightTriangle(float width, float height) => CreatePath(
        new OutlineContour(
            Vector2.Zero,
            new OutlineSegment[] {
                OutlineSegment.Line(new Vector2(width, 0f)),
                OutlineSegment.Line(new Vector2(0f, height)),
                OutlineSegment.Line(Vector2.Zero),
            }));

    /// <summary>Creates a counter-clockwise regular polygon.</summary>
    internal static OutlinePath Polygon(int sides, float centerX, float centerY, float radius) {
        Vector2[] vertices = PolygonVertices(sides, centerX, centerY, radius);
        OutlineSegment[] segments = new OutlineSegment[sides];
        for (int index = 0; index < sides; index++) {
            segments[index] = OutlineSegment.Line(vertices[(index + 1) % sides]);
        }

        return CreatePath(new OutlineContour(vertices[0], segments));
    }

    /// <summary>Creates the vertices of a counter-clockwise regular polygon.</summary>
    internal static Vector2[] PolygonVertices(int sides, float centerX, float centerY, float radius) {
        Vector2[] vertices = new Vector2[sides];
        for (int index = 0; index < sides; index++) {
            float angle = 2f * MathF.PI * index / sides;
            vertices[index] = new Vector2(
                centerX + (radius * MathF.Cos(angle)),
                centerY + (radius * MathF.Sin(angle)));
        }

        return vertices;
    }

    /// <summary>Creates a closed dome: one quadratic arch and a straight baseline.</summary>
    internal static OutlinePath QuadraticDome(
        float startX,
        float startY,
        float controlX,
        float controlY,
        float endX,
        float endY) => CreatePath(
            new OutlineContour(
                new Vector2(startX, startY),
                new OutlineSegment[] {
                    OutlineSegment.Quadratic(new Vector2(controlX, controlY), new Vector2(endX, endY)),
                    OutlineSegment.Line(new Vector2(startX, startY)),
                }));

    /// <summary>Creates a closed capsule from two cubic segments.</summary>
    internal static OutlinePath CubicCapsule(
        float left,
        float right,
        float middleY,
        float radius) => CreatePath(
            new OutlineContour(
                new Vector2(left, middleY),
                new OutlineSegment[] {
                    OutlineSegment.Cubic(
                        new Vector2(left, middleY + radius),
                        new Vector2(right, middleY + radius),
                        new Vector2(right, middleY)),
                    OutlineSegment.Cubic(
                        new Vector2(right, middleY - radius),
                        new Vector2(left, middleY - radius),
                        new Vector2(left, middleY)),
                }));

    /// <summary>Creates a degenerate contour without segments.</summary>
    internal static OutlinePath ContourWithoutEdges(float x, float y) => CreatePath(
        new OutlineContour(new Vector2(x, y), ReadOnlyMemory<OutlineSegment>.Empty));

    /// <summary>Samples every contour of an outline into a closed pixel-space polyline.</summary>
    /// <param name="outline">The outline to sample.</param>
    /// <param name="layout">The layout that supplies the pixel transform.</param>
    /// <param name="samplesPerSegment">The number of samples per curved segment.</param>
    /// <returns>One polyline per contour; the last point of each polyline is its closing point.</returns>
    internal static Vector2[][] SampleContours(
        OutlinePath outline,
        in MsdfGlyphLayout layout,
        int samplesPerSegment) {
        ReadOnlySpan<OutlineContour> contours = outline.Contours.Span;
        Vector2[][] result = new Vector2[contours.Length][];
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++) {
            OutlineContour contour = contours[contourIndex];
            Vector2 from = layout.ToPixel(contour.Start);
            List<Vector2> points = [from];
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++) {
                OutlineSegment segment = segments[segmentIndex];
                Vector2 end = layout.ToPixel(segment.End);
                if (segment.Kind == OutlineSegmentKind.Line) {
                    points.Add(end);
                } else {
                    Vector2 control1 = layout.ToPixel(segment.Control1);
                    Vector2 control2 = layout.ToPixel(segment.Control2);
                    for (int sample = 1; sample <= samplesPerSegment; sample++) {
                        points.Add(Evaluate(
                            from,
                            control1,
                            control2,
                            end,
                            segment.Kind,
                            (float)sample / samplesPerSegment));
                    }
                }

                from = end;
            }

            result[contourIndex] = points.ToArray();
        }

        return result;
    }

    /// <summary>Computes the signed distance to a set of closed polylines in pixel space.</summary>
    internal static float SignedDistanceToContours(Vector2[][] contours, Vector2 point) {
        float squared = float.PositiveInfinity;
        int winding = 0;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++) {
            Vector2[] vertices = contours[contourIndex];
            for (int index = 0; index < vertices.Length; index++) {
                Vector2 start = vertices[index];
                Vector2 end = vertices[(index + 1) % vertices.Length];
                float edgeDistance = SquaredDistanceToSegment(start, end, point);
                if (edgeDistance < squared) {
                    squared = edgeDistance;
                }

                winding += Winding(start, end, point);
            }
        }

        float distance = MathF.Sqrt(squared);
        return winding != 0 ? distance : -distance;
    }

    /// <summary>
    /// Computes the signed distance to a set of closed polylines using the even-odd fill rule, which is
    /// independent of the production non-zero winding test. The analytic test shapes are simple or
    /// nested, so both rules classify them identically.
    /// </summary>
    internal static float SignedDistanceToEvenOddContours(Vector2[][] contours, Vector2 point) {
        float squared = float.PositiveInfinity;
        bool inside = false;
        for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++) {
            Vector2[] vertices = contours[contourIndex];
            for (int index = 0; index < vertices.Length; index++) {
                Vector2 start = vertices[index];
                Vector2 end = vertices[(index + 1) % vertices.Length];
                float edgeDistance = SquaredDistanceToSegment(start, end, point);
                if (edgeDistance < squared) {
                    squared = edgeDistance;
                }

                if (CrossesToTheRight(start, end, point)) {
                    inside = !inside;
                }
            }
        }

        float distance = MathF.Sqrt(squared);
        return inside ? distance : -distance;
    }

    /// <summary>Computes the squared distance from a point to a line segment.</summary>
    internal static float SquaredDistanceToSegment(Vector2 start, Vector2 end, Vector2 point) {
        Vector2 edge = end - start;
        float lengthSquared = edge.LengthSquared();
        float parameter = lengthSquared > 0f
            ? Math.Clamp(Vector2.Dot(point - start, edge) / lengthSquared, 0f, 1f)
            : 0f;
        Vector2 offset = point - start - (edge * parameter);
        return offset.LengthSquared();
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

    private static int Winding(Vector2 start, Vector2 end, Vector2 point) {
        float side = ((end.X - start.X) * (point.Y - start.Y))
            - ((end.Y - start.Y) * (point.X - start.X));
        if (start.Y <= point.Y) {
            if (end.Y > point.Y && side > 0f) {
                return 1;
            }
        } else if (end.Y <= point.Y && side < 0f) {
            return -1;
        }

        return 0;
    }

    private static bool CrossesToTheRight(Vector2 start, Vector2 end, Vector2 point) {
        if ((start.Y > point.Y) == (end.Y > point.Y)) {
            return false;
        }

        float crossingX = start.X + (((point.Y - start.Y) * (end.X - start.X)) / (end.Y - start.Y));
        return point.X < crossingX;
    }
}
