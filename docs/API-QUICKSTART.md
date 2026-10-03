# AURA HELIOS API quickstart

The machine-readable contract is served at `GET /openapi/v1.json`. It is generated from the running API, so it matches what the server accepts. These examples use `curl` against a local API at `http://localhost:5000`. Sandbox results are for integration testing with synthetic documents, never for decisions.

```bash
API=http://localhost:5000
```

## 1. Account and company

```bash
curl -s -X POST $API/api/v1/auth/register -H "Content-Type: application/json" \
  -d '{"email":"dev@example.co.za","password":"Choose-A-Long-Passw0rd","displayName":"Dev"}'
```

Open the verification link that arrives by email (it calls `POST /api/v1/auth/verify-email`). A company can only be created from a verified address.

```bash
TOKEN=$(curl -s -X POST $API/api/v1/auth/login -H "Content-Type: application/json" \
  -d '{"email":"dev@example.co.za","password":"Choose-A-Long-Passw0rd"}' | jq -r .accessToken)

ORG=$(curl -s -X POST $API/api/v1/organizations -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"name":"Example Trading"}' | jq -r .id)

WS=$(curl -s $API/api/v1/workspaces -H "Authorization: Bearer $TOKEN" | jq -r '.[0].id')
TOKEN=$(curl -s -X POST $API/api/v1/auth/select-workspace -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"workspaceId\":\"$WS\"}" | jq -r .accessToken)
```

If two-step sign-in is on, login returns `{"mfaRequired":true,"mfaToken":"…"}`. Exchange it with `POST /api/v1/auth/mfa/login {"mfaToken":"…","code":"123456"}`.

## 2. Enable a product and create a sandbox key

```bash
curl -s $API/api/v1/catalogue | jq '.[] | {slug, releaseState, callableInSandbox}'

curl -s -X POST $API/api/v1/organizations/$ORG/entitlements -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"productSlug":"identity.sa-id-validate","environment":"Sandbox"}'

KEY=$(curl -s -X POST $API/api/v1/api-keys -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"name":"integration","environment":"Sandbox","scopes":["identity.sa-id-validate","documents.invoice"]}' | jq -r .secret)
```

The secret is shown once. Store it in your secret manager; HELIOS keeps only a hash.

## 3. Run a synchronous product

```bash
curl -s -X POST $API/api/v1/products/identity.sa-id-validate/requests -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" -d '{"idNumber":"8001015009087"}'
```

The response is the standard envelope: `requestId`, `status`, `result`, `warnings`, `reviewRequired`, `usage` and `billing`. A failed check (for example a bad checksum) is a successful request with `valid: false`, not an error.

## 4. Process a document

```bash
UPLOAD=$(curl -s -X POST $API/api/v1/uploads -H "X-Api-Key: $KEY" -F "file=@invoice.pdf;type=application/pdf" | jq -r .id)

REQ=$(curl -s -X POST $API/api/v1/products/documents.invoice/requests -H "X-Api-Key: $KEY" \
  -H "Content-Type: application/json" -H "Idempotency-Key: inv-0001" -d "{\"uploadId\":\"$UPLOAD\"}" | jq -r .requestId)

curl -s $API/api/v1/requests/$REQ -H "X-Api-Key: $KEY"            # status (free to poll)
curl -s $API/api/v1/requests/$REQ/result -H "X-Api-Key: $KEY"     # fields, evidence, checks
curl -s "$API/api/v1/requests/$REQ/export?format=csv" -H "X-Api-Key: $KEY" -o invoice.csv
```

- Document products answer `202 Accepted`. Poll the status, or register a webhook (step 6).
- Version 1 products read digital PDFs only; scanned pages come back unreadable and are not charged.
- Repeating a request with the same `Idempotency-Key` and the same input returns the original request. Reusing a key for different input is refused with `409`.

## 5. Review a result

```bash
curl -s -X POST $API/api/v1/requests/$REQ/review -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d '{"decision":"Correct","corrections":{"fields.dueDate":"2026-10-15"},"reason":"From the supplier email"}'
```

The original result is kept. Each decision is recorded with who made it, and exports apply the corrections in force.

## 6. Webhooks (portal session)

```bash
curl -s -X POST $API/api/v1/webhooks -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"url":"https://hooks.example.co.za/helios","events":["request.succeeded","request.needs_review","request.reviewed"]}'
```

Each delivery is signed with `Helios-Signature: t=<unix>,v1=<hex HMAC-SHA256 of "<t>.<body>">`, using the `whsec_…` secret returned once at registration. Payloads carry request metadata, never results or inputs. Deduplicate on `Helios-Event-Id`.

## 7. Live use

Live use needs all of the following:
- The company Owner accepts the published terms and processing agreement (`GET/POST /api/v1/organizations/{id}/agreements`).
- A live entitlement, with a stated purpose for products that process personal information.
- A published live price for the product.
- Funded credit.
- A live key (`hk_live_…`).

Every live request needs an `Idempotency-Key`. Optional caps are a per-key `monthlyBudget` and a company `billing/spend-limit`; exceeding either returns `402`. Product calls are rate limited per key, with `429` and `Retry-After` when exceeded.

## Errors

Errors are RFC 9457 problem details. Each includes `requestId` (quote it to support) and a stable `code`, such as `insufficient_credit`, `idempotency_key_reused`, `product_unavailable`, `key_scope`, `agreements_required`, `key_budget_reached` or `too_many_pages`.
