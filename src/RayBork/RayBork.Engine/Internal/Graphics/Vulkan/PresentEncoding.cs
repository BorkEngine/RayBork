namespace RayBork.Internal;

/// <summary>
/// The encoding the present fragment shader applies to the display-linear color.
/// </summary>
/// <remarks>
/// The numeric values are part of the shader ABI: <c>Present.frag.glsl</c> branches on the
/// <c>encoding</c> push-constant member with exactly these values. Changing a value requires
/// recompiling the committed <c>Present.frag.spv</c> blob in the same change.
/// </remarks>
internal enum PresentEncoding {
    /// <summary>
    /// Writes display-linear values clamped to <c>[0, 1]</c> through a swapchain format that owns
    /// the transfer function, for example <c>B8G8R8A8_SRGB</c>.
    /// </summary>
    LinearClamp = 0,

    /// <summary>
    /// Applies the manual sRGB transfer function and clamps to <c>[0, 1]</c> for a swapchain format
    /// with no hardware transfer function, for example <c>B8G8R8A8_UNORM</c>.
    /// </summary>
    ManualSrgb = 1,

    /// <summary>
    /// Writes linear extended-sRGB (scRGB) values without clamping and scaled by the window SDR white
    /// level, for an HDR half-float swapchain.
    /// </summary>
    ScRgbHdr = 2,
}
