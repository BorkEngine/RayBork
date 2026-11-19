using System.Numerics;

namespace RayBork.Internal;

/// <summary>Projection, viewport, and validation math shared by the camera types.</summary>
/// <remarks>
/// Right-handed, Y-up, looking down local -Z; row-vector matrices compose as
/// <c>View * Projection</c>. Depth is reverse-Z <c>[0, 1]</c>: near maps to <c>1</c>, far to
/// <c>0</c>, and the projections negate Y so world <c>+Y</c> is the Vulkan top.
/// </remarks>
internal static class CameraMath
{
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float MAXIMUM_FIELD_OF_VIEW = 180f;

    /// <summary>Inverts the transform into the world-to-view matrix.</summary>
    /// <exception cref="InvalidOperationException">The transform matrix is singular.</exception>
    internal static Matrix4x4 CreateViewMatrix(Transform transform)
    {
        if (!Matrix4x4.Invert(transform.ToMatrix(), out Matrix4x4 view))
        {
            throw new InvalidOperationException(
                "The camera transform is singular and cannot be inverted. A zero scale component is "
                    + "not a valid camera transform."
            );
        }

        return view;
    }

    /// <summary>Builds a reverse-Z perspective projection with the Vulkan Y-flip.</summary>
    /// <param name="fieldOfView">The vertical field of view in degrees, inside (0, 180).</param>
    /// <param name="aspectRatio">The viewport width divided by its height.</param>
    /// <param name="near">The positive finite near distance.</param>
    /// <param name="far">The far distance, finite or <see cref="float.PositiveInfinity"/>.</param>
    internal static Matrix4x4 CreatePerspectiveProjection(
        float fieldOfView,
        float aspectRatio,
        float near,
        float far
    )
    {
        float f = 1f / MathF.Tan(fieldOfView * DEGREES_TO_RADIANS * 0.5f);
        bool infiniteFar = float.IsPositiveInfinity(far);
        float a = infiniteFar ? 0f : near / (far - near);
        float b = infiniteFar ? near : near * far / (far - near);
        return new Matrix4x4(
            f / aspectRatio,
            0f,
            0f,
            0f,
            0f,
            -f,
            0f,
            0f,
            0f,
            0f,
            a,
            -1f,
            0f,
            0f,
            b,
            0f
        );
    }

    /// <summary>Builds a reverse-Z orthographic projection with the Vulkan Y-flip.</summary>
    /// <param name="height">The visible world-space height.</param>
    /// <param name="aspectRatio">The viewport width divided by its height.</param>
    /// <param name="near">The positive finite near distance.</param>
    /// <param name="far">The finite far distance, greater than <paramref name="near"/>.</param>
    internal static Matrix4x4 CreateOrthographicProjection(
        float height,
        float aspectRatio,
        float near,
        float far
    )
    {
        float width = height * aspectRatio;
        float a = 1f / (far - near);
        float b = far / (far - near);
        return new Matrix4x4(
            2f / width,
            0f,
            0f,
            0f,
            0f,
            -2f / height,
            0f,
            0f,
            0f,
            0f,
            a,
            0f,
            0f,
            0f,
            b,
            1f
        );
    }

