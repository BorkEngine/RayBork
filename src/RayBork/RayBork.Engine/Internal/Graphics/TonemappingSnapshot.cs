namespace RayBork.Internal;

/// <summary>
/// An immutable value snapshot of the runtime tone-mapping settings for one frame.
/// </summary>
/// <remarks>
/// <para>
/// The tick thread copies the live <see cref="TonemappingSettings"/> into this snapshot when
/// <see cref="CameraService.EndFrame"/> seals the frame. The render thread reads only the snapshot,
/// so it never touches the live settings object or the game thread's memory.
/// </para>
/// <para>
/// The values are the ones in effect when the frame was sealed: a game that writes
/// <see cref="EngineContext.Tonemapping"/> during <see cref="IGame.Tick"/> affects the frame that
/// tick records, not an earlier published frame.
/// </para>
/// </remarks>
/// <param name="Exposure">The exposure in stops, applied as <c>2^Exposure</c> before tone mapping.</param>
/// <param name="AgxWhite">The AgX white in scene-linear units before the display headroom scaling.</param>
/// <param name="AgxContrast">The AgX curve contrast.</param>
internal readonly record struct TonemappingSnapshot(float Exposure, float AgxWhite, float AgxContrast) {
    /// <summary>
    /// Gets the startup defaults: neutral exposure, Godot's default AgX white, and its default
    /// contrast.
    /// </summary>
    internal static TonemappingSnapshot Default => new(
        0f,
        AgxToneMap.DEFAULT_AGX_WHITE,
        AgxToneMap.DEFAULT_AGX_CONTRAST);
}
