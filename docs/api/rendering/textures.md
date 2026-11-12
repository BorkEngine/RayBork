# Rendering: Textures

[API overview](../../api.md) · [document map](../../api.md#document-map)

### 7.6 `Texture`, samplers, and formats

```csharp
public sealed class Texture : IAsset {
    public string Path { get; }
    public int Width { get; }
    public int Height { get; }
    public int MipLevels { get; }
    public TextureFormat Format { get; }
    public bool IsSrgb { get; }
    public SamplerState Sampler { get; }
    public bool IsDisposed { get; }

    public void Dispose();
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

> **M3 subset.** M3 loads and exposes `Path`, `Width`, `Height`, `MipLevels`, `Format`, `IsSrgb`,
> `Sampler`, `IsDisposed`, and `Dispose`. The GPU image is created from the packed KTX2 payload.
> Mip generation, format selection, sRGB, and sampler settings are fixed at bake time.

- Textures are immutable GPU resources. The bake chooses format, mip count, sRGB, and sampler;
  v1 has no runtime sampler override or texture upload API.
- `IsSrgb` is true for color textures baked as sRGB. Data textures (normal, mask, MSDF) are linear.
  The renderer decodes sRGB texture samples to linear before shading.
- `Sampler` is baked per texture. The default is `Linear` + `Repeat`, with anisotropy `1`.
- `Rgba16Float` is the default HDR render-target format. Ordinary packed color textures are usually
  `Bc7Srgb`; linear data textures use `Bc7Unorm`, `Bc5Unorm`, or `R8Unorm`.
- Bindless slots are allocated lazily on first use. The renderer queries the device's
  `maxPerStageDescriptorUpdateAfterBindSampledImages` and targets a 4096-slot table. Exceeding the
  table throws `GraphicsException`; the game must reduce live textures or wait for frame
  retirement to free slots.
- `Depth32Float` is used by render targets, not by ordinary packed textures.
- A texture participating in a frame keeps its slot and GPU image alive until the frame fence
  signals, even if the game disposes its public reference earlier.
