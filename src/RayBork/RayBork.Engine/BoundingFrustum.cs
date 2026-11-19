using System.Numerics;

namespace RayBork;

/// <summary>
/// Six inward-facing clip planes from a view-projection matrix in reverse-Z Vulkan clip space.
/// </summary>
/// <remarks>
/// Planes are normalized, so <see cref="Plane.DotCoordinate(Plane, Vector3)"/> returns a
/// world-space signed distance and boundary geometry counts as inside. Vulkan NDC points +Y down,
/// so <see cref="Top"/> and <see cref="Bottom"/> are inverted clip planes, and a default instance
/// has six zero planes that accept every point.
/// </remarks>
public readonly struct BoundingFrustum
{
    /// <summary>The plane slack <see cref="Intersects"/> allows for floating-point drift.</summary>
    private const float PLANE_TOLERANCE = 1e-4f;

    /// <summary>Extracts six normalized planes from a row-vector view-projection matrix.</summary>
    public BoundingFrustum(Matrix4x4 viewProjection)
    {
        Vector4 column0 = new(
            viewProjection.M11,
            viewProjection.M21,
            viewProjection.M31,
            viewProjection.M41
        );
        Vector4 column1 = new(
            viewProjection.M12,
            viewProjection.M22,
            viewProjection.M32,
            viewProjection.M42
        );
        Vector4 column2 = new(
            viewProjection.M13,
            viewProjection.M23,
            viewProjection.M33,
            viewProjection.M43
        );
        Vector4 column3 = new(
            viewProjection.M14,
            viewProjection.M24,
            viewProjection.M34,
            viewProjection.M44
        );

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
    /// Determines whether a world-space point is inside or on every plane.
    /// </summary>
    public bool Contains(Vector3 point) =>
        Plane.DotCoordinate(Left, point) >= 0f
        && Plane.DotCoordinate(Right, point) >= 0f
        && Plane.DotCoordinate(Bottom, point) >= 0f
        && Plane.DotCoordinate(Top, point) >= 0f
        && Plane.DotCoordinate(Near, point) >= 0f
        && Plane.DotCoordinate(Far, point) >= 0f;

    /// <summary>
    /// Determines whether a world-space box intersects the frustum.
    /// </summary>
    /// <remarks>
    /// The test is conservative: a box touching the boundary counts as intersecting, and a small
    /// tolerance keeps boundary-aligned geometry from being culled by plane-extraction rounding.
    /// </remarks>
    public bool Intersects(BoundingBox bounds) =>
        IntersectsPlane(Left, bounds)
        && IntersectsPlane(Right, bounds)
        && IntersectsPlane(Bottom, bounds)
        && IntersectsPlane(Top, bounds)
        && IntersectsPlane(Near, bounds)
        && IntersectsPlane(Far, bounds);

    private static bool IntersectsPlane(Plane plane, BoundingBox bounds)
    {
        Vector3 positiveVertex = new(
            plane.Normal.X >= 0f ? bounds.Max.X : bounds.Min.X,
            plane.Normal.Y >= 0f ? bounds.Max.Y : bounds.Min.Y,
            plane.Normal.Z >= 0f ? bounds.Max.Z : bounds.Min.Z
        );
        // Accept a positive corner that is only within tolerance behind the plane: boundary-aligned
        // geometry must not flicker out of the frame because of plane-extraction rounding.
        return Plane.DotCoordinate(plane, positiveVertex) >= -PLANE_TOLERANCE;
    }

    private static Plane CreatePlane(Vector4 coefficients)
    {
        Vector3 normal = new(coefficients.X, coefficients.Y, coefficients.Z);
        float length = normal.Length();
        if (length > 0f && float.IsFinite(length))
        {
            return new Plane(normal / length, coefficients.W / length);
        }

        return new Plane(normal, coefficients.W);
    }
}
