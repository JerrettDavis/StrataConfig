# Security

## Authentication & Authorization

- Integrate enterprise identity (e.g., Entra ID/AAD, Okta) at the reverse proxy or app layer.
- Apply role-based access control (RBAC) for admin operations.

## CSRF Protection

- Blazor Server interactive endpoints use antiforgery tokens.
- Root page emits `<AntiforgeryToken />`; server enforces via `UseAntiforgery()`.

## Secrets & Config

- Store API keys and connection strings in secure stores (Key Vault, Secrets Manager).
- Use environment variables or secret providers, not source control.

## Data Integrity

- Templates enforce schema; API validates on upsert.
- Consider Git mirror for auditability and change review.

