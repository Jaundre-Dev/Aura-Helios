# Local development runbook

Written for the confirmed target machine. See
[the plan](../plan/01-decisions.md) for why each choice
was made.

## The machine

| | |
| --- | --- |
| CPU | AMD Ryzen 5 8400F, 6 cores |
| GPU | NVIDIA GeForce RTX 5060 Ti, **16 GB VRAM** |
| RAM | **16 GB** |
| OS | Windows 11 Pro for Workstations |

System RAM is the binding constraint here, not the GPU. The whole setup below is shaped
around leaving 8–9 GB free while a model is loaded.

## Prerequisites

| Tool | Status | Notes |
| --- | --- | --- |
| .NET SDK 10 | Installed (10.0.303) | |
| Git | Installed (2.55.0) | |
| Visual Studio 2022 | Assumed | Primary C# environment |
| **Node 22 LTS** | **Not installed** | Required for the web client |
| **Docker Desktop** | **Not installed** | WSL2 backend |
| **Ollama for Windows** | **Not installed** | Native install, not the container |
| MySQL 8.0 | **Already installed** | Native `MySQL80` service, listening on 3306 |

## Database — already set up

Done on 2026-09-07 against the native `MySQL80` service (**8.0.46**).

| | |
| --- | --- |
| Database | `helios`, `utf8mb4` / `utf8mb4_0900_ai_ci` |
| Account | `helios`@`localhost` and `helios`@`127.0.0.1` |
| Privileges | `GRANT ALL ON helios.*` and `helios_test.*` — nothing outside those schemas |
| Migration | `20260907165743_Initial` applied, 13 tables |

The connection string lives in **user-secrets**, not in the repo — matching the convention
the Aura project already uses. `appsettings.json` ships `"MySql": ""` on purpose, and the
application throws a helpful error at startup if nothing supplies it.

```powershell
dotnet user-secrets list --project src/Helios.Api
```

To point at a different server:

```powershell
dotnet user-secrets set "ConnectionStrings:MySql" "Server=127.0.0.1;Port=3306;Database=helios;User=helios;Password=...;AllowPublicKeyRetrieval=true;SslMode=None;GuidFormat=Binary16;" --project src/Helios.Api
```

`GuidFormat=Binary16` is forced in code regardless, so omitting it is not fatal — but keep
it in the string so the value is obvious to a reader.

Verify:

```powershell
curl http://localhost:5080/health/ready
```

Expect `{"status":"ready","mysql":"up"}`.

To undo everything this created:

```sql
DROP DATABASE helios;
DROP DATABASE IF EXISTS helios_test;
DROP USER 'helios'@'localhost', 'helios'@'127.0.0.1';
```

### About the native MySQL

The `MySQL80` Windows service is running natively on port 3306. Two consequences:

- **WP0.2 can start today** with no new installs. Point the connection string at
  `localhost:3306` and generate the first migration against it.
- **The MySQL container cannot bind 3306.** `.env` therefore defaults `MYSQL_HOST_PORT=3307`
  so both can run side by side. To use 3306 for the container instead, `Stop-Service MySQL80`
  first.

Worth deciding early rather than late: the native install is 8.0 and the compose file uses
8.4. That is fine for everything HELIOS does — JSON columns, `FULLTEXT`, and Pomelo all
behave the same — but dev and CI running different server versions is the kind of gap that
surfaces at the worst moment. Either upgrade the local install to 8.4, or use the container
for development and leave the native service stopped. Also check where MySQL 8.0 sits in
Oracle's support lifecycle before committing to it long-term.

### Cap WSL2 memory before installing Docker

WSL2 will take up to half of system RAM by default — 8 GB of your 16. Create
`%USERPROFILE%\.wslconfig` **first**:

```ini
[wsl2]
memory=6GB
processors=4
swap=2GB
```

Then `wsl --shutdown` and start Docker Desktop. Without this, a loaded 14B model and a
default WSL2 VM will contend for the same memory and both will suffer.

## The hybrid stack

```
Windows native          Ollama          -> GPU directly, no WSL2 passthrough
                        Helios.Api      -> F5 from Visual Studio
                        Helios.Worker   -> F5 from Visual Studio
                        helios-web      -> npm run dev

Docker (WSL2)           MySQL, Redis    -> memory-limited, see compose file
```

Ollama runs natively rather than in a container because the container route needs the
NVIDIA Container Toolkit plus GPU passthrough through WSL2, and costs inference
performance for no benefit when the API and Worker are on the host anyway.

### Start the data services

```powershell
Copy-Item deploy/compose/.env.example deploy/compose/.env   # then edit the passwords
docker compose -f deploy/compose/docker-compose.data.yml up -d
```

### Start the applications

```powershell
dotnet run --project src/Helios.Api      # http://localhost:5080/health
dotnet run --project src/Helios.Worker
```

Or set both as startup projects in Visual Studio and press F5.

### Start the web client

```powershell
cd frontend/helios-web
npm install
npm run dev                              # http://localhost:5173
```

Vite proxies `/api` and `/hubs` to port 5080, so no CORS configuration is needed in
development.

### Stop the data services

```powershell
docker compose -f deploy/compose/docker-compose.data.yml down
```

Add `-v` to also drop the MySQL and Redis volumes.

## Models

16 GB of VRAM sets the ceiling. These fit with room for context:

| Model | Quant | VRAM | Use |
| --- | --- | --- | --- |
| `qwen2.5-coder:14b` | Q4_K_M | ~9 GB | Primary local coder — the default |
| `qwen2.5-coder:7b` | Q4_K_M | ~4.7 GB | Fast iteration, cheap tool loops |
| `nomic-embed-text` | — | ~0.3 GB | Embeddings from Phase 4 |

```powershell
ollama pull qwen2.5-coder:14b
ollama pull qwen2.5-coder:7b
ollama pull nomic-embed-text
```

**Do not plan on 32B locally.** `qwen2.5-coder:32b` at Q4 is roughly 19 GB — it exceeds
16 GB of VRAM, spills into system RAM you do not have spare, and drops to a few tokens
per second. Route work that genuinely needs a larger model to a cloud provider instead.

## vLLM is deferred

The compose file keeps a `gpu` profile for vLLM, but it is not part of the development
loop and should not be for now:

- The RTX 5060 Ti is Blackwell (`sm_120`), and vLLM support for it needs a recent CUDA
  toolchain that has been unstable to build against
- vLLM pre-allocates a large share of VRAM at startup, which fights everything else
- Ollama already covers local inference for this hardware

Revisit if a second GPU or a Linux box with more VRAM appears.

## The full compose stack

`deploy/compose/docker-compose.yml` runs everything in containers. It is the reference
for what production looks like and what CI builds, but on 16 GB it is tight with a model
loaded and you lose step-debugging. Use it to verify the Dockerfiles still build, not as
the daily loop:

```powershell
docker compose -f deploy/compose/docker-compose.yml up --build -d
```

## Verifying

```powershell
./scripts/build.ps1        # restore, build, test — the same sequence CI runs
```

```powershell
curl http://localhost:5080/health
```
