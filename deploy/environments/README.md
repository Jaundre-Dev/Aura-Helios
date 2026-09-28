# Deployment environments

The API platform uses separate development, staging and production configuration. Keep credentials out of committed files; use approved secret configuration and document rotation/recovery.

Sandbox and live are separate customer execution/billing environments, not interchangeable with deployment stages. Production must reject mock payment/verification adapters. Route customer data only to approved providers and regions according to product and tenant policy.

See ../../HELIOS-IMPLEMENTATION-PLAN.md and ../../HELIOS-REVIEW.md. Current deployment scaffolding has not passed the new P0 acceptance gate.
