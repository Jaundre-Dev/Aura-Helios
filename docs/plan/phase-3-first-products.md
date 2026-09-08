# Phase 3 — First products

**Goal.** The first vertical slice, end to end — the moment HELIOS stops being
infrastructure and starts being a product.

**Depends on:** Phase 2. **Effort:** 25–30 focused days.

---

## WP3.0 — Build `helios-testbed`

The Gate 3 target, confirmed. A separate repository of roughly 400 lines with a genuine
test suite and **deliberately planted bugs of graded difficulty**: a null check, an
off-by-one, a race condition.

It must reset to a known commit in one command, because comparing agent versions is only
meaningful from identical starting state. Half a day, and it pays for itself the first time
a run goes wrong.

Pointing the agent at HELIOS itself is the worst first choice — a self-modifying loop with
no track record, where an agent bug damages the tool you need to diagnose agent bugs.

## WP3.1 — Repository integration

Clone and fetch, a git worktree per run, branch naming `helios/{run-short-id}-{slug}`, and
a cleanup policy for abandoned worktrees.

## WP3.2 — Context builder v1

No embeddings yet. Ranking signals: path match against goal terms, symbol match, recently
changed files from git log, test files paired with their source, and files importing the
ones already selected.

Budgeted and compressed — signatures rather than bodies for peripheral files.

It reports **what it dropped for budget separately from what it filtered for policy**.
Those are different problems, and conflating them makes both invisible.

## WP3.3 — Engineering agent

Prompt, plan step, edit loop, test loop, self-review, and a pull request body that links
the run trace.

## WP3.4 — QA agent

Diff → affected components → select or generate tests → run → diagnose.

Distinguishing *"this change broke it"* from *"flaky"* from *"already failing"* is most of
the value here.

## WP3.5 — Architect agent

Requirements → components → data model → API → ADR artifact, with Mermaid diagrams stored
as artifacts.

## WP3.6 — Code workspace UI

Monaco split pane, file tree, AI panel (explain, fix, refactor, test, secure), diff view.

---

## Gate 3 — the vertical slice

One continuous demo:

1. Create a project and connect a repository
2. Choose Automatic or Local AI
3. The Engineering agent fixes a small *real* issue
4. The gated action stops for approval and you approve it
5. The code change lands and tests run
6. The security check runs
7. A pull request is opened
8. The whole thing was followed live over SignalR
9. The evaluation is persisted and readable afterwards
