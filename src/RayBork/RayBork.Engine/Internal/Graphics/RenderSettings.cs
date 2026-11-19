namespace RayBork.Internal;

/// <summary>
/// An immutable snapshot of renderer options captured when a <see cref="RayBorkApp"/> is built.
/// </summary>
/// <remarks>
/// The builder is single-use, so no later builder call can change it. Tone-map values seed the
/// runtime settings; the renderer reads the per-frame snapshot instead.
/// </remarks>
internal sealed class RenderSettings
{
    /// <summary>Gets the requested present mode; the renderer may fall back.</summary>
    public required PresentMode PresentMode { get; init; }

    /// <summary>Gets the requested MSAA sample count; the renderer may clamp it.</summary>
    public required int SampleCount { get; init; }

    /// <summary>Gets the tone mapping mode applied by the present pass.</summary>
    public required TonemappingMode Tonemapping { get; init; }

    /// <summary>Gets the startup exposure in stops; <c>0</c> is neutral.</summary>
    public required float Exposure { get; init; }

    /// <summary>Gets the startup AgX white in scene-linear units; default <c>16.29</c>.</summary>
    public float AgxWhite { get; init; } = AgxToneMap.DEFAULT_AGX_WHITE;

    /// <summary>Gets the startup AgX contrast; default <c>1.25</c>.</summary>
    public float AgxContrast { get; init; } = AgxToneMap.DEFAULT_AGX_CONTRAST;

    /// <summary>Gets whether validation is requested: on in Debug, off in Release.</summary>
    public required bool ValidationEnabled { get; init; }
}
