# AGENTS.md — RayBork Agent Workflow

RayBork is a C# 3D game framework. Read the owning document before changing a contract; update it in
the same change. A direct user instruction wins over this file. If documents conflict, stop and ask.

## 1. Reference documents

| Document | Read it for |
| --- | --- |
| [README.md](README.md) | Status, targets, document map. |
| [docs/api.md](docs/api.md) | Public API, lifecycle, assets, rendering, shader ABI, input, threading, AOT. |
| [docs/architecture.md](docs/architecture.md) | Projects, threads, frame handoff, renderer seam, asset pipeline, invariants. |
| [docs/roadmap.md](docs/roadmap.md) | Milestones, gates, parity matrix, risks, post-1.0 work. |
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Dependency inventory, licenses, and notice policy. |
| [.github/pull_request_template.md](.github/pull_request_template.md) | Required PR body sections. |

- Decisions live in the docs they affect; read closed PRs for history, not memory or status files.
- Do not create status, progress, report, planning, or evidence documents; the request and final
  response are the report. `AGENTS.md` is policy, not a progress log; do not edit it unless the
  user asks or add milestone details.

## 2. Code style

[CSharpier](https://csharpier.com/) 1.4.0 formats C# and project XML. The pinned local tool is
[.config/dotnet-tools.json](.config/dotnet-tools.json): run `dotnet tool restore`, then
`dotnet csharpier format .` from the repository root. CI and the §5 gates run
`dotnet csharpier check .`.

- Allman braces: the opening brace goes on its own line, and `else`/`catch`/`finally` start a new
  line after the closing brace.

```csharp
if (amount <= 0)
{
    return;
}
else if (Value + amount > MAX_COUNT)
{
    Value = MAX_COUNT;
}
```

- CSharpier owns whitespace layout for C# and project XML (`csproj`, `props`, `targets`, `slnx`, and
  `NuGet.config`) at a 100-column print width, four-space indentation, and LF.
  [.editorconfig](.editorconfig) still owns naming, accessibility, qualification, and analyzer
  policy and must not be weakened; IDE0055 is disabled because its formatting options conflict with
  CSharpier. Do not hand-format CSharpier output, and do not add `csharpier-ignore` without a
  reviewed reason.
- `SCREAMING_SNAKE_CASE` constants; PascalCase types, members, and enum values; `_camelCase`
  private fields; camelCase parameters and locals; `I`-prefixed interfaces.
- File-scoped namespaces; usings outside the namespace, `System` first; explicit accessibility.
  CSharpier owns the layout: a 100-column print width, four spaces, LF, and a final newline. One
  primary type per file.
- `internal` by default; `public` only for the documented contract. `sealed` by default; composition
  over inheritance in hot paths. Public API changes need user approval. XML docs on public API and
  non-obvious internals; comments explain why. No dead code, `TODO`, or placeholders.
- `System.Threading.Lock` for locks; never lock on an `object`; never hold a lock across an `await`
  or blocking wait. Async methods end in `Async`; no `async void`; no blocking async work in hot
  paths.
- Samples are small, readable user-facing examples, not test fixtures. Test-only logging,
  assertions, and command-line surface belong in tests or gates. Remove temporary diagnostics.

## 3. Milestone loop

The lead owns the milestone end to end and does not implement tasks. Each task owns one branch and
one worktree, and one workflow runs it. There is no time pressure: cut the work into the finest
independently reviewable tasks, with disjoint file ownership, and start every independent task
together. Prefer more small parallel tasks over fewer large ones; sequence only real dependencies.

1. Read the owning docs and related closed PRs; state the change in one paragraph. Resolve every
   question and present the plan, including the task graph; wait for explicit approval.
2. Create `milestone/<name>` from `develop`; commit only prerequisites that later tasks need. For
   each independent task create `milestone/<name>-tasks/<task>` and a sibling worktree. Task branches
   stay local; only the milestone is pushed. A dependent task starts only after its prerequisite
   merges.
