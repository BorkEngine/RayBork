# Pull request

<!-- The PR title becomes the squash commit message. Use "api: ...", "docs: ...", "fix: ...", or "test: ...". -->

## Goal

<!-- What changes and why. Link the issue. -->

## Scope

<!-- What is included. Call out public API or behavior changes. -->

## Decisions

<!-- Only decisions that affect the contract; otherwise "None". -->

## Verification

CI runs unit, architecture, and GPU tests: `build-test` on Windows and Ubuntu, plus the Ubuntu `gpu`
job under Xvfb with lavapipe. The `format`, `docs`, and `pr-title` checks also run in CI: `format`
restores the pinned CSharpier 1.4.0 tool with `dotnet tool restore` and runs
`dotnet csharpier check .` over the whole repository, samples included. Locally,
`dotnet tool restore` and `dotnet csharpier format .` apply the same layout. The sample solutions,
the Native AOT `win-x64` and `linux-x64` publishes, and the Windows and Linux host runs remain local;
report their results. The M9 AOT gate is `samples/Input`; `samples/RenderTarget` remains the previous
M8 gate and stays buildable and publishable. WSL is a Linux build environment only: Linux host runs
use the conformant lavapipe ICD, not WSLg hardware Vulkan. The samples own their solutions and are
not part of `src/RayBork/RayBork.slnx`; a sample that has bakeable sources bakes its pack beside the
sample executable, so no manual bake and no `--assets` argument are required.

- [ ] `dotnet build src/RayBork/RayBork.slnx -c Debug`
- [ ] `dotnet build src/RayBork/RayBork.slnx -c Release`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --filter "Category=Gpu"`
- [ ] `dotnet tool restore`
- [ ] `dotnet csharpier check .` (the root check covers every sample solution)
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] `git diff --check`
- [ ] `dotnet build samples/Input/Input.slnx -c Debug`
- [ ] `dotnet build samples/Input/Input.slnx -c Release`
- [ ] `dotnet run --project samples/Input/Input.csproj -c Debug -- --frames 30` exits `0` (no pack, no `--assets`)
- [ ] `dotnet build samples/RenderTarget/RenderTarget.slnx -c Debug`
- [ ] `dotnet build samples/RenderTarget/RenderTarget.slnx -c Release`
- [ ] `dotnet run --project samples/RenderTarget/RenderTarget.csproj -c Debug -- --frames 30` exits `0`, and `samples/RenderTarget/bin/Debug/net11.0/rendertarget.pck` exists
- [ ] `dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Debug`
- [ ] `dotnet build samples/PrimitiveGallery/PrimitiveGallery.slnx -c Release`
- [ ] `dotnet run --project samples/PrimitiveGallery/PrimitiveGallery.csproj -c Debug -- --frames 30` exits `0` (no pack)
- [ ] `dotnet build samples/Castle/Castle.slnx -c Debug`
- [ ] `dotnet build samples/Castle/Castle.slnx -c Release`
- [ ] `dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 30` exits `0`, and `samples/Castle/bin/Debug/net11.0/castle.pck` exists
- [ ] `dotnet build samples/Pong/Pong.slnx -c Debug`
- [ ] `dotnet build samples/Pong/Pong.slnx -c Release`
- [ ] `dotnet run --project samples/Pong/Pong.csproj -c Debug -- --frames 30` exits `0`, and `samples/Pong/bin/Debug/net11.0/pong.pck` exists
- [ ] `dotnet build samples/Text/Text.slnx -c Debug`
- [ ] `dotnet build samples/Text/Text.slnx -c Release`
- [ ] `dotnet run --project samples/Text/Text.csproj -c Debug -- --frames 30` exits `0`, and `samples/Text/bin/Debug/net11.0/text.pck` exists
- [ ] Input is the M9 AOT gate. `win-x64` publish on Windows: `dotnet publish samples/Input/Input.csproj -c Release -r win-x64 -p:PublishAot=true`; the publish directory contains no `.pck` and no bake tooling (`RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, shaderc natives, glTF packages)
- [ ] Windows host run: `samples/Input/bin/Release/net11.0/win-x64/publish/Input.exe --frames 30` exits `0`
- [ ] `linux-x64` publish from the repository root in WSL: `$HOME/.dotnet/dotnet publish samples/Input/Input.csproj -c Release -r linux-x64 -p:PublishAot=true`; the publish directory contains no `.pck` and no bake tooling
- [ ] Linux host run under lavapipe: `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Input/bin/Release/net11.0/linux-x64/publish/Input --frames 30` exits `0`
- [ ] Both Input AOT publishes finish with zero AOT/trim warnings. `samples/RenderTarget` was the M8 AOT gate and remains buildable and publishable; `samples/Castle`, `samples/Pong`, `samples/Text`, and `samples/PrimitiveGallery` remain buildable and publishable but are not the AOT gate
- [ ] M9 manual checks (user-run, not CI): committed text on Windows and WSL/lavapipe arrives through `IInput.TextInput`, and a real IME places the native candidate window over the text field where the session exposes an IME bridge (the WSL IME candidate-window step applies only where the WSL session exposes one; committed text through `TextInput` remains the unconditional check); clipboard copy/cut/paste works through `Ctrl+C`/`Ctrl+X`/`Ctrl+V`; `Alt+Enter` toggles fullscreen and `B` toggles borderless; `M` moves to the next monitor in stable-index order and a DPI change keeps the HUD aligned; losing focus caps the tick loop at 30 FPS and restoring returns to the display cadence; a gamepad hotplugs with a stable `InstanceId` and rumble is best-effort

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
