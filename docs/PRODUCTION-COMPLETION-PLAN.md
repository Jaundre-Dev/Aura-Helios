# Aura-Helios production completion plan

Date: 2026-09-22  
Status: Planned; no implementation completed by this document.  
Review baseline: 2.5/10 production readiness.

## Objective and scope

Turn the existing foundation into a working multi-tenant specialist AI platform. Keep the layered .NET API/application/domain/infrastructure/contracts/worker architecture and React frontend. Helios owns specialist execution; Aura-AI owns conversations; Enterprise owns business operations. Do not share or directly modify another product's business tables.

This plan expands IMPLEMENTATION-PLAN.md and the H0–H6 scope in Aura-AI/docs/PRODUCT-SPLIT-PLAN.md. Aura-AI's A0–A5 gate is recorded as passed in its implementation status, so Helios foundation work is unblocked. Operational launch gates remain independent for each product.

Passwords and credentials may remain in appsettings as requested by the owner. Changing that storage location is not a readiness task. Existing tenant-secret encryption, persistent key availability and backup recovery still need to work.

## Verified baseline

- Backend Release build: zero warnings/errors in the 2026-09-22 review.
- 22 unit and 4 architecture tests passed during that review. Database integration tests and frontend build were not rerun; earlier results in IMPLEMENTATION-STATUS.md are historical evidence only.
- Existing foundation includes identity, workspaces/projects, tenant isolation, audit, encrypted secret storage and scoped object storage.
- `frontend/helios-web/src/app/routes.tsx` renders placeholder screens; `src/Helios.Worker/Agents/AgentRunWorker.cs` does not execute jobs.
- Queue lifecycle, capability runs and metering remain incomplete. Working folder names and successful builds do not establish working capabilities.

## Delivery strategy

Complete shared foundations, then ship one entire capability rather than exposing many unfinished modules. Default first capability: code review, matching the existing H1 order. A limited release may ship after H0, H1 and the applicable operations gates; it must hide testing/OCR/liveness until each passes its own gate. Full core scope comprises code review, automatic testing, OCR and validated liveness. H5 additional modules are optional backlog.

## H0 — Complete the shared execution foundation

Priority: blocks every capability release.

- [ ] Re-run the backend tests and frontend build. Make database tests use a unique disposable database per run with a safety guard; the current fixed `helios_test` fixture is unsuitable for concurrent runs.
- [ ] Enforce account lockout in `AuthEndpoints`: failed-attempt bookkeeping alone does not prevent login while locked. Add endpoint throttling and tests for locked, inactive and unauthorized users.
- [ ] Verify access revocation, workspace membership and role enforcement for APIs and realtime groups, including existing sessions after removal.
- [ ] Define a versioned capability registry and request/result schemas. Each run records tenant, initiating actor, project, capability/version, input reference, idempotency key, timestamps, status and correlation ID.
- [ ] Implement submit/status/result/artifact/cancel APIs with authorization on every request. Derive workspace from authenticated context, not a supplied tenant field.
- [ ] Implement a durable queue with atomic claim/lease, bounded retries/backoff, cancellation, timeout and dead-letter handling. Recover expired leases after worker crashes; prevent duplicate committed outcomes.
- [ ] Wire real worker dependencies and state transitions: queued → running → succeeded/failed/cancelled, with explicit retry and recovery rules. A cancelled run must not later publish a successful result.
- [ ] Persist artifacts with scoped ownership, bounded upload/download sizes, content-type policy and retention. Protect fetched URLs and repository endpoints against unapproved internal access.
- [ ] Add tenant usage reservations, metering, concurrency limits and provider timeout/error handling. A provider outage must not permit unbounded jobs or charges.
- [ ] Audit submission, cancellation, execution and result access without logging secrets or raw sensitive inputs.

Acceptance: submit a real test job, observe its lifecycle, cancel it, restart the worker midway and recover without duplicate output. Other tenants cannot see its status, events or artifacts. Quota exhaustion and dependency outages behave predictably.

## Shared UI — Replace placeholders with a usable product

Priority: required for the first customer-facing release; build alongside H0/H1.

- [ ] Implement login, workspace/project selection and role-aware navigation.
- [ ] Implement capability submission, run list, live status, retry/cancel, result viewing and authorized artifact download.
- [ ] Show useful validation, empty, loading, failure and reconnect states; preserve run history after refresh/restart.
- [ ] Add minimal administration for members, configured providers, usage and audit according to role.
- [ ] Hide all unimplemented modules. Verify desktop/mobile and keyboard operation.

Acceptance: a user completes the first capability through the browser without developer tools or manual database changes.

## H1 — Complete automatic code review

Priority: recommended first production capability.

- [ ] Implement an explicitly authorized repository connection with project scoping and least-privilege credentials.
- [ ] Validate webhook signatures and replay/idempotency before queueing. Pin the exact commit/diff so results are reproducible.
- [ ] Bound clone/download size and processing time. Treat repository content, comments and prompts as untrusted data.
- [ ] Run selected deterministic checks in isolation where they execute repository tools. Add AI review findings grounded in actual changed files/lines; label confidence and suppress duplicates.
- [ ] Persist a useful report with checked commit, tools executed, findings, evidence, limitations and artifacts. Never claim a check ran when it did not.
- [ ] Keep external posting opt-in and explicitly authorized. Do not automatically merge, deploy or give repository instructions authority over the worker.
- [ ] Test duplicate events, stale commits, revoked connection access, malicious repository text, provider timeout and cancellation.

