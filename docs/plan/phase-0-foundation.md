# Phase 0 — Foundation

**Goal.** A signed-in user creates a workspace and a project through the API, sees it
appear in the web app without a refresh, and every write leaves an audit row and an event.

**Depends on:** nothing. **Effort:** 10–14 focused days. **Status:** in progress.

---

## WP0.1 — Configuration and options · *partial*

Bind `HeliosOptions`, `ProviderOptions`, `WorkerOptions` with
`.ValidateDataAnnotations().ValidateOnStart()`.

The API must **refuse to start** on a bad connection string rather than failing on the
first request. Startup is the cheapest place to find a misconfiguration and the only place
where it is unambiguous.

Done so far: `AddHeliosPersistence` throws at startup with the exact command to fix a
missing connection string. Still to do: the typed options classes themselves.

Files: `Infrastructure/Configuration/`, `Api/Configuration/`.

## WP0.2 — Persistence (MySQL) · **done**

- `HeliosDbContext` with one configuration file per aggregate under
  `Persistence/MySql/Configurations/`
- `AuditableEntityInterceptor` fills audit columns from `IWorkspaceContext`, and refuses to
  let an update rewrite `CreatedAt`/`CreatedBy`
- Global query filters on workspace, project, membership and audit
- Migration `20260907165743_Initial` — 13 tables, applied and verified

> **Decision — GUID storage.** `binary(16)` via Pomelo's `MySqlGuidFormat.Binary16`, forced
> in `MySqlConfiguration` regardless of what the connection string says. The domain issues
> time-ordered UUIDv7, and Binary16 preserves that ordering in big-endian byte order, so
> clustered-index inserts stay sequential. `char(36)` costs 20 extra bytes per row and
> destroys index locality on the tables that grow fastest. `LittleEndianBinary16` stores
> compactly but scrambles the ordering — the worst of both.

> **Decision — snake_case naming.** Applied by convention after all entity configurations.
> MySQL identifier case sensitivity differs between Windows and Linux; this removes the
> class of bug where a migration works locally and fails in a container.

> **Decision — no generic repository.** Handlers use the context directly through
> `IHeliosDbContext`. A repository is added only where a real invariant needs guarding.

> **Decision — Identity types are infrastructure.** `HeliosUser : IdentityUser<Guid>` lives
> in Infrastructure; `Helios.Domain` references users only by `Guid`. Swapping to OIDC later
> touches no domain type.

## WP0.3 — Redis infrastructure · *blocked on Docker*

> **Decision — Streams, not lists.** `XADD` / `XREADGROUP` / `XACK` gives consumer groups,
> at-least-once delivery, and a pending-entries list that survives a crash. A
> `LPUSH`/`BRPOP` list drops in-flight work when the worker holding it dies — which, for a
> thirty-minute agent run, is the difference between a resumed task and a silently lost one.

- `RedisJobQueue : IJobQueue` over Streams
- `RedisLock` — `SET NX PX` with a random owner token, lease renewal while a run executes,
  release via a Lua compare-and-delete so a lapsed owner cannot release someone else's lock
- `RedisRateLimiter`, `RedisHealthCache`

## WP0.4 — Identity, workspace isolation, RBAC · **done**

> **Decision — local ASP.NET Core Identity for v1**, with a claims shape an OIDC provider
> can later fill unchanged. HELIOS is local-first: a developer must be able to run it with
> no network.

Isolation, done earlier: `IWorkspaceContext`, `HttpWorkspaceContext` reading claims,
`SystemWorkspaceContext` for the worker and migrations, global query filters, `WorkspaceRole`
with Owner through Viewer, and role checks in every service.

Sign-in, this work package:

```
POST /api/v1/auth/register          POST /api/v1/auth/login
POST /api/v1/auth/select-workspace  GET  /api/v1/auth/me
```

- Tokens are issued by `JwtTokenIssuer` and validated by JWT bearer. `JwtOptions` binds
  from `Helios:Jwt` and `ValidateOnStart()`s; the signing key lives in user-secrets, never
  in `appsettings.json`, exactly like the connection string (this establishes the WP0.1
  options pattern the rest will follow).
- Claims: `sub`, `workspace_id`, `role`, read with `MapInboundClaims = false` so they
  arrive verbatim. A user signs in first, then `select-workspace` re-issues a token scoped
  to a workspace they belong to — **404, not 403**, for one they do not, matching the
  workspace GET so neither leaks other tenants.
- **SignalR gotcha (handled):** a browser cannot set an `Authorization` header on a
  websocket, so the hubs accept the token from the `access_token` query string in
  `JwtBearerEvents.OnMessageReceived`, but only for paths under `/hubs`.
