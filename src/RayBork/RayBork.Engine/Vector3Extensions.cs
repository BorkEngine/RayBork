using System.Numerics;

namespace RayBork;

/// <summary>
/// Unity-style extensions for <see cref="Vector3"/>.
/// </summary>
/// <remarks>
/// Angle arguments and results are in degrees. Positions and directions use the right-handed,
/// Y-up System.Numerics convention: forward is <c>-Z</c>.
/// </remarks>
public static class Vector3Extensions {
    private const float RADIANS_TO_DEGREES = 180f / MathF.PI;
    private const float MINIMUM_ANGLE_DENOMINATOR = 1e-15f;

    extension(Vector3 value) {
        /// <summary>
        /// Gets the length of the vector.
        /// </summary>
        public float Magnitude => value.Length();

        /// <summary>
        /// Gets the squared length of the vector.
        /// </summary>
        public float SqrMagnitude => value.LengthSquared();

        /// <summary>
        /// Gets the vector scaled to unit length.
        /// </summary>
        public Vector3 Normalized => Vector3.Normalize(value);

        /// <summary>
        /// Returns a copy of the vector with a different X component.
        /// </summary>
        /// <param name="x">The new X component.</param>
        /// <returns>The updated vector.</returns>
        public Vector3 WithX(float x) => new(x, value.Y, value.Z);

        /// <summary>
        /// Returns a copy of the vector with a different Y component.
        /// </summary>
        /// <param name="y">The new Y component.</param>
        /// <returns>The updated vector.</returns>
        public Vector3 WithY(float y) => new(value.X, y, value.Z);

        /// <summary>
        /// Returns a copy of the vector with a different Z component.
        /// </summary>
        /// <param name="z">The new Z component.</param>
        /// <returns>The updated vector.</returns>
        public Vector3 WithZ(float z) => new(value.X, value.Y, z);

        /// <summary>
        /// Gets the vector with its Y component set to zero.
        /// </summary>
        public Vector3 Xz => new(value.X, 0f, value.Z);

        /// <summary>
        /// Returns the unsigned angle between this vector and another vector, in degrees.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <returns>The angle in degrees, or zero when either vector has zero length.</returns>
        public float Angle(Vector3 other) {
            float denominator = MathF.Sqrt(value.LengthSquared() * other.LengthSquared());

            if (denominator < MINIMUM_ANGLE_DENOMINATOR) {
                return 0f;
            }

            float cosine = Math.Clamp(Vector3.Dot(value, other) / denominator, -1f, 1f);
            return MathF.Acos(cosine) * RADIANS_TO_DEGREES;
        }

        /// <summary>
        /// Returns the signed angle from this vector to another vector, in degrees.
        /// </summary>
        /// <param name="other">The other vector.</param>
        /// <param name="axis">The axis whose direction determines the sign of the angle.</param>
        /// <returns>
        /// The angle in degrees. It is positive when the cross product of this vector and
        /// <paramref name="other"/> points along <paramref name="axis"/>, and negative otherwise.
        /// </returns>
        public float SignedAngle(Vector3 other, Vector3 axis) {
            float angle = Angle(value, other);
            float sign = Vector3.Dot(axis, Vector3.Cross(value, other)) < 0f ? -1f : 1f;
            return angle * sign;
        }

        /// <summary>
        /// Projects the vector onto a normal.
        /// </summary>
        /// <param name="normal">The normal to project onto. It must not be zero.</param>
        /// <returns>The projection.</returns>
        /// <exception cref="ArgumentException"><paramref name="normal"/> has zero length.</exception>
        public Vector3 Project(Vector3 normal) {
            float sqrMagnitude = normal.LengthSquared();

            if (sqrMagnitude == 0f) {
                throw new ArgumentException("The normal vector must not be zero.", nameof(normal));
            }

            return normal * (Vector3.Dot(value, normal) / sqrMagnitude);
        }

        /// <summary>
        /// Projects the vector onto the plane defined by a normal.
        /// </summary>
        /// <param name="normal">The plane normal. It must not be zero.</param>
        /// <returns>The projection onto the plane.</returns>
        /// <exception cref="ArgumentException"><paramref name="normal"/> has zero length.</exception>
        public Vector3 ProjectOnPlane(Vector3 normal) => value - value.Project(normal);

        /// <summary>
        /// Reflects the vector off a plane defined by a normal.
        /// </summary>
        /// <param name="normal">The plane normal. It must be normalized and must not be zero.</param>
        /// <returns>The reflected vector.</returns>
        /// <exception cref="ArgumentException"><paramref name="normal"/> has zero length.</exception>
        public Vector3 Reflect(Vector3 normal) {
            if (normal.LengthSquared() == 0f) {
                throw new ArgumentException("The normal vector must not be zero.", nameof(normal));
            }

            return value - (2f * Vector3.Dot(normal, value) * normal);
        }

        /// <summary>
        /// Clamps the length of the vector to a maximum.
        /// </summary>
        /// <param name="maxLength">The maximum length.</param>
        /// <returns>
        /// The vector unchanged when its length is at most <paramref name="maxLength"/>; otherwise a
        /// vector with the same direction and the given maximum length.
        /// </returns>
        public Vector3 ClampMagnitude(float maxLength) {
            float sqrMagnitude = value.LengthSquared();

            if (sqrMagnitude > maxLength * maxLength) {
                float magnitude = MathF.Sqrt(sqrMagnitude);
                return value * (maxLength / magnitude);
            }

            return value;
        }

        /// <summary>
        /// Rotates the vector by a quaternion.
        /// </summary>
        /// <param name="rotation">The rotation.</param>
        /// <returns>The rotated vector.</returns>
        public Vector3 Rotated(Quaternion rotation) => Vector3.Transform(value, rotation);
    }

    extension(Vector3) {
        /// <summary>
        /// Gets the up direction <c>(0, 1, 0)</c>.
        /// </summary>
        public static Vector3 Up => Vector3.UnitY;

        /// <summary>
        /// Gets the down direction <c>(0, -1, 0)</c>.
        /// </summary>
        public static Vector3 Down => -Vector3.UnitY;

        /// <summary>
        /// Gets the left direction <c>(-1, 0, 0)</c>.
        /// </summary>
        public static Vector3 Left => -Vector3.UnitX;

        /// <summary>
        /// Gets the right direction <c>(1, 0, 0)</c>.
        /// </summary>
        public static Vector3 Right => Vector3.UnitX;

        /// <summary>
        /// Gets the forward direction <c>(0, 0, -1)</c>.
        /// </summary>
        public static Vector3 Forward => -Vector3.UnitZ;

        /// <summary>
        /// Gets the backward direction <c>(0, 0, 1)</c>.
        /// </summary>
        public static Vector3 Back => Vector3.UnitZ;
    }
}
