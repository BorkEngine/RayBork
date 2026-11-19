namespace RayBork.Internal;

/// <summary>A cached snapshot of the SDL3 window HDR state.</summary>
/// <remarks>
/// Immutable and safe to read from any thread; the platform refreshes it on the main thread after
/// initialization, HDR changes, and display changes.
/// </remarks>
/// <param name="HdrEnabled">Whether the window has headroom above SDR white.</param>
/// <param name="SdrWhiteLevel">ScRGB scale for display-linear <c>1.0</c> as SDR white.</param>
/// <param name="HdrHeadroom">Peak brightness versus SDR white; <c>1</c> when HDR is off.</param>
internal readonly record struct WindowHdrState(
    bool HdrEnabled,
    float SdrWhiteLevel,
    float HdrHeadroom
)
{
    /// <summary>The SDR state: HDR disabled with both display-linear scales at one.</summary>
    internal static WindowHdrState Sdr { get; } = new(false, 1f, 1f);

    /// <summary>Normalizes raw SDL window properties into a valid state.</summary>
    /// <remarks>HDR off forces SDR; invalid scales fall back to one and headroom clamps up.</remarks>
    internal static WindowHdrState Normalize(
        bool hdrEnabled,
        float sdrWhiteLevel,
        float hdrHeadroom
    )
    {
        if (!hdrEnabled)
        {
            return Sdr;
        }

        float whiteLevel = float.IsFinite(sdrWhiteLevel) && sdrWhiteLevel > 0f ? sdrWhiteLevel : 1f;
        float headroom = float.IsFinite(hdrHeadroom) && hdrHeadroom >= 1f ? hdrHeadroom : 1f;
        return new WindowHdrState(true, whiteLevel, headroom);
    }
}
