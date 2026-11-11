# AGENTS.md — RayBork Agent Workflow

RayBork is a C# 3D game framework. Read the owning document before changing a contract; update it
in the same change. A direct user instruction wins over this file. If documents conflict, stop and
ask.

## 1. Reference documents

| Document | Read it for |
| --- | --- |
| [README.md](README.md) | Status, targets, document map. |
| [docs/api.md](docs/api.md) | Public API, lifecycle, assets, rendering, shader ABI, input, threading, AOT. |
| [docs/architecture.md](docs/architecture.md) | Projects, threads, frame handoff, renderer seam, asset pipeline, invariants. |
| [docs/roadmap.md](docs/roadmap.md) | Milestones, gates, parity matrix, risks, post-1.0 work. |
| [.editorconfig](.editorconfig) | Formatting, naming, and style. |
| [Directory.Build.props](Directory.Build.props) | Target framework, analyzers, warnings-as-errors. |
| [global.json](global.json) | Pinned .NET SDK. |
| [.github/workflows/ci.yml](.github/workflows/ci.yml) | GitHub Actions build/test/docs jobs and triggers. |
| [.github/workflows/pr-title.yml](.github/workflows/pr-title.yml) | PR title convention check. |
| [.github/pull_request_template.md](.github/pull_request_template.md) | Required PR body sections. |
| [scripts/check-docs.ps1](scripts/check-docs.ps1) | Markdown link and code-fence gate. |

- There is no decision log. Decisions live in the docs they affect: API contracts in `api.md`,
  runtime shape in `architecture.md`, delivery in `roadmap.md`.
- Do not create status, progress, report, planning, or evidence documents. The user's request and
  the final response are the report.
- Do not create golden-image tests. Use unit tests, GPU integration tests that assert validation and
  statistics, and offscreen pixel probes only where a numeric assertion is stable.

## 2. Code style

The build enforces [.editorconfig](.editorconfig); do not weaken it. Run `dotnet format`.

- K&R braces: the opening brace stays on the declaration line, `else`/`catch`/`finally` stay on the
  closing-brace line.

```csharp
public sealed class Counter {
    private const int MAX_COUNT = 10;

    public int Value { get; private set; }

    public void Add(int amount) {
        if (amount <= 0) {
            return;
        } else if (Value + amount > MAX_COUNT) {
            Value = MAX_COUNT;
        } else {
            Value += amount;
        }
    }
}
```

- Constants are `SCREAMING_SNAKE_CASE`. Types, members, and enum values are PascalCase.
  `_camelCase` private fields; camelCase parameters and locals; `I`-prefixed interfaces.
- File-scoped namespaces; usings outside the namespace, `System` first; explicit accessibility;
  four spaces; LF; final newline.
- Prefer one primary type per file. `internal` by default; `public` only for the documented
  contract. Public API changes require user approval.
- XML docs on public API and on internal code whose lifetime, threading, or exceptions are not
  obvious. Comments explain why, not what.
- `sealed` by default; composition over inheritance in hot paths. No regions, dead code, `TODO`, or
  placeholders in shipped code.
- Use `System.Threading.Lock` for locks; never lock on a plain `object`; never hold a lock across an
  `await` or blocking wait.
- Async methods end in `Async`; no `async void`; no blocking on async work in hot paths.

## 3. Workflow

1. Read the owning docs and state the change in one paragraph.
2. Resolve public API, behavior, units, defaults, ownership, and dependency questions with the user
   before editing. Use `ask_user_question` with 2-4 concrete options; put the recommended option
   first. Batch related questions and wait for the answer.
3. Split the work into independently reviewable steps. Keep unrelated refactors and formatting
   churn out of the change.
4. Implement, test, update the owning docs, and run the gates.
5. Report result, commands, skipped gates with reasons, and open questions. Do not hide a failure
   behind a success summary.

