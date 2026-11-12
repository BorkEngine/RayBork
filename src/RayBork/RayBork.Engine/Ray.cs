using System.Numerics;

namespace RayBork;

/// <summary>
/// A world-space ray: an origin and a direction.
/// </summary>
/// <remarks>
/// <see cref="Direction"/> is expected to be normalized by the code that creates the ray.
/// <see cref="Camera.ScreenToWorldRay"/> returns a normalized direction. The engine's collision helpers
/// in M5 consume this value type.
/// </remarks>
/// <param name="Origin">The world-space start point.</param>
/// <param name="Direction">The world-space unit direction.</param>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction);
