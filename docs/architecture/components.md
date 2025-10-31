# Components & Projects

## Projects

- `StrataConfig.Core`
  - Domain models, abstractions (`IConfigStore`, `IMergeEngine`, `IRuleEngine`, `ITemplateValidator`).
  - In-memory store for tests and examples.
- `StrataConfig.ApiService`
  - Minimal APIs: templates, scopes, documents, resolve.
  - Validation, merge, and example datasets.
- `StrataConfig.Web`
  - Blazor Server UI (interactive server render mode).
  - Components for tree navigation, filters, layered documents, and resolved view.
- `StrataConfig.AppHost`
  - .NET Aspire AppHost wiring `apiservice` and `webfrontend` with health checks.
- `StrataConfig.Tests`
  - Unit, API, component, and Playwright E2E tests.

## Cross-Cutting

- Service defaults (resilience, discovery, telemetry) via `StrataConfig.ServiceDefaults`.
- OpenTelemetry instrumentation for API and HTTP calls.

