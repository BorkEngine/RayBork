using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Resolves TrueType <c>glyf</c> glyphs through the <c>loca</c> offset table.
/// </summary>
/// <remarks>
/// <para>
/// Simple glyphs decode their end points, repeat-compressed flags, and x/y deltas into contours with
/// explicit quadratic segments; consecutive off-curve points produce the implied on-curve midpoint,
/// a contour without an on-curve point gets the implied start point, and every contour is closed.
/// Composite glyphs apply each component's translation and optional 2x2 transform, concatenate the
/// component contours in component order, and reject reference cycles as malformed. Valid outlines
/// that exceed a defensive parser limit — more than <see cref="MAX_COMPOSITE_DEPTH"/> composite
/// levels, more than <see cref="MAX_POINTS_PER_GLYPH"/> source points, or more than
/// <see cref="MAX_COMPONENT_RESOLUTIONS"/> component resolutions including empty leaves — raise
/// <see cref="FontUnsupportedException"/> because the face is valid but outside the supported size.
/// The point and component budgets are checked before the point arrays or the fanout work grow.
/// Point-matched component arguments are also a valid font feature outside the supported subset.
/// </para>
/// <para>
/// The reader is immutable and safe to read from any thread. Each resolution allocates only the
/// returned contour and segment arrays, the per-glyph point scratch arrays, and a fixed 16-entry
/// active-glyph stack.
/// </para>
/// </remarks>
internal sealed class GlyphOutlineReader {
    /// <summary>The maximum number of nested composite levels a glyph may resolve.</summary>
    internal const int MAX_COMPOSITE_DEPTH = 16;

    /// <summary>The maximum number of source points one resolved glyph may carry.</summary>
    internal const int MAX_POINTS_PER_GLYPH = 20000;

    /// <summary>The maximum number of composite component resolutions one glyph may perform.</summary>
    internal const int MAX_COMPONENT_RESOLUTIONS = 4096;

    private const int GLYPH_HEADER_LENGTH = 10;
    private const short COMPOSITE_CONTOUR_MARKER = -1;
    private const int ON_CURVE_POINT = 0x01;
    private const int X_SHORT_VECTOR = 0x02;
    private const int Y_SHORT_VECTOR = 0x04;
    private const int REPEAT_FLAG = 0x08;
    private const int X_IS_SAME_OR_POSITIVE_X_SHORT = 0x10;
    private const int Y_IS_SAME_OR_POSITIVE_Y_SHORT = 0x20;
    private const int ARG_1_AND_2_ARE_WORDS = 0x0001;
    private const int ARGS_ARE_XY_VALUES = 0x0002;
    private const int WE_HAVE_A_SCALE = 0x0008;
    private const int MORE_COMPONENTS = 0x0020;
    private const int WE_HAVE_AN_X_AND_Y_SCALE = 0x0040;
    private const int WE_HAVE_A_TWO_BY_TWO = 0x0080;
    private const int SCALED_COMPONENT_OFFSET = 0x0800;
    private const int UNSCALED_COMPONENT_OFFSET = 0x1000;
    private const float F2_DOT_14_SCALE = 1f / 16384f;
    private const int LOCA_SHORT_ENTRY_LENGTH = 2;
    private const int LOCA_LONG_ENTRY_LENGTH = 4;

    private readonly ReadOnlyMemory<byte> _glyf;
    private readonly ReadOnlyMemory<byte> _loca;
    private readonly int _glyphCount;
    private readonly bool _longLoca;

