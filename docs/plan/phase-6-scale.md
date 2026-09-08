# Phase 6 — Scale

**Goal.** Multiple agents collaborating, workflows built visually, and third-party tools
running safely.

**Depends on:** Phase 3; benefits from Phase 5. **Effort:** 35–45 focused days.

---

## WP6.1 — Legacy modernizer

Codebase mapping, dependency graph, debt and risk scoring, migration seams, incremental
modernization tasks.

## WP6.2 — Multi-agent teams

A shared project blackboard, an explicit role handoff protocol, and conflict resolution.
PM, Architect, Developer, QA, Security, DevOps and Reviewer over shared project state.

The rule that keeps this tractable: **one writer per file at a time.** Concurrent agents
editing the same file is where multi-agent systems stop being useful and start being a
merge problem.

## WP6.3 — Workflow engine and builder UI

The node kinds already enumerated in `IWorkflowEngine` — Trigger, Agent, Model, Tool,
Condition, HumanApproval, Parallel, Loop, Transform, Wait, Webhook, Notification — each
with explicit inputs, outputs, timeout, retry and failure behaviour.

Workflows are versioned and produce `WorkflowRun` records.

## WP6.4 — Plugin system

> **Decision — out-of-process plugins.** Run third-party tools as separate processes behind
> a tool protocol rather than loading assemblies into the host. An in-process plugin cannot
> be sandboxed, cannot be resource-limited, and takes the whole worker down when it faults —
> everything Phase 2 spent twenty days getting right for agent tools would be bypassed by
> the first plugin.

## Security checkpoint

Work stops for an isolation boundary review before the plugin system ships.
