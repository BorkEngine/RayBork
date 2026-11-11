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
    /// Gets the tone mapping applied by the present pass.
    /// </summary>
    public required TonemappingMode Tonemapping { get; init; }

    /// <summary>
    /// Gets the exposure in stops applied before tone mapping. <c>0</c> is neutral.
    /// </summary>
    public required float Exposure { get; init; }

    /// <summary>
    /// Gets whether the graphics validation layer is requested. It is enabled by default in Debug
    /// builds and disabled by default in Release builds.
    /// </summary>
    public required bool ValidationEnabled { get; init; }

    /// <summary>
    /// Gets the linear scene clear color used until cameras provide their own clear color in M4.
    /// </summary>
    public required Color ClearColor { get; init; }
}
