namespace RayBork;

/// <summary>The projection kind of a <see cref="Camera"/>.</summary>
public enum CameraProjection
{
    /// <summary>A perspective projection whose far plane may be infinite.</summary>
    Perspective,

    /// <summary>An orthographic projection whose far plane must be finite.</summary>
    Orthographic,
}
