using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The pure matrix helpers the camera text draws use to place glyph quads.
/// </summary>
/// <remarks>
/// The engine quad is a unit square centered on the origin in its local XY plane with <c>+Z</c> toward
/// the viewer. A layout instance is the glyph's padded quad in local layout space, with its lower-left
/// corner at <see cref="TextGlyphInstance.Position"/> and its extent in
/// <see cref="TextGlyphInstance.Size"/>. These helpers build the local part of the mapping and stay
/// free of camera, font, and GPU state so the mapping is testable in isolation.
/// </remarks>
internal static class TextDrawMath {
    /// <summary>
    /// Builds the local transform that maps the engine's unit quad onto one laid-out glyph quad.
    /// </summary>
    /// <param name="position">The glyph quad's lower-left corner in local layout space.</param>
    /// <param name="size">The glyph quad's width and height in local layout units.</param>
    /// <returns>The unit-quad-to-layout matrix.</returns>
    /// <remarks>
    /// The matrix scales the centered unit quad by <paramref name="size"/> and moves its center to
    /// <paramref name="position"/> plus half the size, so the quad's lower-left corner lands exactly on
    /// the layout position and its upper-right corner lands on the position plus the size.
    /// </remarks>
    internal static Matrix4x4 CreateGlyphQuad(Vector2 position, Vector2 size) =>
        Matrix4x4.CreateScale(size.X, size.Y, 1f)
        * Matrix4x4.CreateTranslation(
            position.X + (size.X * 0.5f),
            position.Y + (size.Y * 0.5f),
            0f);

    /// <summary>
    /// Builds the local offset that moves a text block's bounds center to the local origin.
    /// </summary>
    /// <param name="boundsMin">The lower-left corner of the block's layout bounds.</param>
    /// <param name="boundsMax">The upper-right corner of the block's layout bounds.</param>
    /// <returns>The translation that centers the bounds on the origin.</returns>
    /// <remarks>
    /// A billboard centers the union of the layout box and every glyph quad on its world position, so
    /// the translation is applied in layout space before the camera-facing orientation.
    /// </remarks>
    internal static Matrix4x4 CreateCenteredBlockOffset(Vector2 boundsMin, Vector2 boundsMax) =>
        Matrix4x4.CreateTranslation(
            -(boundsMin.X + boundsMax.X) * 0.5f,
            -(boundsMin.Y + boundsMax.Y) * 0.5f,
            0f);
}
