namespace RayBork.Internal;

/// <summary>A reusable glyph-record buffer for <see cref="TextLayoutEngine.Arrange"/>.</summary>
/// <remarks>The buffer grows geometrically and never shrinks; <see cref="Glyphs"/> is invalidated by
/// the next arrange. It is not thread-safe.</remarks>
internal sealed class TextLayoutBuffer
{
    private const int DEFAULT_CAPACITY = 64;

    private TextGlyphInstance[] _glyphs = [];

    /// <summary>Initializes an empty buffer.</summary>
    internal TextLayoutBuffer() { }

    /// <summary>Initializes an empty buffer with room for <paramref name="initialCapacity"/>
    /// records.</summary>
    /// <param name="initialCapacity">The initial capacity; non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is
    /// negative.</exception>
    internal TextLayoutBuffer(int initialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        if (initialCapacity > 0)
        {
            _glyphs = new TextGlyphInstance[initialCapacity];
        }
    }

    /// <summary>Gets the number of glyph records written by the last arrange call.</summary>
    internal int Count { get; private set; }

    /// <summary>Gets the current record capacity, which never shrinks.</summary>
    internal int Capacity => _glyphs.Length;

    /// <summary>Gets the glyph records written by the last arrange call.</summary>
    internal ReadOnlySpan<TextGlyphInstance> Glyphs => _glyphs.AsSpan(0, Count);

    /// <summary>Reserves storage for <paramref name="count"/> records and sets <see cref="Count"/> to
    /// it.</summary>
    /// <param name="count">The records about to be written; non-negative.</param>
    /// <returns>A writable span of exactly <paramref name="count"/> records.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is
    /// negative.</exception>
    internal Span<TextGlyphInstance> Reserve(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _glyphs.Length)
        {
            int capacity = Math.Max(count, Math.Max(_glyphs.Length * 2, DEFAULT_CAPACITY));
            Array.Resize(ref _glyphs, capacity);
        }
        Count = count;
        return _glyphs.AsSpan(0, count);
    }
}
