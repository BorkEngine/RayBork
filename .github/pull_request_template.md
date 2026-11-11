# Pull request

<!-- The PR title becomes the squash commit message. Use "api: ...", "docs: ...", "fix: ...", or "test: ...". -->

## Goal

<!-- What changes and why. Link the issue. -->

## Scope

<!-- What is included. Call out public API or behavior changes. -->

## Decisions

<!-- Only decisions that affect the contract; otherwise "None". -->

## Verification

- [ ] `dotnet build src/RayBork/RayBork.slnx -c Debug`
- [ ] `dotnet build src/RayBork/RayBork.slnx -c Release`
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore`
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] `git diff --check`
- [ ] HelloInterop AOT publish for `win-x64` on Windows: `dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r win-x64 -p:PublishAot=true`
- [ ] HelloInterop AOT publish for `linux-x64` from the repository root in WSL (the .NET 11 SDK must be on `PATH`): `dotnet publish samples/HelloInterop/HelloInterop.csproj -c Release -r linux-x64 -p:PublishAot=true`
- [ ] Both AOT publishes finish with zero AOT/trim warnings; the host runs and exits.

<!-- AOT and GPU gates are local. CI covers build-test on Windows and Ubuntu, docs, and pr-title. -->

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
