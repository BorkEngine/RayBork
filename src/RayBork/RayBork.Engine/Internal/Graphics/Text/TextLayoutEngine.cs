using System.Buffers;
using System.Numerics;
using System.Text;

namespace RayBork.Internal;

/// <summary>
/// Lays out a UTF-16 text block on the CPU from font metrics.
/// </summary>
/// <remarks>
/// <para>
/// The engine is pure: it never touches the GPU, the asset catalog, a glyph texture, or tick-affine
/// state, and it is safe to call from any thread. It reads metrics through <see cref="ITextMetrics"/>,
/// which the runtime <c>Font</c> supplies, so it does not depend on a font parser.
/// </para>
/// <para>
/// Text is decoded with <see cref="Rune"/>; an unpaired surrogate decodes to U+FFFD. <c>'\n'</c>
/// always starts a new line and <c>'\r'</c> is ignored. Every other mapped rune advances the pen by its
/// glyph advance; a rune without a glyph produces no record and no advance, and kerning still bridges it
/// to the next mapped glyph. All metrics are in em units, and the local layout space is in world units
/// at the requested size with +X to the right and +Y up.
/// </para>
/// <para>
/// The origin is the top-left of the layout box. The first baseline is at <c>-Ascent * size</c> and
/// line <c>i</c>'s baseline is at <c>-(Ascent + i * LineHeight) * size</c>. The box is
/// <c>maxWidth</c> wide when that is positive and as wide as the widest line otherwise; alignment
/// shifts each line inside the box. The block bounds are the union of the box
/// <c>[0, boxWidth] x [-lineCount * LineHeight * size, 0]</c> and every glyph quad, so the ascent and
/// line boxes of every line are included. Empty text has no lines and <see cref="Vector2.Zero"/> bounds.
/// </para>
/// <para>
/// <see cref="Arrange"/> allocates nothing once the caller's <see cref="TextLayoutBuffer"/> has grown
/// enough. The buffer is caller-owned and not thread-safe; the engine itself holds no state.
/// </para>
/// <para>
/// <see cref="TextWrapping.Word"/> breaks only before a whitespace run that follows a word. The run at
/// the break is dropped and does not indent the next line; leading and trailing whitespace of a
/// paragraph stays on its line. A word wider than the box overflows on a line of its own.
/// <see cref="TextWrapping.Character"/> breaks between any two glyphs and never drops a rune, so
/// whitespace can start a line. Kerning never spans a line break.
/// </para>
/// </remarks>
internal static class TextLayoutEngine {
    private const char CARRIAGE_RETURN = '\r';
    private const int NO_GLYPH = int.MinValue;

    /// <summary>
    /// Lays out one text block and writes its glyph records into <paramref name="buffer"/>.
    /// </summary>
    /// <param name="metrics">The font metrics. All values are in em units.</param>
    /// <param name="text">The UTF-16 text to lay out.</param>
    /// <param name="size">The em height in world units. It must be finite and positive.</param>
    /// <param name="alignment">How each line is aligned inside the layout box.</param>
    /// <param name="wrapping">
    /// Where lines may break. <see cref="TextWrapping.Word"/> and <see cref="TextWrapping.Character"/>
    /// require a positive <paramref name="maxWidth"/>.
    /// </param>
    /// <param name="maxWidth">
    /// The layout box width in world units at <paramref name="size"/>, or <c>0</c> for no box limit.
    /// </param>
    /// <param name="buffer">The reusable output buffer that receives the glyph records.</param>
    /// <param name="result">Receives the line count and the block bounds.</param>
    /// <returns>
    /// The number of glyph records written, which is also <see cref="TextLayoutBuffer.Count"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metrics"/> or <paramref name="buffer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> is not finite and positive; <paramref name="maxWidth"/> is negative or
    /// not finite; a wrapping mode requires a positive <paramref name="maxWidth"/>; or an alignment or
    /// wrapping value is not defined.
    /// </exception>
    internal static int Arrange(
        ITextMetrics metrics,
        ReadOnlySpan<char> text,
        float size,
        TextAlignment alignment,
        TextWrapping wrapping,
        float maxWidth,
        TextLayoutBuffer buffer,
        out TextLayoutResult result) {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(buffer);
        ValidateSize(size);
        ValidateAlignment(alignment);
        ValidateWrapping(wrapping, maxWidth);

        MeasureBlock(
            metrics,
            text,
            size,
            wrapping,
            maxWidth,
            out int lineCount,
            out int glyphCount,
            out float widestLine);
        float boxWidth = maxWidth > 0f ? maxWidth : widestLine;
        Span<TextGlyphInstance> glyphs = buffer.Reserve(glyphCount);
        result = Emit(metrics, text, size, alignment, wrapping, maxWidth, boxWidth, lineCount, glyphs);
        return glyphCount;
    }

