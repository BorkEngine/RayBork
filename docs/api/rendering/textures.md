# Rendering: Textures

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.6 `Texture`, samplers, and formats

```csharp
public sealed class Texture : IAsset {
    public string Path { get; }
    public bool IsProcedural { get; }
    public int Width { get; }
    public int Height { get; }
    public int MipLevels { get; }
    public TextureFormat Format { get; }
    public bool IsSrgb { get; }
    public SamplerState Sampler { get; }
    public bool IsDisposed { get; }

    public void Dispose();
}

public sealed class TextureBuilder {
    public int Width { get; }
    public int Height { get; }
    public TextureFormat Format { get; }
    public SamplerState Sampler { get; }
    public bool GenerateMipsEnabled { get; }

    public void SetPixels(ReadOnlySpan<byte> pixels);
    public void SetPixels(ReadOnlySpan<Color> colors);
    public void GenerateMips();

    public Task<Texture> BuildAsync(CancellationToken cancellationToken = default);
}

public readonly record struct SamplerState(
    TextureFilter Filter,
    TextureWrap Wrap,
    float MaxAnisotropy = 1f);

public enum TextureFilter {
    Nearest,
    Linear,
    Anisotropic,
}

public enum TextureWrap {
    Repeat,
    Clamp,
    Mirror,
}

public enum TextureFormat {
    Rgba8Unorm,
    Rgba8Srgb,
    Bc7Unorm,
    Bc7Srgb,
    Bc5Unorm,
    R8Unorm,
    Rgba16Float,
    Depth32Float,
}
```

> **M3 subset.** M3 loads packed textures and adds procedural `TextureBuilder` creation for the
> uncompressed formats. `Rgba16Float`/`Depth32Float` remain render-target formats, and BC formats
> remain bake-produced texture payloads. M8 adds runtime render-target color and depth views over a
> `RenderTexture`'s resolved images; they are textures but not assets.

- Textures are immutable GPU resources. A loaded texture's format, mip count, sRGB flag, and
  sampler come from the bake and cannot be changed at runtime. A procedural texture chooses its
  sampler, format, and mip behavior in `TextureBuilder` and is immutable after `BuildAsync`. A
  `RenderTexture` color or depth view is the one exception: `RenderTexture.Resize` recreates the
  target's GPU image and updates the same view object's `Width` and `Height` in place. Its format,
  sampler, empty `Path`, and owner stay fixed.
- Procedural formats are `Rgba8Unorm`, `Rgba8Srgb`, and `R8Unorm`. `Rgba8` uses exactly four bytes
  per pixel and `R8Unorm` one byte per pixel. There is no runtime BC encoder or raw KTX2 upload in
  v1.
- `SetPixels(ReadOnlySpan<byte>)` takes tightly packed base-level pixels. For `Rgba8Srgb` the bytes
  are already sRGB-encoded; alpha is linear. `SetPixels(ReadOnlySpan<Color>)` takes one linear
  `Color` per pixel, converts RGB to sRGB for `Rgba8Srgb`, stores linear unorm for `Rgba8Unorm`,
  and stores the red channel for `R8Unorm`; components are clamped to `[0, 1]`.
- `generateMips` defaults to `true`. If no mip chain exists, `BuildAsync` generates a deterministic
  box-filter chain from the base level. `GenerateMips()` regenerates it on demand. A build with
  `generateMips: false` and no chain uploads one level.
- A procedural texture has a reserved synthetic path under `/_procedural/texture/`, has
  `IsProcedural = true`, and cannot be loaded from a pack. The CPU pixels stay owned by the builder;
  the built texture exposes metadata only.
- `BuildAsync` may be called from `Setup`, `Tick`, or any thread, and awaited on any thread. A
  builder instance is not thread-safe. It validates dimensions (positive, at most 16384), format,
  buffer length, finite colors, and that pixels exist, then uploads through the non-blocking GPU
  bridge and returns only after the GPU resource is ready. A failed or canceled build destroys any
  partial resource and leaves the builder reusable. The caller owns one reference and must dispose
  the returned texture exactly once. After app shutdown every builder member throws
  `ObjectDisposedException`.
- `IsSrgb` is true for color textures stored as sRGB. Data textures (normal, mask, MSDF) are linear.
  An sRGB image view and sampler decode the stored samples to linear in the texture unit, so the
  shader reads linear values; the shader does not decode sRGB itself.
