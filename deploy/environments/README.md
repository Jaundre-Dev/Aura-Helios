# Environments

One folder per environment once there is more than a laptop.

| File | Purpose |
| --- | --- |
| `development.env` | Local Docker Compose. Never contains a real credential. |
| `staging.env` | Pre-production. Cloud providers allowed, production tools are not. |
| `production.env` | Real workloads. Values come from the secret store, not from this folder. |

Rules:

- Committed files hold shape and defaults only. Real secrets resolve through `ISecretStore`.
- `RESTRICTED` data stays on local inference in every environment unless a workspace
  policy explicitly permits otherwise.
- Production tool permissions (`WRITE_PRODUCTION`, `DELETE_RESOURCE`, `DEPLOY`) require
  an approval policy — they are never granted by an environment file alone.
