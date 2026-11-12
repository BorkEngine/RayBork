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
job under Xvfb with lavapipe. The `docs` and `pr-title` checks also run in CI. The sample solutions,
the Native AOT `win-x64` and `linux-x64` publishes, and the Castle sample runs remain local; report
their results. The samples own their solutions and are not part of `src/RayBork/RayBork.slnx`; each
sample build bakes its pack beside the sample executable, so no manual bake and no `--assets`
argument are required.

- [ ] `dotnet build src/RayBork/RayBork.slnx -c Debug`
- [ ] `dotnet build src/RayBork/RayBork.slnx -c Release`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore`
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] `git diff --check`
- [ ] `dotnet build samples/Castle/Castle.slnx -c Debug`
- [ ] `dotnet build samples/Castle/Castle.slnx -c Release`
- [ ] `dotnet format samples/Castle/Castle.slnx --verify-no-changes --no-restore`
- [ ] `dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 30` exits `0`, and `samples/Castle/bin/Debug/net11.0/castle.pck` exists
- [ ] `dotnet build samples/HelloInterop/HelloInterop.slnx -c Debug`
- [ ] `dotnet build samples/HelloInterop/HelloInterop.slnx -c Release`
- [ ] `dotnet format samples/HelloInterop/HelloInterop.slnx --verify-no-changes --no-restore`
- [ ] `dotnet run --project samples/HelloInterop/HelloInterop.csproj -c Debug -- --frames 30` exits `0`, auto-mounts the pack, and `samples/HelloInterop/bin/Debug/net11.0/hello.pck` exists
- [ ] Castle AOT publish for `win-x64` on Windows: `dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true`; the publish directory contains `castle.pck` beside the executable and no bake tooling (`Castle.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, shaderc natives, glTF packages)
- [ ] Windows host run: `samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --frames 30` exits `0` with nonzero draw calls and instances; it loads `castle.pck` beside the executable
- [ ] Castle AOT publish for `linux-x64` from the repository root in WSL: `dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true`; the publish directory contains `castle.pck` and no bake tooling
- [ ] WSL host run: `samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --frames 30` exits `0` with nonzero draw calls and instances on the Mesa dzn hardware device; use the WSLg display (`DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir`). Optionally run the same published host under `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json` for a conformant software-only check.
- [ ] Both AOT publishes finish with zero AOT/trim warnings

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