3. Write the task brief: goal, context, file ownership, constraints, acceptance commands, branch,
   base, and risk. Start one background `workflow` from the task worktree (§4) for each task; start
   every independent task, with no concurrency cap.
4. Collect each job with `job_output`; for each `clean` result, verify the worktree
   (`git status --short`, `git diff --check`), merge with `git merge --no-ff`, remove the worktree,
   and delete the branch. Never merge `blocked` or `failed`; resolve it with the user or a reviewed
   `subagent` fix. Resolve conflicts on the milestone branch and record them in the PR body.
5. Run the §5 gates; a failure blocks the work report, so fix it with a new task or report it. Write
   the work report (changed, verification, docs, risks, follow-ups) and wait for approval. Then push,
   open one PR against `develop`, delete every local milestone branch, return to `develop`, and stop.

Rules:
- Ask before product, API, scope, ownership, default, or behavior decisions; a skipped question is
  not permission to guess.
- If the environment blocks a gate, stop and report the exact blocker. Never skip, weaken, or
  substitute a check.
- Ask for visual feedback; do not build screenshot or capture tooling.
- The plan is the session task list; no planning or status documents. Write docs as if merged.
  While a task workflow runs, only its agents touch its worktree.

## 4. Task workflow

A task workflow is one `workflow` run started from the task worktree. It implements, reviews, and
fixes the task with fresh agents; every prompt carries the brief and the current diff. The normal
loop does not use a separate implementer or reviewer subagent. Pass
`args = {task, brief, base, risky, acceptance}`; `risky` adds the tests lens.

- **Start from the worktree.** `cd` to it, start the run with `run_in_background: true`, then `cd`
  back. The engine captures the start directory; the run keeps that worktree after the lead moves.
- **Implement.** A fresh agent gets the brief, file ownership, constraints, branch, and acceptance
  commands. It works only in the worktree, tests, commits, and returns `{status, head, summary,
  acceptance: [{name, status, detail}]}`. It does not create branches, merge, push, or edit outside
  the task.
- **Review.** One fresh reviewer per lens in `parallel`, read-only: no edits, builds, commits, or
  branch changes. Lenses are design (API, architecture, docs, tests) and performance (hot paths,
  allocations, threading); add tests for public API, renderer, threading, or AOT. Each reviews
  `git diff <base>..HEAD` and returns `{severity: "blocking" | "nit", file, line?, problem, fix}`
  findings; an empty diff is blocking.
- **Fix and loop.** A fresh fixer gets the brief, blocking findings, and current diff; it fixes,
  tests, and commits without amending, rebasing, editing outside the task, or merging/pushing. It
  returns the same shape as the implementer. Re-review after each fix. No blocking findings returns
  `{task, status: "clean", rounds, head, acceptance, summary, nits}`; after three reviews, `blocked`
  with the remaining findings. A `null` or failed child result is retried once, then returns
  `{status: "failed", reason}`; nits go to the lead.
- **Limits.** The script has no shell, filesystem, or network access. Use `phase()`/`log()` and
  compact JSON. A workflow child is a leaf; do not nest a workflow.

## 5. Gates

Run these on the milestone branch after the last task merges and before the work report:

```powershell
dotnet build src/RayBork/RayBork.slnx -c Debug
dotnet build src/RayBork/RayBork.slnx -c Release
dotnet test src/RayBork/RayBork.slnx -c Debug --no-build
dotnet test src/RayBork/RayBork.slnx -c Debug --filter "Category=Gpu"
dotnet tool restore
dotnet csharpier check .
pwsh scripts/check-docs.ps1
git diff --check
```

- The root CSharpier check covers the engine, bake, tests, build tooling, and every sample solution,
  so there is no per-solution format command. Build every sample solution: each is a separate
  solution outside `src/RayBork/RayBork.slnx` and bakes its pack beside the executable; no manual
  bake or `--assets`.
