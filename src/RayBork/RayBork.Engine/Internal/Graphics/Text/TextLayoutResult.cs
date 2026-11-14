using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The block-level result of one <see cref="TextLayoutEngine.Arrange"/> call.
/// </summary>
/// <param name="LineCount">The number of laid-out lines; zero for empty text.</param>
/// <param name="BoundsMin">
/// The lower-left corner of the union of the layout box and every glyph quad; <see cref="Vector2.Zero"/>
/// for empty text.
/// </param>
/// <param name="BoundsMax">
/// The upper-right corner of the union of the layout box and every glyph quad; <see cref="Vector2.Zero"/>
/// for empty text.
/// </param>
internal readonly record struct TextLayoutResult(int LineCount, Vector2 BoundsMin, Vector2 BoundsMax) {
    /// <summary>Gets the block size, <see cref="BoundsMax"/> minus <see cref="BoundsMin"/>.</summary>
    internal Vector2 BoundsSize => BoundsMax - BoundsMin;
}