    /// <summary>
    /// Measures one text block. Measurement ignores wrapping and a width limit and always uses
    /// <see cref="TextWrapping.None"/>.
    /// </summary>
    /// <param name="metrics">The font metrics. All values are in em units.</param>
    /// <param name="text">The UTF-16 text to measure.</param>
    /// <param name="size">The em height in world units. It must be finite and positive.</param>
    /// <returns>
    /// The widest line advance including kerning and <c>lineCount * LineHeight * size</c>. Empty text
    /// measures <see cref="Vector2.Zero"/>; every other text has at least one line.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> is not finite and positive.
    /// </exception>
    internal static Vector2 Measure(ITextMetrics metrics, ReadOnlySpan<char> text, float size) {
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateSize(size);
        MeasureBlock(
            metrics,
            text,
            size,
            TextWrapping.None,
            0f,
            out int lineCount,
            out _,
            out float widestLine);
        return new Vector2(widestLine, lineCount * metrics.LineHeight * size);
    }

    private static void ValidateSize(float size) {
        if (!float.IsFinite(size) || size <= 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "The text size must be finite and positive.");
        }
    }

    private static void ValidateAlignment(TextAlignment alignment) {
        if (alignment is not (TextAlignment.Left or TextAlignment.Center or TextAlignment.Right)) {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                "The text alignment is not defined.");
        }
    }

    private static void ValidateWrapping(TextWrapping wrapping, float maxWidth) {
        if (wrapping is not (TextWrapping.None or TextWrapping.Word or TextWrapping.Character)) {
            throw new ArgumentOutOfRangeException(
                nameof(wrapping),
                wrapping,
                "The text wrapping mode is not defined.");
        }
        if (!float.IsFinite(maxWidth) || maxWidth < 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(maxWidth),
                maxWidth,
                "The maximum width must be finite and non-negative.");
        }
        if (wrapping != TextWrapping.None && maxWidth <= 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(maxWidth),
                maxWidth,
                "Text wrapping requires a positive maximum width.");
        }
    }

    private static void MeasureBlock(
        ITextMetrics metrics,
        ReadOnlySpan<char> text,
        float size,
        TextWrapping wrapping,
        float maxWidth,
        out int lineCount,
        out int glyphCount,
        out float widestLine) {
        lineCount = 0;
        glyphCount = 0;
        widestLine = 0f;
        LineEnumerator lines = new(metrics, text, size, wrapping, maxWidth);
        while (lines.MoveNext(out _, out _, out float lineAdvance, out int count)) {
            lineCount++;
            glyphCount += count;
            if (lineAdvance > widestLine) {
                widestLine = lineAdvance;
            }
        }
    }

    private static TextLayoutResult Emit(
        ITextMetrics metrics,
        ReadOnlySpan<char> text,
        float size,
        TextAlignment alignment,
        TextWrapping wrapping,
        float maxWidth,
        float boxWidth,
        int lineCount,
        Span<TextGlyphInstance> glyphs) {
        if (lineCount == 0) {
            return new TextLayoutResult(0, Vector2.Zero, Vector2.Zero);
        }

        float boxHeight = lineCount * metrics.LineHeight * size;
        Vector2 boundsMin = new(0f, -boxHeight);
        Vector2 boundsMax = new(boxWidth, 0f);
        int glyphIndex = 0;
        int lineIndex = 0;
        LineEnumerator lines = new(metrics, text, size, wrapping, maxWidth);
        while (lines.MoveNext(out int lineStart, out int lineEnd, out float lineAdvance, out _)) {
            float offset = alignment switch {
                TextAlignment.Center => (boxWidth - lineAdvance) / 2f,
                TextAlignment.Right => boxWidth - lineAdvance,
                _ => 0f,
            };
            float baselineY = -(metrics.Ascent + lineIndex * metrics.LineHeight) * size;
            float pen = offset;
            int lastGlyphId = NO_GLYPH;
            int index = lineStart;
            while (TryReadGlyph(metrics, text, lineEnd, ref index, out _, out TextGlyphMetrics glyph)) {
                float glyphPen = PlaceGlyph(ref pen, metrics, lastGlyphId, glyph, size);
                Vector2 position = new(glyphPen + glyph.BearingX * size, baselineY + glyph.BearingY * size);
                Vector2 glyphSize = new(glyph.Width * size, glyph.Height * size);
                glyphs[glyphIndex] = new TextGlyphInstance(glyph.GlyphId, position, glyphSize);
                glyphIndex++;
                boundsMin = Vector2.Min(boundsMin, position);
                boundsMax = Vector2.Max(boundsMax, position + glyphSize);
                lastGlyphId = glyph.GlyphId;
            }
            lineIndex++;
        }
        return new TextLayoutResult(lineCount, boundsMin, boundsMax);
    }

    private static bool TryReadGlyph(
        ITextMetrics metrics,
        ReadOnlySpan<char> text,
        int end,
        ref int index,
        out int runeIndex,
        out TextGlyphMetrics glyph) {
        while (index < end) {
            int start = index;
            Rune rune = DecodeRune(text, index, end, out int consumed);
            index += consumed;
            if (rune.Value == CARRIAGE_RETURN) {
                continue;
            }
            if (metrics.TryGetGlyphMetrics(rune, out glyph)) {
                runeIndex = start;
                return true;
            }
        }
        runeIndex = end;
        glyph = default;
        return false;
    }

    private static void MeasureRange(
        ITextMetrics metrics,
        ReadOnlySpan<char> text,
        int start,
        int end,
        float size,
        ref float pen,
        ref int lastGlyphId,
        ref int glyphCount) {
        int index = start;
        while (TryReadGlyph(metrics, text, end, ref index, out _, out TextGlyphMetrics glyph)) {
            PlaceGlyph(ref pen, metrics, lastGlyphId, glyph, size);
            lastGlyphId = glyph.GlyphId;
            glyphCount++;
        }
    }

    /// <summary>
    /// Applies the kerning from the previous glyph, returns the pen position the glyph is placed at, and
    /// advances the pen past the glyph.
    /// </summary>
    /// <remarks>
    /// Measurement, emission, and character wrapping share this step, so the arithmetic that decides a
    /// line break is exactly the arithmetic that places the glyphs.
    /// </remarks>
    private static float PlaceGlyph(
        ref float pen,
        ITextMetrics metrics,
        int lastGlyphId,
        in TextGlyphMetrics glyph,
        float size) {
        if (lastGlyphId != NO_GLYPH) {
            pen += metrics.GetKerning(lastGlyphId, glyph.GlyphId) * size;
        }
        float origin = pen;
        pen += glyph.AdvanceX * size;
        return origin;
    }

    private static Rune DecodeRune(ReadOnlySpan<char> text, int start, int end, out int consumed) {
        OperationStatus status = Rune.DecodeFromUtf16(
            text.Slice(start, end - start),
            out Rune rune,
            out consumed);
        if (status != OperationStatus.Done) {
            consumed = 1;
            return Rune.ReplacementChar;
        }
        return rune;
    }

    private static int FindParagraphEnd(ReadOnlySpan<char> text, int start) {
        int relative = text.Slice(start).IndexOf('\n');
        return relative < 0 ? text.Length : start + relative;
    }

    private ref struct LineEnumerator {
        private readonly ITextMetrics _metrics;
        private readonly ReadOnlySpan<char> _text;
        private readonly float _size;
        private readonly TextWrapping _wrapping;
        private readonly float _maxWidth;
        private int _position;
        private int _paragraphEnd;
        private bool _started;
        private bool _finished;

        internal LineEnumerator(
            ITextMetrics metrics,
            ReadOnlySpan<char> text,
            float size,
            TextWrapping wrapping,
            float maxWidth) {
            _metrics = metrics;
            _text = text;
            _size = size;
            _wrapping = wrapping;
            _maxWidth = maxWidth;
            _position = 0;
            _paragraphEnd = text.Length == 0 ? 0 : FindParagraphEnd(text, 0);
            _started = false;
            _finished = text.Length == 0;
        }

        internal bool MoveNext(out int lineStart, out int lineEnd, out float lineAdvance, out int glyphCount) {
            if (_finished) {
                return Finish(out lineStart, out lineEnd, out lineAdvance, out glyphCount);
            }
            if (_started && _position >= _paragraphEnd) {
                if (_paragraphEnd >= _text.Length) {
                    _finished = true;
                    return Finish(out lineStart, out lineEnd, out lineAdvance, out glyphCount);
                }
                _position = _paragraphEnd + 1;
                _paragraphEnd = FindParagraphEnd(_text, _position);
            }
            _started = true;
            lineStart = _position;
            switch (_wrapping) {
                case TextWrapping.Word:
                    return EmitWordLine(out lineEnd, out lineAdvance, out glyphCount);
                case TextWrapping.Character:
                    return EmitCharacterLine(out lineEnd, out lineAdvance, out glyphCount);
                default:
                    return EmitUnwrappedLine(out lineEnd, out lineAdvance, out glyphCount);
            }
        }

        private static bool Finish(
            out int lineStart,
            out int lineEnd,
            out float lineAdvance,
            out int glyphCount) {
            lineStart = 0;
            lineEnd = 0;
            lineAdvance = 0f;
            glyphCount = 0;
            return false;
        }

        private bool EmitUnwrappedLine(out int lineEnd, out float lineAdvance, out int glyphCount) {
            float pen = 0f;
            int lastGlyphId = NO_GLYPH;
            glyphCount = 0;
            MeasureRange(
                _metrics,
                _text,
                _position,
                _paragraphEnd,
                _size,
                ref pen,
                ref lastGlyphId,
                ref glyphCount);
            lineEnd = _paragraphEnd;
            lineAdvance = pen;
            _position = _paragraphEnd;
            return true;
        }

        private bool EmitWordLine(out int lineEnd, out float lineAdvance, out int glyphCount) {
            int index = _position;
            float pen = 0f;
            int lastGlyphId = NO_GLYPH;
            int count = 0;
            bool hasGlyph = false;
            while (index < _paragraphEnd) {
                int gapStart = index;
                while (index < _paragraphEnd) {
                    Rune rune = DecodeRune(_text, index, _paragraphEnd, out int consumed);
                    if (!Rune.IsWhiteSpace(rune)) {
                        break;
                    }
                    index += consumed;
                }
                int gapEnd = index;
                int wordStart = index;
                while (index < _paragraphEnd) {
                    Rune rune = DecodeRune(_text, index, _paragraphEnd, out int consumed);
                    if (rune.Value != CARRIAGE_RETURN && Rune.IsWhiteSpace(rune)) {
                        break;
                    }
                    index += consumed;
                }
                int wordEnd = index;
                if (wordStart == wordEnd) {
                    MeasureRange(
                        _metrics,
                        _text,
                        gapStart,
                        gapEnd,
                        _size,
                        ref pen,
                        ref lastGlyphId,
                        ref count);
                    break;
                }

                float candidatePen = pen;
                int candidateLastGlyphId = lastGlyphId;
                int candidateCount = count;
                MeasureRange(
                    _metrics,
                    _text,
                    gapStart,
                    gapEnd,
                    _size,
                    ref candidatePen,
                    ref candidateLastGlyphId,
                    ref candidateCount);
                MeasureRange(
                    _metrics,
                    _text,
                    wordStart,
                    wordEnd,
                    _size,
                    ref candidatePen,
                    ref candidateLastGlyphId,
                    ref candidateCount);
                if (hasGlyph && candidatePen > _maxWidth) {
                    lineEnd = gapStart;
                    lineAdvance = pen;
                    glyphCount = count;
                    _position = wordStart;
                    return true;
                }
                pen = candidatePen;
                lastGlyphId = candidateLastGlyphId;
                count = candidateCount;
                hasGlyph = count > 0;
                index = wordEnd;
            }
            lineEnd = _paragraphEnd;
            lineAdvance = pen;
            glyphCount = count;
            _position = _paragraphEnd;
            return true;
        }

        private bool EmitCharacterLine(out int lineEnd, out float lineAdvance, out int glyphCount) {
            int index = _position;
            float pen = 0f;
            int lastGlyphId = NO_GLYPH;
            glyphCount = 0;
            while (index < _paragraphEnd) {
                if (!TryReadGlyph(
                        _metrics,
                        _text,
                        _paragraphEnd,
                        ref index,
                        out int glyphIndex,
                        out TextGlyphMetrics glyph)) {
                    break;
                }
                float candidatePen = pen;
                PlaceGlyph(ref candidatePen, _metrics, lastGlyphId, glyph, _size);
                if (glyphCount > 0 && candidatePen > _maxWidth) {
                    lineEnd = glyphIndex;
                    lineAdvance = pen;
                    _position = glyphIndex;
                    return true;
                }
                pen = candidatePen;
                lastGlyphId = glyph.GlyphId;
                glyphCount++;
            }
            lineEnd = _paragraphEnd;
            lineAdvance = pen;
            _position = _paragraphEnd;
            return true;
        }
    }
}
