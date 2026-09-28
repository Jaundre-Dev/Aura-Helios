# AURA HELIOS implementation plan

Authoritative product plan, 28 September 2026. This replaces the former AI engineering platform direction.

## 1. Product and authority

Build a South African business API platform. Companies register, select services, receive scoped API keys, fund a Rand-denominated account, run services through API or portal, and pay for measured usage. The catalogue includes AI, deterministic software utilities, and authorised partner-backed services.

This file is the implementation source of truth. Read HELIOS-REVIEW.md, API-CATALOGUE.md, CLAUDE.md and docs/IMPLEMENTATION-STATUS.md with it. Former code-review/testing/agent operating-system plans are superseded. No dependency on an Aura-AI completion gate. Do not access, alter, migrate or delete sibling products. Existing HELIOS database migrations and data are preserved.

The owner requested planning and legacy cleanup in this handoff, not a claim that the new platform is implemented. Claude should implement the phases below and record evidence after each slice. Never expose mock results as production verification.

## 2. Commercial strategy

Positioning: one integration and one Rand bill for document capture, customer verification and supplier onboarding. Start with accounting software providers, procurement platforms, property software companies and software agencies. Insurance, lending and HR are later sales tracks with specific diligence requirements. Partner resale contracts, data permissions, unit economics and customer willingness to pay remain unconfirmed.

Validate with 10–15 buyer interviews and 3–5 paid pilots. Obtain representative, lawfully supplied documents, actual monthly volumes, current processing cost, correction rates, required turnaround and procurement requirements. Prefer recurring platform integrations over occasional manual checks. Target outcomes: reduced capture time, fewer payment-detail errors and faster onboarding. Do not advertise unmeasured fraud-prevention or accuracy guarantees.

Revenue: prepaid pay-as-you-go first; volume discounts and approved enterprise postpaid later. Monthly plans may include support, team features and usage credits, but must explain which charges are usage versus subscription. Never sell unlimited upstream consumption. Endpoints with no meaningful stand-alone willingness to pay can be included as low-cost bundle features.

First catalogue: general OCR, SA ID extraction, invoice extraction, statement extraction, payslip extraction, proof-of-address extraction, document classification, ID format validation; add specialist liveness, face comparison, bank verification and company lookup only when their agreements and quality gates are satisfied. A focused paid pilot is tentatively 12–18 weeks for a small experienced team, not a delivery commitment.

## 3. Scope and terminology

- Organisation: legal/billing customer company. Use Organisation/Company language in UI while preserving existing Organization code naming.
- Workspace: existing execution-isolation boundary. Provision one default workspace per company initially; all requests, uploads, jobs and keys retain WorkspaceId and OrganisationId relationships. Multi-workspace UI is deferred.
- Project: retained foundation, interpreted as a customer application/integration, not a source-code repository. Defer project UI unless needed for key organisation and reporting.
- API product: versioned service with schema, unit, pricing, limits and release state.
- Entitlement: whether this organisation may use a product in a particular environment.
- Request/job: a durable execution record, distinct from transport attempts and provider attempts.
- Provider: internal fulfilment supplier or self-hosted implementation; customers do not receive supplier credentials.
- Sandbox and live: distinct credentials, quotas, data and billing policies. Synthetic fixtures only in sandbox by default.

Out of scope: autonomous engineering agents, repository cloning/code review, test-execution hosting, engineering/architecture/legacy dashboards, open-ended tool execution, shared sibling-product databases and a general consumer chatbot. Payment collection is for AURA's own service fees; money movement APIs are a later partner product, not authority to operate a financial institution.

## 4. Customer journeys

### Company onboarding

Sign up → verify email → create company and owner membership atomically → set billing details → receive default workspace → select products → use sandbox → accept required terms/provide permitted-purpose information → production approval when applicable → fund wallet → create a scoped live key. Users may belong to multiple companies but selection must be authorised server-side. Selecting a free entitlement does not silently start a paid subscription.

Collect company name, registration identifier where applicable, billing contact/address, VAT information where applicable, owner identity/contact, accepted terms version, privacy/processing agreement version and intended use of restricted services. Minimise collected data; do not require unnecessary official checks for harmless utility access.

