# HELIOS build plan — overview

**Version 1.2 — 2026-09-07.** One file per phase in this folder. Update them as work
lands rather than writing a second plan somewhere else.

## How to read this

Three rules govern the ordering, and they are worth stating before the detail buries them.

**Build the vertical slice, not the layers.** The temptation with a platform this wide is
to build all of persistence, then all of the AI core, then all of the agents. That defers
every integration problem to the point where it is most expensive to fix. Phases 0 through
3 exist for one purpose: to make a single Engineering-agent run possible end to end.
Anything that does not serve that run waits.

**Gates are binary.** Each phase ends in a numbered checklist of things that are either
demonstrably true or not. "Mostly working" does not open the next phase. The gates are
written so someone else could verify them without asking you what you meant.

**Effort is in focused days.** One focused day is a day of uninterrupted build time by an
experienced .NET and React developer who already knows this codebase. It is not a calendar
day. Divide by your real availability. The ranges are deliberate — the wider the range, the
less certain the estimate.

## Where we are

Phase 0 is **in progress**. Build is green, 46 tests pass, and a signed-in user can create
a workspace and a project through the API against a real database.

| Work package | State |
| --- | --- |
| WP0.1 Configuration and options | partial — `JwtOptions` sets the validate-on-start pattern; other options still to bind |
| **WP0.2 Persistence (MySQL)** | **done** |
| WP0.3 Redis infrastructure | not started — blocked on Docker |
| **WP0.4 Identity, isolation, RBAC** | **done** — sign-in, JWT, workspace selection |
| **WP0.5 Workspace and project endpoints** | **done** |
| WP0.6 Events to Redis to SignalR | not started — blocked on Docker |
| WP0.7 Observability baseline | partial — health endpoints done, Serilog not wired |
| WP0.8 Web: auth, workspace, projects | not started — blocked on Node |

### What exists

- 11 projects, dependency direction enforced by `Helios.ArchitectureTests`
- `Helios.Contracts` — model request/response/stream, chat messages, tool definitions,
  `DataClassification`, `TaskType`, `ToolPermission` with its risk map, `AgentRunState`,
  the event catalogue, hub routes and group naming
- `Helios.Domain` — organizations, workspaces, projects, membership, audit, plus the
  agent, provider and routing aggregates awaiting their phase
- `Helios.Application` — every abstraction the rest implements, plus the workspace,
  project and organization services
- `Helios.Infrastructure` — `HeliosDbContext` with global workspace filters, the audit
  interceptor, snake_case naming, `binary(16)` GUIDs, the initial migration
- `Helios.Api` — health and readiness, six SignalR hubs, three endpoint groups,
  validation filter, problem-details mapping
- `helios-web` — Vite, Fluent UI, theme, navigation, shell, API client, SignalR hook
- Docker Compose stacks, Dockerfiles, prerequisite checker, docs

### What is deliberately absent

No Redis, no provider implementation, no agent runtime, no real UI screens. Each is
scheduled at the phase that needs it. (Sign-in now exists — WP0.4.)

## The critical path

```
Phase 0  Foundation ──┐
                      ├──▶  Phase 2  Agent runtime  ──▶  Phase 3  First products  ──▶  ★ GATE 3
Phase 1  AI core   ───┘                                                                vertical slice

Phase 4  Intelligence   depends on 3
Phase 5  Operations     depends on 4
Phase 6  Scale          depends on 3, benefits from 5
Phase 7  Learning       depends on 3, sharpens everything after it
```

Phases 0 and 1 can overlap — the model gateway needs configuration and logging but not the
identity model, and provider adapters can be built and contract-tested before a single
table exists. For one developer, nothing else parallelises cleanly.

## Totals

| Phase | Focused days |
| --- | --- |
| 0 — Foundation | 10–14 |
| 1 — AI core | 18–24 |
| 2 — Agent runtime | 20–25 |
| 3 — First products | 25–30 |
| 4 — Intelligence | 30–35 |
| 5 — Operations | 15–20 |
| 6 — Scale | 35–45 |
| 7 — Learning | 20–25 |
| **To the vertical slice (0–3)** | **73–93** |
| **To the full platform** | **173–218** |

## Still open

Two things remain genuinely undecided. Neither blocks work.

1. **Which cloud provider is primary.** The gateway makes them interchangeable, which is
   the point, so decide at WP1.3. Pick on the strength of the model for planning and code
   review, since that is where the routing policy sends the expensive work.
2. **The sandbox base image.** WP2.4 needs an image carrying the toolchain of the
   repositories the agent will work on. Picking the language for `helios-testbed` in WP3.0
   answers this for free.
