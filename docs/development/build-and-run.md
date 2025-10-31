# Build & Run (AppHost)

## Local Orchestration

The `.NET Aspire` AppHost (`StrataConfig.AppHost`) composes the API service and Blazor web frontend with health checks and dependency wiring.

### Run

- `dotnet run --project StrataConfig.AppHost`
- AppHost will show assigned ports; open the `webfrontend` external HTTP endpoint.

### Projects wired

- `apiservice` → `StrataConfig.ApiService`
- `webfrontend` → `StrataConfig.Web` (depends on `apiservice`)

## Direct Project Run

You can run the web or API projects directly, but some features (service discovery, health) are best exercised via the AppHost.

