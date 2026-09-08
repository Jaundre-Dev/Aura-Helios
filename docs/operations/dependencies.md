# Dependencies

All versions live in `Directory.Packages.props` (Central Package Management). Project files
carry `<PackageReference Include="..." />` with **no** `Version` attribute — adding one
there is an error, which is the point.

## The one rule that matters

**The EF Core stack is pinned to 9.0.x and must stay there.**

`Pomelo.EntityFrameworkCore.MySql` 9.0.0 declares `Microsoft.EntityFrameworkCore.Relational
[9.0.0, 9.0.999]` — a hard ceiling. There is no Pomelo build for EF Core 10 or 11. Anything
that drags EF to 10.x breaks the provider, sometimes only at runtime.

EF Core 9 targets `net8.0` and runs correctly on `net10.0`. The split is deliberate. See
[ADR-0003](../adr/0003-ef-core-9-pin.md).

## Installed and verified

Builds with **zero warnings**; 14 tests pass.

### EF Core — pinned 9.0.x

| Package | Version | Project |
| --- | --- | --- |
| `Pomelo.EntityFrameworkCore.MySql` | 9.0.0 | Infrastructure |
| `Microsoft.EntityFrameworkCore.Design` | 9.0.19 | Api |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 9.0.19 | Infrastructure |

### ASP.NET Core 10 host

| Package | Version | Project |
| --- | --- | --- |
| `Microsoft.AspNetCore.OpenApi` | 10.0.11 | Api |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.11 | Api |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | 10.0.11 | Api |
| `Microsoft.Extensions.Hosting` | 10.0.11 | Worker |
| `Microsoft.Extensions.Hosting.Abstractions` | 10.0.11 | Infrastructure |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.11 | Application |
| `Microsoft.Extensions.Http.Resilience` | 10.9.0 | Infrastructure |

### Infrastructure and application

| Package | Version | Project |
| --- | --- | --- |
| `StackExchange.Redis` | 3.1.31 | Infrastructure |
| `Serilog.AspNetCore` | 10.0.0 | Api |
| `Serilog.Extensions.Hosting` | 10.0.0 | Worker |
| `Serilog.Sinks.Console` | 6.1.1 | Worker |
| `FluentValidation` | 12.1.1 | Application |

### Testing

| Package | Version | Project |
| --- | --- | --- |
| `xunit` | 2.9.3 | all test projects |
| `xunit.runner.visualstudio` | 3.1.4 | all test projects |
| `Microsoft.NET.Test.Sdk` | 17.14.1 | all test projects |
| `coverlet.collector` | 6.0.4 | all test projects |
| `NSubstitute` | 6.2.0 | UnitTests, AITests |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 | IntegrationTests, EndToEndTests |
| `Testcontainers.MySql` | 4.14.0 | IntegrationTests |
| `Testcontainers.Redis` | 4.14.0 | IntegrationTests |

> **No FluentAssertions.** Version 8 moved to a paid commercial licence (Xceed); v7 is the
> last free release and is no longer maintained. xunit's own assertions cover what this
> codebase needs. If fluent syntax later earns its place, use Shouldly (Apache 2.0).

## Still to add, by phase

### Phase 2 — agent runtime

| Package | Project | For |
| --- | --- | --- |
| `Docker.DotNet` | Infrastructure | Sandboxed terminal and test execution |
| `LibGit2Sharp` | Infrastructure | Git tools |
| `Octokit` | Infrastructure | Pull requests |

### Phase 4 — knowledge

Nothing yet. Embeddings go through `IModelProvider`, and vectors start as MySQL BLOBs with
in-process cosine — no vector database until measured p95 retrieval exceeds 200 ms.

### Phase 5 — observability

`OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`,
`OpenTelemetry.Instrumentation.Http`, plus an exporter.

### Never

No vendor SDK in `Helios.Domain` or `Helios.Application` — enforced by
`DependencyDirectionTests.Application_and_domain_reference_no_provider_sdk`.

## Frontend

Declared in `frontend/helios-web/package.json`: Fluent UI, SignalR client, Monaco, TanStack
Query, React Router, Zustand. **Node is not installed on this machine** — install Node 22
LTS, then `npm install` in that folder. Nothing has been resolved or lock-filed yet, so
those versions are unverified.
