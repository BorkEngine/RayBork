using System.Numerics;

namespace RayBork;

/// <summary>
/// A world-space ray: a finite origin and a unit direction.
/// </summary>
/// <remarks>
/// <para>
/// The constructor validates a finite <see cref="Origin"/> and a finite, non-zero direction and
/// stores <see cref="Direction"/> normalized to unit length. Assigning <see cref="Origin"/> or
/// <see cref="Direction"/> through an <c>init</c> accessor, as an object initializer or a <c>with</c>
/// expression does, applies the same validation and normalization. <c>default(Ray)</c> and an object
/// initializer that never assigns <see cref="Direction"/> leave a zero direction, which is not a
/// valid ray.
/// </para>
/// <para>
/// The collision queries report a miss for a ray with a zero direction, and
/// <see cref="Camera.DrawRay"/> records nothing. Use <see cref="Camera.ScreenToWorldRay"/> to build
/// a ray from a render-pixel position.
/// </para>
/// </remarks>
public readonly record struct Ray {
    private readonly Vector3 _origin;
    private readonly Vector3 _direction;

    /// <summary>
    /// Initializes a ray with a finite origin and a normalized direction.
    /// </summary>
    /// <param name="origin">The finite world-space start point.</param>
    /// <param name="direction">The finite, non-zero world-space direction.</param>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="origin"/> is not finite, or <paramref name="direction"/> is not
    /// finite or is zero.
    /// </exception>
    public Ray(Vector3 origin, Vector3 direction) {
        _origin = ValidateOrigin(origin, nameof(origin));
        _direction = NormalizeDirection(direction, nameof(direction));
    }

    /// <summary>Gets the finite world-space start point.</summary>
    /// <exception cref="ArgumentException">The assigned origin has a non-finite component.</exception>
    public Vector3 Origin {
        get => _origin;
        init => _origin = ValidateOrigin(value, nameof(Origin));
    }

    /// <summary>Gets the unit world-space direction.</summary>
    /// <exception cref="ArgumentException">The assigned direction is non-finite or zero.</exception>
    public Vector3 Direction {
        get => _direction;
        init => _direction = NormalizeDirection(value, nameof(Direction));
    }

    /// <summary>Deconstructs the ray into its origin and unit direction.</summary>
    /// <param name="origin">The finite world-space start point.</param>
    /// <param name="direction">The unit world-space direction.</param>
    public void Deconstruct(out Vector3 origin, out Vector3 direction) {
        origin = _origin;
        direction = _direction;
    }

    private static Vector3 ValidateOrigin(Vector3 origin, string parameterName) {
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z)) {
            throw new ArgumentException("The ray origin must be finite.", parameterName);
        }

        return origin;
    }

    private static Vector3 NormalizeDirection(Vector3 direction, string parameterName) {
        // Measure in double so a large finite direction cannot overflow to infinity before the divide.
        double length = Math.Sqrt(
            ((double)direction.X * direction.X)
            + ((double)direction.Y * direction.Y)
            + ((double)direction.Z * direction.Z));
        if (!(length > 0d) || !double.IsFinite(length)) {
            throw new ArgumentException("The ray direction must be finite and non-zero.", parameterName);
        }

        return new Vector3(
            (float)(direction.X / length),
            (float)(direction.Y / length),
            (float)(direction.Z / length));
    }
}
