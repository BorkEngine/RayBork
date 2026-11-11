using System.Numerics;

namespace RayBork;

/// <summary>
/// A mutable world-space translation, rotation, and scale.
/// </summary>
/// <remarks>
/// Positions and directions use the right-handed, Y-up System.Numerics convention: forward is
/// <c>-Z</c>. Matrices use the row-vector order <c>Scale * Rotation * Translation</c>.
/// </remarks>
public struct Transform {
    private const float MINIMUM_SCALE_LENGTH = 1e-8f;
    private const float SHEAR_TOLERANCE = 1e-4f;

    /// <summary>
    /// Initializes a new transform at the origin with the given rotation and uniform scale.
    /// </summary>
    /// <param name="position">The world-space position.</param>
    /// <param name="rotation">
    /// The rotation. It is normalized; a zero-length quaternion becomes
    /// <see cref="Quaternion.Identity"/>.
    /// </param>
    /// <param name="scale">The scale on each axis.</param>
    public Transform(Vector3 position, Quaternion rotation, Vector3 scale) {
        Position = position;
        Rotation = NormalizeRotation(rotation);
        Scale = scale;
    }

    /// <summary>
    /// Initializes a new transform at the given position with identity rotation and unit scale.
    /// </summary>
    /// <param name="position">The world-space position.</param>
    public Transform(Vector3 position) {
        Position = position;
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    /// <summary>
    /// Gets the transform with no translation, no rotation, and unit scale.
    /// </summary>
    public static Transform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    /// <summary>
    /// Gets or sets the world-space position.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the rotation. Direct assignment does not normalize the quaternion; use the
    /// constructor or <see cref="WithRotation"/> to normalize it.
    /// </summary>
    public Quaternion Rotation { get; set; }

    /// <summary>
    /// Gets or sets the scale on each axis.
    /// </summary>
    public Vector3 Scale { get; set; }

    /// <summary>
    /// Gets the unit vector pointing in the transform's forward direction, which is <c>-Z</c>.
    /// </summary>
    public readonly Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    /// <summary>
    /// Gets the unit vector pointing in the transform's right direction, which is <c>+X</c>.
    /// </summary>
    public readonly Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

    /// <summary>
    /// Gets the unit vector pointing in the transform's up direction, which is <c>+Y</c>.
    /// </summary>
    public readonly Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    /// <summary>
    /// Converts the transform to a row-vector matrix using the order
    /// <c>Scale * Rotation * Translation</c>.
    /// </summary>
    /// <returns>The composed transform matrix.</returns>
    public readonly Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Treats this transform as local to a parent and returns the composed world transform.
    /// </summary>
    /// <param name="parent">The parent transform.</param>
    /// <returns>
    /// The world transform produced by composing <c>localMatrix * parentMatrix</c> and decomposing
    /// the result back into translation, rotation, and scale.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The composed matrix contains shear, which a translation, rotation, and scale cannot represent,
    /// or it is singular and cannot be decomposed.
    /// </exception>
    public readonly Transform WithParent(Transform parent) {
        Matrix4x4 world = ToMatrix() * parent.ToMatrix();

        if (!TryDecompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 translation)) {
            throw new InvalidOperationException(
                "The composed matrix contains shear or is singular and cannot be decomposed into a translation, rotation, and scale.");
        }

        return new Transform(translation, rotation, scale);
    }

    /// <summary>
    /// Returns a copy of this transform with a different position.
    /// </summary>
    /// <param name="position">The new world-space position.</param>
    /// <returns>The updated transform.</returns>
    public readonly Transform WithPosition(Vector3 position) =>
        new() { Position = position, Rotation = Rotation, Scale = Scale };

    /// <summary>
    /// Returns a copy of this transform with a different rotation.
    /// </summary>
    /// <param name="rotation">
    /// The new rotation. It is normalized; a zero-length quaternion becomes
    /// <see cref="Quaternion.Identity"/>.
    /// </param>
    /// <returns>The updated transform.</returns>
    public readonly Transform WithRotation(Quaternion rotation) => new(Position, rotation, Scale);

    /// <summary>
    /// Returns a copy of this transform with a different scale.
    /// </summary>
    /// <param name="scale">The new scale on each axis.</param>
    /// <returns>The updated transform.</returns>
    public readonly Transform WithScale(Vector3 scale) =>
        new() { Position = Position, Rotation = Rotation, Scale = scale };

