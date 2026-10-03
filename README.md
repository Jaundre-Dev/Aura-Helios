# AURA HELIOS

South African business API platform: companies register, select services, receive scoped API keys and pay for measured usage in Rand. Products will include document OCR/extraction, specialist identity and liveness services, authorised company/bank verification, utilities and selected AI services.

## Start here

- [Implementation plan](HELIOS-IMPLEMENTATION-PLAN.md): authoritative scope, architecture, billing rules and acceptance gates.
- [API catalogue](API-CATALOGUE.md): 68 proposed services, buyers, delivery approach and billing units.
- [Repository review](HELIOS-REVIEW.md): findings, cleanup boundaries and verification limitations.
- [Claude instructions](CLAUDE.md) and [copyable handoff prompt](CLAUDE-PROMPT.md).
- [Implementation status](docs/IMPLEMENTATION-STATUS.md).

## Current state

Not yet a paid API platform. The backend now has company membership and roles, hardened sign-in (lockout, throttling, session revocation), an honest 12-service catalogue, sandbox entitlements, hashed scoped API keys, durable request records with idempotency, and one callable sandbox product: `identity.sa-id-validate` (format and checksum only, not identity verification). Billing, jobs, payments, webhooks, document products and partner verification are not implemented. The frontend is still a labelled placeholder shell. See [implementation status](docs/IMPLEMENTATION-STATUS.md) for exactly what was verified and how.

The former AI engineering/agent product and its delivery plans have been retired. Git history preserves the previous source. Database migration history is deliberately retained; schema changes are forward migrations. Other AURA products are outside this repository's scope.

## Toolchain

- .NET SDK: `global.json` requests 10.0.303 with `latestFeature` roll-forward; 10.0.401 builds it. Preserve the EF Core 9 / Pomelo pins until reviewed together.
- Frontend: Node.js LTS and npm. A `package-lock.json` has not been generated yet; `npm ci` (used by the web Dockerfile) needs one.
- Schema: `deploy/sql/helios-migrations.idempotent.sql` applies every migration safely to any existing HELIOS database; regenerate it with `dotnet ef migrations script --idempotent` after adding a migration.

## Tests

```bash
dotnet test Helios.sln
```

Integration tests need a local MySQL. They read only host, port and credentials from `HELIOS_TEST_CONNECTION` (or the Helios.Api user-secrets `ConnectionStrings:MySql`), create their own `helios_it_<timestamp>_<random>` database, and drop only that database afterwards. Non-local hosts are refused unless `HELIOS_TEST_ALLOW_REMOTE_HOST=1`. Never point them at a customer database.