### Developer

Browse catalogue → inspect request/response and charging examples → generate sandbox key → run sample → see request ID, units and synthetic result → enable live service → create key with permitted products, workspace, expiry and budget → run bounded work → inspect redacted logs, usage and webhook deliveries. Show plaintext key once; support rotate and revoke.

### Manual operator

Choose an entitled service → upload permitted files → see size/page constraints and estimated maximum price → submit → view queued/running state → inspect structured results with field evidence and review warnings → correct/export as permitted. Manual runs use the same authorisation, metering and execution path as API calls. Corrections preserve original output and who changed it.

### Finance

View settled balance, reserved credit and available credit separately. Top up through an approved gateway. See per-product charges, credits, reversals, invoices and exports. Finance access does not automatically grant document or biometric access. Enable optional auto-top-up only with explicit payment authorisation and spend limits.

## 5. Roles and controls

Implement permission policies rather than assuming the existing WorkspaceRole ordinal covers new roles. Owner: ownership/team/billing/policies; Admin: approved operational administration; Developer: keys, documentation and redacted request diagnostics; Finance: invoices/payments/usage; Operator: allowed manual executions and results; Auditor: permitted audit reads. Platform staff have separate platform roles with audited support access and no default access to raw customer documents. MFA for platform administrators; plan customer MFA/enterprise SSO after the initial identity work.

Check membership and account state on protected access, including already-issued tokens after removal. Restrict company list/detail, creation of child workspaces and membership changes to authorised actors. Query filters are defence in depth, not proof of full tenant isolation. Block cross-company relationships on writes. Background jobs resolve a narrowly scoped system context for their own tenant; never run all tenant work under an unrestricted context.

## 6. Architecture decision

Retain .NET 10 API/Application/Domain/Infrastructure/Contracts/Worker, React + TypeScript + Vite + Fluent UI, and MySQL with existing EF/Pomelo compatibility pins. Do not rewrite to Node/PostgreSQL merely because those were generic options in the conversation. Preserve architecture tests. Revalidate package support/security and deployment images before production. Python OCR workers are optional only if measured library/provider needs justify a separate runtime.

Start as a modular monolith with separately deployable worker processes. Suggested modules: Identity, Companies, Catalogue, Entitlements, ApiKeys, Requests, Jobs, Providers, Documents, Verification, Billing, Payments, Webhooks, Audit, Administration. Provider SDKs remain in Infrastructure. Contracts contain versioned DTOs; Domain does not call vendors. Use object storage for production blobs, not growing database binary rows. Existing IObjectStore and encrypted secret storage are useful seams, pending audit.

MySQL is durable truth for jobs, idempotency, ledger and outbox. Redis may support cache/rate limiting and transport but must not be the only copy of financial or accepted-job state. Select a durable queue mechanism with leases and acknowledgements. Begin with transactional database-backed jobs/outbox if that reduces consistency risk; document the choice. Do not invent dozens of microservices.

Request flow: authenticate → resolve tenant/environment → authorise entitlement and key scopes → validate bounds → atomically create idempotent request and reserve credit → enqueue through transaction/outbox → execute with approved adapter → validate result → record metered units → atomically settle/release reservation and commit terminal result → publish redacted customer webhook through outbox.

## 7. Data model

Use additive migrations. Every tenant entity needs ownership constraints and access tests; cross-table organisation/workspace consistency must be enforced. Core proposed records:

