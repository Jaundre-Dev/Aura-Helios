# HELIOS implementation status

Updated: 2026-10-03. Authority: [root implementation plan](../HELIOS-IMPLEMENTATION-PLAN.md).

## Planning and cleanup handoff (2026-09-28)

- Repository source reviewed against the new business API direction.
- Root product plan, 68-service catalogue, review, Claude instructions and handoff prompt created.
- Old product plans and unused engineering/agent/tool/model/realtime scaffold removed; deleted-file inventory is [REMOVED-LEGACY-FILES.txt](REMOVED-LEGACY-FILES.txt).
- Customer navigation and package/product descriptions now reflect the API platform, with under-development labels.
- Existing identity/workspace/project, audit, secret and object-store foundation retained.
- Existing migrations, generated baseline SQL and historical audit field retained for schema compatibility.
- No live database, customer data, sibling product, deployment or upstream account changed.

## Delivery tracking

| Phase | State | Evidence / next step |
| --- | --- | --- |
| P0 Safe foundation | Backend complete; gate **not fully passed** | Backend build, unit, architecture and disposable-database integration tests pass (slice P0.1 below). Outstanding: frontend lockfile + production build (no npm on this machine), Docker image builds (no Docker on this machine) |
| P1 Portal/catalogue/keys | Backend complete; gate **not passed** | API side of the gate passes (slice P1.1 below). Outstanding: functional portal pages (sign-in, company, team, catalogue, keys) — blocked on Node.js/npm |
| P2 Jobs/usage/billing | **Gate items pass (API)**; scope gaps listed | All six P2 gate conditions are covered by passing integration tests (slices P2.1–P2.3). Platform administration API with TOTP step-up (P2.4); lease renewal and delivery retention (P2.5). Not done: invoices (blocked on accounting confirmation of tax-invoice rules), a real payment gateway (blocked on contract), portal and admin views (blocked on Node.js) |
| P3 Document products | In progress (gate not passed) | Safe uploads (P3.1); ocr.general and documents.invoice v1 (P3.2); bank statement, payslip, proof of address and classification v1 (P3.3) — native PDFs, sandbox only; review corrections and export (P3.4); evaluation harness (P3.5). Gate needs an owner-approved representative dataset, written targets and an OCR engine decision |
| P4 Verification partners | Blocked on contracts/credentials; not implemented | All four partner products are listed as Planned and cannot be executed. A provider adapter framework can proceed before contracts (see Remaining work, B7) |
| P5 Paid pilots/launch | Not started | Security, quality, economics and operational gates |
| P6 Expansion | Backlog | Customer-led catalogue additions |

## Remaining work (consolidated 2026-10-03)

This is the current list. The "Remaining" notes inside each slice below record what was outstanding when that slice landed, and some have since been done: retention purge (P3.1), lease renewal (P2.5), platform administration for prices and adjustments (P2.4). Validation at this point: `dotnet build Helios.sln` succeeded with 0 warnings; `dotnet test Helios.sln` passed UnitTests 174, ArchitectureTests 4 and IntegrationTests 216.

### A. Needs the owner (decision, purchase, contract or access)

Nothing here can be finished in code alone. Each item names what it unblocks.

| # | What is needed | Unblocks |
| --- | --- | --- |
| A1 | Install Node.js LTS with npm on the build machine | Portal pages (sign-in, company, team, catalogue, keys, playground, requests, usage, billing, webhooks, review), platform admin pages, `package-lock.json`, frontend production build, web Docker image. Gates P0 and P1 depend on this |
| A2 | Install Docker (or name a build host that has it) | Verifying the API, worker and web image builds and compose startup (P0 gate) |
| A3 | A lawfully obtained, representative document set per product, and written accuracy, review-rate and latency targets | Running `helios-evaluate` ([EVALUATION.md](EVALUATION.md)), publishing measured results, and moving any document product beyond Sandbox (P3 gate) |
| A4 | Choice of OCR engine or provider (licence, processing region, cost per page) | Scanned PDFs and photos for every document product, and `documents.sa-id`, whose ID documents are images |
| A5 | Payment gateway contract and merchant credentials | A real `IPaymentGateway` adapter and settlement reconciliation. Only the test gateway exists, and Production refuses it |
| A6 | Approved live prices and tax treatment per product | Live use of any product: Beta and Live need a published live price. No prices are seeded |
| A7 | Accountant's confirmation of tax-invoice versus receipt rules, invoice numbering, VAT treatment and credit terms | Invoices and invoice views |
| A8 | Refund, chargeback and negative-balance policy | Turning `NeedsReview` refund and chargeback events into ledger reversals |
| A9 | Partner contracts and credentials for liveness, face comparison, bank account verification and company lookup | P4 adapters for those products, which are Planned and not executable today |
| A10 | Legal and privacy decisions: customer terms and processing-agreement versions, retention limits per product, hosting and processing regions, a POPIA section 72 basis for any cross-border processing, biometric review | Terms/DPA acceptance records (B2), live approval of restricted products, final retention settings |
| A11 | A ClamAV (or equivalent) deployment for each environment | Live uploads. Production refuses to start without a scanner |
| A12 | Production object storage (bucket, region, encryption, lifecycle) | Large or high-volume documents. The current MySQL store is capped at 16 MB per object |
| A13 | A transactional email provider | Real delivery of email verification and password reset (B1) |
| A14 | A pilot sector and 3–5 pilot customers | P5 pilots, pricing validation and the launch gate |

### B. Engineering work that can proceed now

These items need no owner input, unless one is noted.

| # | Work | Notes |
| --- | --- | --- |
| B1 | Email verification, password reset and customer MFA | Token flows and tests can be built against a development mail sink. Real sending needs A13 |
| B2 | Onboarding records: accepted terms and processing-agreement versions, permitted-purpose statements, production-approval request flow | Final document versions come from A10 |
| B3 | Per-key budgets and expiry policies, company spend limits, per-key and per-company rate limits on product execution | Plan sections 4 and 13; abuse limits for live use |
| B4 | Publish OpenAPI outside Development, with curl examples per product; later TypeScript and Python SDKs generated from the contract | Plan section 8 |
| B5 | Usage and ledger CSV exports for Finance | Invoices themselves wait for A7 |
| B6 | Operational metrics: request counts, latency percentiles, queue delay, failure and review rates, reconciliation age, provider spend | Plan section 13. A metrics backend choice may come later |
| B7 | Provider adapter framework: capability and region metadata, per-provider concurrency limits and circuit breakers, typed "unavailable" adapters | Ready for P4 partners once A9 lands |
| B8 | A customer webhook event when a review decision is recorded | Optional |
| B9 | Two-person approval for credit adjustments above a threshold | Optional. Today the control is the R100 000 cap plus the audit trail |
| B10 | Platform MFA recovery codes | Today a lost authenticator needs another Administrator's reset |
| B11 | Runbooks, a backup restore drill script, and a load-test harness | Running them is P5 work and needs an environment |

