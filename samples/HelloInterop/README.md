# HelloInterop

`HelloInterop` is the M1 diagnostic host and Native AOT sample. It opens an SDL3 window, runs
`Setup`, `Tick`, and `Shutdown` on the tick thread, and logs the engine/SDL versions, window and DPI
state, monitors, input, gamepads, and frame timing. It exits on the window close request, Escape, or
a scripted frame limit.

The host uses `RayBorkAppBuilder` with a resizable window, a 60 FPS tick cap, and a 0.25 second
maximum delta. It is an `Exe` with `PublishAot=true`, so console logging is visible and the AOT gate
can run it on Windows and Linux.

## Build and run

From the repository root:

```powershell
dotnet build samples/HelloInterop/HelloInterop.csproj -c Debug
dotnet run --project samples/HelloInterop/HelloInterop.csproj -- --frames 30
```

Pass `--help` to print the full usage text. A successful run exits `0`; a failed run prints the
exception to stderr and exits `1`; an invalid command line prints the usage to stderr and exits `2`.

## Arguments

| Argument | Contract |
| --- | --- |
| `--frames N` | Run `N` ticks, then request exit. `0` runs until the window closes. Default `0`. |
| `--title TEXT` | Window title. It must not be empty or whitespace. Default `HelloInterop`. |
| `--size WxH` | Window client size in window units. Both values must be positive. Default `1280x720`. |
| `--log-level NAME` | A defined `Microsoft.Extensions.Logging.LogLevel` name, case-insensitive: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None`. Default `Information`. |
| `--help` | Print usage and exit `0`. |

## Controls

| Input | Behavior |
| --- | --- |
| Escape | Requests a graceful exit. |
| Space, Enter, Tab, F1, F11, F12 | Logged as key presses. |
| Mouse buttons and wheel | Button press/release edges and the vertical wheel are logged. |
| Gamepad | Connect/disconnect, plus South/East/West/North, shoulder, and Start button edges are logged. |
| Window close | Requests a graceful exit, like Escape. |

Tick-time, FPS, and frame count are logged every 60 ticks. A tick with no window, keyboard, mouse,
or gamepad change logs nothing.

The sample uses `var` for monitor locals because `RayBork.Monitor` collides with
`System.Threading.Monitor` under implicit usings. Callers that import `System.Threading` should
qualify `RayBork.Monitor` or use `var`.

## Native AOT

Publish and run the Windows host from the repository root:

```powershell
dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r win-x64 -p:PublishAot=true
samples/HelloInterop/bin/Release/net11.0/win-x64/publish/HelloInterop.exe --frames 30
```

Publish and run the Linux host inside WSL from the repository root:

```bash
$HOME/.dotnet/dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r linux-x64 -p:PublishAot=true
samples/HelloInterop/bin/Release/net11.0/linux-x64/publish/HelloInterop --frames 30
```

When the WSL display is unavailable, run the Linux binary with SDL's dummy video driver:

```bash
SDL_VIDEODRIVER=dummy samples/HelloInterop/bin/Release/net11.0/linux-x64/publish/HelloInterop --frames 30
```

Both publishes must finish with zero AOT/trim warnings, and each published host must exit `0`.
Publish output is generated under `samples/HelloInterop/bin/` and is gitignored.
