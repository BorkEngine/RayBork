using System.Numerics;

namespace RayBork.Internal;

/// <summary>One laid-out glyph's padded quad in local layout space, in world units.</summary>
/// <param name="GlyphId">The font glyph id.</param>
/// <param name="Position">The quad's lower-left corner in layout space.</param>
/// <param name="Size">The quad's width and height in layout units.</param>
internal readonly record struct TextGlyphInstance(int GlyphId, Vector2 Position, Vector2 Size);
