# RayBork

RayBork is a simple, modern, generic rendering engine for .NET, inspired by
[raylib](https://www.raylib.com/). It gives small games a direct, immediate-mode API: open a window,
record a camera pass every tick, issue draw calls, and present.

- 2D and 3D drawing: screen-space quads and text, perspective and orthographic cameras, meshes,
  models, instancing, and render targets.
- Vulkan 1.2 renderer with MSAA, HDR output, SSAO, bloom, and AgX tone mapping.
- SDL3 window, input, gamepad, clipboard, and monitor handling.
- Offline asset baking: textures to KTX2/BC, shaders to SPIR-V, models, fonts, raw files, and typed
  data.
- Native AOT compatible. No reflection-based asset discovery and no runtime shader compilation.

RayBork is in alpha. The public API may change between releases.

## Requirements

- [.NET SDK 11.0.100](https://dotnet.microsoft.com/) or a newer 11.0 patch, as pinned by `global.json`.
- A Vulkan 1.2 capable GPU and driver.
- Windows or Linux. macOS is untested.

You do not need the Vulkan SDK, a shader compiler, or a system SDL install to build. Shaders are
committed as SPIR-V and SDL3 ships as a NuGet package.

## Build and test

From the repository root:

```powershell
dotnet build src/RayBork/RayBork.slnx
dotnet test  src/RayBork/RayBork.slnx
```

The solution builds the engine, the build-time asset baker, and the tests. Samples are separate
solutions:

```powershell
dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx
dotnet run --project samples/PrimitiveGallery/PrimitiveGallery.csproj -- --frames 30
```

`--frames 30` stops after 30 ticks; omit it (or pass `--frames 0`) to run until the window closes.
`PrimitiveGallery` has no asset pack and needs no bake step.

## A minimal game

A RayBork game implements `IGame`. `Setup` runs once, `Tick` runs once per frame, and `Shutdown` runs
once at the end. Cameras are created per tick, record their draws, and are disposed before the tick
returns.

```csharp
using System.Numerics;
using RayBork;

using RayBorkApp app = new RayBorkAppBuilder()
    .WithGame(new HelloGame())
    .WithTitle("Hello RayBork")
    .WithSize(800, 450)
    .Build();

app.Run();

internal sealed class HelloGame : IGame
{
    public Task Setup(EngineContext context) => Task.CompletedTask;

    public void Tick(EngineContext context)
    {
        if (context.Input.IsKeyPressed(Key.Escape))
        {
            context.RequestExit();
        }

        using Camera camera = context.CreateCamera(
            new Transform(new Vector3(0f, 0f, 3f)),
            CameraProjection.Perspective,
            fieldOfView: 60f,
            clearColor: Color.FromSrgbBytes(18, 18, 22));

        camera.DrawTriangle(
            new Vector3(-1f, -1f, 0f),
            new Vector3(1f, -1f, 0f),
            new Vector3(0f, 1f, 0f),
            Color.LimeGreen);
    }

    public Task Shutdown() => Task.CompletedTask;
}
```

`Run` must be called once from the process main thread, and the app should be disposed afterwards.
A window close is a graceful exit: save state in `Shutdown`.

To use the engine from your own project, reference `RayBork.Engine.csproj` and copy the package
references from a sample project. The engine is not published to NuGet yet.

```xml
<ItemGroup>
  <PackageReference Include="PolyType" />
  <PackageReference Include="SDL3-CS" />
  <PackageReference Include="SDL3-CS.Native" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="../src/RayBork/RayBork.Engine/RayBork.Engine.csproj" />
</ItemGroup>
```

## Samples

| Sample | Shows |
| --- | --- |
| `PrimitiveGallery` | Every primitive helper, generated meshes, SSAO, and HUD text. No asset pack. |
| `Pong` | A complete 2D game with a baked texture and typed data, bloom, and gamepads. |
| `Input` | Text input, IME, clipboard, window and monitor control, and gamepad state. |
| `Text` | World-space MSDF text with alignment, wrapping, and baked fonts. |
| `RenderTarget` | Offscreen rendering, split-screen cameras, a custom post-process shader, and readback. |
| `Castle` | A small 3D game with a baked model, orbit camera, SSAO, and mouse picking. |

Each sample has its own README with exact build and run commands.

## Documentation

- [Architecture](docs/architecture.md) - projects, threads, and frame flow.
- [Renderer](docs/render.md) - how the Vulkan backend draws and presents a frame.
- [Assets](docs/assets.md) - the bake pipeline and sidecar formats.
- [Cheatsheet](docs/cheatsheet.md) - the public API in one page.

## License

RayBork is available under the MIT license. See [LICENSE](LICENSE).
