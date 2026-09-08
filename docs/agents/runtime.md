# Agent runtime

```
Agent { Goal, Context, Plan, Tools, Permissions, Model policy, Memory, Evaluation policy }
   -> Execution loop { model call, tool call, observation, state transition, evaluation }
   -> Result / Artifact / Event
```

## State machine

```
Created -> Queued -> Planning -> AwaitingApproval -> Executing
Executing -> WaitingForTool -> Executing
Executing -> Evaluating -> Completed
Executing -> Failed -> Retrying | NeedsHuman
```

`IAgentStateMachine` guards every transition. An illegal transition throws — a run can
never end up in a state nothing knows how to resume.

## Agent configuration

Configuration lives on `AgentVersion`, never on `AgentDefinition`, so a change produces
a new version and old runs stay reproducible.

| Setting | Purpose |
| --- | --- |
| Role | developer, security-analyst, architect, qa, pm, devops, reviewer |
| SystemPrompt / PromptVersion | Versioned agent instructions |
| RoutingPolicyId | Routing, locality and data rules |
| AllowedTools | The tool set this version can even see |
| Permissions | Actions it may take |
| RequiresApproval | Actions that block on a human |
| Budgets | Token, cost, duration and tool-call limits |
| MemoryPolicy | Durable memory read and write rules |
| KnowledgeScope | Which project sources it may retrieve |
| EvaluationPolicy | Success criteria and evaluators |

## Tools

`IAgentTool` declares a `RequiredPermission`. Before execution the runtime calls
`IPolicyEngine`, which returns allow, allow-with-approval or deny — and the decision
is written to `AuditLogs` either way.

| Permission | Risk |
| --- | --- |
| READ_REPOSITORY | Low |
| WRITE_REPOSITORY | Medium |
| CREATE_PULL_REQUEST | Medium |
| EXECUTE_TERMINAL | High |
| MERGE_PULL_REQUEST | High |
| READ_PRODUCTION | High |
| DEPLOY | High |
| WRITE_PRODUCTION | Critical |
| DELETE_RESOURCE | Critical |

High and critical default to requiring approval. A model never receives unrestricted
authority.

## What a run records

`AgentRun` captures agent version, prompt version, resolved model, routing decision,
context metadata, tool calls, events, evaluation, cost, duration, result and artifacts.
That record is what makes a regression explainable three weeks later.
