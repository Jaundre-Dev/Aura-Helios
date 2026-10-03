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
| P2 Jobs/usage/billing | **Gate items pass (API)**; scope gaps listed | All six P2 gate conditions are covered by passing integration tests (slices P2.1–P2.3). Not done: invoices (blocked on accounting confirmation of tax-invoice rules), a real payment gateway (blocked on contract), portal views (blocked on Node.js), platform admin for prices/adjustments |
| P3 Document products | In progress (gate not passed) | Safe uploads (P3.1); ocr.general and documents.invoice v1 for native PDFs, sandbox only (P3.2). Gate needs an owner-approved representative dataset, written targets and an OCR engine decision |
| P4 Verification partners | Blocked on contracts/credentials; not implemented | Typed adapters and honest unavailable states can proceed |
| P5 Paid pilots/launch | Not started | Security, quality, economics and operational gates |
| P6 Expansion | Backlog | Customer-led catalogue additions |

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

## Required update format for Claude

For each completed slice record: date, phase, real user-visible behaviour, changed files, exact validation commands and outcomes, remaining blockers, and next concrete step. Mark a phase complete only after its acceptance gate passes. Distinguish synthetic sandbox functionality from verified live integration.