### C. Known limits of the v1 document products

These are by design, documented in the catalogue limitations, and lifted only through new versions measured under A3:

- English labels only. Digital (native) PDFs only; scans need A4.
- One document, one account or one employee per file.
- Statement transactions must be one line each, starting with a date.
- Classification is rule-based, not a trained model.
- Nothing establishes that a document is authentic, verifies identity, employment or income, or decides regulatory acceptability.

### D. Not started

- **P5**: pilots, load testing at expected traffic, restore drills, alerting and runbook validation, support procedures, status communication.
- **P6**: catalogue expansion beyond the initial 12 services. The 68-service list in [API-CATALOGUE.md](../API-CATALOGUE.md) remains a gated roadmap.
- **Deferred by the plan**: multi-workspace UI, enterprise SSO, postpaid billing, private hosting, realtime updates.

## Slice P0.1 — safe test database, company authorisation, sessions (2026-10-03)

### Behaviour now in place

- **Disposable test database.** Integration tests create `helios_it_<utc-timestamp>_<random>` on a local MySQL, migrate it with the real migrations, and drop only that database afterwards. The fixture refuses non-local hosts (unless `HELIOS_TEST_ALLOW_REMOTE_HOST=1`), refuses to adopt a pre-existing name, validates the name pattern before create/drop, and never calls `EnsureDeleted`. Only host/port/credentials are taken from `HELIOS_TEST_CONNECTION` or the Helios.Api user-secrets; the database named there is never connected to.
- **Company membership.** New `organization_members` table with six permission roles (Owner, Admin, Developer, Finance, Operator, Auditor) mapped to named permissions — no ordinal comparisons. Organisation list returns only the caller's active memberships; detail/members return 404 to non-members.
- **Company creation** atomically creates the company, Owner membership, a `default` workspace and its Owner grant.
- **Workspace creation** requires `ManageWorkspaces` in the target company, checked before the transaction (so the denial audit persists) and again with a `FOR SHARE` lock inside it (so a concurrent removal cannot interleave). Non-members get 404.
- **Team management**: add by email, change role, remove (deactivate). Only an Owner can grant/change/remove Owner; the last Owner cannot be removed or demoted. Removing a company member also deletes their grants on that company's workspaces.
- **Workspace grants** can only be given to active members of the workspace's company; new `DELETE /api/v1/workspaces/{id}/members/{memberId}`.
- **Sign-in**: lockout is checked before the password (5 failures → 15 minutes), inactive accounts refused, one generic 401 for every failure, every attempt audited (`auth.login` with reason). Register/login are rate limited per client address (`Helios:RateLimits:Auth`, default 20/minute → 429).
- **Session revocation**: tokens now carry the account security stamp. Every authenticated request re-checks in the database that the account is active, the stamp matches, and — for workspace-scoped tokens — the workspace grant, workspace, company and company membership are all still live and the role unchanged. `POST /api/v1/auth/revoke-sessions` rotates the stamp ("sign out everywhere"). Tokens without a stamp are rejected, so pre-existing tokens require a new sign-in.
- **Request correlation**: every response has `X-Request-Id` (a safe caller-supplied id is honoured, anything else replaced); problem details carry `requestId`; audit rows record `correlation_id` and, where known, `organization_id`. `AuditLog.AgentRunId` is untouched (historical column).
- **Forwarded headers** are trusted only from proxies listed in `Helios:ForwardedHeaders:KnownProxies` (default none).
- `IUnitOfWork.ExecuteInTransactionAsync` replaces the unused, un-committable `BeginTransactionAsync`; it runs inside the retrying execution strategy, which rejects bare user transactions.

### Schema

Forward migration `20261003094154_OrganizationMembershipAndAuditCorrelation` (no applied migration edited): adds `organization_members`, `audit_logs.correlation_id`, `audit_logs.organization_id` and indexes. It **backfills** memberships so existing companies keep administrators: the organisation's `created_by` user becomes Owner; other existing workspace members become Admin (if they held Owner/Admin on any of its workspaces) or Operator. An Owner should review backfilled roles after upgrading. Organisations with no recorded creator and no workspace members get no members and need platform assignment. `deploy/sql/helios-migrations.idempotent.sql` is a full idempotent script for all four migrations (the retained `0001-initial.sql` only covered the first).

### Deployment fixes (static; images not built here)

- API and worker Dockerfiles now copy `Directory.Packages.props` before restore.
- Both compose files: removed `--default-authentication-plugin`, which MySQL 8.4 no longer accepts (the server would not start).
- `docker-compose.yml`: API now receives the JWT signing key and secret-store key (required, fail-fast `${VAR:?}`); `Helios__Database__ServerVersion=8.4.0`; a one-shot `helios-migrate` service applies the idempotent script before API/worker start; Ollama/vLLM removed from the default stack (no product depends on them); MySQL host port defaults to 3307 to avoid the native service.
- Retired AI-provider settings removed from `appsettings.json` and `.env.example` (nothing read them).

### Validation (run 2026-10-03 on this machine)

| Command | Result |
| --- | --- |
| `dotnet --version` (repo root) | 10.0.401 — `global.json` 10.0.303 + `latestFeature` resolves; the earlier resolver failure no longer occurs |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 21 passed; ArchitectureTests 4 passed; IntegrationTests 65 passed; 0 failed (AITests/EndToEndTests contain no tests) |
| One-off check during a run: `information_schema.SCHEMATA LIKE 'helios_it_%'` | Only the current run's database present — the previous full run had dropped its own |

Integration tests ran against the native MySQL 8.0 service using the existing Helios.Api user-secrets credentials; they touched only their own `helios_it_*` databases.

Security regressions added: `OrganizationAuthorizationTests` (list/detail isolation, cross-company workspace creation refused with nothing written, permission denial persisted, Owner-grant and team-management limits, last-Owner protection, cross-company workspace grant refused) and `SessionSecurityTests` (lockout with correct password, failure-count reset, disabled account token rejection, revoke-sessions, workspace-grant removal, company-member removal, role change, stamp-less token, throttling, request id). HTTP tests now use real registered accounts and real JWTs instead of a test authentication handler.

### Remaining P0 blockers

