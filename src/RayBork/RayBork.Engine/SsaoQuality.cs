namespace RayBork;

/// <summary>
/// The baked sample-count quality of a <see cref="SsaoEffect"/>.
/// </summary>
/// <remarks>
/// Each value selects a precompiled ambient-occlusion and denoise shader variant; changing the value
/// never changes a shader's loop bound at runtime. <see cref="Performance"/> uses 8 AO samples and 4
/// denoise samples, <see cref="Low"/> 16/4, <see cref="Medium"/> 16/8 (the default), <see
/// cref="High"/> 64/8, and <see cref="Ultra"/> 64/16. Every value uses two denoise iterations.
/// </remarks>
public enum SsaoQuality {
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
