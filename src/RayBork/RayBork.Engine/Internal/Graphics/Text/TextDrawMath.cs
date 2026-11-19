using System.Numerics;

namespace RayBork.Internal;

/// <summary>The pure matrix helpers used to place camera text glyph quads.</summary>
/// <remarks>The engine quad is a unit square centered at the local origin with <c>+Z</c> toward the
/// viewer.</remarks>
internal static class TextDrawMath
{
    /// <summary>Maps the unit quad onto one laid-out glyph quad.</summary>
    /// <param name="position">The glyph quad's lower-left corner in layout space.</param>
    /// <param name="size">The glyph quad's width and height in layout units.</param>
    /// <returns>The unit-quad-to-layout matrix.</returns>
    internal static Matrix4x4 CreateGlyphQuad(Vector2 position, Vector2 size) =>
        Matrix4x4.CreateScale(size.X, size.Y, 1f)
        * Matrix4x4.CreateTranslation(
            position.X + (size.X * 0.5f),
            position.Y + (size.Y * 0.5f),
            0f
        );

    /// <summary>Builds the offset that moves a text block's bounds center to the local origin.</summary>
    /// <param name="boundsMin">The lower-left corner of the layout bounds.</param>
    /// <param name="boundsMax">The upper-right corner of the layout bounds.</param>
    /// <returns>The translation that centers the bounds on the origin.</returns>
    internal static Matrix4x4 CreateCenteredBlockOffset(Vector2 boundsMin, Vector2 boundsMax) =>
        Matrix4x4.CreateTranslation(
            -(boundsMin.X + boundsMax.X) * 0.5f,
            -(boundsMin.Y + boundsMax.Y) * 0.5f,
            0f
        );
}