    /// <summary>Initializes a TrueType glyph reader and validates the whole <c>loca</c> table.</summary>
    /// <param name="glyf">The complete <c>glyf</c> table bytes.</param>
    /// <param name="loca">The complete <c>loca</c> table bytes.</param>
    /// <param name="glyphCount">The face's <c>numGlyphs</c>.</param>
    /// <param name="longLoca"><see langword="true"/> for 32-bit <c>loca</c> entries.</param>
    /// <exception cref="FontFormatException">
    /// The <c>loca</c> table is too short, its offsets decrease, or an offset reaches past <c>glyf</c>.
    /// </exception>
    internal GlyphOutlineReader(
        ReadOnlyMemory<byte> glyf,
        ReadOnlyMemory<byte> loca,
        int glyphCount,
        bool longLoca) {
        _glyf = glyf;
        _loca = loca;
        _glyphCount = glyphCount;
        _longLoca = longLoca;

        int entryLength = longLoca ? LOCA_LONG_ENTRY_LENGTH : LOCA_SHORT_ENTRY_LENGTH;
        long required = (long)(glyphCount + 1) * entryLength;
        if (loca.Length < required) {
            throw new FontFormatException(
                $"the 'loca' table is {loca.Length} byte(s); {glyphCount + 1} {(longLoca ? "long" : "short")} entries need {required}");
        }

        SfntReader locaReader = new(loca.Span, "the 'loca' table");
        long previous = 0;
        for (int index = 0; index <= glyphCount; index++) {
            long offset = ReadLocaOffset(locaReader, index);
            if (offset < previous) {
                throw new FontFormatException(
                    $"the 'loca' table offsets decrease at entry {index} (offset {offset})");
            }

            if (offset > glyf.Length) {
                throw new FontFormatException(
                    $"the 'loca' table entry {index} is {offset}, past the {glyf.Length}-byte 'glyf' table");
            }

            previous = offset;
        }
    }

    /// <summary>Resolves one glyph's stored bounds and outline.</summary>
    /// <param name="glyphId">The glyph identifier, in <c>[0, numGlyphs)</c>.</param>
    /// <returns>The resolved glyph in font units.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="glyphId"/> is outside the face.</exception>
    /// <exception cref="FontFormatException">The glyph data is malformed or has a reference cycle.</exception>
    /// <exception cref="FontUnsupportedException">
    /// The glyph is valid but exceeds a supported limit: point-matched composite component
    /// arguments, more than <see cref="MAX_COMPOSITE_DEPTH"/> composite levels, more than
    /// <see cref="MAX_POINTS_PER_GLYPH"/> points, or more than
    /// <see cref="MAX_COMPONENT_RESOLUTIONS"/> component resolutions.
    /// </exception>
    internal FontGlyph ReadGlyph(int glyphId) {
        if ((uint)glyphId >= (uint)_glyphCount) {
            throw new ArgumentOutOfRangeException(
                nameof(glyphId),
                glyphId,
                $"The face has {_glyphCount} glyph(s).");
        }

        var resolution = new GlyphResolution(this);
        return resolution.Read(glyphId);
    }

    private long ReadLocaOffset(SfntReader loca, int index) => _longLoca
        ? loca.ReadUInt32(index * LOCA_LONG_ENTRY_LENGTH)
        : loca.ReadUInt16(index * LOCA_SHORT_ENTRY_LENGTH) * 2L;

    /// <summary>
    /// The mutable state of one glyph resolution. It is created per call, so the reader itself stays
    /// immutable and concurrent resolutions share no state.
    /// </summary>
    private sealed class GlyphResolution {
        private readonly GlyphOutlineReader _reader;
        private readonly int[] _active = new int[MAX_COMPOSITE_DEPTH];
        private int _activeCount;
        private int _componentResolutions;
        private int _pointCount;

        internal GlyphResolution(GlyphOutlineReader reader) {
            _reader = reader;
        }

        internal FontGlyph Read(int glyphId) {
            OutlineContour[] contours = ResolveContours(glyphId, 0, out FontBounds bounds);
            if (contours.Length == 0) {
                return new FontGlyph(glyphId, default, OutlinePath.Empty);
            }

            return new FontGlyph(glyphId, bounds, new OutlinePath(contours));
        }

        private OutlineContour[] ResolveContours(int glyphId, int depth, out FontBounds bounds) {
            bounds = default;
            SfntReader loca = new(_reader._loca.Span, "the 'loca' table");
            long start = _reader.ReadLocaOffset(loca, glyphId);
            long end = _reader.ReadLocaOffset(loca, glyphId + 1);
            if (start == end) {
                return [];
            }

            SfntReader glyf = new(_reader._glyf.Span, $"the 'glyf' glyph {glyphId}");
            if (end - start < GLYPH_HEADER_LENGTH) {
                throw new FontFormatException(
                    $"the 'glyf' glyph {glyphId} stores {end - start} byte(s); the 10-byte glyph header is required");
            }

            SfntReader glyph = new(
                glyf.Slice((int)start, (int)(end - start)),
                $"the 'glyf' glyph {glyphId}");
            int contourCount = glyph.ReadInt16(0);
            bounds = new FontBounds(
                glyph.ReadInt16(2),
                glyph.ReadInt16(4),
                glyph.ReadInt16(6),
                glyph.ReadInt16(8));
            if (contourCount == 0) {
                return [];
            }

            if (contourCount > 0) {
                return ReadSimpleGlyph(glyph, contourCount, glyphId);
            }

            if (contourCount != COMPOSITE_CONTOUR_MARKER) {
                throw new FontFormatException(
                    $"the 'glyf' glyph {glyphId} declares the invalid contour count {contourCount}");
            }

            return ReadCompositeGlyph(glyph, glyphId, depth);
        }

