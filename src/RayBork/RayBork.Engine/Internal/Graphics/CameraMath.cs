using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The CPU projection, viewport, and validation math shared by <see cref="CameraService"/> and
/// <see cref="Camera"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine is right-handed and Y-up and the camera looks down local <c>-Z</c>. Matrices use
/// System.Numerics' row-vector convention, so a view-space position transforms as
/// <c>Vector4.Transform(position, projection)</c> and the view-projection matrix is
/// <c>View * Projection</c>.
/// </para>
/// <para>
/// Depth is Vulkan's <c>[0, 1]</c> reverse-Z: the near plane maps to <c>1</c> and the far plane maps
/// to <c>0</c>. The projection also negates the Y row so world <c>+Y</c> maps to the top of the
/// screen, which is Vulkan's negative NDC Y. The conventional column-vector form of each projection is
/// documented in the API reference; the matrices built here are its row-vector transpose.
/// </para>
/// </remarks>
internal static class CameraMath {
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float MAXIMUM_FIELD_OF_VIEW = 180f;

    /// <summary>
    /// Creates the world-to-view matrix from a camera transform.
    /// </summary>
    /// <param name="transform">The world-space camera transform.</param>
    /// <returns>The inverse of the transform matrix.</returns>
    /// <exception cref="InvalidOperationException">The transform matrix is singular.</exception>
    internal static Matrix4x4 CreateViewMatrix(Transform transform) {
        if (!Matrix4x4.Invert(transform.ToMatrix(), out Matrix4x4 view)) {
            throw new InvalidOperationException(
                "The camera transform is singular and cannot be inverted. A zero scale component is "
                    + "not a valid camera transform.");
        }

        return view;
    }

    /// <summary>
    /// Creates a reverse-Z perspective projection with the Vulkan Y-flip.
    /// </summary>
    /// <param name="fieldOfView">The vertical field of view in degrees.</param>
    /// <param name="aspectRatio">The viewport width divided by its height.</param>
    /// <param name="near">The positive finite near distance.</param>
    /// <param name="far">The far distance; <see cref="float.PositiveInfinity"/> is allowed.</param>
    /// <returns>The row-vector projection matrix.</returns>
    internal static Matrix4x4 CreatePerspectiveProjection(
        float fieldOfView,
        float aspectRatio,
        float near,
        float far) {
        float f = 1f / MathF.Tan(fieldOfView * DEGREES_TO_RADIANS * 0.5f);
        bool infiniteFar = float.IsPositiveInfinity(far);
        float a = infiniteFar ? 0f : near / (far - near);
        float b = infiniteFar ? near : near * far / (far - near);
        return new Matrix4x4(
            f / aspectRatio, 0f, 0f, 0f,
            0f, -f, 0f, 0f,
            0f, 0f, a, -1f,
            0f, 0f, b, 0f);
    }

    /// <summary>
    /// Creates a reverse-Z orthographic projection with the Vulkan Y-flip.
    /// </summary>
    /// <param name="height">The visible world-space height.</param>
    /// <param name="aspectRatio">The viewport width divided by its height.</param>
    /// <param name="near">The positive finite near distance.</param>
    /// <param name="far">The finite far distance, greater than <paramref name="near"/>.</param>
    /// <returns>The row-vector projection matrix.</returns>
    internal static Matrix4x4 CreateOrthographicProjection(
        float height,
        float aspectRatio,
        float near,
        float far) {
        float width = height * aspectRatio;
        float a = 1f / (far - near);
        float b = far / (far - near);
        return new Matrix4x4(
            2f / width, 0f, 0f, 0f,
            0f, -2f / height, 0f, 0f,
            0f, 0f, a, 0f,
            0f, 0f, b, 1f);
    }

    /// <summary>
    /// Validates the perspective camera parameters.
    /// </summary>
    /// <param name="fieldOfView">The vertical field of view in degrees.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its documented range.</exception>
    internal static void ValidatePerspective(float fieldOfView, float near, float far) {
        if (!float.IsFinite(fieldOfView) || fieldOfView <= 0f || fieldOfView >= MAXIMUM_FIELD_OF_VIEW) {
            throw new ArgumentOutOfRangeException(
                nameof(fieldOfView),
                fieldOfView,
                $"The vertical field of view must be finite and inside (0, {MAXIMUM_FIELD_OF_VIEW}) degrees.");
        }

        ValidateClipPlanes(near, far, allowInfiniteFar: true);
    }

