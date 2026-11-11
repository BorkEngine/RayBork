namespace RayBork.Internal;

/// <summary>
/// A cached snapshot of the SDL3 window HDR state. The renderer uses it to choose an scRGB
/// swapchain and to scale display-linear output into scRGB.
/// </summary>
/// <remarks>
/// The state can change at runtime, for example when the user toggles HDR or the window moves to a
/// display with different capabilities. The platform refreshes the cache on the main thread after
/// initialization, when SDL reports a window HDR state change, and after display changes. A
/// snapshot is immutable and safe to read from any thread.
/// </remarks>
/// <param name="HdrEnabled">Whether the window has headroom above SDR white.</param>
/// <param name="SdrWhiteLevel">
/// The scRGB scale factor applied to a display-linear <c>1.0</c> to display it as SDR white in
/// linear scRGB; <c>1.0</c> when HDR is off.
/// </param>
/// <param name="HdrHeadroom">
/// The peak brightness as a multiple of SDR white; <c>1.0</c> when HDR is off or there is no
/// headroom.
/// </param>
internal readonly record struct WindowHdrState(bool HdrEnabled, float SdrWhiteLevel, float HdrHeadroom) {
    /// <summary>
    /// Gets the SDR state: HDR is disabled and both display-linear scales are exactly one.
    /// </summary>
    internal static WindowHdrState Sdr { get; } = new(false, 1f, 1f);

    /// <summary>
    /// Normalizes raw SDL window property values into a valid state.
    /// </summary>
    /// <remarks>
    /// HDR disabled forces <see cref="Sdr"/>. Otherwise a finite, positive SDR white level is kept,
    /// and the HDR headroom is clamped up to at least one. An invalid value falls back to one so a
    /// driver cannot produce a zero or infinite scale.
    /// </remarks>
    /// <param name="hdrEnabled">Whether SDL reports that the window has HDR headroom.</param>
    /// <param name="sdrWhiteLevel">The raw SDR white level property.</param>
    /// <param name="hdrHeadroom">The raw HDR headroom property.</param>
    /// <returns>The normalized state.</returns>
    internal static WindowHdrState Normalize(bool hdrEnabled, float sdrWhiteLevel, float hdrHeadroom) {
        if (!hdrEnabled) {
            return Sdr;
        }

        float whiteLevel = float.IsFinite(sdrWhiteLevel) && sdrWhiteLevel > 0f ? sdrWhiteLevel : 1f;
        float headroom = float.IsFinite(hdrHeadroom) && hdrHeadroom >= 1f ? hdrHeadroom : 1f;
        return new WindowHdrState(true, whiteLevel, headroom);
    }
}