    /// <summary>
    /// Transforms a point by applying scale, then rotation, then translation.
    /// </summary>
    /// <param name="point">The point in local space.</param>
    /// <returns>The transformed point in world space.</returns>
    public readonly Vector3 TransformPoint(Vector3 point) =>
        Vector3.Transform(point * Scale, Rotation) + Position;

    /// <summary>
    /// Transforms a direction by applying rotation only.
    /// </summary>
    /// <param name="direction">The direction in local space.</param>
    /// <returns>The rotated direction in world space.</returns>
    public readonly Vector3 TransformDirection(Vector3 direction) => Vector3.Transform(direction, Rotation);

    /// <summary>
    /// Computes the inverse of this transform.
    /// </summary>
    /// <returns>The inverse transform.</returns>
    /// <exception cref="InvalidOperationException">
    /// The transform matrix is singular, or its inverse contains shear and cannot be represented as
    /// a translation, rotation, and scale.
    /// </exception>
    public readonly Transform Inverse() {
        Matrix4x4 matrix = ToMatrix();

        if (!Matrix4x4.Invert(matrix, out Matrix4x4 inverse)) {
            throw new InvalidOperationException("The transform matrix is singular and cannot be inverted.");
        }

        if (!TryDecompose(inverse, out Vector3 scale, out Quaternion rotation, out Vector3 translation)) {
            throw new InvalidOperationException(
                "The inverse matrix contains shear and cannot be represented as a translation, rotation, and scale.");
        }

        return new Transform(translation, rotation, scale);
    }

    /// <summary>
    /// Linearly interpolates two transforms, slerping the rotation and clamping the interval.
    /// </summary>
    /// <param name="a">The transform at <paramref name="t"/> equal to zero.</param>
    /// <param name="b">The transform at <paramref name="t"/> equal to one.</param>
    /// <param name="t">The interpolation factor, clamped to <c>[0, 1]</c>.</param>
    /// <returns>The interpolated transform.</returns>
    public static Transform Lerp(Transform a, Transform b, float t) {
        t = Math.Clamp(t, 0f, 1f);

        return new Transform(
            Vector3.Lerp(a.Position, b.Position, t),
            Quaternion.Slerp(a.Rotation, b.Rotation, t),
            Vector3.Lerp(a.Scale, b.Scale, t));
    }

    private static Quaternion NormalizeRotation(Quaternion rotation) =>
        rotation.LengthSquared() == 0f ? Quaternion.Identity : Quaternion.Normalize(rotation);

    private static bool TryDecompose(
        Matrix4x4 matrix,
        out Vector3 scale,
        out Quaternion rotation,
        out Vector3 translation) {
        Vector3 rowX = new(matrix.M11, matrix.M12, matrix.M13);
        Vector3 rowY = new(matrix.M21, matrix.M22, matrix.M23);
        Vector3 rowZ = new(matrix.M31, matrix.M32, matrix.M33);

        float scaleX = rowX.Length();
        float scaleY = rowY.Length();
        float scaleZ = rowZ.Length();

        scale = Vector3.Zero;
        rotation = Quaternion.Identity;
        translation = new Vector3(matrix.M41, matrix.M42, matrix.M43);

        if (scaleX < MINIMUM_SCALE_LENGTH || scaleY < MINIMUM_SCALE_LENGTH || scaleZ < MINIMUM_SCALE_LENGTH) {
            return false;
        }

        rowX /= scaleX;
        rowY /= scaleY;
        rowZ /= scaleZ;

        if (Vector3.Dot(rowX, Vector3.Cross(rowY, rowZ)) < 0f) {
            scaleX = -scaleX;
            rowX = -rowX;
        }

        if (MathF.Abs(Vector3.Dot(rowX, rowY)) > SHEAR_TOLERANCE
            || MathF.Abs(Vector3.Dot(rowX, rowZ)) > SHEAR_TOLERANCE
            || MathF.Abs(Vector3.Dot(rowY, rowZ)) > SHEAR_TOLERANCE) {
            return false;
        }

        Matrix4x4 rotationMatrix = new(
            rowX.X, rowX.Y, rowX.Z, 0f,
            rowY.X, rowY.Y, rowY.Z, 0f,
            rowZ.X, rowZ.Y, rowZ.Z, 0f,
            0f, 0f, 0f, 1f);

        scale = new Vector3(scaleX, scaleY, scaleZ);
        rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix));

        return true;
    }
}