        private OutlineContour[] ReadSimpleGlyph(SfntReader glyph, int contourCount, int glyphId) {
            int endPointsOffset = GLYPH_HEADER_LENGTH;
            glyph.Require(endPointsOffset, 2 * contourCount);
            int previousEnd = -1;
            for (int index = 0; index < contourCount; index++) {
                int endPoint = glyph.ReadUInt16(endPointsOffset + (2 * index));
                if (endPoint <= previousEnd) {
                    throw new FontFormatException(
                        $"the 'glyf' glyph {glyphId} has unsorted contour end points");
                }

                previousEnd = endPoint;
            }

            int pointCount = previousEnd + 1;
            _pointCount += pointCount;
            if (_pointCount > MAX_POINTS_PER_GLYPH) {
                throw new FontUnsupportedException(
                    $"the glyph {glyphId} resolves to more than {MAX_POINTS_PER_GLYPH} points");
            }

            int instructionOffset = endPointsOffset + (2 * contourCount);
            int instructionLength = glyph.ReadUInt16(instructionOffset);
            byte[] flags = ReadFlags(
                glyph,
                instructionOffset + 2 + instructionLength,
                pointCount,
                glyphId,
                out int coordinatesOffset);
            GlyphPoint[] points = ReadPoints(glyph, flags, coordinatesOffset, pointCount);
            return BuildContours(glyph, endPointsOffset, contourCount, points);
        }

        private static byte[] ReadFlags(
            SfntReader glyph,
            int flagsOffset,
            int pointCount,
            int glyphId,
            out int coordinatesOffset) {
            byte[] flags = new byte[pointCount];
            int position = flagsOffset;
            int point = 0;
            while (point < pointCount) {
                byte flag = glyph.ReadByte(position++);
                flags[point++] = flag;
                if ((flag & REPEAT_FLAG) == 0) {
                    continue;
                }

                int repeat = glyph.ReadByte(position++);
                if (point + repeat > pointCount) {
                    throw new FontFormatException(
                        $"the 'glyf' glyph {glyphId} repeats a flag past its {pointCount} points");
                }

                for (int index = 0; index < repeat; index++) {
                    flags[point++] = flag;
                }
            }

            coordinatesOffset = position;
            return flags;
        }

        private static GlyphPoint[] ReadPoints(
            SfntReader glyph,
            byte[] flags,
            int coordinatesOffset,
            int pointCount) {
            GlyphPoint[] points = new GlyphPoint[pointCount];
            int position = coordinatesOffset;
            int coordinate = 0;
            for (int point = 0; point < pointCount; point++) {
                int flag = flags[point];
                if ((flag & X_SHORT_VECTOR) != 0) {
                    int delta = glyph.ReadByte(position++);
                    coordinate += (flag & X_IS_SAME_OR_POSITIVE_X_SHORT) != 0 ? delta : -delta;
                } else if ((flag & X_IS_SAME_OR_POSITIVE_X_SHORT) == 0) {
                    coordinate += glyph.ReadInt16(position);
                    position += 2;
                }

                points[point].Position = new Vector2(coordinate, 0f);
            }

            coordinate = 0;
            for (int point = 0; point < pointCount; point++) {
                int flag = flags[point];
                if ((flag & Y_SHORT_VECTOR) != 0) {
                    int delta = glyph.ReadByte(position++);
                    coordinate += (flag & Y_IS_SAME_OR_POSITIVE_Y_SHORT) != 0 ? delta : -delta;
                } else if ((flag & Y_IS_SAME_OR_POSITIVE_Y_SHORT) == 0) {
                    coordinate += glyph.ReadInt16(position);
                    position += 2;
                }

                points[point].Position = new Vector2(points[point].Position.X, coordinate);
            }

            for (int point = 0; point < pointCount; point++) {
                points[point].OnCurve = (flags[point] & ON_CURVE_POINT) != 0;
            }

            return points;
        }

