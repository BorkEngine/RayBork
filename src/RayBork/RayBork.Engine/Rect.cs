using System.Numerics;

namespace RayBork;

/// <summary>A screen-space rectangle with a top-left origin.</summary>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>Gets the horizontal coordinate of the right edge.</summary>
    public float Right => X + Width;

    /// <summary>Gets the vertical coordinate of the bottom edge.</summary>
    public float Bottom => Y + Height;

    /// <summary>Gets the top-left corner as a vector.</summary>
    public Vector2 Position => new(X, Y);

    /// <summary>Gets the width and height as a vector.</summary>
    public Vector2 Size => new(Width, Height);

    /// <summary>Determines whether the rectangle contains a point.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns>
    /// <see langword="true"/> when inside. Left and top edges are inclusive; right and bottom
    /// are exclusive.
    /// </returns>
    public bool Contains(Vector2 point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    /// <summary>Creates a rectangle at the origin with the given size.</summary>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    public static Rect FromSize(float width, float height) => new(0f, 0f, width, height);
}