| Record | Required contents/invariants |
| --- | --- |
| OrganisationMembership | organisation, user, permission role, active state; unique membership |
| BillingProfile | organisation, legal name, currency, invoice details and tax configuration |
| ApiProduct/ProductVersion | stable slug, schemas, limits, unit, release state, sensitivity, version |
| Entitlement | organisation, product, environment, enabled/approved state, purpose, limits |
| ApiKey | public prefix, non-recoverable verifier, organisation/workspace/project, environment, scopes, expiry, revoked timestamp |
| Provider/ProviderCapability | secret reference, approved regions, contractual permissions, supported schema, timeout, quota and availability |
| PriceVersion | product, currency, unit, minimum/size bands, effective dates, tax treatment; immutable once referenced |
| ApiRequest | tenant, actor/key, environment, product/schema/price versions, payload fingerprint, idempotency key, status, timestamps |
| Job/Attempt | request, lease owner/expiry/fencing token, attempt count, provider reference, outcome and reconciliation state |
| Upload/ResultArtifact | opaque object reference, owner, size, media type, scan state, retention/deletion timestamps |
| UsageEvent | immutable request/product/version/unit quantity; unique settlement identity |
| LedgerTransaction/Entry | balanced postings, currency/precision, type, request/payment reference, immutable audit linkage |
| Reservation | wallet, request, amount, state; exactly one release or settlement |
| Payment/PaymentEvent | gateway reference, idempotent event ID, amount/currency, verified status |
| Invoice/InvoiceLine | invoice sequence, billing period, frozen legal/rate/tax snapshot, adjustments |
| WebhookEndpoint/Delivery | tenant-owned destination, encrypted signing secret, event ID, attempts, next retry, final status |
| ConsentEvidence | purpose, actor/subject reference, notice/terms version, timestamp, permitted scope; minimise stored evidence |
| ReviewDecision | original result, correction/decision, actor, timestamp, reason |

Unique keys must include tenant and environment when appropriate. Use fixed-precision decimal/integer accounting, never binary floating point. Choose precision allowing sub-cent unit pricing; define when to round invoice totals. Capture foreign provider cost and FX assumptions separately from the contracted Rand customer price.

Preserve historical audit AgentRunId mapping until an additive compatibility migration can introduce ApiRequestId. Do not edit applied migrations or infer that legacy field names mean the removed agent product should return.

## 8. API contract

Keep existing identity routes until migration compatibility is decided. Proposed customer surface:

| Route | Behaviour |
| --- | --- |
| GET /api/v1/catalogue | Accessible product details, versions, units, availability and prices |
| GET/POST /api/v1/api-keys | Authorised key management; create returns secret once |
| POST /api/v1/api-keys/{id}/revoke | Immediately disable further access |
| POST /api/v1/uploads | Validate metadata and issue scoped bounded upload operation |
| POST /api/v1/products/{slug}/requests | Canonical execution endpoint; required Idempotency-Key for billable work |
| GET /api/v1/requests/{id} | Status and permitted result metadata |
| GET /api/v1/requests/{id}/result | Tenant-authorised structured result |
| POST /api/v1/requests/{id}/cancel | Best-effort cancellation under documented billing rules |
| GET /api/v1/usage | Filtered settled/pending usage, environment, product, period |
| GET /api/v1/billing/balance | Settled, reserved and available amounts |
| GET /api/v1/billing/invoices | Authorised financial records |
| POST /api/v1/billing/top-ups | Create approved gateway payment session |
| GET/POST /api/v1/webhooks | Authorised endpoint configuration and delivery inspection |

An accepted async request returns 202, request ID, status URL, estimated/reserved cost and environment. Status/result polling is not a second billable execution. A bounded synchronous utility can return 200 with the same metering envelope. Errors use consistent problem details and request ID, never provider secrets or raw sensitive input. Distinguish invalid input, unauthorised service, insufficient credit, throttling, unavailable provider and indeterminate completion. Publish OpenAPI, curl examples and later TypeScript/Python SDKs from the actual contract.

Envelope: request_id, product, version, environment, status, created_at, completed_at, result, warnings, review_required, evidence_references, usage {unit, quantity}, billing {state, currency, amount}. Fields awaiting settlement must be explicitly pending. A verification no-match is a successful execution result, not an HTTP server error.

## 9. Financial correctness

Implement an append-only balanced ledger with reservations and adjustments. Do not implement money as a mutable balance plus incremented request counter. In a transaction, serialise wallet reservation or use an equivalent proven concurrency mechanism so parallel submissions cannot overspend. Journal transactions and reservations must be traceable to a unique request/payment. Balance checks need atomicity, not cached reads.

