# ADR-0003: Pin the EF Core stack to 9.0.x on a .NET 10 target

- **Status:** Accepted
- **Date:** 2026-09-06
- **Supersedes:** nothing

## Context

The solution targets `net10.0`, and the ASP.NET Core packages are at 10.0.11. The natural
assumption is that every Microsoft package moves to 10.x together.

It does not, because the MySQL provider is not a Microsoft package. `Pomelo.EntityFrameworkCore.MySql`
9.0.0 — the newest stable release — declares:

```
Microsoft.EntityFrameworkCore.Relational  [9.0.0, 9.0.999]
```

That is a hard upper bound, not a minimum. There is no Pomelo build for EF Core 10 or 11.
Raising any EF package to 10.x either fails restore outright or, in the worse case where a
transitive path allows it, restores cleanly and then fails at runtime with a provider
mismatch that surfaces as an obscure `MethodNotFoundException` deep inside query
translation.

## Decision

Pin the entire EF-touching package set to **9.0.x**:

| Package | Version |
| --- | --- |
| `Pomelo.EntityFrameworkCore.MySql` | 9.0.0 |
| `Microsoft.EntityFrameworkCore.Design` | 9.0.19 |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 9.0.19 |

Everything that does not touch EF stays on 10.0.11 — the ASP.NET Core host, SignalR,
JWT bearer authentication, hosting and DI abstractions.

Enforce it with **Central Package Management**. `Directory.Packages.props` holds every
version, csproj files carry no `Version` attribute, and the EF group sits under a comment
explaining why it must not be bumped. `CentralPackageTransitivePinningEnabled` stops a
transitive dependency from quietly dragging in EF 10.

## Consequences

- The mixed graph builds with **zero warnings** — verified, not assumed. EF Core 9 targets
  `net8.0` and loads correctly on `net10.0`; this is a supported configuration, not a hack
  waiting to be tidied up.
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` is dragged to 9.0.19 by association,
  since it depends on EF Core. That is the one non-obvious consequence: an Identity package
  sitting a major version behind the ASP.NET Core host it runs in. It works, because
  Identity's EF integration depends on EF, not on the web host.
- A future `dotnet add package Microsoft.EntityFrameworkCore` will pull 10.x and break the
  provider. Central Package Management makes that a single visible edit in one file rather
  than a silent change buried in a csproj.
- Reverting is cheap: when Pomelo ships an EF Core 10 provider, change three lines in
  `Directory.Packages.props` and rebuild.

## Alternatives considered

**Switch to PostgreSQL.** Npgsql tracks EF Core releases closely and has an EF 10 build.
Rejected: MySQL is specified throughout the system plan and the data architecture, and
swapping the database to dodge a provider release lag trades a two-line pin for a
migration of every entity configuration.

**Drop EF for MySqlConnector plus Dapper.** More control and no provider lag, but it means
hand-writing all mapping and losing migrations — which the plan leans on heavily for the
phased table rollout. Rejected as disproportionate.

**Target `net9.0` instead.** Would align every package at 9.x, at the cost of giving up the
.NET 10 runtime already installed. Rejected: the runtime is the more valuable half.
