# HELIOS documentation

Everything here is the working record. When a document and the code disagree, fix the
document — a stale doc is worse than a missing one.

## The plan

`plan/` is the build. One file per phase, each ending in a gate you can verify.

| | |
| --- | --- |
| [plan/00-overview.md](plan/00-overview.md) | How to read the plan, where we are, the critical path, totals |
| [plan/01-decisions.md](plan/01-decisions.md) | Confirmed decisions and the target environment |
| [plan/phase-0-foundation.md](plan/phase-0-foundation.md) | Solution, persistence, Redis, auth, endpoints, events |
| [plan/phase-1-ai-core.md](plan/phase-1-ai-core.md) | Providers, registry, router, gateway, streaming |
| [plan/phase-2-agent-runtime.md](plan/phase-2-agent-runtime.md) | State machine, tools, sandbox, approvals, trace |
| [plan/phase-3-first-products.md](plan/phase-3-first-products.md) | Engineering, QA, Architect — the vertical slice |
| [plan/phase-4-intelligence.md](plan/phase-4-intelligence.md) | Knowledge, Security, Incidents |
| [plan/phase-5-operations.md](plan/phase-5-operations.md) | Decisions engine, observability |
| [plan/phase-6-scale.md](plan/phase-6-scale.md) | Legacy, multi-agent teams, workflows, plugins |
| [plan/phase-7-learning.md](plan/phase-7-learning.md) | Evaluation, benchmarking, promotion gates |
| [plan/cross-cutting.md](plan/cross-cutting.md) | Testing, CI, versioning, security checkpoints, data rollout, risks |

## Reference

| | |
| --- | --- |
| [architecture/overview.md](architecture/overview.md) | Layers, dependency direction, non-negotiables |
| [architecture/data-architecture.md](architecture/data-architecture.md) | MySQL tables, Redis boundary, context rule |
| [architecture/events.md](architecture/events.md) | Event catalogue, hubs, group isolation |
| [agents/runtime.md](agents/runtime.md) | Execution loop, state machine, permissions |
| [security/architecture.md](security/architecture.md) | Controls, classification, threat notes |
| [api/surface.md](api/surface.md) | Route map and conventions |
| [operations/runbook.md](operations/runbook.md) | Local setup for this machine |
| [operations/dependencies.md](operations/dependencies.md) | Package versions and the EF pin |

## Decisions

`adr/` holds the architecture decision records — what was chosen, why, and what it costs.
These are the only documents that are never rewritten: superseded ADRs get a successor,
not an edit.

| | |
| --- | --- |
| [0001](adr/0001-modular-monolith-first.md) | Start as a modular monolith |
| [0002](adr/0002-provider-abstraction.md) | All model access goes through the gateway |
| [0003](adr/0003-ef-core-9-pin.md) | Pin the EF Core stack to 9.0.x on a .NET 10 target |

## Source

`HELIOS_Full_System_Plan.docx` is the original system plan this was built from. Kept for
provenance; `plan/` supersedes it everywhere the two differ.
