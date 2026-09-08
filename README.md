# HELIOS

**AI Engineering Operating System.** One extensible platform containing ten specialized
AI systems, sharing a single model gateway, agent runtime, tool system, knowledge layer,
workflow engine, evaluation system, permission model and realtime event bus.

`C# / ASP.NET Core 10` · `React + TypeScript + Fluent UI` · `SignalR` · `Redis` · `MySQL` · `Ollama` · `vLLM`

> Modules request model *capabilities*, not vendors. Ollama, vLLM, OpenAI, Anthropic,
> Gemini, Azure OpenAI and any OpenAI-compatible endpoint are implementation details
> behind the model gateway.

## The ten modules

| Module | Primary flow |
| --- | --- |
| Engineering | Issue, repository analysis, plan, branch, code, tests, security, pull request |
| Security | Static analysis, dependencies, secrets, threat model, findings, remediation |
| Incidents | Logs and traces, timeline, hypotheses, evidence, root cause, postmortem |
| Architecture | Requirements, architecture, data model, APIs, infrastructure, ADRs |
| Knowledge | Ingest, index, relationships, retrieval, cited answers |
| QA | Diff, affected components, tests, execution, failure diagnosis, regression |
| Legacy | Map codebase, dependencies, debt, risk, migration seams, modernization |
| Decisions | Data, observations, options, scenarios, risks, recommendation |
| Multi-agent team | PM, Architect, Developer, QA, Security, DevOps sharing project state |
| Learning | Runs, evaluations, failure analysis, experiments, gated promotion |

## Layout

```
Helios/
├── src/
│   ├── Helios.Contracts/       DTOs, enums, events — references nothing
│   ├── Helios.Domain/          Entities, invariants, state machines
│   ├── Helios.Application/     Use cases and the abstractions everything implements
│   ├── Helios.Infrastructure/  MySQL, Redis, providers, Git, Docker, storage
│   ├── Helios.Api/             REST, auth, SignalR hubs
│   └── Helios.Worker/          Agents, workflows, ingestion, evaluations
├── frontend/helios-web/        React + TypeScript + Fluent UI
├── tests/                      Unit, integration, architecture, AI, end-to-end
├── deploy/                     Docker, compose, kubernetes, environments
├── docs/                       Architecture, ADRs, API, agents, security, operations
└── scripts/                    dev-up, dev-down, build
```

Dependency direction, enforced by `Helios.ArchitectureTests`:

```
Contracts <- Domain <- Application <- Infrastructure <- Api / Worker
```

## Getting started

```powershell
dotnet build Helios.sln
dotnet run --project src/Helios.Api      # http://localhost:5080/health
```

The web client needs Node 22 LTS, which is not installed on this machine yet:

```powershell
cd frontend/helios-web
npm install
npm run dev                              # http://localhost:5173
```

Full stack with MySQL, Redis and Ollama — see
[docs/operations/runbook.md](docs/operations/runbook.md):

```powershell
Copy-Item deploy/compose/.env.example deploy/compose/.env
./scripts/dev-up.ps1 -Build
```

## Where things stand

Phase 0 scaffold: solution, layering, core abstractions, SignalR hubs, worker host,
web shell, compose stack and docs. No provider, persistence or agent implementation
yet — [docs/plan/](docs/plan/00-overview.md) has the phase-by-phase build, and
every folder that is still empty carries a README saying what belongs there.

## Non-negotiables

1. No application module calls a provider SDK directly.
2. No long-running AI execution lives inside an HTTP request.
3. No agent has unrestricted tools.
4. Every important action emits an event.
5. MySQL is the source of truth.
6. Local versus cloud execution is policy-driven.
7. Agents, prompts, models, tools and workflows are versioned.
8. Every important AI run is observable.
9. Human approval is required for high-impact actions.

Full plan: [docs/](docs/) — original source in `docs/HELIOS_Full_System_Plan.docx`.
