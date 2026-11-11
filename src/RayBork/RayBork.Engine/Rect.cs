using System.Numerics;

namespace RayBork;

/// <summary>
/// A rectangle in screen space with a top-left origin.
/// </summary>
/// <param name="X">The horizontal coordinate of the left edge.</param>
/// <param name="Y">The vertical coordinate of the top edge.</param>
/// <param name="Width">The width of the rectangle.</param>
/// <param name="Height">The height of the rectangle.</param>
public readonly record struct Rect(float X, float Y, float Width, float Height) {
    /// <summary>
    /// Gets the horizontal coordinate of the right edge.
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// Gets the vertical coordinate of the bottom edge.
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// Gets the top-left corner as a vector.
    /// </summary>
    public Vector2 Position => new(X, Y);

    /// <summary>
    /// Gets the width and height as a vector.
    /// </summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>
    /// Determines whether the rectangle contains a point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns>
    /// <see langword="true"/> when the point is inside the rectangle. The left and top edges are
    /// inclusive; the right and bottom edges are exclusive.
    /// </returns>
    public bool Contains(Vector2 point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>
    /// Creates a rectangle at the origin with the given size.
    /// </summary>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <returns>A rectangle at <c>(0, 0)</c> with the given size.</returns>
    public static Rect FromSize(float width, float height) => new(0f, 0f, width, height);
}
