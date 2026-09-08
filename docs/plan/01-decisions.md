# Confirmed decisions and target environment

Answered 2026-09-05, hardware detected 2026-09-06. Every phase assumes these.

## Decisions

| Question | Answer | What it changes |
| --- | --- | --- |
| Team | **Solo** | Plan stands as written. Strict phase order; only 0 and 1 overlap |
| Authentication | **Local ASP.NET Core Identity** | ~2 days in WP0.4. Claims shaped so OIDC is a later swap, not a rewrite |
| Gate 3 target repo | **A purpose-built `helios-testbed`** | WP3.0. Resettable, so agent versions compare from identical starting state |
| Model access | **Cloud *and* local** | All adapters except vLLM land in Phase 1, which grows to 18–24 days. Enables a real hybrid routing policy from the start |
| Hardware | **RTX 5060 Ti 16 GB, Ryzen 5 8400F, 16 GB RAM** | Local models capped at 14B. vLLM deferred. System RAM, not VRAM, is the binding constraint |
| Container host | **Docker Desktop + WSL2**, hybrid dev stack | Ollama runs natively; only MySQL and Redis are containerised in the dev loop |

## Target environment

A Ryzen 5 8400F with an RTX 5060 Ti and 16 GB of system RAM. The GPU is comfortable.
**System RAM is the constraint that shapes everything** — and it is the one people get
wrong, because the GPU is the number you notice.

### Development runs hybrid

- **Ollama native on Windows** — reaches the GPU directly, with none of the NVIDIA
  Container Toolkit and WSL2 passthrough the container route needs
- **MySQL and Redis in Docker**, with explicit memory limits in
  `deploy/compose/docker-compose.data.yml`
- **API, Worker and web on the host**, so Visual Studio keeps the debugger
- Leaves roughly **8–9 GB free** with a model loaded

### Cap WSL2 before installing Docker

WSL2 takes half of system RAM by default — 8 GB of your 16. The cap has to exist before
Docker creates its VM, or the VM and a loaded model contend for the same memory.

```ini
# %USERPROFILE%\.wslconfig
[wsl2]
memory=6GB
processors=4
swap=2GB
```

Template at `scripts/wslconfig.template`. Then `wsl --shutdown`.

### Local models cap at 14B

| Model | Quant | VRAM | Use |
| --- | --- | --- | --- |
| `qwen2.5-coder:14b` | Q4_K_M | ~9 GB | Primary local coder — the default |
| `qwen2.5-coder:7b` | Q4_K_M | ~4.7 GB | Fast iteration, cheap tool loops |
| `nomic-embed-text` | — | ~0.3 GB | Embeddings, from Phase 4 |
| `qwen2.5-coder:32b` | Q4 | ~19 GB | **Exceeds VRAM.** Spills to RAM you do not have and collapses to a few tokens/sec. Route to cloud instead |

### vLLM is deferred, not scheduled

The RTX 5060 Ti is Blackwell (`sm_120`), and vLLM's support for it needs a CUDA toolchain
that has been unstable to build against. It also pre-allocates a large share of VRAM at
startup, which fights everything else on a 16 GB card.

Ollama covers local inference on this hardware. `VllmProvider` is descoped from Phase 1 —
and nothing is lost, because the generic OpenAI-compatible adapter already speaks vLLM's
API whenever a suitable machine appears.

### Database

Set up 2026-09-07 against the native `MySQL80` service (**8.0.46**), which was already
installed. See [../operations/runbook.md](../operations/runbook.md).

| | |
| --- | --- |
| Database | `helios`, utf8mb4 / utf8mb4_0900_ai_ci |
| Account | `helios`@`localhost` and `helios`@`127.0.0.1` |
| Privileges | `GRANT ALL ON helios.*` and `helios_test.*` — nothing outside those schemas |
| Credentials | user-secrets, never the repository |

The native install is 8.0 and the compose file runs 8.4. Fine for everything HELIOS does,
but dev and CI on different server versions is the kind of gap that surfaces at the worst
moment. Worth aligning before Phase 3.

The full compose stack remains the reference for production and CI. It is not the daily
loop on this machine.
