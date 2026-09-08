# ADR-0002: All model access goes through the gateway

- **Status:** Accepted
- **Date:** 2026-09-04

## Context

Model providers churn constantly: endpoints move, SDKs break, pricing changes, models
are retired. Any module holding a vendor SDK becomes a rewrite the day that vendor
shifts. HELIOS also has to run some workloads on local inference for data reasons.

## Decision

Application modules request a *capability* through `IModelGateway`, never a vendor.
`IModelRouter` resolves provider and model from routing policy, data classification,
health, cost and latency. `IModelProvider` implementations are the only code allowed
to reference a provider SDK, and they live in `Helios.Infrastructure/AI/Providers`.

Build `GenericOpenAICompatibleProvider` first — it covers Ollama, vLLM and most
hosted endpoints, which makes the second provider nearly free.

## Consequences

- A new provider is one adapter, not a codebase change.
- Every request carries a `DataClassification`, so `RESTRICTED` work can be forced to
  local inference by policy rather than by convention.
- Every response carries a `RoutingDecision`, so any result can be explained after
  the fact.
- Provider-specific features only reach modules if they are first modelled as a
  capability. That friction is deliberate.

## Alternatives considered

Direct SDK calls with a thin wrapper — rejected: it leaks vendor concepts into
modules, which is exactly what makes provider churn expensive.
