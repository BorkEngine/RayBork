namespace RayBork;

/// <summary>
/// The description of one runtime render texture.
/// </summary>
/// <remarks>
/// <para>
/// The default color format is <see cref="TextureFormat.Rgba16Float"/> because the renderer works in
/// linear HDR. <see cref="TextureFormat.Rgba8Unorm"/> and <see cref="TextureFormat.Rgba8Srgb"/> create
/// an SDR target for LDR intermediate work; the sampling shader decodes an sRGB target through the
/// texture's sRGB view. The packed block-compressed formats and
/// <see cref="TextureFormat.Depth32Float"/> are not valid color formats.
/// </para>
/// <para>
/// <see cref="Depth"/> creates a depth attachment and, when <see langword="true"/>, gives the target a
/// public <see cref="RenderTexture.DepthTexture"/> view over its single-sample resolved depth image. The
/// depth format is always <see cref="TextureFormat.Depth32Float"/>; a multisampled target resolves the
/// depth attachment with the Vulkan 1.2 depth/stencil resolve path. A target with
/// <see cref="Depth"/> <see langword="false"/> has no depth attachment and
/// <see cref="RenderTexture.DepthTexture"/> is <see langword="null"/>; a camera whose target declares no
/// depth attachment still records color-only draws, and depth-tested and depth-writing draws are no-ops
/// for that target.
/// </para>
/// <para>
/// <see cref="SampleCount"/> is the requested MSAA count and must be <c>1</c>, <c>2</c>, <c>4</c>, or
/// <c>8</c>. The effective count is the highest device-supported count that does not exceed the
/// request, or <c>1</c> when none is supported, and a fallback logs a warning. The effective count is
/// reported by <see cref="RenderTexture.SampleCount"/>.
/// </para>
/// </remarks>
/// <param name="Width">The target width in pixels. It must be positive and at most <c>16384</c>.</param>
/// <param name="Height">The target height in pixels. It must be positive and at most <c>16384</c>.</param>
/// <param name="ColorFormat">
/// The color format: <see cref="TextureFormat.Rgba16Float"/> (the default),
/// <see cref="TextureFormat.Rgba8Unorm"/>, or <see cref="TextureFormat.Rgba8Srgb"/>.
/// </param>
/// <param name="Depth">Whether the target owns an internal depth attachment.</param>
/// <param name="SampleCount">The requested MSAA sample count: <c>1</c>, <c>2</c>, <c>4</c>, or <c>8</c>.</param>
public readonly record struct RenderTextureDescription(
    int Width,
    int Height,
    TextureFormat ColorFormat = TextureFormat.Rgba16Float,
    bool Depth = true,
    int SampleCount = 4);