    /// <summary>
    /// Validates the orthographic camera parameters.
    /// </summary>
    /// <param name="height">The visible world-space height.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is outside its documented range.</exception>
    internal static void ValidateOrthographic(float height, float near, float far) {
        if (!float.IsFinite(height) || height <= 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "The orthographic height must be finite and greater than zero.");
        }

        ValidateClipPlanes(near, far, allowInfiniteFar: false);
    }

    /// <summary>
    /// Resolves a requested viewport against the target render size and returns the clamped pixel
    /// rectangle.
    /// </summary>
    /// <param name="viewport">The requested viewport, or <see langword="null"/> for the full target.</param>
    /// <param name="targetWidth">The target render width in pixels.</param>
    /// <param name="targetHeight">The target render height in pixels.</param>
    /// <param name="parameterName">The public parameter name used by thrown exceptions.</param>
    /// <returns>The finite, positive, target-clamped viewport.</returns>
    /// <exception cref="ArgumentException">
    /// The target or the requested viewport is empty, has a non-finite component, or clamps to an empty
    /// rectangle.
    /// </exception>
    internal static Rect ResolveViewport(
        Rect? viewport,
        int targetWidth,
        int targetHeight,
        string parameterName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (targetWidth <= 0 || targetHeight <= 0) {
            throw new ArgumentException(
                $"The camera target is {targetWidth}x{targetHeight} pixels; a camera needs a non-empty "
                    + "render target.",
                parameterName);
        }

        if (viewport is null) {
            return new Rect(0f, 0f, targetWidth, targetHeight);
        }

        Rect requested = viewport.Value;
        if (!float.IsFinite(requested.X)
            || !float.IsFinite(requested.Y)
            || !float.IsFinite(requested.Width)
            || !float.IsFinite(requested.Height)) {
            throw new ArgumentException("The viewport components must all be finite.", parameterName);
        }

        if (requested.Width <= 0f || requested.Height <= 0f) {
            throw new ArgumentException(
                "The viewport width and height must both be greater than zero.",
                parameterName);
        }

        float left = Math.Clamp(requested.X, 0f, targetWidth);
        float top = Math.Clamp(requested.Y, 0f, targetHeight);
        float right = Math.Clamp(requested.X + requested.Width, 0f, targetWidth);
        float bottom = Math.Clamp(requested.Y + requested.Height, 0f, targetHeight);
        if (right <= left || bottom <= top) {
            throw new ArgumentException(
                "The viewport is outside the target and clamps to an empty rectangle.",
                parameterName);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Resolves the camera clear color: <see langword="null"/> means linear opaque black and every
    /// finite HDR value is accepted.
    /// </summary>
    /// <param name="clearColor">The requested color, or <see langword="null"/> for opaque black.</param>
    /// <param name="parameterName">The public parameter name used by the thrown exception.</param>
    /// <returns>The linear clear color.</returns>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    internal static Color ResolveClearColor(Color? clearColor, string parameterName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (clearColor is not { } color) {
            return Color.Black;
        }

        if (!float.IsFinite(color.R)
            || !float.IsFinite(color.G)
            || !float.IsFinite(color.B)
            || !float.IsFinite(color.A)) {
            throw new ArgumentException("The clear color components must all be finite.", parameterName);
        }

        return color;
    }

    private static void ValidateClipPlanes(float near, float far, bool allowInfiniteFar) {
        if (!float.IsFinite(near) || near <= 0f) {
            throw new ArgumentOutOfRangeException(
                nameof(near),
                near,
                "The near clip distance must be finite and greater than zero.");
        }

        if (float.IsPositiveInfinity(far) && allowInfiniteFar) {
            return;
        }

        if (!float.IsFinite(far) || far <= near) {
            throw new ArgumentOutOfRangeException(
                nameof(far),
                far,
                allowInfiniteFar
                    ? "The far clip distance must be finite and greater than near, or positive infinity."
                    : "The far clip distance must be finite and greater than near for an orthographic camera.");
        }
    }
}
