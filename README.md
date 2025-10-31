# StrataConfig

[![CI](https://github.com/JerrettDavis/StrataConfig/actions/workflows/ci.yml/badge.svg)](https://github.com/JerrettDavis/StrataConfig/actions/workflows/ci.yml)
[![Docs](https://github.com/JerrettDavis/StrataConfig/actions/workflows/docs.yml/badge.svg)](https://jerrettdavis.github.io/StrataConfig/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)

Enterprise-grade, layered configuration platform for .NET — with a Blazor admin UI, a clean REST API, a deterministic merge engine, and clear environment separation (Development, Staging, Production, …). Built for teams that need traceable, typed, and rule-driven configuration at scale.

- Layered scoping: Global → Division → Org → Site → Device/User → App → Environment
- Deterministic merge + rule engine (include/exclude/override)
- Typed documents via templates (JSON Schema) with validation
- Powerful admin UI: explore layers, edit (JSON + form), clone, import/export, and diff
- Clean REST API and client integration points
- .NET Aspire AppHost for local orchestration
- CI + DocFX documentation (GitHub Pages)

## Table of Contents

- Overview
- Architecture
- Concepts
  - Scopes & Precedence
  - Environments
  - Templates
- Admin UI
  - Environments & Namespaces
  - Scope Tree
  - Documents Editor (JSON + Form)
  - Clone, Import/Export, Diff
- API
- Developer Guide
  - Quick Start
  - Using with IConfiguration
  - Testing
  - Docs (DocFX)
  - CI/CD
- Roadmap
- Contributing
- Security
- Badges & Links

## Overview

StrataConfig centralizes configuration definition and delivery across services, tenants, geos, devices, and users — all while staying strongly typed and auditable. Configuration is stored as logical documents organized by namespace, validated against JSON Schema templates, and combined deterministically through layers (scope + rules) into a flattened, consumable key/value view.

## Architecture

Solution layout:

- `StrataConfig.Core` — domain model, store abstractions, merge and rule engines
- `StrataConfig.ApiService` — Minimal APIs for templates, scopes, documents, resolve
- `StrataConfig.Web` — Blazor Server admin UI (interactive server rendering)
- `StrataConfig.AppHost` — .NET Aspire AppHost wiring API + Web for local runs
- `StrataConfig.Tests` — unit, component (bUnit), API, and Playwright E2E tests
- `docs/` — DocFX site content and configuration

Providers (store) are abstracted. The in-memory store is provided for development; EF Core and Git providers are on the roadmap.

## Concepts

### Scopes & Precedence

Scopes form a hierarchy. Default precedence (low → high):

Global < Org < Division < Site < Device < User < App < Environment < AdHocOverride

Each layer contributes zero or more documents per namespace. Later layers override earlier ones by key. The merge engine produces a stable, flattened key/value dictionary for a given context and revision watermark.

### Environments

Environments (Development, Staging, Production, …) are first-class contexts that vary configuration globally without changing the scope taxonomy you browse. The admin UI treats the current environment as an explicit context you can switch or create; the environment feeds into the resolve pipeline and environment layers participate in merges.

### Templates

Documents are typed using JSON Schema templates:

- Enforced at upsert (server-side validation)
- Drive editor experiences (form mode) and validation hints
- Versionable and evolvable with schema versions

## Admin UI

### Environments & Namespaces

- Switch environments from the Home toolbar
- Create new environments (modeled as environment scope nodes) without GUID prompts
- Switch and create namespaces from the Documents sidebar

### Scope Tree

- Browse Global/Division/Org/Site/Device/User
- Environments are a separate context (not in the browsing tree)

### Documents Editor (JSON + Form)

- JSON mode: full JSON editor with server-side template validation on save
- Form mode: property grid for dot-path keys; “Seed from template” pre-populates typed fields using Template.JsonSchema (strings, numbers, booleans, enums, and one-level nested objects)
- Template suggestions via datalist, filtered by namespace

### Clone, Import/Export, Diff

- Clone: pick destination via a ScopeTree — no GUIDs — and optionally create the destination scope inline
- Import/export: JSON arrays for bulk operations
- Diff: added/removed/changed keys between two documents (by id or inline content)

## API

Selected endpoints:

- Templates
  - `GET /api/templates` — list
  - `GET /api/templates/{id}` — single template
- Namespaces
  - `GET /api/namespaces` — list
  - `POST /api/namespaces` — create namespace placeholder
- Scopes
  - `GET /api/scopes` — tree
  - `GET /api/scopes/{id}` — details
  - `POST /api/scopes` — create scope node (use kind: "environment" to add environments)
- Documents
  - `GET /api/documents?ns={namespace}&scopeId={optional}` — list
  - `GET /api/documents/{id}` — get
  - `POST /api/documents` — upsert (validates against template)
  - `DELETE /api/documents/{id}` — delete
  - `POST /api/documents/clone` — clone to destination scope
  - `POST /api/documents/diff` — diff two docs (by id or inline content)
  - `GET /api/namespaces/{ns}/export?scopeId={optional}` — export
  - `POST /api/namespaces/{ns}/import` — import (upserts)
- Resolve
  - `POST /api/config/resolve` — flattened key/value configuration for a scope context

## Developer Guide

### Quick Start

Prerequisites:

- .NET SDK 10.0
- PowerShell for Playwright browser install scripts

Run locally (Aspire AppHost):

- `dotnet run --project StrataConfig.AppHost`
- Open the printed webfrontend endpoint

Build & run tests:

- `dotnet build`
- Install Playwright browsers (once):
  - `pwsh ./StrataConfig.Tests/bin/Debug/net10.0/playwright.ps1 install`
- `dotnet test StrataConfig.Tests`

### Using with IConfiguration

Pull a flattened configuration for a namespace and add it into your app configuration at startup. This example calls the `/api/config/resolve` endpoint and layers values into `IConfiguration`.

```csharp
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Host.ConfigureAppConfiguration((ctx, config) =>
{
    var env = ctx.HostingEnvironment.EnvironmentName ?? "Development";
    var app = ctx.HostingEnvironment.ApplicationName ?? "MyApp";
    var ns = Environment.GetEnvironmentVariable("STRATA_NAMESPACE") ?? "ui";
    var baseUrl = Environment.GetEnvironmentVariable("STRATA_URL") ?? "https://localhost:7298"; // StrataConfig.ApiService base

    using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

    var payload = new
    {
        Scope = new
        {
            Environment = env,
            AppName = app,
            AppVersion = ctx.HostingEnvironment.ApplicationVersion,
            Dimensions = new Dictionary<string, string>
            {
                ["division"] = "Retail",
                ["org"] = "Contoso",
                ["site"] = "Seattle HQ",
                ["device"] = "POS SEA-01"
            },
            Tags = Array.Empty<string>()
        },
        Namespace = ns
    };

    var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    var resp = http.PostAsync("/api/config/resolve", req).GetAwaiter().GetResult();
    resp.EnsureSuccessStatusCode();
    var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();

    // Layer StrataConfig on top of existing providers
    config.AddInMemoryCollection(values);
});

var app = builder.Build();
// ...
app.Run();
```

Notes:

- Use Aspire service discovery when StrataConfig runs alongside your services (replace `baseUrl`).
- Map dimensions based on your app identity (tenant, region, device, user).
- For strongly-typed options, bind to your `Options<T>` after the provider is added.

### Testing

- xUnit for unit tests
- bUnit for component tests (Blazor)
- Playwright for E2E (auto-hosts Aspire AppHost by default)
  - Optionally target a running instance by setting `E2E_BASE_URL`

### Docs (DocFX)

- `dotnet tool update -g docfx`
- `docfx docs/docfx.json`
- Open `docs/_site/index.html`

### CI/CD

- `.github/workflows/ci.yml` — build + tests on push/PR
- `.github/workflows/docs.yml` — builds DocFX and publishes to GitHub Pages
  - In repo Settings → Pages, select GitHub Actions for deployment

## Roadmap

- Schema-driven editor (Seed from template) with typed widgets and validation hints
- Context bar (Namespace / Environment / scope breadcrumb + search)
- EF Core provider (SQLite/SQL Server) implementing store contracts and migrations
- Git provider that mirrors scope/namespace/docs and supports diff/PR workflows
- E2E coverage for new flows (form create/save, clone picker, diff picker, import/export)
- RBAC & auth integration (e.g., Entra ID) for admin operations

## Contributing

- Keep changes focused and incremental
- Follow existing structure and naming
- Add/adjust tests with functional changes
- Avoid drive-by refactors and unrelated formatting

## Security

- Antiforgery enabled in the Blazor Server app
- Recommend fronting the admin UI with your SSO / reverse proxy and applying RBAC
- Store providers should avoid embedding secrets in documents; use references where appropriate

## Badges & Links

- CI: `./.github/workflows/ci.yml`
- Docs: `./.github/workflows/docs.yml` → https://jerrettdavis.github.io/StrataConfig/
- OpenAPI: run locally and browse `StrataConfig.ApiService` Swagger UI in Development

