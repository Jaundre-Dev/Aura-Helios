# ADR-0001: Start as a modular monolith

- **Status:** Accepted
- **Date:** 2026-09-04

## Context

HELIOS covers ten product modules over a shared platform core. Splitting that into
services on day one means ten deployment pipelines, cross-service transactions and a
distributed debugging story before a single agent has fixed a single bug.

## Decision

Ship `Helios.Api`, `Helios.Worker` and `helios-web` as the entire deployment. Enforce
module boundaries inside the codebase — feature folders, the abstraction layer and
`Helios.ArchitectureTests` — rather than at the network.

## Consequences

- Fast inner loop: one solution, one debugger, one compose file.
- Boundaries must be enforced by tests, because the compiler alone will not do it.
- Extraction stays cheap as long as modules keep talking through `Application`
  abstractions instead of reaching into each other.
- Scaling the worker independently of the API is the first thing that will force a
  split, and that is the trigger to revisit this.

## Alternatives considered

Microservices from the start — rejected as premature. The plan explicitly lists
"dozens of microservices" as a v1 non-goal.