- `Sampler` is per texture. Loaded textures use the baked sampler; procedural textures use the
  sampler passed to `CreateTextureBuilder`, defaulting to `Linear` + `Repeat` with anisotropy `1`.
- A texture is sampled through the material binding of the draw that uses it. The textured
  `DrawQuad` overloads and `DrawBillboard` set the shared built-in `/shaders/mesh` material's
  `mainTexture` immediately before the recorded call and clear it immediately afterwards, and the
  frame retains the texture until the renderer releases the slot. The texture's own `Sampler`
  selects nearest, linear, or anisotropic filtering and the wrap mode; sampling is the same whether
  the quad is world-space or authored on a `ScreenToWorldTransform` plane. There is no
  `DrawTexture`; a sprite is a textured `DrawQuad` or `DrawBillboard(faceCamera: false)`.
- `Rgba16Float` is the default HDR render-target format. Ordinary packed color textures are usually
  `Bc7Srgb`; linear data textures use `Bc7Unorm`, `Bc5Unorm`, or `R8Unorm`.
- A `RenderTexture.ColorTexture` is a runtime `Texture` view over the target's single-sample resolved
  image. It reports an empty `Path` because it is not a pack asset, `IsProcedural = false`,
  `MipLevels = 1`, the target's `Width`/`Height`/`Format`/`IsSrgb`, and a linear clamp sampler. It
  samples, batches, and is retained by a recorded frame exactly like a pack or procedural texture. The
  `RenderTexture` owns it: disposing the view directly throws `InvalidOperationException`; dispose the
  render texture instead, which retires the view and releases the target after the frames that used it
  complete. A render-target view is never mounted, cached, or returned by the asset catalog. A resize
  keeps the same view object and updates its `Width` and `Height` in place; the color format, sampler,
  and empty `Path` do not change, and there is no separate view dispose.
- A `RenderTexture.DepthTexture` is the same kind of runtime view over the target's single-sample
  resolved depth image. It reports an empty `Path`, `IsProcedural = false`, `MipLevels = 1`,
  `Format = TextureFormat.Depth32Float`, `IsSrgb = false`, and a nearest-filter clamp sampler. It
  samples the stored reverse-Z depth as a float and is retained by a recorded frame like the color view.
  The `RenderTexture` owns it; a direct `Dispose` throws `InvalidOperationException`, and disposing the
  render texture retires it. It is `null` when the target declares `Depth: false`. A resize keeps the
  same view object and updates its `Width` and `Height` in place. A scene target's resolved depth image
  is internal and has no public `Texture` view.
- Bindless slots are allocated when a texture's GPU upload completes, not lazily at the first draw.
  The renderer creates a fixed table of 2048 slots, matching the shader ABI's
  `RayBorkTextures[2048]` descriptor array. A depth-bearing `RenderTexture` consumes one slot for
  `ColorTexture` and a second slot for `DepthTexture`; a depthless target consumes only the color slot.
  A device whose
  `maxPerStageDescriptorUpdateAfterBindSampledImages` or
  `maxDescriptorSetUpdateAfterBindSampledImages` limit is below 2048 is rejected during device
  selection with a clear diagnostic; the capacity is never reduced. Slot 0 is always reserved for
  the renderer's built-in 1x1 white texture, so a `null` texture binding and a texture binding that
  uses the bake default sample white. A render target keeps its one or two slots across a
  `RenderTexture.Resize`; the renderer rewrites their descriptors to the recreated views before any
  later frame samples them. A device recovery replaces the table, so every live target re-reserves its
  slots behind the unchanged public views.
- If the table is full, the texture upload fails with `AssetLoadException` (diagnostic code
  `RB1010`). The message names the table capacity and the failing texture path; the game must
  reduce the number of live textures or dispose unused textures and wait for frame retirement before
  retrying. A slot is released only when the texture's GPU resource is destroyed, which already
  waits for every submitted frame that can reference it to complete.
- Samplers are cached per device and keyed by `(TextureFilter, TextureWrap, MaxAnisotropy)`.
  Every texture with the same sampler state shares one sampler; the cache is destroyed with the
  logical device. Anisotropic filtering is enabled only when the device advertises and the renderer
  enables `samplerAnisotropy`; otherwise the sampler falls back to linear filtering with a warning.
- `Depth32Float` is used by render targets, not by ordinary packed textures.
- A texture participating in a frame keeps its slot and GPU image alive until the frame fence
  signals, even if the game disposes its public reference earlier.
