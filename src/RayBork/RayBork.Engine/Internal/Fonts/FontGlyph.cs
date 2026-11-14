namespace RayBork.Internal;

/// <summary>
/// One resolved glyph: its stored bounds and its font-unit, y-up outline.
/// </summary>
/// <remarks>
/// <see cref="Bounds"/> is the glyph header's bounding box, not a box computed from the resolved
/// segments. An empty glyph has an all-zero box and <see cref="OutlinePath.Empty"/> as its outline.
/// The instance is immutable and safe to read from any thread.
/// </remarks>
internal readonly struct FontGlyph {
    /// <summary>Initializes a resolved glyph.</summary>
    /// <param name="glyphId">The glyph identifier.</param>
    /// <param name="bounds">The stored glyph bounds in font units.</param>
    /// <param name="outline">The closed outline in font units.</param>
    internal FontGlyph(int glyphId, FontBounds bounds, OutlinePath outline) {
        GlyphId = glyphId;
        Bounds = bounds;
        Outline = outline;
    }

    /// <summary>Gets the glyph identifier.</summary>
    internal int GlyphId { get; }

    /// <summary>Gets the stored glyph bounds in font units.</summary>
    internal FontBounds Bounds { get; }

    /// <summary>Gets the closed outline in font units.</summary>
    internal OutlinePath Outline { get; }

    /// <summary>Gets a value indicating whether the glyph has no outline.</summary>
    internal bool IsEmpty => Outline.IsEmpty;
}
