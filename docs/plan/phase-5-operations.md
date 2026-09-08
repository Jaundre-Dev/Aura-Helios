# Phase 5 — Operations

**Goal.** The platform can be operated and reasoned about — decisions backed by evidence,
and telemetry across all six layers.

**Depends on:** Phase 4. **Effort:** 15–20 focused days.

---

## WP5.1 — Decision engine

Evidence, options, scenarios, risk, recommendation. `DecisionCases`, `Options`,
`Recommendations`.

## WP5.2 — OpenTelemetry

| Layer | Track |
| --- | --- |
| Infrastructure | CPU, memory, service health, MySQL, Redis, workers |
| API | Requests, errors, latency, throughput |
| Model | Provider, model, tokens, latency, failures, cost metadata |
| Agent | Success, duration, tool calls, retries, human interventions |
| Workflow | Completion, failed nodes, waits, approvals |
| Business | Tasks, incidents, vulnerabilities, PRs, tests |

## WP5.3 — Dashboards

The observability screens under the Platform section of the navigation.

## WP5.4 — Operational workflows

The scheduled and triggered runs that let the platform maintain itself.
