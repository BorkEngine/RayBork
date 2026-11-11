namespace RayBork;

/// <summary>
/// The immutable sampler state baked for a texture.
/// </summary>
/// <param name="Filter">The filtering mode.</param>
/// <param name="Wrap">The address mode applied to both axes.</param>
/// <param name="MaxAnisotropy">
/// The maximum anisotropy when <paramref name="Filter"/> is <see cref="TextureFilter.Anisotropic"/>. It
/// defaults to <c>1</c> and is clamped to the device limit.
/// </param>
public readonly record struct SamplerState(
    TextureFilter Filter,
    TextureWrap Wrap,
    float MaxAnisotropy = 1f);
