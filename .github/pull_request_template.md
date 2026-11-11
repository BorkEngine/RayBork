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
- [ ] `dotnet test src/RayBork/RayBork.slnx -c Debug --no-build`
- [ ] `dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore`
- [ ] `pwsh scripts/check-docs.ps1`
- [ ] AOT publish when the sample exists
- [ ] GPU/offscreen tests when renderer behavior changed

## Docs

<!-- Owning docs updated; "None" only for docs-only changes. -->

## Risks and follow-ups

<!-- Known risks, skipped gates with reasons, and follow-ups. -->