    /// <summary>Validates the projection and rejects a nonzero unused size argument.</summary>
    /// <exception cref="ArgumentException">The unused size argument is nonzero.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The projection is undefined or a selected
    /// parameter is out of range.</exception>
    internal static void ValidateProjection(
        CameraProjection projection,
        float fieldOfView,
        float orthographicHeight,
        float near,
        float far
    )
    {
        switch (projection)
        {
            case CameraProjection.Perspective:
                if (orthographicHeight != 0f)
                {
                    throw new ArgumentException(
                        "A perspective camera uses fieldOfView and requires orthographicHeight to be 0.",
                        nameof(orthographicHeight)
                    );
                }

                ValidatePerspective(fieldOfView, near, far);
                break;
            case CameraProjection.Orthographic:
                if (fieldOfView != 0f)
                {
                    throw new ArgumentException(
                        "An orthographic camera uses orthographicHeight and requires fieldOfView to be 0.",
                        nameof(fieldOfView)
                    );
                }

                ValidateOrthographic(orthographicHeight, near, far);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(projection),
                    projection,
                    "The projection is not a defined value."
                );
        }
    }

    /// <summary>Validates a finite field of view in (0, 180) and the clip planes.</summary>
    internal static void ValidatePerspective(float fieldOfView, float near, float far)
    {
        if (
            !float.IsFinite(fieldOfView)
            || fieldOfView <= 0f
            || fieldOfView >= MAXIMUM_FIELD_OF_VIEW
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(fieldOfView),
                fieldOfView,
                $"The vertical field of view must be finite and inside (0, {MAXIMUM_FIELD_OF_VIEW}) degrees."
            );
        }

        ValidateClipPlanes(near, far, allowInfiniteFar: true);
    }

    /// <summary>Validates a finite positive height and the finite clip planes.</summary>
    internal static void ValidateOrthographic(float orthographicHeight, float near, float far)
    {
        if (!float.IsFinite(orthographicHeight) || orthographicHeight <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(orthographicHeight),
                orthographicHeight,
                "The orthographic height must be finite and greater than zero."
            );
        }

        ValidateClipPlanes(near, far, allowInfiniteFar: false);
    }

    /// <summary>Resolves the viewport to a target-clamped, positive pixel rectangle.</summary>
    /// <param name="viewport">Requested viewport, or <see langword="null"/> for the full target.</param>
    /// <param name="targetWidth">The target render width in pixels.</param>
    /// <param name="targetHeight">The target render height in pixels.</param>
    /// <param name="parameterName">The public parameter name used by thrown exceptions.</param>
    /// <exception cref="ArgumentException">The target or viewport is empty, non-finite, or
    /// clamps to nothing.</exception>
    internal static Rect ResolveViewport(
        Rect? viewport,
        int targetWidth,
        int targetHeight,
        string parameterName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException(
                $"The camera target is {targetWidth}x{targetHeight} pixels; a camera needs a non-empty "
                    + "render target.",
                parameterName
            );
        }

        if (viewport is null)
        {
            return new Rect(0f, 0f, targetWidth, targetHeight);
        }

        Rect requested = viewport.Value;
        if (
            !float.IsFinite(requested.X)
            || !float.IsFinite(requested.Y)
            || !float.IsFinite(requested.Width)
            || !float.IsFinite(requested.Height)
        )
        {
            throw new ArgumentException(
                "The viewport components must all be finite.",
                parameterName
            );
        }

        if (requested.Width <= 0f || requested.Height <= 0f)
        {
            throw new ArgumentException(
                "The viewport width and height must both be greater than zero.",
                parameterName
            );
        }

        float left = Math.Clamp(requested.X, 0f, targetWidth);
        float top = Math.Clamp(requested.Y, 0f, targetHeight);
        float right = Math.Clamp(requested.X + requested.Width, 0f, targetWidth);
        float bottom = Math.Clamp(requested.Y + requested.Height, 0f, targetHeight);
        if (right <= left || bottom <= top)
        {
            throw new ArgumentException(
                "The viewport is outside the target and clamps to an empty rectangle.",
                parameterName
            );
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Resolves the linear clear color, or opaque black for <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    internal static Color ResolveClearColor(Color? clearColor, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        if (clearColor is not { } color)
        {
            return Color.Black;
        }

        if (
            !float.IsFinite(color.R)
            || !float.IsFinite(color.G)
            || !float.IsFinite(color.B)
            || !float.IsFinite(color.A)
        )
        {
            throw new ArgumentException(
                "The clear color components must all be finite.",
                parameterName
            );
        }

        return color;
    }

    private static void ValidateClipPlanes(float near, float far, bool allowInfiniteFar)
    {
        if (!float.IsFinite(near) || near <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(near),
                near,
                "The near clip distance must be finite and greater than zero."
            );
        }

        if (float.IsPositiveInfinity(far) && allowInfiniteFar)
        {
            return;
        }

        if (!float.IsFinite(far) || far <= near)
        {
            throw new ArgumentOutOfRangeException(
                nameof(far),
                far,
                allowInfiniteFar
                    ? "The far clip distance must be finite and greater than near, or positive infinity."
                    : "The far clip distance must be finite and greater than near for an orthographic camera."
            );
        }
    }
}
