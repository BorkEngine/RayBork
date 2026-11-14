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
| [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) | Dependency inventory, licenses, and the runtime/test/tooling notice policy. |
| [.editorconfig](.editorconfig) | Formatting, naming, and style. |
| [Directory.Build.props](Directory.Build.props) | Target framework, analyzers, warnings-as-errors. |
| [global.json](global.json) | Pinned .NET SDK. |
| [.github/workflows/ci.yml](.github/workflows/ci.yml) | GitHub Actions build/test/docs jobs and triggers. |
| [.github/workflows/pr-title.yml](.github/workflows/pr-title.yml) | PR title convention check. |
| [.github/pull_request_template.md](.github/pull_request_template.md) | Required PR body sections. |
| [scripts/check-docs.ps1](scripts/check-docs.ps1) | Markdown link and code-fence gate. |

- There is no decision log. Decisions live in the docs they affect: API contracts in `api.md`,
  runtime shape in `architecture.md`, delivery in `roadmap.md`.
- To learn what earlier work did or decided, read the closed pull requests. Do not rely on
  memory, status files, or this workflow document.
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
- Samples are user-facing examples, not test fixtures. Keep them small, readable, and commented like
  the raylib example they mirror. Test-only logging, assertions, and command-line surface belong in
  tests or gates, not in the sample. Temporary diagnostics must be removed before the final report.

## 3. Roles and workflow

### 3.1 Roles

- The agent that receives the milestone request is the **lead**. The lead owns the milestone from
  the first question to the pull request.
- The lead must present an implementation plan to the user and wait for explicit approval before
  implementing anything.
- The lead must not make product, API, scope, ownership, default, or behavior decisions alone. Ask
  the user; a skipped question is not permission to guess.
- If the lead or a worker is blocked by the environment, a missing tool/SDK, unavailable root
  access, a failing external dependency, or any other condition that needs user action, stop and
  report the exact blocker. Do not silently skip a gate, weaken a check, or substitute a different
  environment. The user will provide what is needed or give different instructions.
- If you need visual feedback (whether a window resized, how a frame looks, colors, layout), ask the
  user. Do not guess and do not build complex screenshot/capture tooling; the user can provide
  feedback or a screenshot of the point of interest.
- A subagent is a **worker**. A worker owns exactly one task branch and one repository copy. It
  implements, tests, and commits that task. It does not create branches, merge, push, open pull
  requests, or edit files outside its task.
- The lead chooses each subagent's `reasoning_effort`: `high` for design, rendering, public API, and
  risky work; `low` for simple or mechanical tasks. Set it on the tool call, not in the prompt.
- A **review subagent** is read-only: it does not edit, branch, or commit. After every implementing
  worker reports, the lead runs a review subagent over the task diff. The review thoroughly inspects
  the code for repetition, weak API or architecture decisions, hot-path allocations and other
  performance problems (priority 1), and whether the engine stays generic, extensible, and easy to
  use (priority 2); it also inspects the tests for missing behavior and edge cases and for
  meaningless tests, and returns findings to the lead. The lead sends the findings to the
  implementing worker for fixes and repeats until the review is clean. A review subagent is not a
  worker and does not own a task branch.
- The plan is the session task list. Do not create a planning, status, progress, report, or evidence
  document.
- There is no time pressure. Split complex work into as many small, independently reviewable tasks
  as needed; there is no upper limit. Prefer a clear, correct result over speed.
- `AGENTS.md` is workflow policy, not a progress log. Do not edit it unless the user asks. Do not
  record milestone-specific details in it.

### 3.2 Branch and copy naming

- Milestone branch: `milestone/<milestone>`, created from the current `develop`.
- Task branch: `milestone/<milestone>-tasks/<task>`, created from the milestone branch.
- Task copy: a sibling `git worktree` outside the repository, one task per copy.
- Task branches stay local. Only the milestone branch is pushed for the pull request.
- After a task merges, remove its worktree and delete its branch.

### 3.3 Milestone loop

1. Read the owning documents and the closed pull requests that cover related work. State the change
   in one paragraph.
2. Resolve every open question with the user and present the implementation plan. Wait for explicit
   approval before creating task branches or changing code.
3. Create the milestone branch from `develop`. Commit only prerequisites that later tasks depend on
   directly.
4. Split the approved plan into independently reviewable tasks with disjoint file ownership and
   explicit acceptance gates. Use the session task list as the plan.
5. For each task, create the task branch from the milestone branch and a sibling worktree copy.
6. Give one worker the complete task: goal, context, file ownership, constraints, acceptance
   commands, and the branch to commit on. Choose the worker's reasoning effort per §3.1. The worker
   works only in its copy.
7. After the worker reports, run the review subagent over the task diff (§3.1). Send its findings to
   the implementing worker and repeat until the review is clean. The worker runs the task's
   acceptance commands itself; the lead does not rerun them for a single task.
8. Merge the task branch into the milestone branch with `git merge --no-ff`. Remove the worktree and
   delete the task branch. If a merge conflict occurs, resolve it on the milestone branch and
   record the resolution in the PR body.
9. Run the full gates only when merging task branches that ran in parallel, and after all tasks
   before the work report. A single task's gates are the worker's responsibility.
10. Before opening or updating a pull request, run the full gates, write a **work report** for the
    user (what changed, verification, docs, risks, follow-ups), and wait for the user's review.
    The user may add follow-up tasks.
