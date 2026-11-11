using System.Numerics;

namespace RayBork;

/// <summary>
/// An axis-aligned bounding box in mesh or model space.
/// </summary>
/// <remarks>
/// <para>
/// The engine defines this value type because the .NET base class library does not provide a
/// bounding-box type. <see cref="Min"/> and <see cref="Max"/> are the opposite corners along the
/// world axes, so <c>Min.X &lt;= Max.X</c>, <c>Min.Y &lt;= Max.Y</c>, and
/// <c>Min.Z &lt;= Max.Z</c>.
/// </para>
/// <para>
/// M3 uses it for <see cref="Mesh.Bounds"/>. Collision helpers and model bounds arrive with M4 and
/// M5; this type carries only the two corners.
/// </para>
/// </remarks>
/// <param name="Min">The corner with the smallest X, Y, and Z values.</param>
/// <param name="Max">The corner with the largest X, Y, and Z values.</param>
public readonly record struct BoundingBox(Vector3 Min, Vector3 Max);
