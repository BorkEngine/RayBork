using System.Numerics;

namespace RayBork;

/// <summary>
/// The result of a ray collision query.
/// </summary>
/// <remarks>
/// A miss is <c>default(RayCollision)</c>: <see cref="Hit"/> is <c>false</c> and the other members
/// are zero. <see cref="Distance"/> is the ray parameter <c>t</c> in
/// <c>origin + direction * t</c>; it is a world distance only when the ray direction is normalized.
/// </remarks>
public readonly struct RayCollision {
    /// <summary>
    /// Gets whether the ray hit the shape.
    /// </summary>
    public bool Hit { get; }

    /// <summary>
    /// Gets the ray parameter of the nearest hit.
    /// </summary>
    public float Distance { get; }

    /// <summary>
    /// Gets the hit point in the space of the query.
    /// </summary>
    public Vector3 Point { get; }

    /// <summary>
    /// Gets the normalized surface normal at the hit point.
    /// </summary>
    public Vector3 Normal { get; }

    /// <summary>
    /// Initializes a hit result. Misses use <c>default(RayCollision)</c> instead.
    /// </summary>
    /// <param name="distance">The ray parameter of the hit.</param>
    /// <param name="point">The hit point.</param>
    /// <param name="normal">The normalized surface normal at the hit point.</param>
    internal RayCollision(float distance, Vector3 point, Vector3 normal) {
        Hit = true;
        Distance = distance;
        Point = point;
        Normal = normal;
    }
}
