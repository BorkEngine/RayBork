using System.Numerics;

namespace RayBork;

/// <summary>
/// Unity-style extensions for <see cref="Quaternion"/>.
/// </summary>
/// <remarks>
/// Rotations use the right-handed, Y-up System.Numerics convention: forward is <c>-Z</c>.
/// Angle arguments and results are in degrees.
/// </remarks>
public static class QuaternionExtensions {
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RADIANS_TO_DEGREES = 180f / MathF.PI;
    private const float MINIMUM_LENGTH_SQUARED = 1e-6f;
    private const float MINIMUM_SIN_HALF_ANGLE = 1e-6f;
    private const float OPPOSITE_DOT_THRESHOLD = 1e-6f;
    private const float GIMBAL_LOCK_THRESHOLD = 1f - 1e-6f;
    private const float LINEAR_SLERP_THRESHOLD = 1f - 1e-5f;

    extension(Quaternion) {
        /// <summary>
        /// Creates a rotation from Euler angles in degrees.
        /// </summary>
        /// <param name="x">The angle around the X axis, in degrees.</param>
        /// <param name="y">The angle around the Y axis, in degrees.</param>
        /// <param name="z">The angle around the Z axis, in degrees.</param>
        /// <returns>The composed rotation.</returns>
        public static Quaternion Euler(float x, float y, float z) =>
            Quaternion.CreateFromYawPitchRoll(y * DEGREES_TO_RADIANS, x * DEGREES_TO_RADIANS, z * DEGREES_TO_RADIANS);

        /// <summary>
        /// Creates a rotation of a given angle around an axis.
        /// </summary>
        /// <param name="degrees">The clockwise angle in degrees around the axis.</param>
        /// <param name="axis">The rotation axis. It must not be zero.</param>
        /// <returns>The normalized rotation.</returns>
        /// <exception cref="ArgumentException"><paramref name="axis"/> has zero length.</exception>
        public static Quaternion AngleAxis(float degrees, Vector3 axis) {
            float lengthSquared = axis.LengthSquared();

            if (lengthSquared == 0f) {
                throw new ArgumentException("The rotation axis must not be zero.", nameof(axis));
            }

            return Quaternion.Normalize(
                Quaternion.CreateFromAxisAngle(axis / MathF.Sqrt(lengthSquared), degrees * DEGREES_TO_RADIANS));
        }

