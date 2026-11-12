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
# WSL, from the repository root, on the WSLg display (dzn hardware):
# DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30
# WSL, optional conformant software run:
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
gate is `samples/Castle`: the published hosts load `/models/obj/castle` and
`/models/obj/castle_diffuse`, bind the texture to the loaded parts' built-in `/shaders/mesh`
materials, draw the castle, and must exit `0`. The sample is a plain user-facing example and contains
no test-only draw assertion. Both publishes must finish with zero AOT/trim warnings.

On the WSL gate host, WSLg selects Mesa's dzn Vulkan driver
(`Microsoft Direct3D12 (AMD Radeon(TM) 8060S Graphics)`) and prints "dzn is not a conformant Vulkan
implementation, testing use only". The M4 gate runs on that hardware driver: the fixed 2048-element
bindless texture array avoids dzn's runtime-descriptor-array DXIL lowering and fits its 2048-slot
sampler heap, so the published host exits `0` on the hardware dzn device and needs no
`DZN_DEBUG` workaround. The conformant lavapipe ICD remains available for a software-only Linux run
with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json`. The Windows gate runs on the hardware
driver.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
