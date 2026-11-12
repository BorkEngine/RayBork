# HelloInterop.Assets

`HelloInterop.Assets` is the build-time assets executable for the M3 `HelloInterop` sample. It is a
JIT-only console app that configures `BakeApplicationBuilder`, runs the default processors plus the
sample's `CircleProcessor`, and writes `hello.pck` next to its own executable. No runtime project
references it, and it is never part of an AOT publish graph; see
[docs/api/tooling.md](../../docs/api/tooling.md#13-asset-tooling-rayborkbake) §13 and
[docs/architecture.md](../../docs/architecture.md#assets).

## Sources

| Source | Processor | Output |
| --- | --- | --- |
| `Assets/data/hello.toml` | TOML data binder | `Data` entry at `/data/hello`, bound to `HelloInterop.HelloSettings`. |
| `Assets/models/box.obj` + `Assets/models/box.toml` | built-in model | `Model` entry at `/models/box`, one `Mesh` part at `/models/box/body`, and the mesh payload. The sidecar points the part at `shaders/simple`. |
| `Assets/shaders/simple.toml` + `simple.vert`/`simple.frag` | built-in shader | `Shader` entry at `/shaders/simple` plus compiled vertex and fragment SPIR-V. |
| `Assets/textures/circle.circle` | `CircleProcessor` | `Texture` entry at `/textures/circle` plus the generated 64x64 RGBA KTX2 payload. |

`HelloSettings` is the same file the host uses: the project links
`../HelloInterop/HelloSettings.cs`, and both sides register the type by name. The sample's
`CircleProcessor` generates its pixels deterministically and calls `TextureBaker.AddTo`, so it never
writes MessagePack, KTX2 headers, or pack bytes itself.

The builder uses `WithProjectRoot(".")`, so the project sets
`RunWorkingDirectory=$(MSBuildProjectDirectory)`: `dotnet run` starts in this directory and the
checked-in `Assets/` root resolves. The bake output root defaults to `AppContext.BaseDirectory`, so
the pack lands next to the assets executable, for example
`samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck`.

## Bake

From the repository root:

```powershell
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- bake
```

Require exit code `0` and a written `samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck`.
Useful diagnostics commands:

```powershell
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- bake --check
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- inspect
dotnet run --project samples/HelloInterop.Assets/HelloInterop.Assets.csproj -c Release -- clean
```

Pass `--verbose` to any command for the full diagnostic list. The incremental manifest and packs are
generated under `bin/` and are gitignored; do not commit them.

## Run the sample

```powershell
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --assets samples/HelloInterop.Assets/bin/Release/net11.0/hello.pck --frames 3
```

The host logs the texture, mesh, shader, and custom-data metadata and exits `0`; it does not draw
because the M4 draw API is not part of this milestone. See
[samples/HelloInterop/README.md](../HelloInterop/README.md) for the full host options and the Native
AOT gate.
