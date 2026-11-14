using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// One laid-out glyph: the glyph id and the padded quad in local layout space, in world units at the
/// requested size.
/// </summary>
/// <param name="GlyphId">The font's glyph id.</param>
/// <param name="Position">The quad's lower-left corner in local layout space.</param>
/// <param name="Size">The quad's width and height.</param>
internal readonly record struct TextGlyphInstance(int GlyphId, Vector2 Position, Vector2 Size);
