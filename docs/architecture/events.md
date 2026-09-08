# Events and realtime

```
Application action -> HeliosEvent -> Redis -> { workers, observability, notifications, SignalR } -> React UI
```

## Event types

Defined in `Helios.Contracts.Events.EventTypes` and mirrored in the web client.

| Group | Events |
| --- | --- |
| Agent | `agent.started`, `agent.state_changed`, `agent.completed`, `agent.failed` |
| Model | `model.request_started`, `model.request_completed` |
| Tool | `tool.started`, `tool.completed`, `tool.failed` |
| Approval | `approval.requested`, `approval.granted`, `approval.rejected` |
| Workflow | `workflow.started`, `workflow.completed`, `workflow.failed` |
| Security | `security.finding_created`, `security.finding_resolved` |
| Test | `test.started`, `test.completed`, `test.failed` |
| Incident | `incident.created`, `incident.updated`, `incident.resolved` |
| Artifact | `artifact.created` |

## Hubs

| Hub | Route | Carries |
| --- | --- | --- |
| `WorkspaceHub` | `/hubs/workspace` | Workspace-wide activity |
| `AgentHub` | `/hubs/agent` | Live run trace and streamed model output |
| `ProjectHub` | `/hubs/project` | Project-scoped changes |
| `NotificationHub` | `/hubs/notification` | Per-user notifications and approval requests |
| `WorkflowHub` | `/hubs/workflow` | Workflow run progress |
| `MonitoringHub` | `/hubs/monitoring` | Provider health, queue depth, worker status |

## Group isolation

A connection receives nothing until it joins a group. Group names come from
`HubGroups` — `workspace:{id}`, `project:{id}`, `run:{id}`, `user:{id}` — so one
workspace can never observe another workspace's run trace.
