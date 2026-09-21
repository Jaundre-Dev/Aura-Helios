# Aura-Helios implementation plan

The authoritative plan is [Aura-AI product split plan](../../Aura-AI/docs/PRODUCT-SPLIT-PLAN.md), sections 7–9. Do not implement Helios until Aura-AI phases A0–A5 pass their acceptance gate.

Delivery sequence:
1. H0: multi-tenant identity, authorization, projects, scoped storage, provider/run contracts, worker queue, secrets, quotas and audit.
2. H1: automatic repository code review with verified webhook inputs and evidence-based findings.
3. H2: automatic testing in bounded isolated runners with reproducible result artifacts.
4. H3: OCR extraction, confidence/provenance, review and export.
5. H4: validated liveness, replay defenses, inconclusive/review outcomes and restricted evidence retention.
6. H5: customer-prioritized specialist AI modules using shared capability contracts.
7. H6: evaluations, monitoring, recovery, backup/restore and deployment gates.

Retain existing layered backend and React frontend initially. Keep every API, database query, job, storage path, cache entry, credential and realtime group tenant scoped. Aura-AI owns company conversation UX; Helios owns specialist execution. No shared business tables. Aura-Enterprise is read-only.

Status: all phases pending. The planning task did not run application builds, tests or runtime verification. Maintain IMPLEMENTATION-STATUS.md here once implementation starts.