Acceptance: a configured repository event produces an authorized, reproducible review report in the UI; repeated delivery creates no duplicate run/outcome; tenant boundaries hold.

## H2 — Complete automatic testing

Priority: required before advertising test execution.

- [ ] Execute in disposable isolated runners with CPU, memory, disk, wall-time and network limits. Do not expose host Docker sockets or production credentials.
- [ ] Enforce allowed commands and safe checkout paths; isolate caches and workspaces between tenants and clean up failed/cancelled jobs.
- [ ] Capture actual commands, exit codes, logs, test counts and artifacts. Distinguish test failure, infrastructure failure and a check that was not run.
- [ ] Support configured repository triggers, idempotent runs and cancellation through the common APIs/UI.
- [ ] If AI-generated tests are offered, keep them in a reviewable branch and use deterministic execution to establish results.
- [ ] Test malicious scripts, oversized output, resource exhaustion, hung processes, worker restart and cross-tenant access attempts.

Acceptance: supported repositories produce repeatable test reports and cannot escape runner boundaries or exhaust the shared host beyond enforced limits.

## H3 — Complete OCR

Priority: required before advertising document extraction.

- [ ] Inventory preserved Aura-AI OCR data/code and define an additive export/mapping process. Do not directly share tables or remove the source data during migration.
- [ ] Implement async file ingestion, provider execution, schema mapping, field provenance/confidence and explicit missing/uncertain values.
- [ ] Build document/results review, correction, export and retention/deletion UI on the common run contracts.
- [ ] Enforce file limits, tenant-scoped download authorization, safe parsing and failure recovery.
- [ ] Establish an approved representative test dataset and agreed field-accuracy/latency thresholds before claiming suitability. Include poor scans, unsupported formats and ambiguous fields.

Acceptance: each supported document type meets the agreed evaluation threshold; low-confidence output goes to review; exports match corrected results; source documents and results remain isolated by tenant.

## H4 — Complete validated liveness

Priority: required before enabling or advertising liveness verification. It does not block a code-review-only launch.

- [ ] Select and validate a specialist approach/provider; no assumption of a paid contract. Keep liveness distinct from identity matching.
- [ ] Implement short-lived scoped capture sessions, challenge expiry, replay protection, authenticated callbacks and explicit pass/fail/inconclusive outcomes.
- [ ] Keep evidence access restricted and audited; define consent/capture disclosure, retention and deletion behavior with the product owner before collecting production evidence.
- [ ] Build operator review and safe failure handling. Do not treat an LLM opinion or simple motion detection as sufficient verification.
- [ ] Evaluate representative presentation attacks, false acceptance/rejection, device conditions and demographic variation. Agree acceptance thresholds and document limitations before activation.
- [ ] Import any retained Aura-AI data only through reviewed mappings and an explicit cutover/rollback procedure.

Acceptance: the selected method meets documented evaluation thresholds and replay/access/retention tests; otherwise the feature remains disabled and is not marketed as verified liveness.

## H5 — Optional modules

Document classification/redaction, transcription, knowledge search, orchestration, security triage and incident/architecture assistance are future choices based on customer demand. Each must reuse the shared tenant/run/provider/usage contracts and supply its own evaluation dataset, UI journey and failure/recovery tests. None is required simply to complete the initial four-capability scope.

## H6 — Operations and release gates

Begin these during H0; apply them before the first release and every later capability release.

- [ ] Add CI for backend Release build/tests, real disposable MySQL integration tests, frontend install from lockfile/build and critical browser smoke tests. Required infrastructure checks must fail clearly rather than silently skip.
- [ ] Publish API, worker and frontend to production-like staging. Replace development deployment assumptions; verify HTTPS/proxy handling, allowed origins, authentication and private dependency networking.
- [ ] Version schema changes and rehearse migration plus rollback with old queued jobs/results present. Define compatibility rules for capability versions across deployment.
- [ ] Back up database, artifacts/object storage and encryption keys to independent storage. Restore into another environment and verify decryptability, authorized results and queue recovery. Set and measure recovery point/time targets.
- [ ] Monitor API failures, queue depth/age, expired leases, dead letters, provider errors, usage, disk and backup failures; attach actionable runbook steps.
- [ ] Set first-release concurrency and job-size targets; load-test them and verify bounded overload behavior, cancellation and worker restart.
- [ ] Run cross-tenant checks for APIs, persistence, artifacts, worker context, cache keys and realtime channels. Verify callbacks and repeated events are authenticated and retry-safe.
- [ ] Save a release record with commit, capability versions, tests, evaluation results, load/recovery evidence, enabled feature list and rollback instructions.

Acceptance: an enabled capability completes in the browser with real dependencies, meets its quality/capacity thresholds, survives restart/retry and can be restored. No placeholders or unsupported quality claims remain in the enabled product.

## Completion checkpoints

1. Foundation complete: H0 lifecycle and authorization gates pass.
2. First production release: foundation, working UI, H1 and applicable H6 gates pass; unfinished capabilities hidden.
3. Testing release: H2 and its H6 gates pass.
4. OCR release: H3 evaluation and operations gates pass.
5. Full core scope: H4 validation and operations gates pass in addition to the preceding releases.

After each slice update IMPLEMENTATION-STATUS.md with implemented behavior, test/evaluation evidence, migration status and remaining blockers. Do not overwrite historical results as if they were new verification. Keep changes confined to Helios unless a separately scoped integration requires work in another system.
