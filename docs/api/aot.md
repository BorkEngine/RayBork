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

Publish commands used by the gate:

```text
dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Release
dotnet publish samples/PrimitiveGallery/PrimitiveGallery.csproj -c Release -r win-x64   -p:PublishAot=true
dotnet publish samples/PrimitiveGallery/PrimitiveGallery.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/PrimitiveGallery/bin/Release/net11.0/win-x64/publish/PrimitiveGallery.exe --frames 30
# WSL, from the repository root, on the WSLg display (dzn hardware):
# DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/PrimitiveGallery/bin/Release/net11.0/linux-x64/publish/PrimitiveGallery --frames 30
# WSL, optional conformant software run:
# VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/PrimitiveGallery/bin/Release/net11.0/linux-x64/publish/PrimitiveGallery --frames 30
```

The sample is its own solution, `samples/PrimitiveGallery/PrimitiveGallery.slnx`, outside
`src/RayBork/RayBork.slnx`. It is a single project with no assets executable and no pack: `Setup`
builds its only texture with `CreateTextureBuilder`, and the sample reads only the engine's built-in
pack. `--frames` and `--help` are the only options. The publish directory must contain the host and
its native libraries and must not contain `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the
shaderc native libraries, or the glTF packages.

`samples/Castle` remains a buildable and publishable sample, but it is not the M5 gate. Its build
invokes the JIT assets executable out of process with the sample's configuration but without its
publish settings, bakes `castle.pck`, and copies the pack beside the sample executable; `dotnet
publish` copies the same pack into the publish directory, so the published hosts load the adjacent
`castle.pck` with no `--assets` argument. Its publish directory must contain `castle.pck` beside the
host and must not contain `Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder,
the shaderc native libraries, or the glTF packages.

Both gate publishes must start, load the assets the sample needs, draw, and shut down. AOT publish
is a per-milestone gate, not a release-only check. The M5 gate is `samples/PrimitiveGallery`: the
published hosts open a window, build their procedural texture, draw the M5 primitive helpers through
the built-in `/shaders/primitive` and `/shaders/mesh` programs, and must exit `0`. The M5 helper
surface is 46 overloads over 17 helper families: the line, point, and triangle helpers, the twelve
shape helpers (`DrawCube`, `DrawSphere`, `DrawPlane`, `DrawCylinder`/`DrawWireCylinder`,
`DrawCapsule`/`DrawWireCapsule`, `DrawCircle`, `DrawTriangleStrip`, `DrawRay`, `DrawBoundingBox`,
`DrawWireCube`, `DrawWireSphere`, and `DrawGrid`) with their shape, position, tessellation, start/end,
and `Transform` overloads, and the eight `DrawBillboard` overloads. The M5 camera also ships
`DrawWireModel`. The gate path exercises the zero-uniform primitive program, which has no texture
binding at all, and the alpha-blended mesh program used by billboards; the polygon-mode-line pipeline
used by `DrawWireModel` is covered by the GPU tests because the gallery has no model asset. The sample
is a plain user-facing example and contains no test-only draw assertion. Both publishes must finish
with zero AOT/trim warnings. The M5 suite is 1,881 tests, 92 of them GPU integration tests, and every
sample solution builds.

On the WSL gate host, WSLg selects Mesa's dzn Vulkan driver
(`Microsoft Direct3D12 (AMD Radeon(TM) 8060S Graphics)`) and prints "dzn is not a conformant Vulkan
implementation, testing use only". The gate runs on that hardware driver: the fixed 2048-element
bindless texture array avoids dzn's runtime-descriptor-array DXIL lowering and fits its 2048-slot
sampler heap, so the published host exits `0` on the hardware dzn device and needs no `DZN_DEBUG`
workaround. The conformant lavapipe ICD remains available for a software-only Linux run with
`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`. The Windows gate runs on the hardware
driver.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
