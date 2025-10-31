# Deployment

## Hosting Options

- Containerized deployment of `StrataConfig.ApiService` and `StrataConfig.Web` behind a reverse proxy.
- .NET Aspire can assist in local composition and optionally inform cloud topologies.

## Path Base / Reverse Proxy

- The UI uses Static Web Assets mapping (`Assets[...]`) to generate correct URLs under arbitrary path bases.
- Avoid absolute-root asset URLs (e.g., `/lib/...`).

## Environment Configuration

- `appsettings*.json` for environment-specific values.
- Service discovery and resilience policies via `StrataConfig.ServiceDefaults`.

## Health Checks

- AppHost config includes HTTP health probes (`/health`).
- Propagate equivalent probes to your orchestrator (Kubernetes, etc.).

