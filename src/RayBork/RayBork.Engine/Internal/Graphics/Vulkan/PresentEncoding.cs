namespace RayBork.Internal;

/// <summary>
/// The encoding the present fragment shader applies to the display-linear color.
/// </summary>
/// <remarks>
/// The numeric values are part of the shader ABI; changing one requires recompiling
/// <c>Present.frag.spv</c> in the same change.
/// </remarks>
internal enum PresentEncoding
{
    /// <summary>
    /// Clamped display-linear values through a hardware transfer function, for example
    /// <c>B8G8R8A8_SRGB</c>.
    /// </summary>
    LinearClamp = 0,

    /// <summary>
    /// Manual sRGB transfer function and clamping to <c>[0, 1]</c>, for example
    /// <c>B8G8R8A8_UNORM</c>.
    /// </summary>
    ManualSrgb = 1,

    /// <summary>
    /// Unclamped scRGB scaled by the window SDR white level, for an HDR half-float swapchain.
    /// </summary>
    ScRgbHdr = 2,
}