Idempotency identity: tenant + environment + product/version + key, with a request payload fingerprint. Same key and same input returns the original job/response; same key and changed input returns conflict. Publish retention/replay windows. Do not expire a key while the request is running or financially unresolved. Deduplicate gateway callbacks and usage settlement independently.

Rules: reject invalid requests before processing without charge; successful no-match/failed-liveness business outcomes may be charged at disclosed rates; platform technical failure is not charged; provider timeout with unknown completion goes to reconciliation rather than blind retry and duplicate payment; cancelled jobs release unused reservations, but already completed billable steps follow the published product policy. A multi-step bundle itemises completed checks. Correct ledger mistakes with reversing entries, never edits/deletes.

Payment credit is posted only from a verified gateway event or confirmed reconciliation, not the user's browser success redirect. Match merchant, amount, currency and order; use signature verification and replay protection. Refunds and chargebacks have explicit reversal/negative-balance policies. Receipt versus tax invoice rules and credit terms require accounting confirmation. Test duplicate callbacks, stale callbacks, partial refunds and concurrent spend before live payments.

Initial pricing hypotheses for validation only: OCR R0.20–R0.80/page; invoice R1–R4/document; payslip R2–R6/document; bank statement R3–R15 with a stated page allowance; ID extraction R0.75–R3; PDF generation R0.10–R0.50; small summary R0.50–R3. Do not seed these as approved live prices. Obtain partner quotes for liveness, identity, company and bank checks. Include retries, storage, support, gateway fees, FX and tax treatment in economics. Avoid provider-level cost disclosure in public responses.

## 10. Durable execution and provider integration

States: accepted/queued/running/succeeded/failed/cancelled plus needs_review and reconciling where appropriate. Separate execution status from billing status. Claim jobs atomically with leases and fencing tokens. Recover expired leases and reject stale-worker commits. Queue delivery may be at-least-once; terminal results and settlements must be idempotent. Add bounded retries/backoff, dead-letter inspection, deadlines and cancellation arbitration. Test crash points before/after vendor call, result save and charge settlement.

Adapter contract: supported products/versions; capability and region metadata; input validation; cost/unit estimation; execute; reconcile by provider request reference; normalised errors; cancellation where supported. Use provider idempotency when available. Never blindly repeat a potentially charged external check after an ambiguous timeout. Apply concurrency limits and circuit breakers per provider and customer. Fallback is permitted only for approved providers/regions with compatible semantics and pricing policy.

Ollama Cloud is optional and blocked for live use until the intended commercial arrangement is confirmed. Published terms observed on 2026-09-28 restrict competing products; token prices, quotas, residency and agreements must be rechecked before contracting. Do not build the business on presumed unlimited subscriptions. Self-hosted Ollama is a separate option requiring model licence review and measured hardware economics. No upstream credential may reach the browser/customer SDK.

Uploaded document text is untrusted input. Model instructions must not allow documents to trigger tools, change billing, reveal another tenant's data or choose arbitrary outbound URLs. Return schema-validated JSON, explicit missing values and evidence locations. Never equate model self-confidence with calibrated accuracy. Use deterministic arithmetic/format/date checks, reviewed benchmarks and human review thresholds.

## 11. OCR and verification requirements

OCR pipeline: upload validation and malware scan → identify supported format/pages → quality check → OCR/layout extraction → document-specific mapping → deterministic validation → evidence/uncertainty annotations → result/export → retention enforcement. Avoid rasterising native-text PDFs unnecessarily. Evaluate SA formats, multi-page statements, blurry photos, rotations, missing pages, handwritten fields and mixed-language text. Record template/model/parser version for reproducibility.

Bank statement output includes institution label, period, account holder/reference with masking policy, balances and transactions with dates, descriptions, debit/credit and balance where present. Reconcile statement arithmetic where possible; distinguish extraction from affordability or fraud decisions. Invoice output includes supplier/customer fields, invoice reference/dates, line items, subtotal, tax and total with arithmetic warnings. Proof-of-address extraction is not authoritative address verification. ID checksum validity is not Home Affairs verification.