- Registration is open in v1 (local-first). Gating it behind an invite or an org admin is a
  later hardening, not a Phase 0 concern.

> **Decision — endpoint gate is authentication; RBAC stays in the services.** The REST
> groups only `RequireAuthorization()`. The per-workspace role checks live in the services,
> where they can return 404 for a non-member instead of the 403 an endpoint policy would —
> a role policy at the door would leak the existence of other tenants' workspaces.

## WP0.5 — Workspace and project endpoints · **done**

Minimal API groups under `Api/Endpoints/`, FluentValidation behind an endpoint filter that
emits RFC 9457 problem details, and `HeliosExceptionHandler` mapping expected failures to
status codes.

```
GET    /api/v1/organizations            POST   /api/v1/organizations
GET    /api/v1/organizations/{id}

GET    /api/v1/workspaces               POST   /api/v1/workspaces
GET    /api/v1/workspaces/{id}          PATCH  /api/v1/workspaces/{id}
GET    /api/v1/workspaces/{id}/members  POST   /api/v1/workspaces/{id}/members

GET    /api/v1/projects                 POST   /api/v1/projects
GET    /api/v1/projects/{id}            PATCH  /api/v1/projects/{id}
DELETE /api/v1/projects/{id}
```

Behaviour worth knowing:

- Creating a workspace makes the creator **Owner** in the same transaction. A workspace
  nobody can reach is not a useful failure mode.
- A non-member gets **404, not 403**, for someone else's workspace. 403 confirms the
  workspace exists and leaks the presence of other tenants.
- An Admin **cannot grant a role above their own**. Privilege escalation by invitation is
  still privilege escalation.
- **Lowering** a project's `Classification` requires Admin and writes its own
  `project.reclassify` audit row, because it widens where that project's code may be sent.
- `DELETE` on a project **deactivates** rather than deletes. Runs, findings and audit rows
  reference projects, and a hard delete orphans the history that makes them explainable.

The convention for long work is established here even though nothing long-running exists
yet: **`202 Accepted` with a run id**, and progress over SignalR. Nothing streams from the
request that started it.

## WP0.6 — Events to Redis to SignalR · *blocked on Docker*

- `RedisEventPublisher` publishes to the `helios:events` stream
- `SignalREventRelay : BackgroundService` lives **in the API** and forwards to hub groups.
  The worker publishes; the API relays. Workers never hold hub connections.
- Mapping: run events to `run:{id}` and `workspace:{id}`; approvals to `user:{id}`

> **Decision — use the SignalR Redis backplane** rather than hand-rolling per-instance
> stream reads. A consumer group delivers each event to exactly one API instance, so
> clients connected to the others see nothing — a bug that only appears once you scale past
> one instance, which is the worst possible time to find it.

## WP0.7 — Observability baseline · *partial*

Done: `/health` liveness (touches nothing else, so a database outage never causes an
orchestrator to kill a healthy container) and `/health/ready` which actually probes MySQL.

Still to do: Serilog with a correlation id per request and `AgentRunId` in the log scope,
so a whole run greps out by one value.

## WP0.8 — Web: auth, workspace, projects · *blocked on Node*

Login, workspace switcher, project list and create, activity feed driven by the workspace
hub. Build the Ctrl+K palette as a **registry** now, so later phases register commands
instead of rebuilding it.

---

## Gate 0

| # | Check | State |
| --- | --- | --- |
| 1 | `docker compose up` brings up API, worker, web, MySQL, Redis and Ollama | blocked — Docker |
| 2 | A user signs in, creates a workspace, creates a project | **done** — verified end to end via the API |
| 3 | The new project appears in a second browser tab with no refresh | blocked — WP0.6 |
| 4 | `AuditLogs` has a row for every write, with actor and workspace | **done** — covered by test |
| 5 | `/health/ready` reports unhealthy when MySQL is stopped | **done** |
| 6 | Integration tests run against MySQL and Redis in CI | partial — MySQL yes, Redis blocked |
| 7 | Architecture tests still pass | **done** |

## Risks

Pomelo JSON mapping needs a value comparer or change tracking silently misses list
mutations. SignalR authentication on the websocket handshake — the other classic time sink —
is handled in WP0.4: the token comes off the `access_token` query string for `/hubs` paths.

**Learned the hard way:** the first integration-test fixture overrode the connection string
through configuration, was silently outranked by the API's own user-secrets, and dropped
the real `helios` database. The fixture now re-registers the `DbContext` outright and
asserts the resolved database name before anything destructive runs. Any fixture that calls
`EnsureDeleted` needs that guard.
