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
the Native AOT `win-x64` and `linux-x64` publishes, and the Windows and Linux host runs remain local;
report their results. WSL is a Linux build environment only: Linux host runs use the conformant
lavapipe ICD, not WSLg hardware Vulkan. The samples own their solutions and are not part of
`src/RayBork/RayBork.slnx`; each sample build bakes its pack beside the sample executable, so no
manual bake and no `--assets` argument are required.

- [ ] `dotnet build src/RayBork/RayBork.slnx -c Debug`
- [ ] `dotnet build src/RayBork/RayBork.slnx -c Release`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore`
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] `git diff --check`
- [ ] `dotnet build samples/Pong/Pong.slnx -c Debug`
- [ ] `dotnet build samples/Pong/Pong.slnx -c Release`
- [ ] `dotnet format samples/Pong/Pong.slnx --verify-no-changes --no-restore`
- [ ] `dotnet run --project samples/Pong/Pong.csproj -c Debug -- --frames 30` exits `0`, and `samples/Pong/bin/Debug/net11.0/pong.pck` exists
- [ ] `dotnet build samples/Castle/Castle.slnx -c Debug`
- [ ] `dotnet build samples/Castle/Castle.slnx -c Release`
- [ ] `dotnet format samples/Castle/Castle.slnx --verify-no-changes --no-restore`
- [ ] `dotnet run --project samples/Castle/Castle.csproj -c Debug -- --frames 30` exits `0`, and `samples/Castle/bin/Debug/net11.0/castle.pck` exists
- [ ] `dotnet build samples/Text/Text.slnx -c Debug`
- [ ] `dotnet build samples/Text/Text.slnx -c Release`
- [ ] `dotnet format samples/Text/Text.slnx --verify-no-changes --no-restore`
- [ ] `dotnet run --project samples/Text/Text.csproj -c Debug -- --frames 30` exits `0`, and `samples/Text/bin/Debug/net11.0/text.pck` exists
- [ ] Pong AOT publish for `win-x64` on Windows: `dotnet publish samples/Pong/Pong.csproj -c Release -r win-x64 -p:PublishAot=true`; the publish directory contains `pong.pck` beside the executable and no bake tooling (`Pong.Assets.dll`, `RayBork.Bake.dll`, Tomlyn, StbImageSharp, BCnEncoder, shaderc natives, glTF packages)
- [ ] Windows host run: `samples/Pong/bin/Release/net11.0/win-x64/publish/Pong.exe --frames 30` exits `0` and loads `pong.pck` beside the executable
- [ ] Pong AOT publish for `linux-x64` from the repository root in WSL: `dotnet publish samples/Pong/Pong.csproj -c Release -r linux-x64 -p:PublishAot=true`; the publish directory contains `pong.pck` beside the executable and no bake tooling
- [ ] Linux host run under lavapipe: `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json DISPLAY=:0 XDG_RUNTIME_DIR=/mnt/wslg/runtime-dir samples/Pong/bin/Release/net11.0/linux-x64/publish/Pong --frames 30` exits `0`
- [ ] Both Pong AOT publishes finish with zero AOT/trim warnings; the Text, Castle, and PrimitiveGallery samples remain buildable but are not the AOT gate

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