Liveness uses a specialist validated provider and supported capture SDK. Short-lived tenant-scoped sessions, binding to applicant/request, anti-replay and authenticated callbacks are mandatory. Return pass/fail/inconclusive/review according to provider semantics; define attempt charging. Face comparison is a separate check, with validated thresholds and quality handling. Never use a generic LLM/vision prompt as biometric assurance. No official identity, company, credit, tax or qualification data access without authorisation and redistribution permission.

## 12. Portal and platform administration

Customer navigation: Overview, API Catalogue, Playground, Requests, API Keys, Usage, Billing, Webhooks, Company, Team and Settings. Build real empty/loading/error states, accessible forms, mobile-friendly layouts and redacted results according to role. No fake revenue/chart data in production. Catalogue distinguishes planned, sandbox, beta, live, suspended and deprecated services; only callable services have an enabled execution action.

Platform administration: tenant/service approvals, provider status, catalogue/schema versions, price effective dates, spend/abuse limits, failed/reconciling jobs, webhook dead letters, payment reconciliation, credit adjustments, retention tasks, audit inspection and support access. Record who changed prices/entitlements/refunds. Prevent support users from granting themselves broad data access. Polling is sufficient initially; reintroduce realtime only after resource-ownership and group-membership tests exist.

## 13. Privacy and operations

Implement customer processing agreements, permitted-purpose records, subprocessors/regions, classification-aware routing, data minimisation, retention, subject-request handling, access audits and incident response. Biometrics require specific review under POPIA; cross-border processing needs an applicable section 72 basis. An SA application server does not make an overseas AI call SA-resident. Do not market the platform as automatically making customers FICA compliant.

Make retention a versioned product/customer policy with legally reviewed bounds. Prefer short-lived raw documents and biometric evidence; retain necessary billing/audit metadata independently. Cover derivatives, temporary files, failed uploads, exports, backups and vendor retention/deletion. No raw ID numbers, tokens, documents or biometric payloads in logs. Restrict temporary download URLs by owner, purpose and expiry. Validate outbound webhook URLs against SSRF, including DNS resolution, redirects and private/link-local destinations.

Use secret configuration/vault mechanisms with documented rotation and recovery. Never copy credentials from local settings into plans, examples or commits. Database storage encryption and production blob encryption remain deployment obligations even though tenant secrets have application encryption. Backups need restore drills; redact diagnostics. Collect request count, latency percentiles, queue delay, failure/review rates, provider spend, settled revenue, reconciliation age and estimated contribution per product. Set service-level targets after measuring provider capacity and pilot performance.

## 14. Delivery phases and acceptance gates

### P0 — Re-establish a safe foundation

Read the review; inspect current diff; fix SDK/toolchain and reproducible frontend dependencies without arbitrary framework upgrades. Repair organisation membership/isolation and child-workspace authorisation; enforce login lockout, throttling, inactive users and session revocation; add unique disposable test databases with explicit safe connection validation. Verify API configuration, secret persistence, deployment and tenant audit/storage. Retained foundation code is not certified secure by this plan.

Gate: clean backend build, unit/architecture tests, database integration tests against a uniquely named test schema, frontend production build and security regressions for organisation/workspace access and revoked membership. Document exact commands and counts. No real customer deployment before these pass.

### P1 — Company portal, catalogue and keys

Implement onboarding, roles, billing profile, catalogue/product states, sandbox entitlements, scoped hashed keys and key lifecycle. Seed only honest planned/sandbox statuses. Build functional sign-in/company/team/catalogue/key pages. Include an actually implemented deterministic SA ID-format utility as the first vertical slice; explain that it does not verify identity.

Gate: two companies can independently sign up and run the sandbox utility; keys cannot cross tenant/environment/product boundaries; revoked keys fail immediately; finance users cannot read document results.

### P2 — Jobs, usage and prepaid billing

Build durable requests/jobs, idempotency, atomic wallet reservation, versioned rates, usage settlement, ledger, payment adapter, verified callbacks, request history, usage and invoice views. Use an explicitly named fake payment/provider adapter only in tests/sandbox; production startup rejects mock configuration. Implement signed customer webhooks and delivery records.

