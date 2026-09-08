# Phase 7 — Learning

**Goal.** The platform improves on evidence rather than on impression — and never silently.

**Depends on:** Phase 3; sharpens everything after it. **Effort:** 20–25 focused days.

---

## WP7.1 — Evaluation framework

Case sets per capability, with three evaluator kinds:

- **Deterministic** — it compiles, tests pass, lint is clean
- **Rubric** — an LLM judge on a *pinned* model and prompt version. An unpinned judge makes
  every historical score incomparable
- **Cost and latency**

## WP7.2 — Benchmarking

The same case set across models. This is also how quality scores in the model registry stop
being guesses.

## WP7.3 — Failure classification

A fixed taxonomy: context miss, tool misuse, planning error, hallucination, budget
exhaustion, provider failure. A taxonomy that grows per incident is not a taxonomy.

## WP7.4 — Promotion gates

```
Agent Version A
 ↓ evaluation set
 ↓ results
 ↓ failure classification
 ↓ candidate version B
 ↓ same evaluation set
 ↓ compare
 ↓ human / policy approval
 ↓ promote
```

A candidate must beat the incumbent on the same case set with no safety regression, plus
human sign-off. **Never promote on the evidence of a single failure.**