        /// <summary>
        /// Creates a rotation that looks along a forward direction with a given up direction.
        /// </summary>
        /// <param name="forward">The forward direction. It must not be zero.</param>
        /// <param name="upwards">The up direction. It must not be zero or parallel to the forward direction.</param>
        /// <returns>The normalized rotation whose forward axis is <paramref name="forward"/>.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="forward"/> or <paramref name="upwards"/> has zero length, or the two
        /// directions are parallel.
        /// </exception>
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) {
            float forwardLengthSquared = forward.LengthSquared();
            float upwardsLengthSquared = upwards.LengthSquared();

            if (forwardLengthSquared == 0f) {
                throw new ArgumentException("The forward vector must not be zero.", nameof(forward));
            }

            if (upwardsLengthSquared == 0f) {
                throw new ArgumentException("The upwards vector must not be zero.", nameof(upwards));
            }

            Vector3 forwardDirection = forward / MathF.Sqrt(forwardLengthSquared);
            Vector3 upwardsDirection = upwards / MathF.Sqrt(upwardsLengthSquared);
            Vector3 right = Vector3.Cross(forwardDirection, upwardsDirection);
            float rightLengthSquared = right.LengthSquared();

            if (rightLengthSquared < MINIMUM_LENGTH_SQUARED) {
                throw new ArgumentException(
                    "The forward and upwards vectors must not be parallel.",
                    nameof(upwards));
            }

            right /= MathF.Sqrt(rightLengthSquared);
            Vector3 up = Vector3.Cross(right, forwardDirection);

            Matrix4x4 rotationMatrix = new(
                right.X, right.Y, right.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                -forwardDirection.X, -forwardDirection.Y, -forwardDirection.Z, 0f,
                0f, 0f, 0f, 1f);

            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix));
        }

        /// <summary>
        /// Creates the shortest rotation that maps one direction to another.
        /// </summary>
        /// <param name="from">The starting direction. It must not be zero.</param>
        /// <param name="to">The target direction. It must not be zero.</param>
        /// <returns>The normalized rotation that maps <paramref name="from"/> to <paramref name="to"/>.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="from"/> or <paramref name="to"/> has zero length.
        /// </exception>
        public static Quaternion FromToRotation(Vector3 from, Vector3 to) {
            float fromLengthSquared = from.LengthSquared();
            float toLengthSquared = to.LengthSquared();

            if (fromLengthSquared == 0f) {
                throw new ArgumentException("The from vector must not be zero.", nameof(from));
            }

            if (toLengthSquared == 0f) {
                throw new ArgumentException("The to vector must not be zero.", nameof(to));
            }

            Vector3 fromDirection = from / MathF.Sqrt(fromLengthSquared);
            Vector3 toDirection = to / MathF.Sqrt(toLengthSquared);
            float dot = Math.Clamp(Vector3.Dot(fromDirection, toDirection), -1f, 1f);

            if (dot > 1f - OPPOSITE_DOT_THRESHOLD) {
                return Quaternion.Identity;
            }

            if (dot < -1f + OPPOSITE_DOT_THRESHOLD) {
                Vector3 perpendicularAxis = Vector3.Cross(Vector3.UnitX, fromDirection);

                if (perpendicularAxis.LengthSquared() < MINIMUM_LENGTH_SQUARED) {
                    perpendicularAxis = Vector3.Cross(Vector3.UnitY, fromDirection);
                }

                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(perpendicularAxis), MathF.PI);
            }

            Vector3 axis = Vector3.Normalize(Vector3.Cross(fromDirection, toDirection));
            return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
        }
    }

    extension(Quaternion value) {
        /// <summary>
        /// Gets the quaternion scaled to unit length.
        /// </summary>
        public Quaternion Normalized => Quaternion.Normalize(value);

        /// <summary>
        /// Gets the Euler angles in degrees, ordered as X, Y, and Z.
        /// </summary>
        public Vector3 EulerAngles {
            get {
                if (value.LengthSquared() == 0f) {
                    return Vector3.Zero;
                }

                Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(value));
                float sinPitch = Math.Clamp(-matrix.M32, -1f, 1f);
                float pitch = MathF.Asin(sinPitch);
                float yaw;
                float roll;

                if (MathF.Abs(sinPitch) > GIMBAL_LOCK_THRESHOLD) {
                    yaw = MathF.Atan2(-matrix.M13, matrix.M11);
                    roll = 0f;
                } else {
                    yaw = MathF.Atan2(matrix.M31, matrix.M33);
                    roll = MathF.Atan2(matrix.M12, matrix.M22);
                }

                return new Vector3(
                    NormalizeDegrees(pitch * RADIANS_TO_DEGREES),
                    NormalizeDegrees(yaw * RADIANS_TO_DEGREES),
                    NormalizeDegrees(roll * RADIANS_TO_DEGREES));
            }
        }

        /// <summary>
        /// Returns the unsigned angle between two rotations, in degrees.
        /// </summary>
        /// <param name="other">The other rotation.</param>
        /// <returns>The angle in degrees, in the range <c>[0, 180]</c>.</returns>
        public float Angle(Quaternion other) {
            float dot = Quaternion.Dot(NormalizeOrIdentity(value), NormalizeOrIdentity(other));
            return MathF.Acos(Math.Clamp(MathF.Abs(dot), 0f, 1f)) * 2f * RADIANS_TO_DEGREES;
        }

        /// <summary>
        /// Decomposes the rotation into an angle in degrees and a rotation axis.
        /// </summary>
        /// <param name="angle">The resulting angle in degrees, in the range <c>[0, 360]</c>.</param>
        /// <param name="axis">
        /// The resulting normalized rotation axis, or <c>(1, 0, 0)</c> when the rotation is the
        /// identity.
        /// </param>
        public void ToAngleAxis(out float angle, out Vector3 axis) {
            Quaternion normalized = NormalizeOrIdentity(value);
            float sinHalfAngle = MathF.Sqrt(MathF.Max(0f, 1f - (normalized.W * normalized.W)));

            angle = 2f * MathF.Acos(Math.Clamp(normalized.W, -1f, 1f)) * RADIANS_TO_DEGREES;

            if (sinHalfAngle < MINIMUM_SIN_HALF_ANGLE) {
                axis = Vector3.UnitX;
                return;
            }

            axis = new Vector3(normalized.X, normalized.Y, normalized.Z) / sinHalfAngle;
        }

        /// <summary>
        /// Rotates towards a target rotation with a maximum angular step.
        /// </summary>
        /// <param name="target">The target rotation.</param>
        /// <param name="maxDegreesDelta">The maximum rotation in degrees for this call.</param>
        /// <returns>
        /// The rotation that is at most <paramref name="maxDegreesDelta"/> degrees from this rotation
        /// in the direction of <paramref name="target"/>, or the target when it is closer.
        /// </returns>
        public Quaternion RotateTowards(Quaternion target, float maxDegreesDelta) {
            float angle = Angle(value, target);

            if (angle <= 0f) {
                return NormalizeOrIdentity(target);
            }

            return Slerp(value, target, MathF.Min(1f, maxDegreesDelta / angle));
        }

        /// <summary>
        /// Spherically interpolates towards a target rotation, clamping the interpolation factor.
        /// </summary>
        /// <param name="target">The target rotation.</param>
        /// <param name="t">The interpolation factor, clamped to <c>[0, 1]</c>.</param>
        /// <returns>The interpolated rotation.</returns>
        public Quaternion Slerp(Quaternion target, float t) {
            t = Math.Clamp(t, 0f, 1f);

            Quaternion from = NormalizeOrIdentity(value);
            Quaternion to = NormalizeOrIdentity(target);
            float dot = Quaternion.Dot(from, to);

            if (dot < 0f) {
                to = Quaternion.Negate(to);
                dot = -dot;
            }

            if (dot > LINEAR_SLERP_THRESHOLD) {
                return Quaternion.Normalize(Quaternion.Lerp(from, to, t));
            }

            float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f));
            float sinAngle = MathF.Sin(angle);
            float fromWeight = MathF.Sin((1f - t) * angle) / sinAngle;
            float toWeight = MathF.Sin(t * angle) / sinAngle;

            return new Quaternion(
                (from.X * fromWeight) + (to.X * toWeight),
                (from.Y * fromWeight) + (to.Y * toWeight),
                (from.Z * fromWeight) + (to.Z * toWeight),
                (from.W * fromWeight) + (to.W * toWeight));
        }
    }

    private static Quaternion NormalizeOrIdentity(Quaternion rotation) =>
        rotation.LengthSquared() == 0f ? Quaternion.Identity : Quaternion.Normalize(rotation);

    private static float NormalizeDegrees(float degrees) => ((degrees % 360f) + 360f) % 360f;
}