1. **Frontend**: no Node.js/npm installation on this machine (only a private runtime inside another application). `package-lock.json` cannot be generated, so the frontend production build and the web Docker image (`npm ci`) remain unverified. Needs Node.js LTS with npm installed by the owner.
2. **Docker**: not installed here; API/worker/web image builds and compose startup are unverified.
3. Customer MFA, email verification and password reset are not implemented (plan: after initial identity work).

## Slice P1.1 — catalogue, entitlements, scoped keys, first sandbox product (2026-10-03)

### Behaviour now in place (API only — no portal pages yet)

- **Catalogue** `GET /api/v1/catalogue[/{slug}]` (public): the initial 12 services from plan section 2, seeded by migration. Only `identity.sa-id-validate` is callable, in sandbox only; the other 11 are `Planned` with stated limitations, and the four partner products say they need contracts. "Callable" is computed from release state **and** a registered executor, so the catalogue cannot offer what the code cannot run. No prices are seeded.
- **Entitlements** `GET/POST/DELETE /api/v1/organizations/{id}/entitlements`: per product and environment. Sandbox for a callable product is free and self-service (`ManageEntitlements`); planned products and live use of sandbox-only products are refused (`409 product_unavailable`). Live special-personal-information products would require a purpose and platform approval (`PendingApproval`).
- **API keys** `GET/POST /api/v1/api-keys`, `POST …/{id}/revoke`, `POST …/{id}/rotate` (`ManageApiKeys`, workspace-scoped token): format `hk_test_…`/`hk_live_…` with a random public id and 256-bit secret; only SHA-256 is stored and compared in constant time; secret shown once. Scopes must be products the company has enabled in that environment. Keys are pinned to company, workspace and environment.
- **Key authentication** (`X-Api-Key` or `Authorization: Bearer hk_…`) reads the key row on every request, so revocation, expiry and company/workspace deactivation take effect immediately. API keys are refused (403) on all management endpoints.
- **Execution** `POST /api/v1/products/{slug}/requests`: one path for keys and signed-in users (portal runs need `ExecuteProducts`). Checks in order: environment (a key cannot act outside its own), product exists, callable, company entitlement, key scope, body size (1 MB hard cap, then the version's own bound — 1 KB here), input schema (unknown fields rejected). Invalid input returns 400 and records nothing. Returns the plan-section-8 envelope; a business "no" (bad checksum) is a 200 with `valid: false`.
- **SA ID utility v1**: format, date of birth (century inferred), status digit (citizen / permanent resident / refugee) and Luhn checksum. Sex is deliberately not returned. Every response states that this is not identity or Home Affairs verification. Deterministic first-party code, not a mock.
- **Request records** `api_requests`: tenant, product/version, environment, channel, actor/key, status, usage, billing state. Raw input is never stored — only an HMAC fingerprint (HKDF subkey of the secret-store key, key id recorded for rotation). Results expire after `Helios:Requests:ResultRetentionDays` (default 30; retention purge job not yet built) and then return 410.
- **Idempotency** (`Idempotency-Key`, optional while nothing is billable): same key + same canonical input replays the original (`Idempotent-Replayed: true`); different input returns `409 idempotency_key_reused`; concurrent duplicates resolve to one record via a unique index.
- **Reads** `GET /api/v1/requests[/{id}[/result]]`: workspace-isolated by query filter and explicit checks; keys see only their environment and scoped products. Results need `ViewResults` — Owner, Admin, Operator. Finance gets 403 on both metadata and results; Developer and Auditor get metadata only.
- **Billing profile** `GET/PUT /api/v1/organizations/{id}/billing-profile`: legal name, billing contact and address, registration and VAT number (SA VAT format check only), currency fixed to ZAR. `ViewBilling`/`ManageBilling` (Owner, Finance).
- Contract enums serialise as strings; problem responses carry a machine-readable `code`.

### Schema

Forward migration `20261003100142_CatalogueKeysAndRequests`: `api_products`, `api_product_versions`, `entitlements`, `api_keys`, `api_requests`, `billing_profiles`, plus the catalogue seed (`20261003100142_CatalogueSeed.cs`). `deploy/sql/helios-migrations.idempotent.sql` regenerated (five migrations).

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 52 passed; ArchitectureTests 4 passed; IntegrationTests 102 passed; 0 failed |
| Mutation check: key-scope check disabled; Finance given result access | `A_key_cannot_call_a_product_outside_its_scope` and `Finance_cannot_read_results_while_operators_can` both failed as intended; source restored and suite re-run green |

P1 gate items covered by `ApiKeyAndExecutionTests`: two companies sign up and run the utility independently; cross-company request reads 404 for keys and users; sandbox key refused in live; live key refused without live entitlement; out-of-scope product refused; disabled entitlement stops existing keys; revoked/expired/rotated-out/tampered/unknown keys 401; deactivated company's keys 401; Finance cannot read results. Unit tests cover the ID algorithm (including leap days, century inference, Luhn reference values) and key format.

### Remaining P1 blockers and gaps

1. **Portal UI** (gate requirement): sign-in, company, team, catalogue and key pages are not built. Needs Node.js LTS + npm to install dependencies, produce `package-lock.json`, typecheck and build. Writing pages without being able to compile them would not meet the completion standard.
2. Email verification, terms/DPA acceptance records and production-approval workflow are not implemented (live use is impossible anyway until P2 billing).
3. Retention purge of expired results is not yet a job (results stop being served at expiry; rows keep the payload until P2 jobs exist).
4. Key budgets (plan section 4) wait for the P2 ledger.

### Next step

Install Node.js LTS, then build the P1 portal pages against these endpoints. Independent of that: P2 durable jobs, transactional ledger with atomic reservations, versioned prices, and payment adapter interfaces.

## Slice P2.1 — ledger, reservations, prices, durable jobs (2026-10-03)

### Behaviour now in place

- **Ledger** (`ledger_accounts`, `ledger_transactions`, `ledger_entries`): append-only double entry in ZAR, `decimal(19,6)`. Every transaction balances (legs sum to zero) and carries a unique posting key (`reserve:{request}`, `settle:{request}`, `release:{request}`, `topup:{payment}`, `adjust:{key}`), so any repeat is a no-op. Customer accounts (`CustomerAvailable`, `CustomerReserved`) hold a balance projection updated in the same transaction under `SELECT … FOR UPDATE`; platform accounts (revenue, gateway clearing, adjustments) receive entries only and are never locked, so settlements do not serialise across tenants. Corrections are reversing entries, never edits.
- **Reservations**: one per billable request, `Held` → exactly one of `Settled` (charge ≤ reserved, remainder returned) or `Released`. The reservation row is locked when resolved.
- **Prices** (`price_versions`): versioned per product and environment, immutable; publishing closes the previous version. Charge = unit price × quantity, minimum applied, rounded half away from zero to six places. **No prices are seeded**; `PriceService.PublishAsync` exists but has no customer or admin endpoint yet (platform administration is later work).
- **Usage** (`usage_events`): one immutable row per request (unique), recording product version, price version, units and amount.
- **Execution flow** for every product request: validation → (live) required `Idempotency-Key` and a current price → one transaction creating the request, its durable job (input sealed with AES-GCM, purged at a terminal state) and, for live, the reservation of the maximum charge (`402 insufficient_credit` rolls everything back) → execution through the shared `JobRunner`. Synchronous products run inline under a lease and return 200; asynchronous ones return 202 with a status URL. Sandbox is never billable.
- **Durable jobs** (`jobs`): claimed with `FOR UPDATE SKIP LOCKED`, leased, and fenced by a token that increments on every claim; every transition is a conditional update on that token, so a worker whose lease was taken over commits nothing. Definite provider failures retry with exponential backoff, then fail and **release** the reservation. An ambiguous outcome (`ProviderOutcomeUnknownException`) moves to `Reconciling` with money held; reconciliation settles if the provider completed, re-runs only if the provider confirms it did not, and hands to `NeedsReview` after the configured attempts. An attempt interrupted by a crash is reconciled, not repeated, unless the executor declares itself safe to repeat.
- **Cancellation** `POST /api/v1/requests/{id}/cancel`: only while queued; releases the reservation in full. A worker claiming concurrently wins and the cancel returns 409.
- **Worker host** (`Helios.Worker`): configurable pollers running the same `JobWorker`; each job is processed in a scope confined to its own workspace (claiming alone uses system scope).
- **Production safety**: a Production host refuses to start with `test.*` executors, the fake payment gateway, or webhook SSRF protection disabled.
- Envelopes now carry `error` and, while held, `billing.reserved`. A replay of an in-flight request returns 202 with `Idempotent-Replayed: true`.

### Bug found and fixed during this slice

EF identity resolution returned an already-tracked ledger account (with its pre-lock balance) instead of the row a `FOR UPDATE` read had just returned. In the inline API path that would have computed a settlement from a stale balance and overwritten a concurrent change. Locking reads now overwrite the tracked instance with the locked row's values. A mutation run (fix removed) makes `Concurrent_spend_never_exceeds_available_credit` fail twice in a row; with the fix it passes.

### Schema

Forward migration `LedgerPricingAndJobs`: the seven tables above plus `api_requests.price_version_id` and `reserved_amount`. Idempotent deployment script regenerated (six migrations).

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test tests/Helios.UnitTests` | 60 passed |
| `dotnet test tests/Helios.IntegrationTests` | 120 passed |
| Mutation: balance check under lock disabled | `Concurrent_spend…` and `Insufficient_credit…` failed as intended |
| Mutation: interrupted non-repeatable attempt re-executed instead of reconciled | `A_crash_mid_call…` failed as intended |
| Mutation: stale tracked ledger rows (fix removed) | `Concurrent_spend…` failed as intended |

P2 gate items covered by `BillingAndExecutionTests` (test-only `test.metered` / `test.provider` products, registered only by the test host): 30 concurrent R1 requests against R10 → exactly 10 succeed, 20 get 402, balance ends at 0, never negative; 10 concurrent duplicates → one charge; stale worker after lease takeover → one settlement and one usage row; crash mid-call → reconciled after lease expiry, provider called once; unknown outcome → reconciled and settled once; provider-confirmed non-completion → safe re-run; permanent unknown → `NeedsReview` with money held; internal failure → 3 attempts then full release; 20 status/result polls post nothing. Each test ends by checking every transaction balances and every customer balance equals the sum of its entries. `WorkerHostTests` boots the real worker wiring and watches it settle a job.

### Remaining P2 work

Payment gateway adapter and verified, deduplicated callbacks (no gateway contract exists — a clearly named fake test gateway will stand in, refused in Production); customer balance/usage/transaction endpoints; signed customer webhooks with delivery records and SSRF protection; invoices (blocked on accounting confirmation of tax-invoice rules); lease renewal for long-running jobs; a price/adjustment administration surface for platform staff.

## Slice P2.2 — top-ups, verified payment callbacks, finance views (2026-10-03)

### Behaviour now in place

- **Gateway boundary** `IPaymentGateway`: create checkout; verify a callback (authenticity + freshness) and parse it. Configured by `Helios:Payments:Gateway`. With none configured, top-ups return `503 payments_unavailable` — nothing is faked. Any unknown gateway name fails startup.
- **`fake-test` gateway** (development/tests only — **no real gateway is integrated; that needs a signed contract**): HMAC-SHA256 over `"{timestamp}.{body}"`, constant-time comparison, ±5-minute freshness window, unroutable `payments.invalid` checkout URLs. Production refuses to start with it.
- **Top-ups** `POST /api/v1/organizations/{id}/billing/top-ups` (`ManageBilling`: Owner, Finance): R10–R100 000 in whole cents; creates a pending payment, then asks the gateway for a checkout. Nothing is credited at this point, and there is no endpoint that credits on a browser redirect.
- **Callbacks** `POST /api/v1/payments/callbacks/{gateway}` (anonymous, signature-authenticated, 64 KB cap): unverifiable → 401 and nothing recorded. A verified event is recorded once per (gateway, event id); the payment row is locked; the event must match merchant, currency, exact amount and a known reference or it is recorded as `Rejected`. A success credits the ledger once (posting key `topup:{payment}` is a second guard); duplicates and a failure after success have no effect; refunds and chargebacks are recorded as `NeedsReview` with no ledger change, because no reversal or negative-balance policy has been approved.
- **Finance views** (`ViewBilling`: Owner, Finance): `GET …/billing/balance` (settled, reserved, available), `…/transactions` (ledger history as available and reserved changes), `…/usage?from&to` (per product and environment, company-wide), `…/payments`.

### Schema

Forward migration `Payments`: `payments` (unique gateway + reference), `payment_events` (unique gateway + event id).

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 60, ArchitectureTests 4, IntegrationTests 135 — all passed |
| Mutation: amount match disabled | `…mismatched_callback_is_rejected(amount)` failed as intended; restored |

`PaymentTests` cover: no credit before confirmation; three replays plus a distinct duplicate success credit once; ten concurrent deliveries credit once (one ledger transaction); amount, currency, merchant and unknown-reference mismatches are rejected and recorded; forged, stale (30 min) and unsigned callbacks are 401 with nothing recorded; a failure after success is ignored; a refund changes no money; Developer cannot top up or see balance, a non-member gets 404, Finance can do both; amount bounds; and end to end, a verified top-up funds live usage that then appears in balance, usage and transactions.

### Remaining

Real gateway adapter (contract, merchant credentials, settlement reconciliation); refund/chargeback policy; invoices (tax-invoice vs receipt rules need accounting confirmation); customer webhooks (next slice).

## Slice P2.3 — signed customer webhooks (2026-10-03)

### Behaviour now in place

- **Endpoints** `GET/POST /api/v1/webhooks`, `DELETE /api/v1/webhooks/{id}`, `GET /api/v1/webhooks/{id}/deliveries` (portal users with `ManageApiKeys`; API keys and Finance are refused). Up to 10 active per workspace. The `whsec_…` signing secret is shown once and stored sealed (AES-GCM, keyring).
- **Events**: `request.succeeded`, `request.failed`, `request.needs_review`, `request.cancelled`.
- **Transactional outbox**: deliveries are written in the same transaction as the request's terminal state (job completion, failure, review hand-off, cancellation), with a deterministic event id unique per endpoint, so an event is never lost, invented or duplicated by a retry.
- **Payload** is redacted: request id, product/version, environment, status, error code, usage, billing and a result URL — never the result itself or any input.
- **Delivery** by the worker: leased (`SKIP LOCKED`), signed `Helios-Signature: t={unix},v1={HMAC-SHA256("{t}.{body}")}` plus `Helios-Event-Id`/`Helios-Event-Type`; 2xx = delivered; otherwise exponential backoff (×4 from 30 s, capped at 6 h) up to 8 attempts, then dead-lettered (`Failed`) and visible in the delivery list. Delivery is at-least-once; receivers deduplicate on the event id. Inactive endpoints dead-letter pending deliveries.
- **SSRF protection**: registration requires https, no credentials and no internal host/IP literal; at connection time the real handler resolves the name and refuses loopback, private, CGNAT, link-local (including 169.254.169.254 metadata), multicast/reserved, IPv6 unique-local/link-local and IPv4-mapped forms — so DNS rebinding after registration is still blocked. No redirects, no proxy, 5 s connect / 10 s total timeout. `Helios:Webhooks:AllowPrivateNetworks` exists for local development only and Production refuses it.

### Schema

Forward migration `Webhooks`: `webhook_endpoints`, `webhook_deliveries` (unique endpoint + event id). Idempotent deployment script regenerated (eight migrations).

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 81, ArchitectureTests 4, IntegrationTests 153 — all passed |
| Mutation: address classifier always "allowed" | 7 `WebhookTests` failed (IP-literal registrations and all connect-time refusals); restored |

`WebhookTests` cover signed delivery verified with the customer's secret and no result or input in the body; unsubscribed events not sent; 3 failed attempts then dead letter (test host max = 3); deactivation stops delivery; cross-company list/deliveries/delete are empty or 404; Finance and API keys refused; nine unsafe URLs refused at registration; the production handler refuses to connect to 127.0.0.1, `localhost` and 169.254.169.254. Delivery tests use an in-memory receiver in place of the network.

## P2 gate assessment (2026-10-03)

| Gate condition (plan section 14) | Evidence |
| --- | --- |
| Concurrent spend cannot exceed available credit | `Concurrent_spend_never_exceeds_available_credit` (30 × R1 vs R10 → 10 succeed, 20 × 402, balance 0) |
| Duplicate submissions never double-charge | `Concurrent_duplicate_submissions_charge_once`, `Concurrent_requests_with_one_idempotency_key_record_once` |
| Duplicate callbacks never double-credit | `A_replayed_callback_credits_once`, `Concurrent_deliveries_credit_once` |
| Restart recovery cannot duplicate settlement | `A_stale_worker_cannot_commit…`, `A_crash_mid_call…`, `Settlement_and_release_are_each_applied_at_most_once` |
| Unknown vendor completion reconciles | `An_unknown_provider_outcome_reconciles…`, `A_provider_confirming_non_completion…`, `A_permanently_unknown_outcome_goes_to_review…` |
| Failed internal processing releases credit | `Failed_internal_processing_releases_the_reservation` |
| Status polling is free | `Status_polling_is_free` |

These are proven against MySQL with test-only products and the fake gateway. They are **not** evidence of a live paid service: no real product is priced, no real gateway is integrated, and no portal exists. Outstanding P2 scope: invoices (needs the owner's accountant to confirm receipt versus tax-invoice rules and credit terms), a real gateway adapter (contract), platform administration for prices and credit adjustments, lease renewal for long jobs, and a retention job purging expired results and old deliveries.

## Slice P3.1 — safe document uploads and retention (2026-10-03)

### Behaviour now in place

- **Uploads** `POST /api/v1/uploads` (multipart field `file`; API keys or users with `ExecuteProducts`), `GET /api/v1/uploads/{id}`, `GET …/{id}/content`, `DELETE …/{id}`. Each upload belongs to one company, workspace and environment.
- **Checked before anything is stored**: type detected from the bytes (PDF, PNG, JPEG, TIFF only) and compared with the client's claim; size limit (10 MB default) and page limit (50 default); PDFs parsed (malformed or encrypted refused); **active content refused by two independent layers** — a raw-byte name scan (stream bodies skipped to avoid false positives, `#xx` hex escapes decoded) and a structural catalogue check (open actions running JavaScript/Launch, document actions, script/embedded-file name trees, XFA); image headers checked against decompression-bomb limits. Refusals are audited; refused files are never stored.
- **Malware scanning**: a ClamAV adapter (`INSTREAM` over TCP). With no scanner configured, uploads are marked `NotScanned` and usable in sandbox only; live uploads are refused (`503 scanner_unavailable`), and live requests refuse unscanned uploads. **Production refuses to start without a scanner.** ClamAV itself is not installed here; the adapter is tested against a protocol-level fake that flags the EICAR test string.
- **Access**: metadata for executors, result readers and diagnostics roles; document download requires `ViewResults` (Finance and Developer cannot download). Keys see only their own environment. File names are sanitised display names, never paths.
- **Retention**: uploads expire after `Helios:Uploads:RetentionDays` (default 7). The worker's retention sweep deletes expired document content and expired result payloads, workspace by workspace, keeping metadata for audit and billing. This also closes the P1 gap where expired results were hidden but still stored.
- Document products receive uploads through a workspace-confined accessor; acceptance checks that a referenced upload belongs to the caller's workspace and environment, is still available, is a supported type and within the product's page limit — before anything is reserved.
- New dependency: **PdfPig 0.1.16** (Apache-2.0) for PDF parsing and test fixture generation. No OCR engine is included.

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 94, ArchitectureTests 4, IntegrationTests 171 — all passed |
| Mutation: raw-byte scan off | Both JavaScript fixtures still refused by the catalogue layer |
| Mutation: catalogue check off | Both still refused by the raw-byte layer |
| Mutation: both layers off | `…(active_content)` and `Hex_escaped_…` failed as intended; restored |

A first version of the JavaScript fixture lacked a cross-reference table, so it was refused as malformed and never reached the catalogue check; the layer-by-layer mutation run exposed this and the fixtures are now well-formed PDFs.

## Slice P3.2 — native-PDF text and invoice extraction (2026-10-03)

- **`ocr.general` v1** (Sandbox): text, lines and normalised line boxes from a PDF's own text layer. Pages without a text layer (scans) are returned empty, flagged for review and **not charged** (billed per page read). No OCR engine — that is an owner decision.
- **`documents.invoice` v1** (Sandbox): invoice number, dates, supplier/customer names (only when labelled), VAT numbers, currency, subtotal, VAT, total and simple line items, each with page, box and source line; checks `totals_add_up`, `line_items_sum`, `vat_rate` (15%, "differs" not "fail"), `due_after_issue`, `supplier_vat_format`. Missing fields are null and listed; review is required when a required field is missing or a check fails.
- Both are asynchronous (202 then worker), accept only PDFs in the caller's workspace/environment, enforce page limits (50 / 20) at acceptance, and fail once without retry (`upload_unavailable`) if the document is deleted after acceptance. A zero-unit outcome is never charged the minimum.
- Forward data migration `DocumentProductsV1` publishes both as **Sandbox only** with schemas and stated limitations.

Validation: `dotnet test Helios.sln` → UnitTests 116, ArchitectureTests 4, IntegrationTests 180, all passed; 0 build warnings.

**P3 gate not passed:** accuracy, review-rate and latency have only been exercised on synthetic PDFs. The gate needs an owner-approved, lawfully obtained representative dataset and written targets. Remaining P3 work: an evaluation harness/report over such a dataset, an OCR engine decision for scanned documents, and the other document products (SA ID, statement, payslip, proof of address, classification).

## Slice P2.4 — platform administration (2026-10-03)

### Behaviour now in place (API only)

- **Platform staff** (`platform_staff`): roles Administrator, Finance, Support, mapped to named permissions and held apart from customer company roles. Support has no access to money, staff, approvals or the catalogue; Finance has no access to staff, approvals or the catalogue. No staff member can grant, change, deactivate or reset their own record. No platform route returns customer inputs, documents or results.
- **First Administrator**: a host console command, `dotnet Helios.Api.dll platform-staff grant <email> <Administrator|Finance|Support>`, for an account that has already registered. It is not reachable over HTTP and is audited with source `console`.
- **Mandatory MFA (plan section 5)**: `POST /api/v1/platform/mfa/enrol` returns an RFC 6238 authenticator secret once. `…/confirm` and `…/verify` take a code and issue a **15-minute platform session** (`platform_role` + `amr=mfa` claims). That session is the only credential `/api/v1/platform/*` accepts; ordinary tokens (even a staff member's own) and API keys get 403. A code is accepted only for a time step later than the last one accepted, using one conditional update, so it cannot be replayed. Five bad codes lock the staff member for 15 minutes. Once an authenticator is confirmed, it can only be re-enrolled after another Administrator resets it. Secrets are sealed with the keyring. Every request re-checks the staff row, so deactivation, a role change or an MFA reset ends the session immediately.
- **Administration** (`/api/v1/platform/…`):
  - Tenant list with members and balances.
  - Approve or reject restricted entitlements with a reason. An approval cannot enable a product that is not callable.
  - Release-state changes. Callable states require an executor and a published version; Beta and Live also require a current live price.
  - Price publishing. Prices cannot take effect in the past.
  - Credit adjustments: idempotent per reference, reusing a reference for a different amount gives 409, R100 000 cap, whole cents, and a debit cannot go below zero.
  - Review lists of payment events (refunds, chargebacks, rejected callbacks).
  - A cross-tenant audit trail (Administrator only).
- **Operations**:
  - Job queue (NeedsReview and Reconciling by default).
  - Resolution of requests stuck in review. **Release** fails the request, releases the reservation once and notifies the customer. **Reconcile** asks the provider again with a fresh budget. There is deliberately no "settle without a result".
  - Dead-letter webhook list, and retry to active endpoints only.
- Every denial and change is audited with actor, tenant and reason.

### Fix found during this slice

The webhook outbox now skips an event an endpoint already has. Before, a request returning to review a second time would hit the unique index and roll back its own state change. It also reads endpoints with an explicit workspace filter instead of the ambient one, so platform-side resolutions notify the customer.

### Schema

Forward migration `20261003140132_PlatformAdministration`: `platform_staff` (unique user, FK to users). Idempotent deployment script regenerated (nine migrations, additive diff only).

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 131, ArchitectureTests 4, IntegrationTests 196 — all passed |
| Mutation: TOTP replay check relaxed (`<` → `<=`) | `A_code_is_accepted_once_and_never_replayed` failed as intended; restored |

Unit tests check TOTP against the RFC 6238 SHA-1 vectors and RFC 4648 base32, plus the platform role separations. `PlatformAdministrationTests` (16) covers:

- ordinary sessions and keys refused;
- customers cannot enrol;
- replay refused, and lockout after five codes;
- deactivation, role change and MFA reset end sessions;
- no self-changes;
- role separation with audited denials;
- credit idempotency, bounds and overdraw;
- backdated, unimplemented and unpriced release refusals;
- suspend and restore audited;
- future price closes the current one;
- approval and rejection with reasons;
- a review release charges nothing and notifies once;
- re-reconciliation returns to review without a duplicate event;
- dead-letter retry, including the deactivated-endpoint refusal;
- the console command.

### Remaining

- No admin portal UI (needs Node.js).
- Customer MFA, email verification and password reset are still not built.
- A refund and chargeback reversal policy needs an owner or accountant decision before those events can move money.
- No two-person approval for large adjustments (the cap is the current control).

## Slice P3.3 — statement, payslip, proof-of-address and classification v1 (2026-10-03)

All four read the native PDF text layer with deterministic rules and are published as **Sandbox only** by the forward data migration `20261003141528_DocumentProductsV2`. They share the P3.2 path: asynchronous (202, then the worker), PDF only, caller's workspace and environment only, page limits enforced at acceptance, billed one document. A document with no text layer is `readable: false`, flagged for review and not charged. Values come only from labelled lines and carry page, box and source line as evidence; missing values are null and listed, never guessed.

- **`documents.bank-statement`** (20 pages):
  - Fields: institution (only a known SA bank name printed above the transactions), account holder, account number **masked to the last four digits in the value and the evidence**, period, opening and closing balances.
  - Transactions: dated lines with amount, direction and running balance. Direction comes from a printed sign or Cr/Dr marker, or from the running balance; otherwise it is null and the statement is flagged.
  - Year-less dates ("28 Dec") are read only inside the stated period.
  - Checks: `balance_continuity`, `closing_balance_reconciles`, `dates_within_period`, `period_order`.
- **`documents.payslip`** (5 pages):
  - Fields: employer, employee, employee number, pay date, pay period, gross, total deductions, net, PAYE, UIF. Identity numbers are deliberately not extracted.
  - Checks: `net_equals_gross_minus_deductions`, `net_not_above_gross`, `pay_date_in_period` (10-day grace, "differs").
- **`documents.proof-of-address`** (5 pages):
  - Fields: issuer and account holder (labelled only), the address block after an address label (ends at a four-digit postal code, at most 6 lines, one combined evidence box), postal code, document date ("Date of birth" is never taken as the date), masked account number.
  - Checks: `not_future_dated`, `recent` (≤ 92 days; older is "differs" because acceptable age is the customer's policy), `postal_code_present`. It does not decide regulatory acceptability.
- **`documents.classify`** (50 pages):
  - Rule-based: it counts distinctive labels for invoice, bank_statement, payslip, proof_of_address and identity_document, each with evidence.
  - A type is chosen only with at least 3 indicators and a lead of 2; otherwise it returns `unknown` and needs review. Scores are counts, not probabilities.
  - Catalogue delivery is corrected from AI to Build.
- `DocumentText` and `TextLayerProduct` hold the shared labelled-field, money and date parsing and the executor shape.

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 150, ArchitectureTests 4, IntegrationTests 203 — all passed |
| Mutation: account-number masking returns the raw number | 5 unit tests and `A_statement_is_extracted_reconciled_and_masked` failed as intended; restored |

Existing tests were updated where their premise changed: the catalogue honesty test now lists the seven implemented sandbox products, and the "planned product cannot execute" test uses `documents.sa-id`, which is still planned.

### Remaining P3 work

- `documents.sa-id` needs an OCR engine, because ID documents are images.
- The P3 gate needs an owner-approved representative dataset with written accuracy, review-rate and latency targets for every product. All results so far are on synthetic PDFs.
- Review corrections and export are next.

## Slice P3.4 — review corrections and export (2026-10-03)

### Behaviour now in place

- **Review** `POST /api/v1/requests/{id}/review` takes one of three decisions:
  - `Approve` takes no corrections.
  - `Correct` needs at least one correction.
  - `Reject` needs a reason.

  Corrections are addressed by path: `fields.<name>` (also fills a field that was not found), or `<list>[i].<property>` for a plain value in a list item (a statement transaction, an invoice line). Paths must exist in the result. Evidence and markers cannot be edited, and values are text, numbers, booleans, null or short text lists. The limit is 100 per decision.

  Decisions are **append-only** (`review_decisions`): the stored result is never modified; each decision keeps its actor (user or API key), reason and time; the corrections in force are all `Correct` decisions applied in order. State: `not_required`, `pending`, `approved`, `corrected` or `rejected`. Audit rows name the corrected paths, never the values.
- **Permissions**: a new `ReviewResults` permission is held by Owner, Admin and Operator. Finance and Developer cannot review. Reading reviews and exporting need `ViewResults`. API keys may review and export only their own environment and scoped products. Other workspaces get 404.
- **Export** `GET /api/v1/requests/{id}/export?format=json|csv`:
  - JSON carries the corrected result (with `corrected: true` on changed values and the original evidence kept), the untouched original and the review state.
  - CSV has one row per value: section, name, original, correction, final, page, source. Cells a spreadsheet would run as formulas get an apostrophe; negative amounts stay numbers.
  - Each export is audited.
- **Retention**: when a result expires, the sweep also removes corrected values (`corrections_json` → null, `purged_at` set). The decision record is kept for audit, and exports then return 410.

### Bug found during this slice

Export showed every uncorrected value as "corrected to blank": a dictionary lookup on a struct-valued map returned an undefined `JsonElement` instead of null. A unit test exposed it, and it is fixed.

### Schema

Forward migration `20261003142237_ReviewDecisions`: `review_decisions` (FK to `api_requests`, workspace-filtered). Idempotent deployment script regenerated.

### Validation (2026-10-03)

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 160, ArchitectureTests 4, IntegrationTests 213 — all passed |
| Mutation: key environment/scope check on review removed | `Only_reviewing_roles_review_and_only_result_readers_export` failed as intended; restored |

## Slice P2.5 — lease renewal and delivery retention (2026-10-03)

- **Lease renewal**: while an executor (or a reconciliation call) runs, a heartbeat extends the job's lease every `Helios:Execution:LeaseRenewalSeconds` (default a third of the lease). Each renewal is one conditional UPDATE on its own connection, matched on job id, fencing token and Running state. Long work is therefore no longer reclaimed and repeated after two minutes. If a renewal finds the lease taken over, the executor's cancellation token fires and the attempt commits nothing. The new owner reconciles or re-runs under the usual rules. A failed renewal (database blip) is retried at the next interval.
- **Delivery retention**: the retention sweep deletes delivered and dead-lettered webhook deliveries older than `Helios:Webhooks:DeliveryRetentionDays` (default 30). Pending deliveries, which form the outbox, are never deleted.

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 160, ArchitectureTests 4, IntegrationTests 216 — all passed |
| Mutation: renewal reports success without updating | Both lease tests failed as intended; restored |

`LeaseAndRetentionTests` cover three cases:
- a held-open provider call whose lease visibly advances, then completes and settles once;
- a takeover (fencing token moved) that cancels the executor, commits nothing, and is later recovered with exactly one usage row and one settlement;
- old delivered and failed deliveries purged while old pending and recent ones are kept.

## Slice P3.5 — accuracy evaluation harness (2026-10-03)

- **`helios-evaluate`** (new console project `src/Helios.Evaluation`) runs a product version in process over an owner-supplied labelled dataset and writes `report.json` and `report.md`. It reports:
  - per-path accuracy (correct, wrong, missed, invented);
  - invented values;
  - review rate;
  - p50/p95 processing time (after one untimed warm-up);
  - each written target as met or missed.

  Exit 0 means all targets met; 2 means missed or none written; 1 means the run could not start.
- Paths are the reviewer paths (`fields.total`, `transactions[0].direction`, `type`). An expected `null` means the value must not be extracted.
- Safeguards:
  - Refuses a dataset folder inside a HELIOS checkout, so customer documents stay out of source control.
  - Document paths cannot escape their dataset folder.
  - Reports omit document values unless `--include-values` is given.
  - Without written targets the gate cannot pass.
- Format and usage: [EVALUATION.md](EVALUATION.md). Core logic: `Application/Features/Evaluation`.

| Command | Result |
| --- | --- |
| `dotnet build Helios.sln` | Succeeded, 0 warnings, 0 errors |
| `dotnet test Helios.sln --no-build` | UnitTests 174, ArchitectureTests 4, IntegrationTests 216 — all passed |
| `dotnet run --project src/Helios.Evaluation -- <synthetic statements outside repo>` | All written targets met, exit 0; reports written |
| Same command with the dataset inside the repository | Refused, exit 1 |

`EvaluatorTests` cover:
- per-path tallies, including a deliberately wrong label and a missed field;
- targets met and missed, and the absent-target case;
- redaction of values;
- unreadable-document errors failing the gate;
- product mismatch refusal;
- the comparison rules;
- path resolution.

**This is a measuring tool, not a measurement.** No representative dataset exists yet, so no product has a measured result and the P3 gate is still open.

## Slice B1/B10 — email verification, password reset, customer MFA, recovery codes (2026-10-03)

- **Email** (`Helios:Email:Sender` = `smtp` | `file`): an SMTP sender for any contracted relay (TLS by default; credentials from secret configuration), and a development file drop that Production refuses. With no sender configured, the flows that need email return `503 email_unavailable`.
- **Tokens**: single-use random tokens; only SHA-256 hashes are stored (`account_tokens`). Verification links last 48 hours, reset links 30 minutes. Issuing a new token kills the previous one. Redemption is a conditional update, so it is race-safe and works on any API instance (ASP.NET data-protection tokens were deliberately not used).
- **Email verification**: a link is sent at registration; `POST /auth/verify-email` and `/auth/resend-verification` handle it. **Creating a company now requires a verified email** (plan section 4). Existing accounts verify the first time they create a company.
- **Password reset**: `POST /auth/forgot-password` always returns 202 and sends nothing for unknown addresses. `POST /auth/reset-password` checks the new password before spending the link. A completed reset rotates the security stamp (signing the account out everywhere), lifts any lockout and marks the email verified.
- **Customer two-step sign-in**:
  - Set-up: `POST /auth/mfa/enrol`, then `…/confirm`, which returns 10 one-time recovery codes; `…/disable` needs a code; `…/recovery-codes` regenerates them with a current code; `GET /auth/security` shows the state.
  - Sign-in: with an authenticator on, the password alone returns `{mfaRequired, mfaToken}`. That token is a 5-minute JWT for a separate audience, so bearer authentication rejects it outright. `POST /auth/mfa/login` exchanges it, with a fresh TOTP code or an unused recovery code, for a session.
  - Rules: codes cannot be replayed; five failures lock for 15 minutes; "sign out everywhere" kills open challenges.
- **Platform recovery codes (B10)**: `POST /api/v1/platform/recovery-codes`. A recovery code stands in for the authenticator once at `/platform/mfa/verify`. An MFA reset invalidates the staff member's codes.

### Bug found and fixed

The code-lockout counter (customer and platform staff) locked after **four** failures, not five. Both values were set in one UPDATE with assignments referring to each other, and MySQL evaluates SET clauses left to right. It is now a compare-and-set on the observed count. The platform lockout test now asserts `mfa_invalid` for the first five failures, so it would catch this.

### Schema

Forward migration `AccountSecurity`: `account_tokens`, `user_authenticators`, `recovery_codes`. Idempotent script regenerated.

| Command | Result |
| --- | --- |
| `dotnet test Helios.sln --no-build` | UnitTests 174, ArchitectureTests 4, IntegrationTests 221 — all passed |

`AccountSecurityTests` (5) cover:
- company creation blocked until verification; links single-use, replaced on resend, bound to their account;
- reset: no enumeration, weak passwords refused without spending the link, single use, old sessions revoked, lockout lifted;
- MFA: the challenge is not a bearer token, no replay, a recovery code works once, revoke kills challenges;
- lockout after exactly five bad codes; disabling MFA needs a code;
- platform recovery codes.

Test accounts now verify their email by following the emailed link.

## Slice B2 — agreement acceptance and live gating (2026-10-03)

- **Published documents** (`Helios:Legal:Documents`, e.g. `terms` and `dpa` mapped to their current version). The texts and versions are the owner's legal decision (A10). Until they are configured, enabling any **live** entitlement returns `409 agreements_not_published`. Sandbox is unaffected.
- **Acceptance**: `GET /api/v1/organizations/{id}/agreements` lists each required document, its current version and this company's acceptance. `POST …/agreements {document, version}` records acceptance of the **current** version only. A new `AcceptAgreements` permission restricts this to the Owner. The record (`agreement_acceptances`) keeps the document, version, who accepted, when and the client address, unique per company, document and version. It is append-only and audited.
- **Live gating**: enabling a live entitlement requires every published document's current version to be accepted (`409 agreements_required`). Live entitlements already enabled keep working after a version change; re-acceptance is needed before new live enables.
- **Permitted purpose**: live use of any product that processes personal information (sensitivity Personal or SpecialPersonal) requires a stated purpose (`409 purpose_required`). It is stored on the entitlement. SpecialPersonal products still wait for platform approval (P2.4).

Forward migration `AgreementAcceptances`; idempotent script regenerated.

| Command | Result |
| --- | --- |
| `dotnet test Helios.sln --no-build` | UnitTests 174, ArchitectureTests 4, IntegrationTests 225 — all passed |

`AgreementTests` (4) cover:
- refusal before acceptance;
- Owner-only, current-version-only acceptance, and idempotent repeats;
- unknown documents rejected;
- sandbox needing nothing;
- the purpose requirement for personal-information products;
- live use closed while no documents are published.

Test companies accept the test documents before going live.

## Required update format for Claude

For each completed slice record: date, phase, real user-visible behaviour, changed files, exact validation commands and outcomes, remaining blockers, and next concrete step. Mark a phase complete only after its acceptance gate passes. Distinguish synthetic sandbox functionality from verified live integration.
