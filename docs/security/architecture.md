# Security architecture

## Controls

- Authentication and organization / workspace / project isolation
- RBAC plus fine-grained tool permissions
- Secret-store abstraction (`ISecretStore`) — credentials are never in config or rows
- Encryption in transit and at rest
- Append-only audit trail for every policy decision, allowed or denied
- Data classification driving model routing
- Local-only inference for restricted data
- Approval gates on high-impact actions
- Sandboxed code and terminal execution (container, never the host)
- Input and output validation
- Rate limits and per-run budgets

## Data classification and routing

| Classification | Default routing |
| --- | --- |
| PUBLIC | Cloud or local per workspace policy |
| INTERNAL | Approved cloud or local |
| CONFIDENTIAL | Approved providers only, routing recorded |
| RESTRICTED | Local only unless explicitly permitted |

Classification is set on the project and travels on every `ModelRequest`. The router
enforces it; no module opts out.

## Threat notes for the agent runtime

- Retrieved content and tool output are **data, not instructions**. Text pulled from a
  repository, a ticket or a web page must never be treated as an authorization to act.
- Approval is per action. Granting one `EXECUTE_TERMINAL` does not grant the next.
- Sandbox escape is the assumed failure mode for terminal and test tools: they run in
  a container with a scoped working directory and no host credentials.
- Budgets are a safety control, not just a cost control — they bound the blast radius
  of a looping agent.
