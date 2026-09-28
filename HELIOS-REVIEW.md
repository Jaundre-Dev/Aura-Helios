# HELIOS repository review and reset

Date: 2026-09-28. Reviewed repository: `AURA-HELIOS/Aura-Helios`; starting tracked working tree was clean at commit `e3851ec` (Create PRODUCTION-COMPLETION-PLAN.md). Review is source inspection and cleanup verification, not a penetration test or production certification.

## Outcome

The repository contained a useful tenant/application foundation underneath an obsolete AI engineering product scaffold. The new company API platform is not implemented. Preserve the foundation, repair its access controls, and follow HELIOS-IMPLEMENTATION-PLAN.md. Do not restart all infrastructure from scratch or treat the previous green-test claims as evidence for the current product.

## Retained assets

- Layered .NET API/Application/Domain/Infrastructure/Contracts/Worker solution.
- React/TypeScript/Vite/Fluent UI shell, HTTP client, theme and build configuration.
- ASP.NET Identity/JWT registration/sign-in and workspace selection.
- Organisation/workspace/project services and contracts; projects can represent customer applications.
- MySQL EF configurations, migrations, audit interceptor and naming rules.
- Tenant secret encryption with rotation-key references and existing secret-store tests.
- IObjectStore with current bounded MySQL implementation and isolation tests.
- Existing identity, workspace, storage, architecture and naming tests.
- Docker/development scaffolding, subject to the deployment findings below.

Retention does not certify these components for production. No database was inspected or altered; existing data volume, deployment status and real credentials were not established.

## Findings Claude must address first

| Priority | Finding and source | Impact / required action |
| --- | --- | --- |
| P0 | Organisation list/detail have no membership filter: `src/Helios.Application/Features/Identity/OrganizationService.cs:20` and `:26`; routes require authentication only | Any authenticated caller can query unrelated organisations. Add organisation membership/ownership and tests for list/detail/create-child access. |
| P0 | Workspace creation checks only whether an organisation exists: `src/Helios.Application/Features/Workspaces/WorkspaceService.cs:77` | A signed-in caller can create an owner-controlled workspace under an unrelated organisation. Verify the caller's organisation permission inside the creation transaction. |
| P1 | Login directly calls CheckPasswordAsync without enforcing IsLockedOutAsync: `src/Helios.Api/Endpoints/AuthEndpoints.cs:72` | Failed-attempt bookkeeping does not stop a locked account with a correct password from signing in. Use an appropriate lockout-aware flow and regression tests, plus endpoint throttling. |
| P1 | JWT setup validates signature/expiry but has no central current-user/session-state check; select-workspace does not reject inactive users | Verify disabled-account and revoked-membership behaviour for existing tokens across every relevant endpoint. Add revocation/security-stamp or equivalent policy; do not assume token claims remain authoritative. |
| P1 | Test fixture uses fixed `helios_test` and EnsureDeletedAsync: `tests/Helios.IntegrationTests/Fixtures/HeliosApiFactory.cs:88`, `:189`, `:200` | Running tests can delete an existing fixed-name database and parallel runs collide. Replace with unique disposable schemas, explicit host/database safeguards and cleanup restricted to that run. |
| P1 | No catalogue entitlements, API-key authentication, financial ledger, durable job implementation, payment reconciliation or working provider services | The core new business is still to be built. Prioritise shared financial/execution correctness before exposing paid endpoints. |
| P1 | API and worker Dockerfiles copy Directory.Build.props/global.json but omit Directory.Packages.props before restore | Central dependency versions are unavailable in the image restore stage. Fix both COPY lists and test image builds in P0. |
| P1 | Web Dockerfile executes npm ci, but repository has no package-lock.json | Reproducible Docker frontend install cannot work as written. Establish a compatible lockfile and validate the build; do not replace npm ci with an unpinned install as the final fix. |
| P2 | MySqlObjectStore stores full object content in rows, with a 16 MB implementation bound | Keep the abstraction and tests; select production object storage, scanning, retention and resource limits before large document workloads. |
| P2 | Customer UI is only placeholder routes; worker has no processor | No catalogue, key/billing screen or processing workflow is complete. Build vertical slices and keep release states honest. |

