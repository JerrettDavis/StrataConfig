# Getting Started

## Prerequisites

- .NET SDK matching the repo’s target framework: `net10.0`.
- PowerShell (for Playwright install script on Windows).
- Optional: Redis (when experimenting with distributed caching locally).

## Build & Run

- Restore and build:
  - `dotnet build`
- Run the distributed app via Aspire AppHost (recommended):
  - `dotnet run --project StrataConfig.AppHost`
- Access the web UI:
  - Navigate to the `webfrontend` external HTTP endpoint shown in the console.

## Projects

- `StrataConfig.Core` — domain, abstractions, merge engine, rules, templates.
- `StrataConfig.ApiService` — Minimal APIs for templates, scopes, documents, resolve.
- `StrataConfig.Web` — Blazor Server admin UI (interactive server rendering).
- `StrataConfig.AppHost` — .NET Aspire AppHost that wires up API + Web.
- `StrataConfig.Tests` — unit/component/API/E2E tests.

## Tests

- Unit + API tests:
  - `dotnet test StrataConfig.Tests`
- Browser-based E2E tests (auto-host):
  - First install browsers: `pwsh ./StrataConfig.Tests/bin/Debug/net10.0/playwright.ps1 install`
  - Then run: `dotnet test StrataConfig.Tests`
- To target an already-running instance, set `E2E_BASE_URL`.

