# Data architecture

## MySQL — durable source of truth

| Group | Tables |
| --- | --- |
| Identity | Users, Organizations, Workspaces, WorkspaceMembers, ProjectMembers |
| AI | Providers, ProviderCredentials, Models, ModelCapabilities, RoutingPolicies |
| Agents | Agents, AgentVersions, AgentTools, AgentRuns, AgentSteps, AgentMessages |
| Tools | Tools, ToolVersions, ToolPermissions |
| Workflow | Workflows, WorkflowVersions, WorkflowRuns, WorkflowSteps |
| Knowledge | Sources, Documents, Chunks, Entities, Relations |
| Engineering | Repositories, Branches, PullRequests, CodeChanges |
| Security | SecurityFindings, ThreatModels, SecurityScans |
| Incidents | Incidents, IncidentEvents, IncidentHypotheses, IncidentReports |
| QA | TestSuites, TestCases, TestRuns, TestResults |
| Architecture | Architectures, Components, Decisions |
| Legacy | LegacyProjects, ModernizationTasks |
| Decision | DecisionCases, Options, Recommendations |
| Evaluation | EvaluationCases, EvaluationRuns, EvaluationResults |
| Platform | Approvals, Notifications, Artifacts, AuditLogs |

`ProviderCredentials` stores a reference into the secret store, never a raw key.
`AuditLogs` is append-only: no update path, no delete path.

## Redis — speed and coordination

- Background job queues (`AgentRunJob` and friends)
- Distributed locks so only one worker owns a run
- Transient agent state during a step
- Rate limiting and budget counters
- Provider and model health cache
- Streaming coordination between worker and SignalR
- Event fan-out

If Redis is lost, work is delayed and caches are cold. Nothing durable is gone —
that is the boundary, and it is the reason it holds no business state.

## Context rule

Never send an entire repository to a model by default. `IContextBuilder` selects,
ranks, compresses and policy-filters context against a token budget, and reports what
it dropped for budget versus what it filtered for policy.
