# Aura-Helios implementation status

Living status for Helios delivery. The authoritative plan is
[Aura-AI product split plan](../../Aura-AI/docs/PRODUCT-SPLIT-PLAN.md) §§7–9 and the focused
[Helios plan](IMPLEMENTATION-PLAN.md). Aura-AI phases A0–A5 passed their acceptance gate on
2026-09-20 (see Aura-AI/docs/IMPLEMENTATION-STATUS.md §A5), which unblocked Helios.

## Baseline (verified 2026-09-20)

The scaffold + earlier work-packages build and test green on .NET 10:

- **Build:** `dotnet build Helios.sln` — 0 warnings / 0 errors.
- **Tests:** 55 pass, 0 failed, 0 skipped — Unit 22, Architecture 4, Integration 29.
  Integration tests run against a real MySQL (`helios_test`), applying the real EF migrations.
- Architecture tests enforce the dependency direction (Api → Application → Domain; Infrastructure
  implements Application abstractions).

## H0 — Multi-tenant foundation

Target (plan §8): *identity, roles, provisioning, projects, scoped storage, secrets, queue/worker
lifecycle, capability registry, audit, metering and authorization tests.*

### Done

- **Identity, roles, auth (WP0.4):** local ASP.NET Core Identity, JWT sign-in/registration, the
  `sub`/`workspace_id`/`role` claim shape, and the authorization gate. Covered by `AuthEndpointTests`
  and the token-issuer tests.
- **Organizations, workspaces, projects, membership + provisioning (WP0.5):** minimal-API endpoints,
  services and validators; creating a workspace makes the creator its owner; projects are workspace
  scoped; RBAC forbids granting a role above your own.
- **Tenant isolation + audit:** EF Core global query filters keyed on `IWorkspaceContext` make
  isolation the default (a forgotten `WHERE` still cannot cross tenants); a stranger gets 404, not
  403; every write stages an audit row in the same transaction (`AuditableEntityInterceptor`,
  `AuditWriter`). Proven by `WorkspaceEndpointTests`.
- **Secrets (WP0.6, 2026-09-20 — this slice):** a tenant-scoped `ISecretStore` implementation.
  Values are sealed with **AES-256-GCM** by a configured, rotation-ready **`SecretKeyring`** before
  they touch the database — the row carries only the `nonce‖ciphertext‖tag` envelope and the id of
  the wrapping key, never the plaintext, never a plaintext column, never a log or audit line.
  Secrets are reached only through the current workspace, so one tenant can neither read nor
  overwrite another's under the same reference, and a system caller with no workspace is refused
  outright. The master key is required from configuration (never generated at boot, never committed
  to appsettings), failing before any secret is written rather than with a corrupted value. Nine
  integration tests cover round-trip, overwrite-in-place, cross-tenant isolation, same-reference
  independence, encryption-at-rest, audit-without-value, the system-caller refusal, and GCM
  tamper-detection.

### Remaining for H0

- **Scoped storage** — a tenant-scoped object/blob store for artifacts (interfaces are stubbed).
- **Queue / worker lifecycle** — a real `IJobQueue` with bounded retries, leases, cancellation and
  dead-letter handling; the worker currently has only the agent-run skeleton.
- **Capability registry + runs API** — the `POST /api/v1/capabilities/{capability}/runs` contract
  (run id, status, artifacts, cancel) and its registry.
- **Metering** — per-tenant usage records.

## H1–H6

Pending. H1 (code review), H2 (testing), H3 (OCR), H4 (liveness), H5 (modules), H6 (operations) —
see the plan. Aura-AI's preserved OCR/liveness/automation tables are exported/mapped into Helios in
the phase that owns each.

## Notes

- **Aura-Enterprise remains untouched** and read-only.
- The MySQL driver's `GuidFormat=Binary16` reads any 16-byte binary value back as a GUID, so
  sealed secrets are stored as one variable-length envelope rather than separate fixed-width
  nonce/tag columns — a foundation detail worth remembering for any future binary column.
