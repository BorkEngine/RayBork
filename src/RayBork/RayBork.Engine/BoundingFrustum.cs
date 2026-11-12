using System.Numerics;

namespace RayBork;

/// <summary>
/// Six inward-facing planes extracted from a view-projection matrix, in reverse-Z Vulkan clip space.
/// </summary>
/// <remarks>
/// <para>
/// The plane names follow the engine's Y-flipped NDC convention. Vulkan NDC has <c>+Y</c> pointing
/// down and the projection negates row 1 so world <c>+Y</c> maps to the top of the screen. As a
/// result <see cref="Top"/> and <see cref="Bottom"/> are the inverted clip planes: a point is inside
/// when it lies on or below <see cref="Top"/> and on or above <see cref="Bottom"/>.
/// </para>
/// <para>
/// The depth range is Vulkan's <c>[0, 1]</c> reverse-Z: the near plane maps to <c>1</c> and the far
/// plane maps to <c>0</c>, so <see cref="Near"/> is the <c>z &gt;= 0</c> plane and <see cref="Far"/>
/// is the <c>z &lt;= w</c> plane. Every plane is normalized, so
/// <see cref="Plane.DotCoordinate(Plane, Vector3)"/> returns a world-space signed distance.
/// <see cref="Contains"/> and <see cref="Intersects"/> use that distance and are conservative:
/// geometry on the boundary counts as inside.
/// </para>
/// <para>
/// A default <see cref="BoundingFrustum"/> has six zero planes and accepts every point; construct it
/// from a real projection before using it for culling.
/// </para>
/// </remarks>
public readonly struct BoundingFrustum {
    /// <summary>
    /// Extracts the six normalized planes from a view-projection matrix.
    /// </summary>
    /// <param name="viewProjection">
    /// The view-projection matrix in the engine's row-vector convention: a position transforms as
    /// <c>Vector4.Transform(new Vector4(position, 1), viewProjection)</c>.
    /// </param>
    public BoundingFrustum(Matrix4x4 viewProjection) {
        Vector4 column0 = new(viewProjection.M11, viewProjection.M21, viewProjection.M31, viewProjection.M41);
        Vector4 column1 = new(viewProjection.M12, viewProjection.M22, viewProjection.M32, viewProjection.M42);
        Vector4 column2 = new(viewProjection.M13, viewProjection.M23, viewProjection.M33, viewProjection.M43);
        Vector4 column3 = new(viewProjection.M14, viewProjection.M24, viewProjection.M34, viewProjection.M44);

        Left = CreatePlane(column0 + column3);
        Right = CreatePlane(column3 - column0);
        Bottom = CreatePlane(column1 + column3);
        Top = CreatePlane(column3 - column1);
        Near = CreatePlane(column2);
        Far = CreatePlane(column3 - column2);
    }

    /// <summary>Gets the left clip plane (<c>x &gt;= -w</c>).</summary>
    public Plane Left { get; }

    /// <summary>Gets the right clip plane (<c>x &lt;= w</c>).</summary>
    public Plane Right { get; }

    /// <summary>Gets the top clip plane (<c>y &lt;= w</c> in the Y-flipped NDC).</summary>
    public Plane Top { get; }

    /// <summary>Gets the bottom clip plane (<c>y &gt;= -w</c> in the Y-flipped NDC).</summary>
    public Plane Bottom { get; }

    /// <summary>Gets the near clip plane (<c>z &gt;= 0</c> in reverse-Z).</summary>
    public Plane Near { get; }

    /// <summary>Gets the far clip plane (<c>z &lt;= w</c> in reverse-Z).</summary>
    public Plane Far { get; }

    /// <summary>
    /// Determines whether a world-space point is inside every plane.
    /// </summary>
    /// <param name="point">The world-space point.</param>
    /// <returns><see langword="true"/> when every signed plane distance is non-negative.</returns>
    public bool Contains(Vector3 point)
        => Plane.DotCoordinate(Left, point) >= 0f
            && Plane.DotCoordinate(Right, point) >= 0f
            && Plane.DotCoordinate(Bottom, point) >= 0f
            && Plane.DotCoordinate(Top, point) >= 0f
            && Plane.DotCoordinate(Near, point) >= 0f
            && Plane.DotCoordinate(Far, point) >= 0f;

    /// <summary>
    /// Determines whether an axis-aligned world-space box intersects or lies inside the frustum.
    /// </summary>
    /// <param name="bounds">The world-space box.</param>
    /// <returns>
    /// <see langword="true"/> when the box is not fully outside any plane. The test is conservative:
    /// a box that only touches the boundary counts as intersecting.
    /// </returns>
    public bool Intersects(BoundingBox bounds)
        => IntersectsPlane(Left, bounds)
            && IntersectsPlane(Right, bounds)
            && IntersectsPlane(Bottom, bounds)
            && IntersectsPlane(Top, bounds)
            && IntersectsPlane(Near, bounds)
            && IntersectsPlane(Far, bounds);

    private static bool IntersectsPlane(Plane plane, BoundingBox bounds) {
        Vector3 positiveVertex = new(
            plane.Normal.X >= 0f ? bounds.Max.X : bounds.Min.X,
            plane.Normal.Y >= 0f ? bounds.Max.Y : bounds.Min.Y,
            plane.Normal.Z >= 0f ? bounds.Max.Z : bounds.Min.Z);
        return Plane.DotCoordinate(plane, positiveVertex) >= 0f;
    }

    private static Plane CreatePlane(Vector4 coefficients) {
        Vector3 normal = new(coefficients.X, coefficients.Y, coefficients.Z);
        float length = normal.Length();
        if (length > 0f && float.IsFinite(length)) {
            return new Plane(normal / length, coefficients.W / length);
        }

        return new Plane(normal, coefficients.W);
    }
}
