using System.Numerics;

namespace RayBork;

/// <summary>
/// The result of a ray collision query.
/// </summary>
/// <remarks>
/// A miss is <c>default(RayCollision)</c> with every member zero. <see cref="Distance"/> is the
/// world-space ray parameter along the unit <see cref="Ray.Direction"/>.
/// </remarks>
public readonly struct RayCollision
{
    /// <summary>Gets whether the ray hit the shape.</summary>
    public bool Hit { get; }

    /// <summary>Gets the ray parameter of the nearest hit, a world-space distance.</summary>
    public float Distance { get; }

    /// <summary>Gets the hit point in the space of the query.</summary>
    public Vector3 Point { get; }

    /// <summary>Gets the normalized surface normal at the hit point.</summary>
    public Vector3 Normal { get; }

    /// <summary>Initializes a hit result; misses use <c>default(RayCollision)</c>.</summary>
    internal RayCollision(float distance, Vector3 point, Vector3 normal)
    {
        Hit = true;
        Distance = distance;
        Point = point;
        Normal = normal;
    }
}
