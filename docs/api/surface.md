# API surface

All routes are versioned under `/api/v1`. Most require a bearer token and act inside the
workspace its `workspace_id` claim names; `auth/register` and `auth/login` are the anonymous
entry points, and `organizations`/`workspaces` are scoped to the caller rather than to a
single workspace.

| Route | Module |
| --- | --- |
| `/api/v1/auth` | Sign-in: register, login, select-workspace, me *(implemented)* |
| `/api/v1/organizations` | Organizations *(implemented)* |
| `/api/v1/workspaces` | Workspaces and members *(implemented)* |
| `/api/v1/projects` | Projects, repositories, environments *(implemented)* |
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
- Auth is a JWT bearer token from `auth/login`. To act inside a workspace, call
  `auth/select-workspace` to get a token carrying the `workspace_id` and `role` claims.
  Hubs take the token from the `access_token` query string; REST takes the `Authorization`
  header.