        private static OutlineContour[] BuildContours(
            SfntReader glyph,
            int endPointsOffset,
            int contourCount,
            GlyphPoint[] points) {
            OutlineContour[] contours = new OutlineContour[contourCount];
            int first = 0;
            for (int index = 0; index < contourCount; index++) {
                int last = glyph.ReadUInt16(endPointsOffset + (2 * index));
                contours[index] = BuildContour(points, first, last);
                first = last + 1;
            }

            return contours;
        }

        private static OutlineContour BuildContour(GlyphPoint[] points, int first, int last) {
            int count = last - first + 1;
            int firstOnCurve = -1;
            for (int index = 0; index < count; index++) {
                if (points[first + index].OnCurve) {
                    firstOnCurve = index;
                    break;
                }
            }

            Vector2 start;
            int processStart;
            int processCount;
            if (firstOnCurve >= 0) {
                start = points[first + firstOnCurve].Position;
                processStart = firstOnCurve + 1;
                processCount = count - 1;
            } else {
                start = Midpoint(points[last].Position, points[first].Position);
                processStart = 0;
                processCount = count;
            }

            List<OutlineSegment> segments = new(count);
            Vector2 current = start;
            int previousOffCurve = -1;
            for (int step = 0; step < processCount; step++) {
                int index = first + ((processStart + step) % count);
                GlyphPoint point = points[index];
                if (point.OnCurve) {
                    if (previousOffCurve >= 0) {
                        segments.Add(OutlineSegment.Quadratic(
                            points[previousOffCurve].Position,
                            point.Position));
                        previousOffCurve = -1;
                    } else if (point.Position != current) {
                        segments.Add(OutlineSegment.Line(point.Position));
                    }

                    current = point.Position;
                    continue;
                }

                if (previousOffCurve >= 0) {
                    Vector2 middle = Midpoint(points[previousOffCurve].Position, point.Position);
                    segments.Add(OutlineSegment.Quadratic(
                        points[previousOffCurve].Position,
                        middle));
                    current = middle;
                }

                previousOffCurve = index;
            }

            if (previousOffCurve >= 0) {
                segments.Add(OutlineSegment.Quadratic(points[previousOffCurve].Position, start));
            } else if (current != start) {
                segments.Add(OutlineSegment.Line(start));
            }

            return new OutlineContour(start, segments.ToArray());
        }

