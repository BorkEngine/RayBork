namespace RayBork;

/// <summary>
/// The projection kind used by a <see cref="Camera"/>.
/// </summary>
public enum CameraProjection {
    /// <summary>
    /// A perspective projection with a vertical field of view. The far plane may be infinite.
    /// </summary>
    Perspective,

    /// <summary>
    /// An orthographic projection with a visible world-space height. The far plane must be finite.
    /// </summary>
    Orthographic,
}
