using System.Numerics;

namespace RayBork;

/// <summary>An axis-aligned bounding box in mesh or model space.</summary>
/// <remarks>
/// <see cref="Min"/> and <see cref="Max"/> are opposite corners with <c>Min &lt;= Max</c> on
/// every axis.
/// </remarks>
/// <param name="Min">The corner with the smallest X, Y, and Z values.</param>
/// <param name="Max">The corner with the largest X, Y, and Z values.</param>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max);
