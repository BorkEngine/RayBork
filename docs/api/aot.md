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

Publish commands used by the M7 gate:

```text
dotnet build samples/Pong/Pong.slnx -c Release
dotnet publish samples/Pong/Pong.csproj -c Release -r win-x64   -p:PublishAot=true
dotnet publish samples/Pong/Pong.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/Pong/bin/Release/net11.0/win-x64/publish/Pong.exe --frames 30
# WSL, from the repository root, on the WSLg display with the conformant lavapipe ICD:
# VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Pong/bin/Release/net11.0/linux-x64/publish/Pong --frames 30
```

`samples/Pong` is the M7 sample and Native AOT gate. It is its own solution,
`samples/Pong/Pong.slnx`, outside `src/RayBork/RayBork.slnx`, and contains the sample and the JIT
`Pong.Assets` executable. The solution builds `Pong` once and then `Pong.Assets`; the assets
project runs the bake executable out of process, bakes `pong.pck` from the original 32x128 opaque
paddle PNG and the `Pong.PongSettings` TOML data, and copies it beside the sample executable. The
sample loads it automatically and has no `--assets` argument. `PongSettings` lives only in the
sample project and `Pong.Assets` references that project directly; `Pong` does not reference the
assets project, so the graph has no cycle. Every tick records one orthographic camera at
`(0, 0, 1)` with `orthographicHeight = Window.RenderHeight`, fits the `1280x720` design playfield
into the render pixels, and anchors every draw with `Camera.ScreenToWorldTransform`. `DrawQuad`
draws the playfield, border, net, score panels, shaded overlay, and textured paddles; `DrawDisc`
draws the ball; `DrawText` draws the scores, status line, and pause/winner overlay. The match is
player vs AI: the human moves the left paddle with W/S, Up/Down, or gamepad 0 left stick/d-pad, and
a deterministic AI moves the right paddle at `AiPaddleSpeed` while tracking the ball. The match uses
a fixed opening serve, fixed `1/120` second substeps, `Collision.CheckBoxSphere` paddle collisions,
a paddle-hit speed-up of `BallSpeedupPerHit` up to `MaxBallSpeed`, scoring with the serve going to
the loser, a max-score win, and pause/restart. `--frames` and `--help` are the only options.

The publish does not build `Pong.Assets`; the Release solution build above produces `pong.pck`
beside the sample executable, and `PongCopyPackToPublish` copies it into the publish directory. A
missing pack fails the publish with a clear error that names the Release solution build. The publish
directory must contain the host, its native libraries, and `pong.pck` beside the host, and must not
contain `Pong.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the shaderc native
libraries, or the glTF packages.

Both M7 publishes started, loaded the baked settings and paddle texture, played the player-vs-AI
match, and shut down. The Windows `win-x64` publish finished with zero AOT/trim warnings: its
publish directory contains `pong.pck` beside `Pong.exe` and no bake tooling, and the published host
ran `--frames 30` to exit `0` on the hardware driver. The `linux-x64` publish also finished with
zero warnings, contains `pong.pck` beside `Pong` and no bake tooling, and the published host ran
`--frames 30` to exit `0` on the conformant lavapipe/llvmpipe software driver with an SDR
`B8G8R8A8_SRGB` swapchain, FIFO presentation, 4x MSAA, and a 1280x720 window. WSLg's hardware
Vulkan path (Mesa dzn) is not a target.

`samples/Text` was the M6 sample and Native AOT gate, and it remains a buildable and publishable
sample. Its solution is `samples/Text/Text.slnx`; it bakes `text.pck` from the checked-in Lora face
and draws the world-space text scene with the built-in Fira Sans and JetBrains Mono faces. It is no
longer the gate. `samples/Castle` remains a buildable and publishable sample, but it is not the M7
gate. Its build
invokes the JIT assets executable out of process with the sample's configuration but without its
publish settings, bakes `castle.pck`, and copies the pack beside the sample executable; `dotnet
publish` copies the same pack into the publish directory, so the published hosts load the adjacent
`castle.pck` with no `--assets` argument. Its publish directory must contain `castle.pck` beside the
host and must not contain `Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder,
the shaderc native libraries, or the glTF packages. `samples/PrimitiveGallery` remains a buildable
and publishable single-project sample with no assets executable and no pack; it builds its only
texture at runtime.

AOT publish is a per-milestone gate, not a release-only check. The M7 gate is `samples/Pong`: the
published hosts open a window, load the baked `Pong.PongSettings` data and texture and the built-in
texture, shader, and font assets, record the player-vs-AI orthographic screen-space pass through
`ScreenToWorldTransform`, and exit `0`. The gate path exercises the custom-data descriptor decode,
the texture upload and sampler path, the orthographic projection, `DrawQuad`/`DrawDisc`, the
`DrawText` HUD path with glyph generation, and the same batch planner as the 3D draws. The sample is
a plain user-facing example and contains no test-only draw assertion. The Windows suite is 2,695
tests with no skips (114 GPU); the lavapipe Linux run is 2,688 passed with 7 skipped HDR/
scRGB-present tests because the driver exposes only SDR.

On WSL, run the Linux host with the conformant lavapipe ICD:
`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`. WSLg's hardware Vulkan path (Mesa dzn) is
not a target and is not part of the gate. The fixed 2048-element bindless texture array is the
engine's shader ABI and table capacity, not a driver-specific workaround. Glyph textures are ordinary
bindless procedural textures, so the text path uses that same fixed array. The Windows gate runs on
the hardware driver.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