- Publish the milestone's AOT gate for `win-x64` and `linux-x64` and run both hosts; exact commands
  are in the README and the gate sample's README. Windows uses the hardware driver; Linux uses
  lavapipe with `VK_ICD_FILENAMES`; WSLg dzn is not a target. Both publishes have zero AOT/trim
  warnings and no bake tooling in the publish directory.
- GPU tests that cannot run are skips with reasons; never drop one silently. Linux GPU coverage is
  lavapipe only. AOT stays local. Shader SPIR-V is committed; regenerate only with
  `pwsh scripts/compile-shaders.ps1` and the Vulkan SDK.
- Run the Windows and WSL groups together. In each, restore once, then Debug+Release, samples, AOT,
  and hosts. Sample builds need a current engine build; AOT needs the RID's engine Release. Never
  build the same project/configuration twice in one tree; use `--no-restore` and
  `-p:BuildProjectReferences=false` only after the prerequisite. WSL: rsync -a --delete to
  `$HOME/raybork` with `--include='/samples/*/*/Assets/***'` and `bin/`/`obj/` excludes so source
  `obj` directories survive; that clone is the build tree, the Windows checkout is only the sync
  source. Use `$HOME/.dotnet/dotnet`; never build `/mnt/d`.
- Run tests, format, docs, and diff checks after the builds that feed them, and report every failed
  group or skipped gate before the work report.

A docs-only change runs `pwsh scripts/check-docs.ps1` and `git diff --check`; it does not need the
build, test, or AOT gates.

## 6. Testing

- xUnit v3 and Moq. Every behavior change gets a test; every fix gets a regression test.
- Category traits in `RayBork.Tests`: unit, GPU integration, AOT, performance. No golden images.
- Deterministic tests: no sleeps, retries, races, or order dependence; join workers and assert state.
- Cover lifecycle, disposal, thread affinity, asset retention, pack corruption, path normalization,
  shader snapshots, and render statistics. Renderer tests use validation cleanliness, counts,
  readback tolerances, and leak checks.
- Do not commit generated packs, images, publish output, or test artifacts.

## 7. Pull requests and git

The GitHub remote is `BorkEngine/RayBork`; `develop` is the integration branch. One milestone lands
by one PR from its milestone branch.

- Work report before the PR, then wait for approval. Push the milestone branch, open the PR against
  `develop`, delete every local milestone branch, return to `develop`, and stop.
- PR creation ends the session: do not wait for, poll, or fix GitHub Actions afterward. CI is the
  user's merge gate; failures are reported later. Never disable or weaken a check.
- Fill the PR body from [.github/pull_request_template.md](.github/pull_request_template.md): goal,
  scope, decisions, verification, docs, risks, follow-ups. The PR body is the work report.
- The user merges and handles remote cleanup. Before merge: `build-test` on Windows and Ubuntu,
  `format`, `docs`, `pr-title`, one approval, and no unresolved comments. Squash and merge with
  `api: ...`, `build: ...`, `docs: ...`, `fix: ...`, or `test: ...`.
- Do not force-push or rewrite published history. Do not merge to a protected branch.
- Commits are scoped and imperative. Never commit `bin/`, `obj/`, publish output, IDE state, logs,
  secrets, packs, or generated assets.

## 8. Review checklist

- [ ] Scope matches; no unrelated refactors or formatting churn.
- [ ] API/behavior matches `api.md`; architecture/threading/renderer matches `architecture.md`.
- [ ] Every merged task returned `clean`; blocked or failed tasks were resolved and reviewed.
- [ ] Tests cover behavior and failure modes; no golden images.
- [ ] Docs updated; links resolve; no work-in-progress wording.
- [ ] `dotnet csharpier check .` passes; CSharpier-owned layout with no manual re-formatting.
- [ ] Full gates pass; skipped gates reported; AOT clean.
- [ ] PR body complete; local branches and worktrees cleaned.
