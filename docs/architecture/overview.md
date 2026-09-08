# Architecture overview

HELIOS is an AI engineering operating system, not ten chatbots. The ten products are
separate experiences sharing one model gateway, agent runtime, tool system, knowledge
layer, workflow engine, evaluation system, permission model and event bus.

## Layers

| Layer | Project | Responsibility |
| --- | --- | --- |
| Web | `frontend/helios-web` | Engineering workstation, dashboards, command palette, live runs, code and workflow UI. |
| API | `Helios.Api` | REST endpoints, auth, authorization, SignalR hubs, request orchestration. |
| Worker | `Helios.Worker` | Agents, workflows, ingestion, evaluations, maintenance. |
| Application | `Helios.Application` | Use cases and the abstractions everything else implements. |
| Domain | `Helios.Domain` | Entities, invariants, state machines. |
| Infrastructure | `Helios.Infrastructure` | MySQL, Redis, providers, Git, Docker, storage, observability. |
| Contracts | `Helios.Contracts` | Dependency-free DTOs, enums and event shapes shared with clients. |

## Dependency direction

```
Contracts  <-  Domain  <-  Application  <-  Infrastructure  <-  Api / Worker
```

`Contracts` references nothing. `Domain` references only `Contracts`. Nothing in
`Application` or `Domain` references a provider SDK. `Helios.ArchitectureTests` is
where these rules get enforced rather than remembered.

## Principles

| Principle | How it shows up |
| --- | --- |
| Provider independence | Modules call `IModelGateway`; provider SDKs live behind `IModelProvider`. |
| Explicit state | Agent and workflow state is durable and driven by a state machine. |
| Least privilege | Every tool action passes `IPolicyEngine` before it runs. |
| Human control | High and critical risk actions block on an `Approval`. |
| Observable AI | Runs record model, routing decision, tools, timing, cost and evaluation. |
| Version everything | Agents, prompts, models, tools, workflows and policies are versioned. |
| Async execution | Long AI work runs in the worker, never in an HTTP request. |
| MySQL is truth | Redis coordinates and caches; it stores no durable business state. |
| Event-driven UX | Important state changes become events and reach the UI via SignalR. |

## Non-negotiable rules

1. No application module calls a provider SDK directly.
2. No long-running AI execution lives inside an HTTP request.
3. No agent has unrestricted tools.
4. Every important action emits an event.
5. MySQL is the source of truth.
6. Local versus cloud execution is policy-driven, never hardcoded.
7. Agents, prompts, models, tools and workflows are versioned.
8. Every important AI run is observable.
9. Human approval is required for high-impact actions.

## Start as a modular monolith

`Helios.Api`, `Helios.Worker` and `helios-web` are the whole deployment. Extraction of
`Helios.AI.Gateway`, `Helios.Agent.Runtime`, `Helios.Knowledge`, `Helios.Security`,
`Helios.Observability` or `Helios.Workflow` happens only when a specific scaling or
isolation need justifies it — not on principle.
