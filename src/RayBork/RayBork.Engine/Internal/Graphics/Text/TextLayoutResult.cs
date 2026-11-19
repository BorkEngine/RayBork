using System.Numerics;

namespace RayBork.Internal;

/// <summary>The block-level result of one <see cref="TextLayoutEngine.Arrange"/> call.</summary>
/// <remarks>Bounds are the union of the layout box and every glyph quad; empty text has zero lines and
/// zero bounds.</remarks>
/// <param name="LineCount">The laid-out line count; zero for empty text.</param>
/// <param name="BoundsMin">The lower-left bounds corner; <see cref="Vector2.Zero"/> when empty.</param>
/// <param name="BoundsMax">The upper-right bounds corner; <see cref="Vector2.Zero"/> when empty.</param>
internal readonly record struct TextLayoutResult(
    int LineCount,
    Vector2 BoundsMin,
    Vector2 BoundsMax
)
{
    /// <summary>Gets the block size, <see cref="BoundsMax"/> minus <see cref="BoundsMin"/>.</summary>
    internal Vector2 BoundsSize => BoundsMax - BoundsMin;
}
