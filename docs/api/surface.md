# API surface

All routes are versioned under `/api/v1` and scoped to a workspace the caller belongs to.

| Route | Module |
| --- | --- |
| `/api/v1/projects` | Projects, repositories, environments |
| `/api/v1/agents` | Agent definitions and versions |
| `/api/v1/agents/{id}/runs` | Start, inspect, cancel and trace runs |
| `/api/v1/models` | Model registry and the model console |
| `/api/v1/providers` | Provider configuration and health |
| `/api/v1/tools` | Tool registry and permission grants |
| `/api/v1/workflows` | Workflow definitions, versions and runs |
| `/api/v1/knowledge` | Ingestion, search and cited answers |
| `/api/v1/engineering` | Engineering agent tasks and pull requests |
| `/api/v1/security` | Scans, findings and threat models |
| `/api/v1/incidents` | Incidents, timelines and postmortems |
| `/api/v1/qa` | Test suites, runs and results |
| `/api/v1/architecture` | Architectures, components and decisions |
| `/api/v1/legacy` | Legacy mapping and modernization tasks |
| `/api/v1/decisions` | Decision cases, options and recommendations |
| `/api/v1/evaluations` | Evaluation cases and benchmark runs |
| `/api/v1/learning` | Failure analysis, experiments and promotions |

Non-versioned: `GET /health`, and the SignalR hubs under `/hubs`.

## Conventions

- Starting long work returns `202 Accepted` with a run id. Progress arrives over
  SignalR; it is never streamed from the request that started it.
- Errors use `ProblemDetails`.
- Anything that mutates records an `AuditLog` row.
