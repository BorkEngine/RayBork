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
dotnet publish -c Release -r win-x64   -p:PublishAot=true
dotnet publish -c Release -r linux-x64 -p:PublishAot=true
```

Both publishes must start, load a pack and any registered custom data, draw where the milestone has
a draw path, and shut down. AOT publish is a per-milestone gate, not a release-only check. The M3
gate bakes a sample pack with the assets executable, publishes `samples/HelloInterop`, runs it with
`--assets <pack> --frames 30` on `win-x64` and `linux-x64`, and requires exit code `0` with zero
AOT/trim warnings.

Single-file publishing is supported when the native SDL3/Vulkan libraries are deployed alongside
the executable or extracted by the host. A fully self-extracting single binary is not a v1 gate.