No implementation starts before the API and architecture documents agree. No new dependency enters
`RayBork.Engine` unless it is pinned, license-reviewed, and passes the Native AOT publish gate.

## 4. Gates

```powershell
dotnet build src/RayBork/RayBork.slnx -c Debug
dotnet build src/RayBork/RayBork.slnx -c Release
dotnet test src/RayBork/RayBork.slnx -c Debug --no-build
dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
dotnet publish samples/Castle/Castle.csproj -c Release -r win-x64 -p:PublishAot=true
dotnet publish samples/Castle/Castle.csproj -c Release -r linux-x64 -p:PublishAot=true
```

The exact sample paths are added in M11. Until then, the AOT gate uses the current sample host.
Both publishes must finish with zero AOT/trim warnings. GPU tests render offscreen and do not
require a visible window. A GPU test that cannot run on the host is reported as a skip with the
reason, never silently dropped.

## 5. Testing

- xUnit v3 and Moq. Every behavior change gets a test; every bug fix gets a regression test.
- Deterministic tests: no sleeps, wall-clock races, retries, or order dependence. Join workers and
  assert state, not scheduling order.
- Cover lifecycle, disposal, thread affinity, asset retention, pack corruption, path normalization,
  shader-state snapshots, and render statistics.
- No golden-image suite. Renderer tests use validation-layer cleanliness, draw/instance counts,
  readback values with explicit tolerances, and crash/leak checks.
- Do not commit generated packs, images, publish output, or test artifacts.

## 6. Pull requests

The GitHub remote is `BorkEngine/RayBork`. `develop` is the integration branch; work happens on a
short-lived branch and lands by pull request.

- Branch from the current `develop` using a lower-case slug: `git switch -c <slug> develop`.
- Open the PR against `develop`. Do not commit or push directly to `develop`.
- Keep the PR scoped to one change. Draft it until CI is green and the description is complete.
- Use [.github/pull_request_template.md](.github/pull_request_template.md): goal, scope, decisions,
  verification, docs, risks and follow-ups. The PR body is the report; do not create a status doc.
- Required before merge: GitHub Actions `build-test` on Windows and Ubuntu, `docs`, `pr-title`, and
  at least one approval with no unresolved comments. The user performs the merge.
- Squash and merge. The PR title becomes the squash commit message and must follow the commit
  convention (`api: ...`, `docs: ...`, `fix: ...`, `test: ...`). Do not create a merge commit,
  force-push, or rewrite published history.
- If CI fails, fix the same branch and push again. Never disable or weaken a check to make it green.
- AOT and GPU checks remain local until their CI jobs exist; report skipped gates with reasons.

```powershell
git switch -c <slug> develop
# edit, build, test, format, docs check
git push -u origin <slug>
gh pr create --base develop --head <slug> --title "<type: summary>"
```

## 7. Git

- Commits are scoped and imperative: `api: ...`, `docs: ...`, `fix: ...`, `test: ...`.
- Do not commit `bin/`, `obj/`, publish output, IDE state, logs, secrets, or generated assets.
- Do not force-push or rewrite published history. Do not merge to a protected branch without the
  user's instruction.
- Run `git diff --check` before review.

## 8. Review checklist

- [ ] Scope matches the request; no unrelated refactors or formatting churn.
- [ ] Public API, units, defaults, ownership, and exceptions match `api.md`.
- [ ] Architecture, threading, and renderer-backend rules match `architecture.md`.
- [ ] Tests cover the behavior and failure modes; no golden-image dependency.
- [ ] Docs updated in the same change; relative links resolve.
- [ ] GitHub Actions `build-test`, `docs`, and `pr-title` are green on the PR.
- [ ] PR uses the template, has an approval, and is squash-merged with a conventional title.
- [ ] `dotnet format` and `git diff --check` pass.
- [ ] AOT publish passes for the supported RIDs.
- [ ] No `TODO`, placeholder, dead code, or unreported skipped gate.
