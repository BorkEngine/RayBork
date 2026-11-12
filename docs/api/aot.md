# Native AOT

[API overview](../api.md) · [document map](../api.md#document-map)

## 12. Native AOT

RayBork's runtime is designed for Native AOT:

- `RayBork.Engine` sets `IsAotCompatible`, `EnableTrimAnalyzer`, and `EnableAotAnalyzer`.
- No runtime reflection-based serialization. Pack metadata and custom data use compiled
  `PolyType` shapes with `Nerdbank.MessagePack`; pack metadata is read with explicit binary readers.
- No runtime shader compilation. Shader reflection is bake-generated metadata.
- `Load<T>` maps built-in engine asset types with a closed type switch. Custom data types are
  registered at build time with `WithAssetType<T>()`, which stores a compiled `IShapeable<T>`
  loader delegate; `LoadData` covers opaque bytes without reflection.
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
  never uses reflection serialization. `Load<T>` maps engine asset types with a closed type switch
  and custom data through the registered shape delegate.

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
dotnet build samples/Castle/Castle.slnx -c Release
dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64   -p:PublishAot=true
dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --frames 30
# WSL, from the repository root, on the WSLg display:
# DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
# WSL, when WSLg's dzn driver crashes on the first geometry draw, use the conformant lavapipe ICD:
# VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
```

The sample is its own solution, `samples/Castle/Castle.slnx`, outside `src/RayBork/RayBork.slnx`.
Its build invokes the JIT assets executable out of process with the sample's configuration but
without its publish settings, bakes `castle.pck`, and copies the pack beside the sample executable.
`dotnet publish` copies the same pack into the publish directory, so the published hosts load the
adjacent `castle.pck` with no `--assets` argument; `--assets` remains an optional override. The
publish directory must contain `castle.pck` and must not contain `Castle.Assets.dll`,
`RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, the shaderc native libraries, or the glTF
packages.

Both publishes must start, load a pack and any registered custom data, draw where the milestone has
a draw path, and shut down. AOT publish is a per-milestone gate, not a release-only check. The M4
gate is `samples/Castle`: it recreates raylib's `models_loading` example, so the published hosts load
`/models/obj/castle`, `/models/obj/castle_diffuse`, `/shaders/castle`, and the built-in
`/shaders/default`, draw the unlit castle model and the reference grid, and must exit `0` with a
completed frame that reports nonzero draw calls and instances. The sample records whether any
completed frame drew and fails at shutdown when none ever did, so an invisible scene cannot pass and
a slow host is judged on completed frames instead of a fixed tick number. Both publishes must finish
with zero AOT/trim warnings.

On the WSL gate host, WSLg selects the non-conformant dzn Vulkan driver and the first geometry draw
currently crashes inside `libvulkan_dzn.so` (see the M4 as-built notes in
[the roadmap](../roadmap.md#m4--core-3d-renderer-shader-state-and-instancing)). Run the Linux
functional gate under the conformant lavapipe ICD with
`VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`; the same published host then exits `0`
with the expected draw calls and instances. The Windows gate runs on the hardware driver.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
