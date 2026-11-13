namespace RayBork.Internal;

/// <summary>
/// An immutable snapshot of the renderer options that a <see cref="RayBorkAppBuilder"/> captures
/// when it builds a <see cref="RayBorkApp"/>.
/// </summary>
/// <remarks>
/// Later M2 renderer tasks read this snapshot on the render thread. The builder is single-use, so no
/// later builder call can change it.
/// </remarks>
internal sealed class RenderSettings {
    /// <summary>
    /// Gets the requested present mode. The renderer may fall back to another supported mode and
    /// reports the effective mode in <see cref="EngineInfo.PresentMode"/>.
    /// </summary>
    public required PresentMode PresentMode { get; init; }

    /// <summary>
    /// Gets the requested MSAA sample count. The renderer falls back to the highest supported count
    /// that does not exceed the request and reports the effective count in
    /// <see cref="EngineInfo.SampleCount"/>.
    /// </summary>
    public required int SampleCount { get; init; }

    /// <summary>
    /// Gets the tone mapping mode applied by the present pass. It is a startup app setting; the runtime
    /// exposure, AgX white, and AgX contrast are mutable through <see cref="TonemappingSettings"/>.
    /// </summary>
    public required TonemappingMode Tonemapping { get; init; }

    /// <summary>
    /// Gets the startup exposure in stops applied before tone mapping. <c>0</c> is neutral. The app
    /// copies this value into the runtime <see cref="TonemappingSettings"/>; the renderer reads the
    /// frame's snapshot, not this value.
    /// </summary>
    public required float Exposure { get; init; }

    /// <summary>
    /// Gets the startup AgX white in scene-linear units before the display headroom is applied.
    /// <c>16.29</c> is Godot's default. The app copies this value into the runtime
    /// <see cref="TonemappingSettings"/>; the renderer reads the frame's snapshot, not this value.
    /// </summary>
    public float AgxWhite { get; init; } = AgxToneMap.DEFAULT_AGX_WHITE;

    /// <summary>
    /// Gets the startup AgX contrast. <c>1.25</c> is Godot's default. The app copies this value into
    /// the runtime <see cref="TonemappingSettings"/>; the renderer reads the frame's snapshot, not
    /// this value.
    /// </summary>
    public float AgxContrast { get; init; } = AgxToneMap.DEFAULT_AGX_CONTRAST;

    /// <summary>
    /// Gets whether the graphics validation layer is requested. It is enabled by default in Debug
    /// builds and disabled by default in Release builds.
    /// </summary>
    public required bool ValidationEnabled { get; init; }
}
