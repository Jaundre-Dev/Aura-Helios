# Phase 4 — Intelligence

**Goal.** Knowledge with citations, security findings the agent can reason about, and
incident investigation from real evidence.

**Depends on:** Phase 3. **Effort:** 30–35 focused days.

---

## WP4.1 — Knowledge ingestion

Source adapters for repositories, docs, tickets and incidents; parse, metadata, chunk,
embed, index.

**Chunk on structure** — headings, function boundaries — not fixed windows, which cut
definitions in half and then retrieve the half without the meaning.

## WP4.2 — Vector storage

Start in MySQL: `chunk_embeddings(chunk_id, model_id, dim, vector BLOB)` with in-process
brute-force cosine over a project's chunk set.

> **Decision — hold the vector database.** This is honestly good to roughly 50–100k chunks
> per project on a warm cache. Move to a dedicated vector store when measured p95 retrieval
> exceeds **~200 ms** — and not before that number exists. The system plan lists "requiring
> a vector database before Knowledge needs one" as a v1 non-goal, and it is right: an
> unmeasured migration buys latency you had no problem with and adds a service you now have
> to operate.

## WP4.3 — Hybrid retrieval

MySQL `FULLTEXT` BM25 combined with vector cosine through reciprocal-rank fusion, then a
rerank pass.

## WP4.4 — Citations

Every knowledge answer carries `ContextSource[]`, and the UI renders **retrieved evidence
visually distinct from model interpretation**. The system must never present a generated
claim with the same weight as a quoted source.

## WP4.5 — Security module

> **Decision — do not write a scanner.** Orchestrate Semgrep, Trivy, Gitleaks and
> `dotnet list package --vulnerable` in containers. The AI's job is triage, deduplication,
> exploitability reasoning and a remediation diff — not pattern matching that mature tools
> already do better. Findings record their tool provenance, so a false positive is
> traceable to the rule that produced it.

## WP4.6 — Threat modelling

Built from the Architecture module's component graph, so the model describes the system
that exists rather than the one someone remembers.

## WP4.7 — Incident investigator

Evidence adapters for logs, metrics, traces and deployments. Timeline assembly, hypothesis
generation with evidence scoring, root cause with an **explicit confidence**, postmortem
artifact.
