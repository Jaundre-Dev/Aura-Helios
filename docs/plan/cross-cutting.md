# Cross-cutting concerns

Applies to every phase.

## Testing strategy

| Layer | What it covers | Project |
| --- | --- | --- |
| Unit | Domain invariants, state machine transitions, router scoring, permission risk, naming conventions | `Helios.UnitTests` |
| Integration | EF mappings and migrations, Redis queue and lock semantics, endpoints, SignalR round trip | `Helios.IntegrationTests` |
| Architecture | Dependency direction, no provider SDK in Domain or Application, every tool declares a permission | `Helios.ArchitectureTests` |
| AI | Provider contract tests against a stub server, router decisions against fixture registries | `Helios.AITests` |
| End to end | The vertical slice, approval flow, worker restart recovery | `Helios.EndToEndTests` |

**AI tests must be deterministic.** Record model responses as fixtures. Live-model runs
belong in the evaluation framework, where variance is the subject rather than the enemy — a
test suite that fails on model nondeterminism gets ignored within a week, and then so does
everything next to it.

**Any fixture that drops a database must assert the database name first.** Learned the hard
way: an early integration fixture set the connection string through configuration, was
silently outranked by the app's own user-secrets, and dropped the real development schema.
`HeliosApiFactory` now re-registers the `DbContext` outright and checks
`db.Database.GetDbConnection().Database` before anything destructive runs.

## Continuous integration

- Restore, build, unit and architecture tests on **every push**
- Integration tests on pull requests
- The evaluation suite **nightly**, not per commit

## Versioning discipline

Never edit an active `AgentVersion`, prompt version, `RoutingPolicy` or workflow version in
place. Clone and supersede.

Add a repository guard **and** an architecture test for it — this is the rule most likely to
be broken under time pressure, and the one whose breach is hardest to detect afterwards.

## Security checkpoints

Four points where work stops for a review:

1. Before the first `terminal.run` — sandbox threat model
2. Before the first `pr.create` — token scope and blast radius
3. Before any production permission becomes grantable — approval and audit review
4. Before the plugin system ships — isolation boundary review

## Data model rollout

Nothing is created before the code that uses it.

| Phase | Tables |
| --- | --- |
| **0** | users, roles, user_roles, user_claims, user_logins, user_tokens, role_claims, organizations, workspaces, workspace_members, projects, project_members, audit_logs |
| 1 | Providers, ProviderCredentials, Models, ModelCapabilities, RoutingPolicies, ModelCallLog |
| 2 | Agents, AgentVersions, AgentTools, AgentRuns, AgentSteps, AgentMessages, Tools, ToolVersions, ToolPermissions, Approvals, Artifacts, AgentMemory, ProjectMemory |
| 3 | Repositories, Branches, PullRequests, CodeChanges, TestSuites, TestCases, TestRuns, TestResults, Architectures, Components, Decisions |
| 4 | Sources, Documents, Chunks, ChunkEmbeddings, Entities, Relations, SecurityFindings, ThreatModels, SecurityScans, Incidents, IncidentEvents, IncidentHypotheses, IncidentReports |
| 5 | DecisionCases, Options, Recommendations, Notifications |
| 6 | LegacyProjects, ModernizationTasks, Workflows, WorkflowVersions, WorkflowRuns, WorkflowSteps |
| 7 | EvaluationCases, EvaluationRuns, EvaluationResults |

Phase 0 tables are **created and live**.

## Risk register

| Risk | Phase | Early signal | Mitigation |
| --- | --- | --- | --- |
| Sandbox escape | 2 | Any tool reading outside `/workspace` | Path confinement tests, no-network default, non-root, threat model before first use |
| Agent loops burning budget | 2 | Runs hitting the tool-call ceiling | Budgets checked every iteration; the ceiling is a safety control, not just a cost one |
| Streaming tool-call bugs | 1 | Tool use works unstreamed, fails streamed | Fragment accumulation covered by per-provider contract tests |
| Context overload | 3 | Prompts near the window, degraded answers | Context builder reports what it dropped; rank and compress before expanding the budget |
| Provider churn | 1 | An adapter breaks on a vendor change | Generic adapter first; vendor adapters only where genuinely necessary |
| Silent regression after a prompt tweak | 7 | Quality drops with no code change | Version everything; regression evaluation before promotion |
| Redis loss | 0 | Queue empty, runs stalled | Durable state in MySQL; orphan sweeper requeues from the database |
| Vector store adopted too early | 4 | A new service with no measured need | Hold until measured p95 retrieval exceeds 200 ms |
| Multi-agent write conflicts | 6 | Agents overwriting each other's edits | One writer per file at a time, enforced by the blackboard |
| Test fixture touching real data | all | A fixture that calls `EnsureDeleted` | Assert the resolved database name before destructive calls |
| Scope drift across ten modules | all | Phase gates slipping | Gates are binary; the vertical slice comes before breadth |

## Non-negotiable architecture rules

1. No application module calls a provider SDK directly.
2. No long-running AI execution lives inside an HTTP request.
3. No agent has unrestricted tools.
4. Every important action emits an event.
5. MySQL is the source of truth.
6. Local versus cloud execution is policy-driven, never hardcoded.
7. Agents, prompts, models, tools and workflows are versioned.
8. Every important AI run is observable.
9. Human approval is required for high-impact actions.