Gate: concurrent spend cannot exceed available credit; duplicate submissions and callbacks never double-charge/credit; restart recovery cannot duplicate settlement; unknown vendor completion reconciles; failed internal processing releases credit; status polling is free.

### P3 — Document products

Choose a legally usable OCR provider/engine and implement general OCR and invoices end-to-end first, then ID, payslip, statement, proof-of-address and classification. Add upload scanning, bounds, evidence, review/export, deletion and per-document/page pricing. Run the same paths through API and portal.

Gate: each product has an approved representative dataset and written field-specific accuracy, review-rate and latency targets. Publish measured results and unsupported formats. Missing/uncertain data is not invented. Cross-tenant upload/download/result tests, page-limit and malicious-file tests pass.

### P4 — Contracted verification

Integrate liveness, face comparison, bank verification and company lookup behind approved adapter contracts. Keep unavailable products visibly unavailable and continue independent work if contracts are pending. Official identity/AML/credit/tax checks are additional gated products, not implied by OCR.

Gate: capture session expiry/replay/callback checks pass; adverse business results are charged as disclosed; technical failures are distinguishable; production vendor credentials and contracts are verified by owner; sandbox output cannot be mistaken for live verification.

### P5 — Paid pilots and launch hardening

Run 3–5 pilots, validate pricing/contribution, complete privacy/contract/accounting review, restore drills, provider outage handling, load testing at expected traffic and alert/runbook validation. Add support procedures, rate limits, status communication and documentation. Launch only live-qualified products; optional partner delays do not block independently qualified document products.

Gate: end-to-end company signup → verified top-up → entitled execution → result → one correct charge → invoice works; cancellation/timeout/failure paths also reconcile. No placeholder page is presented as a finished service. Report launch-blocking gaps honestly.

### P6 — Demand-driven expansion

Add supplier/applicant/AP bundles, utilities, communication and AI APIs from API-CATALOGUE.md according to paid demand, permissions and margin. Enterprise postpaid/SSO/multiple workspaces/private hosting follow signed requirements. Every new service supplies schema/version, limits, billing rules, adapter, evidence benchmark, documentation, retention and monitoring before live enablement.

## 15. Claude execution rules and status reporting

Work in this repository. Preserve user changes and schema history. Complete coherent vertical slices, run proportionate tests, update docs/IMPLEMENTATION-STATUS.md with changed files/commands/results/open blockers/next step. Do not mark roadmap items complete because a class, table or placeholder route exists. Do not erase tests to obtain green results, aside from explicitly obsolete product tests removed during the reset. Required security and ledger tests are substantive acceptance requirements.

When credentials/contracts/sample data are missing, implement typed boundaries, synthetic sandbox fixtures and honest unavailable states; report the dependency and continue independent scope. Do not buy subscriptions, send supplier messages, publish deployments or delete databases without the owner's relevant authorisation. Do not reintroduce the former product from historical Git files. Keep changes reviewable and do not promise a production-ready result without measured evidence.

## 16. Research references and unresolved decisions

Sources consulted for the planning conversation on 2026-09-28; recheck before supplier selection: [CIPC API catalogue](https://developer.cipc.co.za/), [SoftiDoc document APIs](https://softidoc.com/api-docs), [VerifyNow verification APIs](https://www.verifynow.co.za/api-docs), [Stitch bank verification](https://stitch.money/payouts), [Ollama terms](https://www.ollama.com/terms), [Ollama pricing/regions](https://ollama.com/pricing), [POPIA special information](https://inforegulator.org.za/knowledge-base/category/popia/chapter-3-conditions-for-lawful-processing/part-b-processing-of-special-personal-information/), [POPIA cross-border provisions](https://inforegulator.org.za/knowledge-base/category/popia/chapter-9-transborder-information-flows/).

These demonstrate existing categories and constraints, not reseller permission or proven willingness to buy AURA. Decisions still requiring business input: initial pilot sector, provider quotations and approved contracts, hosting/processing regions, retention limits, tax/payment setup, evaluated document samples, final live prices and service-level commitments. Default to building the independent foundation and document workflow while these are resolved.
