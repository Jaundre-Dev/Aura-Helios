# AURA HELIOS

South African business API platform: companies register, select services, receive scoped API keys and pay for measured usage in Rand. Products will include document OCR/extraction, specialist identity and liveness services, authorised company/bank verification, utilities and selected AI services.

## Start here

- [Implementation plan](HELIOS-IMPLEMENTATION-PLAN.md): authoritative scope, architecture, billing rules and acceptance gates.
- [API catalogue](API-CATALOGUE.md): 68 proposed services, buyers, delivery approach and billing units.
- [Repository review](HELIOS-REVIEW.md): findings, cleanup boundaries and verification limitations.
- [Claude instructions](CLAUDE.md) and [copyable handoff prompt](CLAUDE-PROMPT.md).
- [Implementation status](docs/IMPLEMENTATION-STATUS.md).

## Current state

This is a retained .NET 10 / React / MySQL foundation, not a working paid API platform. Identity, workspace/project, audit, encrypted secret storage and database object-storage implementations remain. They need the security and operational repairs in phase P0 before use with customers. Catalogue, keys, billing, durable jobs and service execution are not implemented. The frontend is an explicitly labelled placeholder shell.

The former AI engineering/agent product and its delivery plans have been retired. Git history preserves the previous source. Database migration history is deliberately retained; no database cleanup has been run. Other AURA products are outside this repository's scope.

## Toolchain

The repository requests .NET SDK 10.0.303 through global.json. During this review only 10.0.302 was installed, so the SDK resolver blocked the build. Resolve this deliberately in P0. Preserve the existing EF/Pomelo compatible versions until reviewed together.

The frontend uses Node and npm; package.json defines build and typecheck. Establish and commit a reproducible lockfile before CI. Database integration tests currently recreate a fixed helios_test database; replace this fixture with a unique disposable schema before running integration tests. Do not point tests at a customer or development database.

See HELIOS-REVIEW.md for the exact verification status; earlier test counts are not current evidence.
