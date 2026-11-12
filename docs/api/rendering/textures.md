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
> remain bake-produced texture payloads.

- Textures are immutable GPU resources. A loaded texture's format, mip count, sRGB flag, and
  sampler come from the bake and cannot be changed at runtime. A procedural texture chooses its
  sampler, format, and mip behavior in `TextureBuilder` and is immutable after `BuildAsync`.
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
  The renderer decodes sRGB texture samples to linear before shading.
- `Sampler` is per texture. Loaded textures use the baked sampler; procedural textures use the
  sampler passed to `CreateTextureBuilder`, defaulting to `Linear` + `Repeat` with anisotropy `1`.
- `Rgba16Float` is the default HDR render-target format. Ordinary packed color textures are usually
  `Bc7Srgb`; linear data textures use `Bc7Unorm`, `Bc5Unorm`, or `R8Unorm`.
- Bindless slots are allocated lazily on first use. The renderer queries the device's
  `maxPerStageDescriptorUpdateAfterBindSampledImages` and targets a 4096-slot table. Exceeding the
  table throws `GraphicsException`; the game must reduce live textures or wait for frame
  retirement to free slots.
- `Depth32Float` is used by render targets, not by ordinary packed textures.
- A texture participating in a frame keeps its slot and GPU image alive until the frame fence
  signals, even if the game disposes its public reference earlier.
