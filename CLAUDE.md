# Claude instructions for HELIOS

## Authority

Read HELIOS-IMPLEMENTATION-PLAN.md, HELIOS-REVIEW.md, API-CATALOGUE.md and docs/IMPLEMENTATION-STATUS.md before changes. This repository is now a South African business API platform, not an AI engineering or agent operating system. Retired plans in Git history do not govern new work. The initial catalogue is 12 services; the 68-service list is a gated roadmap, not permission to advertise unfinished endpoints.

## Working boundaries

- Retain the .NET/React/MySQL foundation unless evidence justifies a documented change.
- Preserve user changes, existing migrations and database contents. Do not modify sibling AURA products.
- Start with P0 findings: organisation ownership, workspace creation permissions, lockout/revocation, safe integration test databases and build tooling.
- Do not run the current database integration fixture before replacing its fixed-schema destructive setup with a guarded unique disposable database.
- Workspace/project terms are retained foundation concepts; do not rebuild repository management or agent execution around them.
- Historical AuditLog.AgentRunId remains only for database compatibility. Add request correlation through a forward migration.
- Do not add keys, provider credentials or actual customer documents to source/examples/logs.
- Do not assume cloud provider resale permission, official registry access or customer compliance. Keep restricted services unavailable until approved.

## Completion standard

Implement vertical slices with meaningful tests. A scaffold, mock, empty worker or placeholder screen is not a finished feature. Production must reject fake adapters. All billable work uses scoped keys, idempotency, a transactional ledger and durable execution. Preserve tenant ownership across APIs, jobs, files, keys, invoices and webhooks. Update docs/IMPLEMENTATION-STATUS.md after each slice with commands/results/blockers. Continue independent work when a supplier contract or credentials are missing; report the dependency accurately.

The planning cleanup did not establish a green build. See the review for exact environment constraints. Never copy historical test counts into new completion claims. Ask for business decisions only when genuinely required; use the plan's defaults otherwise. Publishing, purchasing, supplier messaging and destructive database operations need the owner's relevant authorisation.
