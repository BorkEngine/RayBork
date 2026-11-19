namespace RayBork;

/// <summary>
/// The description of a render texture.
/// </summary>
/// <remarks>
/// The color format is <see cref="TextureFormat.Rgba16Float"/> by default,
/// <see cref="TextureFormat.Rgba8Unorm"/>, or <see cref="TextureFormat.Rgba8Srgb"/>; compressed and
/// depth formats are invalid. The requested MSAA count is lowered to the highest supported
/// count not above it, or <c>1</c>, and <see cref="RenderTexture.SampleCount"/> reports the
/// effective value.
/// </remarks>
/// <param name="Width">The width in pixels; positive and at most <c>16384</c>.</param>
/// <param name="Height">The height in pixels; positive and at most <c>16384</c>.</param>
/// <param name="ColorFormat">
/// The color format, <see cref="TextureFormat.Rgba16Float"/> by default.
/// </param>
/// <param name="Depth">
/// Whether the target has a <see cref="TextureFormat.Depth32Float"/> attachment and a
/// <see cref="RenderTexture.DepthTexture"/> view; without one, depth-tested draws are no-ops.
/// </param>
/// <param name="SampleCount">MSAA count: <c>1</c>, <c>2</c>, <c>4</c>, or <c>8</c>.</param>
public readonly record struct RenderTextureDescription(
    int Width,
    int Height,
    TextureFormat ColorFormat = TextureFormat.Rgba16Float,
    bool Depth = true,
    int SampleCount = 4
);
