namespace RayBork.Internal;

/// <summary>
/// A reusable glyph-record buffer for <see cref="TextLayoutEngine.Arrange"/>.
/// </summary>
/// <remarks>
/// <para>
/// The buffer owns a single array that grows geometrically and never shrinks. In steady state the
/// layout engine writes into the existing storage and allocates nothing. The app-lifetime
/// <c>CameraService</c> owns one buffer and reuses it for every camera and text draw it records, so
/// the buffer warms up across frames.
/// </para>
/// <para>
/// A buffer is not thread-safe and must not be shared between recording contexts. The span returned by
/// <see cref="Glyphs"/> is valid until the next <see cref="TextLayoutEngine.Arrange"/> call on the same
/// buffer, so it must be consumed before that call.
/// </para>
/// </remarks>
internal sealed class TextLayoutBuffer {
    private const int DEFAULT_CAPACITY = 64;

    private TextGlyphInstance[] _glyphs = [];

    /// <summary>
    /// Initializes an empty buffer.
    /// </summary>
    internal TextLayoutBuffer() {
    }

    /// <summary>
    /// Initializes an empty buffer with room for <paramref name="initialCapacity"/> records.
    /// </summary>
    /// <param name="initialCapacity">The initial record capacity. It must be non-negative.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is negative.</exception>
    internal TextLayoutBuffer(int initialCapacity) {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);
        if (initialCapacity > 0) {
            _glyphs = new TextGlyphInstance[initialCapacity];
        }
    }

    /// <summary>Gets the number of glyph records written by the last arrange call.</summary>
    internal int Count { get; private set; }

    /// <summary>Gets the current record capacity. It never shrinks.</summary>
    internal int Capacity => _glyphs.Length;

    /// <summary>Gets the glyph records written by the last arrange call.</summary>
    internal ReadOnlySpan<TextGlyphInstance> Glyphs => _glyphs.AsSpan(0, Count);

    /// <summary>
    /// Reserves storage for <paramref name="count"/> records and sets <see cref="Count"/> to it.
    /// </summary>
    /// <param name="count">The number of records the caller is about to write. It must be non-negative.</param>
    /// <returns>A writable span of exactly <paramref name="count"/> records.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    internal Span<TextGlyphInstance> Reserve(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _glyphs.Length) {
            int capacity = Math.Max(count, Math.Max(_glyphs.Length * 2, DEFAULT_CAPACITY));
            Array.Resize(ref _glyphs, capacity);
        }
        Count = count;
        return _glyphs.AsSpan(0, count);
    }
}
