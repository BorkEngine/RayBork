using RayBork;

namespace Input;

/// <summary>
/// The sample's single-line editable field with committed text, clipboard insertion, and tail
/// fitting.
/// </summary>
/// <remarks>
/// Backspace removes one text element, so it never splits a surrogate pair. Append replaces
/// control characters and line breaks with spaces.
/// </remarks>
internal sealed class TextField
{
    private const string ELLIPSIS = "…";

    private string _text = string.Empty;

    /// <summary>Gets the committed field text.</summary>
    internal string Text => _text;

    /// <summary>Gets the field length in UTF-16 code units.</summary>
    internal int Length => _text.Length;

    /// <summary>Appends committed text after sanitizing control characters.</summary>
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

    /// <summary>Removes the last UTF-16 text element, including a surrogate pair.</summary>
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

    /// <summary>Fits the text into the width, keeping the visible tail with an ellipsis.</summary>
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

    /// <summary>Replaces control characters and line breaks with spaces.</summary>
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

    /// <summary>Gets the start of the text element that ends at <paramref name="end"/>.</summary>
    private int PreviousElementStart(int end) =>
        end >= 2 && char.IsHighSurrogate(_text[end - 2]) && char.IsLowSurrogate(_text[end - 1])
            ? end - 2
            : end - 1;

    /// <summary>The visible field text and its measured width in render pixels.</summary>
    internal readonly record struct View(string Text, float Width);
}