        private OutlineContour[] ReadCompositeGlyph(SfntReader glyph, int glyphId, int depth) {
            if (depth >= MAX_COMPOSITE_DEPTH) {
                throw new FontUnsupportedException(
                    $"the composite glyph {glyphId} nests more than {MAX_COMPOSITE_DEPTH} composite levels");
            }

            PushActive(glyphId);
            List<OutlineContour> contours = [];
            int position = GLYPH_HEADER_LENGTH;
            while (true) {
                int flags = glyph.ReadUInt16(position);
                int componentGlyph = glyph.ReadUInt16(position + 2);
                position += 4;
                int argument1;
                int argument2;
                if ((flags & ARG_1_AND_2_ARE_WORDS) != 0) {
                    argument1 = glyph.ReadInt16(position);
                    argument2 = glyph.ReadInt16(position + 2);
                    position += 4;
                } else {
                    argument1 = (sbyte)glyph.ReadByte(position);
                    argument2 = (sbyte)glyph.ReadByte(position + 1);
                    position += 2;
                }

                if ((flags & ARGS_ARE_XY_VALUES) == 0) {
                    throw new FontUnsupportedException(
                        $"the composite glyph {glyphId} uses point-matched component arguments; only XY offsets are supported");
                }

                float a = 1f;
                float b = 0f;
                float c = 0f;
                float d = 1f;
                if ((flags & WE_HAVE_A_SCALE) != 0) {
                    a = ReadF2Dot14(glyph, position);
                    d = a;
                    position += 2;
                } else if ((flags & WE_HAVE_AN_X_AND_Y_SCALE) != 0) {
                    a = ReadF2Dot14(glyph, position);
                    d = ReadF2Dot14(glyph, position + 2);
                    position += 4;
                } else if ((flags & WE_HAVE_A_TWO_BY_TWO) != 0) {
                    a = ReadF2Dot14(glyph, position);
                    b = ReadF2Dot14(glyph, position + 2);
                    c = ReadF2Dot14(glyph, position + 4);
                    d = ReadF2Dot14(glyph, position + 6);
                    position += 8;
                }

                if (componentGlyph >= _reader._glyphCount) {
                    throw new FontFormatException(
                        $"the composite glyph {glyphId} references glyph {componentGlyph}; the face has {_reader._glyphCount} glyph(s)");
                }

                if (IsActive(componentGlyph)) {
                    throw new FontFormatException(
                        $"the composite glyph {glyphId} has a reference cycle through glyph {componentGlyph}");
                }

                _componentResolutions++;
                if (_componentResolutions > MAX_COMPONENT_RESOLUTIONS) {
                    throw new FontUnsupportedException(
                        $"the glyph resolves more than {MAX_COMPONENT_RESOLUTIONS} composite components");
                }

                float offsetX = argument1;
                float offsetY = argument2;
                if ((flags & SCALED_COMPONENT_OFFSET) != 0
                    && (flags & UNSCALED_COMPONENT_OFFSET) == 0) {
                    offsetX = (a * argument1) + (c * argument2);
                    offsetY = (b * argument1) + (d * argument2);
                }

                OutlineContour[] component = ResolveContours(componentGlyph, depth + 1, out _);
                for (int index = 0; index < component.Length; index++) {
                    contours.Add(Transform(component[index], a, b, c, d, offsetX, offsetY));
                }

                if ((flags & MORE_COMPONENTS) == 0) {
                    break;
                }
            }

            _activeCount--;
            return contours.ToArray();
        }

        private void PushActive(int glyphId) => _active[_activeCount++] = glyphId;

        private bool IsActive(int glyphId) {
            for (int index = 0; index < _activeCount; index++) {
                if (_active[index] == glyphId) {
                    return true;
                }
            }

            return false;
        }

        private static OutlineContour Transform(
            OutlineContour contour,
            float a,
            float b,
            float c,
            float d,
            float offsetX,
            float offsetY) {
            ReadOnlySpan<OutlineSegment> segments = contour.Segments.Span;
            OutlineSegment[] transformed = new OutlineSegment[segments.Length];
            for (int index = 0; index < segments.Length; index++) {
                OutlineSegment segment = segments[index];
                transformed[index] = segment.Kind switch {
                    OutlineSegmentKind.Line => OutlineSegment.Line(
                        TransformPoint(segment.End, a, b, c, d, offsetX, offsetY)),
                    OutlineSegmentKind.Quadratic => OutlineSegment.Quadratic(
                        TransformPoint(segment.Control1, a, b, c, d, offsetX, offsetY),
                        TransformPoint(segment.End, a, b, c, d, offsetX, offsetY)),
                    _ => OutlineSegment.Cubic(
                        TransformPoint(segment.Control1, a, b, c, d, offsetX, offsetY),
                        TransformPoint(segment.Control2, a, b, c, d, offsetX, offsetY),
                        TransformPoint(segment.End, a, b, c, d, offsetX, offsetY)),
                };
            }

            return new OutlineContour(
                TransformPoint(contour.Start, a, b, c, d, offsetX, offsetY),
                transformed);
        }

        private static Vector2 TransformPoint(
            Vector2 point,
            float a,
            float b,
            float c,
            float d,
            float offsetX,
            float offsetY) => new(
                (a * point.X) + (c * point.Y) + offsetX,
                (b * point.X) + (d * point.Y) + offsetY);

        private static float ReadF2Dot14(SfntReader glyph, int offset) =>
            glyph.ReadInt16(offset) * F2_DOT_14_SCALE;

        private static Vector2 Midpoint(Vector2 first, Vector2 second) =>
            new((first.X + second.X) * 0.5f, (first.Y + second.Y) * 0.5f);
    }

    private struct GlyphPoint {
        internal Vector2 Position;
        internal bool OnCurve;
    }
}
