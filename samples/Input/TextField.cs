using RayBork;

namespace Input;

/// <summary>
/// The single-line editable text of the sample's field: committed text, clipboard insertion, the
/// last-text-element backspace rule, and the visible tail that fits the field.
/// </summary>
/// <remarks>
/// The field stores plain UTF-16 text. Backspace removes one text element rather than one
/// <see cref="char"/>, so it never splits a surrogate pair, and every append replaces control
/// characters and line breaks with spaces to keep the field single-line. Fitting measures the tail
/// with <see cref="Font.Measure(ReadOnlySpan{char}, float)"/> and prefixes an ellipsis when the
/// full text is wider than the field, so the caret position is always the measured end of the
/// visible text.
/// </remarks>
internal sealed class TextField
{
    private const string ELLIPSIS = "…";

    private string _text = string.Empty;

    /// <summary>Gets the committed field text.</summary>
    internal string Text => _text;

    /// <summary>Gets the field length in UTF-16 code units.</summary>
    internal int Length => _text.Length;

    /// <summary>
    /// Appends committed text after replacing control characters and line breaks with spaces.
    /// </summary>
    /// <param name="text">The text to append; may be empty.</param>
    /// <returns>
    /// The sanitized text that was appended, or an empty string when nothing was appended.
    /// </returns>
    internal string Append(string text)
    {
        string sanitized = Sanitize(text);
        if (sanitized.Length == 0)
        {
            return string.Empty;
        }

        _text += sanitized;
        return sanitized;
    }

    /// <summary>
    /// Removes the last UTF-16 text element. A trailing surrogate pair is removed whole.
    /// </summary>
    internal void RemoveLastElement()
    {
        if (_text.Length == 0)
        {
            return;
        }

        int start = _text.Length - 1;
        if (
            start > 0
            && char.IsHighSurrogate(_text[start - 1])
            && char.IsLowSurrogate(_text[start])
        )
        {
            start--;
        }

        _text = _text[..start];
    }

    /// <summary>Clears the field.</summary>
    internal void Clear() => _text = string.Empty;

    /// <summary>
    /// Fits the field text into the available width, keeping the visible tail and prefixing an
    /// ellipsis when the full text does not fit.
    /// </summary>
    /// <param name="font">The field's font.</param>
    /// <param name="size">The em height in render pixels.</param>
    /// <param name="maxWidth">The available width in render pixels.</param>
    /// <returns>The visible text and its measured width.</returns>
    internal View Fit(Font font, float size, float maxWidth)
    {
        if (_text.Length == 0 || maxWidth <= 0f)
        {
            return new View(string.Empty, 0f);
        }

        float width = font.Measure(_text, size).X;
        if (width <= maxWidth)
        {
            return new View(_text, width);
        }

        float ellipsisWidth = font.Measure(ELLIPSIS, size).X;
        float available = maxWidth - ellipsisWidth;
        if (available <= 0f)
        {
            return new View(ELLIPSIS, ellipsisWidth);
        }

        // Walk backwards one text element at a time and stop at the first tail that no longer fits.
        // The loop is bounded by the visible tail, not by the whole field, so a long field costs
        // the same as a short one.
        int start = _text.Length;
        float tailWidth = 0f;
        while (start > 0)
        {
            int previous = PreviousElementStart(start);
            float candidate = font.Measure(_text.AsSpan(previous), size).X;
            if (candidate > available)
            {
                break;
            }

            start = previous;
            tailWidth = candidate;
        }

        string visible = string.Concat(ELLIPSIS, _text.AsSpan(start));
        return new View(visible, ellipsisWidth + tailWidth);
    }

    /// <summary>
    /// Replaces control characters and line breaks with spaces for the single-line field. A CRLF
    /// pair becomes one space instead of two.
    /// </summary>
    /// <param name="text">The text to sanitize.</param>
    /// <returns>
    /// The sanitized text, or the original instance when it contains no control character.
    /// </returns>
    private static string Sanitize(string text)
    {
        int index = 0;
        while (index < text.Length && !char.IsControl(text[index]))
        {
            index++;
        }

        if (index == text.Length)
        {
            return text;
        }

        // Rewrite in place from the first control character; write can never pass read, so the
        // unread characters are still intact when they are copied.
        char[] characters = text.ToCharArray();
        int write = index;
        for (int read = index; read < characters.Length; read++)
        {
            char character = characters[read];
            if (character == '\r' && read + 1 < characters.Length && characters[read + 1] == '\n')
            {
                continue;
            }

            characters[write] = char.IsControl(character) ? ' ' : character;
            write++;
        }

        return new string(characters, 0, write);
    }

    /// <summary>
    /// Gets the start of the text element that ends at <paramref name="end"/>.
    /// </summary>
    private int PreviousElementStart(int end) =>
        end >= 2 && char.IsHighSurrogate(_text[end - 2]) && char.IsLowSurrogate(_text[end - 1])
            ? end - 2
            : end - 1;

    /// <summary>
    /// The visible field text and its measured width in render pixels.
    /// </summary>
    /// <param name="Text">The visible text, including the ellipsis when clipped.</param>
    /// <param name="Width">The measured width of <paramref name="Text"/>.</param>
    internal readonly record struct View(string Text, float Width);
}
