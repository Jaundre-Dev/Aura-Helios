# Phase 1 — AI core

**Goal.** Any module can ask for a model capability and get an answer, streamed, from a
local or cloud backend, with the routing decision recorded and data policy enforced.

**Depends on:** WP0.1, WP0.2, WP0.7. **Effort:** 18–24 focused days — cloud *and* local is
confirmed, so every adapter except vLLM lands here.

---

## WP1.1 — GenericOpenAICompatibleProvider — build this first

`POST /v1/chat/completions` with SSE parsing, `/v1/models`, `/v1/embeddings`.

**Why first:** one adapter covers vLLM, LM Studio, llama.cpp server, OpenRouter, Together,
Groq and Ollama's compatibility endpoint. It is the highest-leverage file in the phase and
what makes provider churn cheap.

**The fiddly part:** tool-call deltas arrive fragmented by index. Accumulate
`tool_calls[i].function.arguments` across chunks before parsing. Get this wrong and tool
use fails intermittently — and only under streaming.

## WP1.2 — OllamaProvider

Native `/api/chat` (NDJSON streaming), `/api/tags`, `/api/show`, `/api/embed`.

Use the native API over Ollama's OpenAI shim: `/api/tags` and `/api/show` return real
registry metadata — context length, family, quantization, capabilities — that the OpenAI
shape does not expose. The registry is only as good as its metadata.

## WP1.3 — Cloud providers

OpenAI, Anthropic, Gemini, Azure OpenAI, Ollama Cloud. Each behind a feature flag; disabled
providers never register in DI.

Anthropic warrants a dedicated adapter rather than bending the generic one: the system
prompt is a top-level field, content is a block array, tool use and results are block types,
and the SSE event taxonomy is its own. Forcing that through an OpenAI-shaped adapter
produces a worse version of both.

`VllmProvider` is **descoped** — see [01-decisions.md](01-decisions.md). The generic adapter
already speaks vLLM's API whenever a suitable machine appears.

## WP1.4 — Model registry

- `ModelRegistry : IModelRegistry` — MySQL rows with a 60-second Redis cache
- `ProviderDiscoveryWorker` upserts models from `GetModelsAsync`, marks vanished ones
  unavailable
- `ProviderHealthWorker` — every 30 s into the Redis health cache, emitting monitoring events
- Manual overrides for quality score, cost and capability flags a provider under-reports

## WP1.5 — Router

```
1. Resolve policy   request pin ▸ agent version ▸ workspace default for TaskType ▸ global

2. Candidates       registry models where available AND healthy

3. Hard filters     — each ejection recorded with its reason
                    classification   Restricted ⇒ IsLocal;  LocalOnly ⇒ IsLocal
                    capabilities     tools / vision / structured output as demanded
                    context window   ≥ estimated prompt + MaxOutputTokens
                    ceilings         cost, and latency vs rolling p95

4. Score            w_q·quality − w_c·cost − w_l·latency + localityBonus

5. Order            head + fallback chain → RoutingDecision
```

`RoutingDecision` records the winner, the reason, **and the rejected candidates with why**.
That last field is what makes "why did it use the slow model" answerable three weeks later,
and it costs nothing to populate at decision time.

Fallback walks the chain on **transient** failure only — 429, 5xx, timeout, open circuit. A
401 or a schema rejection fails loudly. Silently falling back on an auth error hides a
broken configuration behind a working system.

> **Decision — token estimation.** Characters ÷ 4 with a per-family correction factor.
> Routing needs an estimate, not a count. Add real tokenizers only if budget enforcement
> proves too loose — a tokenizer per family is a dependency and a maintenance surface for
> precision nothing yet needs.

## WP1.5a — Default routing policy

Cloud and local both being available makes this decidable now rather than in Phase 3. Seed
these with a migration.

| TaskType | Classification | Route |
| --- | --- | --- |
| Planning, Reasoning | Public, Internal | Cloud primary — where a frontier model earns its cost |
| CodeGeneration, CodeReview | Public, Internal | Cloud primary, `qwen2.5-coder:14b` fallback |
| CodeGeneration, CodeReview | Confidential | `qwen2.5-coder:14b`, local only |
| Summarize, Classify, Extract | Public, Internal | `qwen2.5-coder:7b` — cheap, fast, entirely adequate |
| Embedding | Public → Confidential | `nomic-embed-text`, local |
| **Anything** | **Restricted** | **Local only. No fallback, no exceptions** |

The Restricted row must be covered by a **test**, not a convention. It is also Gate 1
check 3.

## WP1.6 — Gateway

`ModelGateway : IModelGateway` is the only public door. Pipeline: classification guard,
budget check, route, resilience-wrapped provider call, usage accounting, telemetry events,
persist a `ModelCallLog` row.

Resilience through `Microsoft.Extensions.Http.Resilience`: per-provider timeout, retry with
jitter on transient failures, circuit breaker keyed by provider.

## WP1.7 — Streaming to the UI

The gateway tees the stream into the SignalR relay and into the accumulated transcript.

**Batch chunks on a ~50 ms timer.** One hub message per token means a 700-token answer is
700 websocket frames, and the browser will feel every one. This is not an optimisation for
later — retrofitting it changes the client contract.

## WP1.8 — Model console

Providers with health; models table with provider, model, local/cloud, context window,
capabilities, p95 latency, cost, availability. A playground that sends through the gateway
with an explicit pin and shows the returned `RoutingDecision` — so a model can be tested
without touching any agent's configuration.

---

## Gate 1

1. `ollama pull qwen2.5-coder:14b` and the model appears in the console within one
   discovery cycle
2. The playground streams tokens into the browser
3. A `Restricted` project's request rejects every cloud candidate and lands on Ollama, with
   the rejection reasons visible in the routing decision
4. Stopping Ollama mid-conversation makes the next call fall back to a cloud model where
   policy allows, recording `FallbackReason`
5. `ModelCallLog` rows carry tokens, latency and estimated cost
6. Provider adapters have contract tests against a stub HTTP server — no network in CI

## Risks

Streaming tool-call accumulation differs per provider and is the likeliest source of
intermittent bugs. Cost metadata goes stale constantly — treat it as advisory and keep it
somewhere cheap to correct.
