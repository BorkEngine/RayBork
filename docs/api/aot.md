# Native AOT

[API overview](../api.md) · [document map](../api.md#document-map)

## 12. Native AOT

RayBork's runtime is designed for Native AOT:

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection-based serialization. Pack metadata and custom data use compiled
  `PolyType` shapes with `Nerdbank.MessagePack`; pack metadata is read with explicit binary readers.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` resolves the registered strategy from the merged registry built at `Build`: engine asset
  formats are built-in registrations, `WithAssetType<T>()` stores a compiled `IShapeable<T>` shape
  delegate, and `WithAssetLoader<T>()` stores the caller's loader. Every strategy is a statically
  referenced type or delegate, and the runtime never scans assemblies or uses reflection. `LoadData`
  covers opaque bytes without reflection.
- Logging uses `Microsoft.Extensions.Logging`; ZLogger is the default provider and is AOT-tested.
- P/Invoke is direct. SDL3 native libraries are deployed per RID; the Vulkan loader is resolved from
  the system/GPU driver with `NativeLibrary` and a custom `INativeContext`.
- The engine may use `unsafe` internally; game projects do not need `AllowUnsafeBlocks`.
- The bake tool is JIT and may use reflection, native tooling, and shader compilation. It is never
  referenced by a runtime project and never enters the publish graph.
- The built-in pack is generated at engine build time by a JIT-only generator that runs the ordinary
  bake tool. Its generated C# byte array is compiled into `RayBork.Engine`; the generator, its
  packages, and shaderc are not part of the runtime publish graph.
- Pack descriptors and custom data use `[GenerateShape]` and `Nerdbank.MessagePack`; the runtime
  never uses reflection serialization. `Load<T>` maps engine asset types through the built-in
  registrations and registered types through the compiled shape delegate or the caller's loader.

A game project uses `WinExe` for release. Samples and diagnostic hosts use `Exe` so console logging
is visible.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RayBork\RayBork.Engine\RayBork.Engine.csproj" />
  </ItemGroup>
</Project>
```

The current gate is `samples/Input`, the M9 sample and Native AOT gate. Publish and run the
`win-x64` and `linux-x64` hosts:

```text
dotnet publish samples/Input/Input.csproj -c Release -r win-x64 -p:PublishAot=true
samples/Input/bin/Release/net11.0/win-x64/publish/Input.exe --frames 30
# WSL, from the $HOME clone (never /mnt/d), with the WSL .NET and the conformant lavapipe ICD:
# $HOME/.dotnet/dotnet publish samples/Input/Input.csproj -c Release -r linux-x64 -p:PublishAot=true
# VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Input/bin/Release/net11.0/linux-x64/publish/Input --frames 30
```

`samples/Input` is a single-project solution, `samples/Input/Input.slnx`, outside
`src/RayBork/RayBork.slnx`, with no assets executable, no pack, and no `--assets` argument. It has no
bakeable sources, so it publishes without a Release solution build and without a bake step; the
engine's built-in pack supplies its shaders and fonts. Each publish directory must contain no `.pck`
and no bake tooling: no `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the shaderc native
libraries, or the glTF packages. Both publishes must finish with zero AOT/trim warnings, and each
gate host must run `--frames 30` and exit `0`. The milestone lead runs both publishes and host runs,
and the work report records the results; this page records the commands and the automated results,
not an AOT outcome.

The automated suite is the Windows full run on the hardware driver: 3,024 tests total, 3,023 passed,
0 failed, and 1 skipped (a dummy-driver borderless check), with 239 GPU integration tests, and the
`Category=Gpu` filter is 239 passed with no skips. No M9 lavapipe run is recorded here; the milestone
gate records it in the work report.

`samples/RenderTarget` was the M8 sample and Native AOT gate, and it remains buildable and
publishable. Publish commands used by the M8 gate:

```text
dotnet build samples/RenderTarget/RenderTarget.slnx -c Release
dotnet publish samples/RenderTarget/RenderTarget.csproj -c Release -r win-x64   -p:PublishAot=true
dotnet publish samples/RenderTarget/RenderTarget.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/RenderTarget/bin/Release/net11.0/win-x64/publish/RenderTarget.exe --frames 30
# WSL, from the repository root, on the WSLg display with the conformant lavapipe ICD:
# VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/RenderTarget/bin/Release/net11.0/linux-x64/publish/RenderTarget --frames 30
```

Its solution, `samples/RenderTarget/RenderTarget.slnx`, is outside `src/RayBork/RayBork.slnx` and
contains the sample and the JIT `RenderTarget.Assets` executable. The solution builds `RenderTarget`
once and then `RenderTarget.Assets`; the assets project runs the bake executable out of process,
bakes `rendertarget.pck` from the checked-in vignette post-process shader sources, and copies it
beside the sample executable. The sample loads it automatically and has no `--assets` argument. Every
tick records up to four camera passes: an offscreen perspective camera into a `640x360`
`Rgba16Float` render texture with depth and 4x MSAA; the single scene camera, or two half-width
split-screen cameras when `S` is on, drawing a procedural scene and sampling the target's
`ColorTexture` on a monitor; and a later orthographic HUD camera with `ClearMode.Depth`. The single
scene camera runs an app-lifetime `PostProcessStack` with one baked `ShaderPostProcessEffect` (`E`
toggles the stack). The interactive sample calls `ReadColorsAsync` and `ReadPixelsAsync` on `F2` and
writes a 24-bit uncompressed bottom-up BMP screenshot with the readback's opaque alpha dropped;
`--frames` and `--help` are the only options. The M8 published `--frames 30` run exercised the
render-texture creation, camera target routing, same-tick target sampling, the custom post-process
chain, and the store-back. The two readback methods are covered by the RenderTexture GPU tests and
the sample's interactive `F2` path, which a `--frames` run does not press.

The M8 publish did not build `RenderTarget.Assets`; the Release solution build produces
`rendertarget.pck` beside the sample executable, and the publish target copies it into the publish
directory. A missing pack fails the publish with a clear error that names the Release solution build.
The M8 publish directory contained the host, its native libraries, and `rendertarget.pck` beside the
host, and no `RenderTarget.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the
shaderc native libraries, or the glTF packages.

Both M8 publishes started, created the offscreen target, recorded the scene and HUD passes, ran the
custom post-process chain, and shut down. The Windows `win-x64` publish finished with zero AOT/trim
warnings: its publish directory contained `rendertarget.pck` beside `RenderTarget.exe` and no bake
tooling, and the published host ran `--frames 30` to exit `0` on the hardware driver. The `linux-x64`
publish also finished with zero warnings, contained `rendertarget.pck` beside `RenderTarget` and no
bake tooling, and the published host ran `--frames 30` to exit `0` on the conformant
lavapipe/llvmpipe software driver with an SDR `B8G8R8A8_SRGB` swapchain, FIFO presentation, 4x MSAA,
and a 1280x720 window. WSLg's hardware Vulkan path (Mesa dzn) is not a target.

The final M8 suite was 2,977 tests with no skips on the Windows hardware path (239 GPU); the lavapipe
Linux run was 2,970 passed with 7 skipped HDR/scRGB-present tests because the driver exposes only SDR.

`samples/Pong` was the M7 sample and Native AOT gate, and it remains a buildable and publishable
sample. Its solution is `samples/Pong/Pong.slnx`; it bakes `pong.pck` from the original paddle PNG
and the `Pong.PongSettings` TOML data, and draws the player-vs-AI match through `DrawQuad`/`DrawDisc`/
`DrawText` on `ScreenToWorldTransform`, with the scene-camera bloom stack and the stackless HUD
camera added in M8. The earlier M7 publishes finished with zero AOT/trim warnings and ran
`--frames 30` to exit `0` on the same two drivers.

`samples/Text` was the M6 sample and Native AOT gate, and it remains a buildable and publishable
sample. Its solution is `samples/Text/Text.slnx`; it bakes `text.pck` from the checked-in Lora face
and draws the world-space text scene with the built-in Fira Sans and JetBrains Mono faces. It is no
longer the gate. `samples/Castle` remains a buildable and publishable sample, but it is not the gate.
Its build
invokes the JIT assets executable out of process with the sample's configuration but without its
publish settings, bakes `castle.pck`, and copies the pack beside the sample executable; `dotnet
publish` copies the same pack into the publish directory, so the published hosts load the adjacent
`castle.pck` with no `--assets` argument. Its publish directory must contain `castle.pck` beside the
host and must not contain `Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder,
the shaderc native libraries, or the glTF packages. `samples/PrimitiveGallery` remains a buildable
and publishable single-project sample with no assets executable and no pack; it builds its only
texture at runtime.

AOT publish is a per-milestone gate, not a release-only check. The M9 gate is `samples/Input`: the
gate publishes the host and runs `--frames 30`; the host opens a window, draws the input HUD from the
built-in pack with no external pack or bake tooling, calls `IWindow.SetTextInputArea` on a drawn tick,
and exits `0`. The gate path
exercises the SDL3 window and input surface, the built-in pack descriptor decode, the bindless text
and primitive draws, and the same batch planner as the 3D draws. The M8 gate was
`samples/RenderTarget`: its published hosts opened a window, created the offscreen render texture,
loaded the built-in texture, shader, and font assets and the baked vignette shader, recorded the
offscreen/scene/split/HUD passes, ran the custom post-process chain, sampled the target's
`ColorTexture`, and exited `0`; the readback methods were not part of the `--frames` gate, and the
RenderTexture GPU tests and the interactive `F2` path cover them. The sample is a plain
user-facing example and contains no test-only draw assertion.

On WSL, run the Linux host with the conformant lavapipe ICD:
`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`. WSLg's hardware Vulkan path (Mesa dzn) is
not a target and is not part of the gate. The fixed 2048-element bindless texture array is the
engine's shader ABI and table capacity, not a driver-specific workaround. Glyph textures are ordinary
bindless procedural textures, so the text path uses that same fixed array. The Windows gate runs on
the hardware driver.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