11. Only after the user approves the work report: remove every task worktree and task branch, push
    the milestone branch, and open or update the single pull request against `develop`.
12. After the pull request is created, delete every local branch for that milestone, including the
    local milestone/PR branch; the remote branch remains for the PR. Use `develop` as the local
    checkout afterwards.
13. Write all documentation as if the PR is already merged. Do not leave "pending PR", "will be
    merged", "this branch", or similar work-in-progress wording in shipped docs.

## 4. Gates

```powershell
dotnet build src/RayBork/RayBork.slnx -c Debug
dotnet build src/RayBork/RayBork.slnx -c Release
dotnet test src/RayBork/RayBork.slnx -c Debug --no-build
dotnet test src/RayBork/RayBork.slnx -c Debug --filter "Category=Gpu"
dotnet format src/RayBork/RayBork.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
git diff --check
```

- Every sample is a separate solution under `samples/` and is outside
  `src/RayBork/RayBork.slnx`. Build and format every sample solution. Each sample build bakes its
  pack beside the sample executable; do not require a manual bake or an `--assets` argument.
- Publish the milestone's documented Native AOT gate for `win-x64` and `linux-x64` and run each
  published host. The current gate sample and exact host commands are recorded in the README, the
  sample README, and the PR; read them before running the gate.
- Both AOT publishes must finish with zero AOT/trim warnings and must not include bake tooling in
  the publish directory.
- GPU tests open a hidden window through the real video driver. A GPU test that cannot run on the
  host is reported as a skip with the reason, never silently dropped. CI runs the GPU-filtered suite
  under `xvfb-run` with the lavapipe ICD.
- Shader SPIR-V is regenerated with `pwsh scripts/compile-shaders.ps1` and the Vulkan SDK; CI and
  normal builds consume the committed blobs and never need `glslc`.

## 5. Testing

- xUnit v3 and Moq. Every behavior change gets a test; every bug fix gets a regression test.
- Mark tests with the category traits in `RayBork.Tests`: unit, GPU integration, AOT, and
  performance. There is no golden-image category.
- Deterministic tests: no sleeps, wall-clock races, retries, or order dependence. Join workers and
  assert state, not scheduling order.
- Cover lifecycle, disposal, thread affinity, asset retention, pack corruption, path normalization,
  shader-state snapshots, and render statistics.
- No golden-image suite. Renderer tests use validation-layer cleanliness, draw/instance counts,
  readback values with explicit tolerances, and crash/leak checks.
- Do not commit generated packs, images, publish output, or test artifacts.

## 6. Pull requests

The GitHub remote is `BorkEngine/RayBork`. `develop` is the integration branch. A milestone lands by
one pull request from its milestone branch.

- Before opening or updating the PR, provide the work report and wait for approval. Only then push
  the milestone branch and create/update the PR against `develop`.
- Run the full gates on the milestone branch before pushing. Keep the PR draft until CI is green and
  the description is complete.
- Keep the PR scoped to the milestone. Use [.github/pull_request_template.md](.github/pull_request_template.md):
  goal, scope, decisions, verification, docs, risks and follow-ups. The PR body is the report; do
  not create a status doc.
- Required before merge: GitHub Actions `build-test` on Windows and Ubuntu, `docs`, `pr-title`, and
  at least one approval with no unresolved comments. The user performs the merge.
- Squash and merge. The PR title becomes the squash commit message and must follow the commit
  convention (`api: ...`, `build: ...`, `docs: ...`, `fix: ...`, `test: ...`). Do not create a merge
  commit, force-push, or rewrite published history.
- If CI fails, fix the same branch and push again; if the local branch was already deleted, fetch
  the remote milestone branch first. Never disable or weaken a check to make it green.
- After the PR is created, clean up every local branch for the milestone, including the local
  milestone/PR branch, and switch back to `develop`. The remote branch stays with the PR.
- After the user merges, delete the milestone branch remotely if it still exists.
- AOT checks remain local; the `gpu` CI job runs the GPU tests. Report skipped gates with reasons.

## 7. Git

- Branch names follow §3.2. Task branches are local and are deleted after merge.
- Commits are scoped and imperative: `api: ...`, `build: ...`, `docs: ...`, `fix: ...`, `test: ...`.
- Do not commit `bin/`, `obj/`, publish output, IDE state, logs, secrets, or generated assets.
- Do not force-push or rewrite published history. Do not merge to a protected branch without the
  user's instruction.
- Run `git diff --check` and `git worktree list` before review; remove stale task worktrees.

## 8. Review checklist

- [ ] Scope matches the request; no unrelated refactors or formatting churn.
- [ ] Public API, units, defaults, ownership, and exceptions match `api.md`.
- [ ] Architecture, threading, and renderer-backend rules match `architecture.md`.
- [ ] Tests cover the behavior and failure modes; no golden-image dependency.
- [ ] Docs updated in the same change; relative links resolve; docs read as already merged.
- [ ] GitHub Actions `build-test`, `docs`, and `pr-title` are green on the PR.
- [ ] PR uses the template, has an approval, and is squash-merged with a conventional title.
- [ ] `dotnet format`, `pwsh scripts/check-docs.ps1`, and `git diff --check` pass.
- [ ] AOT publish passes for the supported RIDs.
- [ ] No `TODO`, placeholder, dead code, or unreported skipped gate.
- [ ] Task branches and worktrees are cleaned up, then all local milestone branches are deleted
  after the PR is created.
