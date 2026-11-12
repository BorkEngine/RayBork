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
job under Xvfb with lavapipe. The `docs` and `pr-title` checks also run in CI. The Native AOT
`win-x64` and `linux-x64` publishes and the Castle sample runs remain local; report their results.

- [ ] `dotnet build src/RayBork/RayBork.slnx -c Debug`
- [ ] `dotnet build src/RayBork/RayBork.slnx -c Release`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore`
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] `git diff --check`
- [ ] Castle pack bake: `dotnet run --project samples/Castle.Assets/Castle.Assets.csproj -c Release -- bake` produces `samples/Castle.Assets/bin/Release/net11.0/castle.pck`
- [ ] Castle AOT publish for `win-x64` on Windows: `dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true`
- [ ] Windows host run: `samples/Castle/bin/Release/net11.0/win-x64/publish/Castle.exe --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30` exits `0` with nonzero draw calls and instances
- [ ] Castle AOT publish for `linux-x64` from the repository root in WSL: `dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true`
- [ ] WSL host run: `samples/Castle/bin/Release/net11.0/linux-x64/publish/Castle --assets samples/Castle.Assets/bin/Release/net11.0/castle.pck --frames 30` exits `0` with nonzero draw calls and instances; use the WSLg display (`DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir`)
- [ ] Both AOT publishes finish with zero AOT/trim warnings

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
