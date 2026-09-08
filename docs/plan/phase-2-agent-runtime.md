# Phase 2 — Agent runtime

**Goal.** An agent executes asynchronously in the worker, calls permissioned tools inside a
sandbox, blocks on a human for high-risk actions, streams its trace to the UI, and survives
a worker restart.

**Depends on:** Phases 0 and 1. **Effort:** 20–25 focused days.

---

## WP2.1 — State machine

```
Created → Queued → Planning → AwaitingApproval → Executing
Executing → WaitingForTool → Executing
Executing → Evaluating → Completed
Executing → Failed → Retrying | NeedsHuman
```

An explicit transition table — `FrozenDictionary<AgentRunState, ImmutableArray<AgentRunState>>`.
An illegal transition **throws** rather than silently correcting. Every transition writes an
`AgentStep` of kind `transition` and emits `agent.state_changed`.

## WP2.2 — Execution loop

```
load run, agent version, allowed tools
transition Queued ▸ Planning
context = IContextBuilder.Build(goal, project, tokenBudget)

loop:
  guard budgets (tokens, cost, duration, tool calls)
      exceeded ▸ Failed | NeedsHuman

  response = gateway.Chat(messages + toolDefinitions)

  if no tool calls:
      transition Evaluating ▸ evaluate ▸ Completed;  break

  for each tool call:
      tool = registry.Find(name)          // unknown ▸ observation, not a crash
      decision = policy.Evaluate(permission, classification, workspace)

      if denied:        append "denied: {reason}"; audit; continue
      if needsApproval: approvals.Request(...)
                        transition AwaitingApproval; persist; return
                        // resumed by the approval event

      transition WaitingForTool
      result = tool.Execute(context)
      transition Executing
      append observation from result
```

The loop is **re-entrant by construction**. All state lives in MySQL; Redis holds only the
lease. A killed worker loses the in-flight step and nothing else.

## WP2.3 — Tool registry and the first tools

Keyed DI registration; `ForAgentVersion` filters by the version's `AllowedTools`.

Minimum set for the slice: `repo.search`, `file.read`, `file.write`, `terminal.run`,
`test.run`, `git.branch`, `git.commit`, `git.diff`, `pr.create`.

Every tool declares a JSON schema, a permission, a timeout and an **output truncation
policy**. A 4 MB test log cannot enter the context window — head and tail, plus an
`Artifact` reference to the whole thing.

## WP2.4 — Sandbox

`Docker.DotNet`. **One container per run**, not per call — container start costs about a
second, and per-call would dominate a fifty-step run.

Constraints: no network unless a tool opts in, CPU and memory caps, non-root user,
read-only root filesystem with writable `/workspace` and `/tmp`, pids limit. Every file
tool resolves its path and asserts it stays under `WorkingDirectory`, rejecting `..`
traversal and symlinks pointing outside.

**This is the highest-risk component in the system.** Threat-model it explicitly *before*
the first `terminal.run` executes, not after.

## WP2.5 — Policy, approval, audit

`PolicyEngine` intersects the agent version's grants with workspace policy, derives risk
from `ToolPermissionRisk`, and requires approval when the version says so or risk is High
or above.

| Permission | Risk | Approval by default |
| --- | --- | --- |
| `READ_REPOSITORY` | Low | no |
| `WRITE_REPOSITORY` | Medium | no |
| `CREATE_PULL_REQUEST` | Medium | no |
| `EXECUTE_TERMINAL` | High | **yes** |
| `MERGE_PULL_REQUEST` | High | **yes** |
| `READ_PRODUCTION` | High | **yes** |
| `DEPLOY` | High | **yes** |
| `WRITE_PRODUCTION` | Critical | **yes** |
| `DELETE_RESOURCE` | Critical | **yes** |

Every evaluation writes an `AuditLogs` row — allowed and denied alike.

Flow: request → `approval.requested` → notification hub → decision → `approval.granted` →
requeue the job → runtime resumes at the pending call. An expiry sweeper moves runs with
lapsed approvals to `NeedsHuman`.

## WP2.6 — Worker execution

Concurrency bounded by a `SemaphoreSlim` at `MaxConcurrentRuns`. A lease-renewal task per
run; an orphan sweeper requeues runs whose lease lapsed and increments `RetryCount`.
Graceful shutdown stops accepting, lets in-flight runs reach a persist point, releases
leases.

## WP2.7 — Memory

`AgentMemory` and `ProjectMemory`, with a write policy on the agent version — none,
explicit or automatic. Retrieval joins into the context builder **under a hard cap**, so
memory can never crowd out the actual task.

## WP2.8 — Trace UI

Goal, state pill, step list, streamed output, tool cards with input and output, inline
approval prompts, cancel. On reconnect, replay persisted steps then resume the live stream,
so a refresh leaves no gap in the trace.

---

## Gate 2

1. A run started from the UI streams its steps live
2. A `terminal.run` blocks and surfaces an approval card; approving resumes the run
3. Rejecting records the rejection and the agent continues without that tool
4. Killing the worker mid-run lets another worker pick the run up and finish it
5. Cancel stops the run and tears down its container
6. `AuditLogs` shows every tool decision, allowed and denied
7. A run that exceeds its budget stops at the limit
8. `file.read ../../../etc/passwd` is denied and audited

## Security checkpoint

Work stops for a sandbox threat model **before** the first `terminal.run`, and for a token
scope review before the first `pr.create`.
