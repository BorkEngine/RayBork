namespace RayBork.Internal;

/// <summary>An immutable snapshot of the runtime tone-mapping settings for one frame.</summary>
/// <remarks>
/// The tick thread copies the live <see cref="TonemappingSettings"/> when the frame is sealed, and
/// the render thread reads only the snapshot.
/// </remarks>
/// <param name="Exposure">The exposure in stops, applied as <c>2^Exposure</c>.</param>
/// <param name="AgxWhite">The AgX white in scene-linear units.</param>
/// <param name="AgxContrast">The AgX curve contrast.</param>
internal readonly record struct TonemappingSnapshot(
    float Exposure,
    float AgxWhite,
    float AgxContrast
)
{
    /// <summary>Gets the defaults: neutral exposure, Godot's AgX white and contrast.</summary>
    internal static TonemappingSnapshot Default =>
        new(0f, AgxToneMap.DEFAULT_AGX_WHITE, AgxToneMap.DEFAULT_AGX_CONTRAST);
}