Additional P0 deployment review: verify MySQL 8.4 startup options, JWT and encryption-key configuration for API/worker, runtime dependencies, API/web connectivity, external service profiles and secret persistence. These were inspected as scaffolding, not run or certified. No assumptions about successful Docker startup.

## Removed during this reset

128 obsolete or redundant scaffold/document files are listed in `docs/REMOVED-LEGACY-FILES.txt`:

- Superseded implementation and production-completion plans with Aura-AI completion dependencies and code-review-first delivery.
- Agent definitions/runs/steps/versions, tool execution/permissions, engineering-oriented model routing and unused approval/workflow/knowledge abstractions.
- Unimplemented AI provider/model records and contracts, to be replaced by product/provider contracts matching this plan.
- Engineering/architecture/QA/legacy/agent/knowledge placeholder feature files and stale feature README stubs.
- All old SignalR hub classes/contracts/client hooks; they accepted user-supplied workspace/project/run/user group IDs without checking resource ownership. The routes, registration, query-string JWT handling and nginx hub proxy were removed as well. Realtime can return only with tenant-authorised subscriptions and tests.
- The no-op agent worker. Worker entrypoint now clearly logs that no processor is configured.
- Agent tool-permission tests that apply exclusively to the removed product. The architecture test now selects the Application assembly through the retained persistence abstraction.

Navigation now lists API Catalogue, Playground, Requests, API Keys, Usage, Billing, Webhooks, Company, Team and Settings. These are labelled under development, not presented as working functionality. Old SignalR and Monaco frontend dependencies and the backend SignalR package reference were removed.

Git history is the recovery source for deleted tracked files; no history rewrite, reset, commit or push was performed. Existing migration files, generated baseline SQL and AuditLog.AgentRunId/configuration are deliberately preserved for schema compatibility. The identifier is a historical column, not a surviving active agent feature. Its naming-conversion test also remains valid. Schema changes belong in forward migrations.

No sibling product, customer data, runtime database, cloud account or deployed service was changed. Empty local directories may be left by file deletion; Git does not track them and they contain no active legacy implementation.

## Verification and limitations

- `git diff --check` passed after cleanup (Git emitted line-ending conversion warnings, not whitespace errors).
- Static search confirms no remaining references to the removed agent/model/tool interfaces or SignalR registrations in active application source; historical audit mappings are the documented exception for old terminology.
- Confirmed all 128 manifest-listed files are absent; catalogue contains exactly 68 products; all ProjectReference targets across 11 project files resolve; root/status Markdown links resolve; removed-interface reference scan is clear. Final static verification is not equivalent to compilation.
- `dotnet --version` in the repository fails SDK resolution: global.json requests 10.0.303 with latestFeature, while only 10.0.302 is installed. No SDK pin was silently downgraded. Backend build/tests remain unverified until P0 resolves tooling.
- Node v24.19.0 is available in the bundled runtime, but npm is not on PATH and repository node_modules/lockfile are absent. Frontend production build was not run; dependency installation is part of P0.
- Database integration tests were intentionally not run because the current fixture deletes a fixed schema. No Docker deployment or live provider verification was attempted.
- Historic 63-test and 26-test counts in retired documents are not carried forward as current evidence.

## Handoff order

1. Read CLAUDE.md and the root plan/catalogue.
2. Resolve tooling, test isolation and company/auth findings in P0, with meaningful regressions.
3. Deliver one actual sandbox utility through company onboarding, catalogue and scoped keys.
4. Implement durable jobs and billing correctness before paid providers.
5. Add evaluated document processing, then contracted verification and pilot gates.
