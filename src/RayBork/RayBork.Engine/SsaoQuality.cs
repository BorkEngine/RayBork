namespace RayBork;

/// <summary>The baked sample-count quality of a <see cref="SsaoEffect"/>.</summary>
/// <remarks>
/// Each value selects a precompiled AO and denoise variant, so sample counts are compile-time.
/// Every value runs two denoise iterations.
/// </remarks>
public enum SsaoQuality
{
    /// <summary>8 AO samples and 4 denoise samples per pixel.</summary>
    Performance,

    /// <summary>16 AO samples and 4 denoise samples per pixel.</summary>
    Low,

    /// <summary>16 AO samples and 8 denoise samples per pixel.</summary>
    Medium,

    /// <summary>64 AO samples and 8 denoise samples per pixel.</summary>
    High,

    /// <summary>64 AO samples and 16 denoise samples per pixel.</summary>
    Ultra,
}
