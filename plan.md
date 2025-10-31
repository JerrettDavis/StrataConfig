# StrataConfig

## Plans: Styling Robustness + E2E Coverage

- Fix static asset resolution across environments
  - Use `Assets["..."]` for CSS/JS to avoid path-base breakage.
  - Keep `UseStaticFiles` + `MapStaticAssets` for wwwroot + SWA.
- Guard UI against stale CSS isolation
  - Add minimal global fallbacks in `wwwroot/app.css` for layout/colors.
- Add browser-based E2E tests
  - Validate CSS isolation (grid applied), key interactions (scenario select, scope select, search, tags).
  - Make tests no-op without browsers or base URL; wire Aspire self-host later.
- Documentation
  - Author docs on styling, isolation tokens, asset mapping, and E2E usage.

### Done

- Interactivity fixed via `InteractiveServer` + `<AntiforgeryToken />`.
- Static assets switched to `Assets[...]` mapping.
- Global fallback styles added in `wwwroot/app.css` to ensure sane layout.
- Initial Playwright E2E tests implemented.
 - E2E tests auto-host the Aspire AppHost and discover `webfrontend` URL.

### Next

- Add Aspire.Testing fixture to launch `webfrontend` for E2E without `E2E_BASE_URL`.
- Expand E2E: quick filters, namespace switch, scope selection, resolved filter.
- Optional: CI workflow to run API/unit, and conditional E2E (with browsers).

## Next Steps (Evening Wrap-Up)

- Schema‑driven forms in Documents editor
  - Seed from `Template.JsonSchema` (top‑level + one‑level nested objects)
  - Typed inputs (string/number/boolean/enum) with basic validation hints
- Context bar (Home + Documents)
  - Quick switching for Namespace / Environment / scope breadcrumb + search
- Persistence providers
  - EF Core provider (SQLite/SQL Server) implementing store contracts + migrations
  - Git provider to mirror scope/namespace/docs and enable diff/PR workflows
- E2E coverage for new UX
  - Form create/save, clone picker, diff picker, import/export flows
- Security & RBAC
  - Hook up authn/z (e.g., Entra ID) for admin operations

### Immediate TODOs (tracked)

- API tests: add TinyBDD coverage for `POST /api/scopes` (create environments) and `POST /api/namespaces` (happy + invalid cases)
- Documents page: environment picker is visible; selection not yet passed to resolve calls on this page (only used on Home). Wire through when adding preview-resolved.
- Form editor: broaden “Seed from template” coverage (booleans/enums/nested objects) + validation hints; ensure checkbox handler respects event state (fixed).
- Clone & Diff modals: add additional filtering and success-state assertions in E2E.
- Providers: implement EF Core and Git stores; wire migrations and repository layout for Git mirroring.
- SDK: optional IConfiguration provider package for easy app integration (wrap `/api/config/resolve`).
- SDK: background refresh (e.g., timer + ETag revision) to hot-reload config at runtime.
- CI: NETSDK1057 warning due to preview SDK — consider pinning via `global.json`.

*A composable, rule‑scoped configuration platform for .NET apps with rich admin UI, pluggable stores (Git/DB), and hot‑reload via Redis.*

---

## Vision

StrataConfig gives you a central place to define configuration once and deliver it everywhere, with **hierarchical, rule‑driven scoping** (Org → Division → Site → Device/User, etc.), **typed templates**, **audit/versioning**, and a **pluggable storage model**. Apps consume config via a standard `IConfigurationProvider` that merges the right layers for their current scope. Admins manage everything from a **Blazor** UI.

* **First‑class .NET**: C#, EF Core, Blazor, minimal APIs, and **.NET Aspire** for local orchestration.
* **Backends**:

    * **Database** (EF Core; SQLServer/Postgres; migrations + seeding) for canonical truth.
    * **Git** (LibGit2Sharp or `git` CLI) for GitOps workflows & PRs.
    * **Redis** (StackExchange.Redis) for fast read‑through caching & change broadcast.
* **Portability**: import/export bundles (YAML/NDJSON) at any tree node for environment promotion.
* **Observability**: OpenTelemetry traces/metrics; structured logs.

---

## High‑level architecture

```
StrataConfig.sln
├─ src/
│  ├─ StrataConfig.Core/                # abstractions, domain, merge engine, rules, templates
│  ├─ StrataConfig.Providers.EF/        # EF Core store provider (Db canonical source)
│  ├─ StrataConfig.Providers.Git/       # Git store provider (GitOps, PR-based)
│  ├─ StrataConfig.Providers.Redis/     # Redis cache + pub/sub invalidation
│  ├─ StrataConfig.ASP/                 # Admin/API host (Minimal APIs) + Workers (background)
│  ├─ StrataConfig.Blazor/              # Blazor (Server) admin UI
│  ├─ StrataConfig.Client/              # NuGet: IConfigurationProvider/IOptions interop
│  └─ StrataConfig.AppHost/             # .NET Aspire orchestration (dev)
└─ tests/
   ├─ StrataConfig.Core.Tests/
   ├─ StrataConfig.Merge.Tests/
   └─ StrataConfig.E2E.Tests/
```

### Core flow

1. **Scope resolution**: app supplies scope attributes (e.g., `OrgId`, `SiteId`, `DeviceId`, `UserId`, `Environment`, `Tags`).
2. **Layer query**: the store provider(s) return all matching key/value docs per scope layer.
3. **Rule engine** applies include/override directives; **Template validator** ensures typed/required fields.
4. **Merge**: deterministic precedence (e.g., Org < Division < Site < Device < User < App overrides).
5. **Cache**: merged result cached in Redis keyed by scope hash; store revision watermark included.
6. **Watch**: changes publish an invalidation message; clients refresh.

---

## Domain model (EF canonical)

```csharp
// Scope graph / taxonomy
class ScopeNode {
    Guid Id;                      // e.g., Org, Division, Site, Device, User nodes
    string Kind;                  // "org"|"division"|"site"|"device"|"user"|custom
    string Name;
    Guid? ParentId;               // null for root
    Dictionary<string,string> Labels; // arbitrary attributes for rule matching
}

// A logical configuration document attached to a scope
class ConfigDocument {
    Guid Id;
    Guid ScopeId;                 // where attached in the tree
    string Namespace;             // e.g., "payments", "ui.theme"
    string TemplateRef;           // schema id & version
    string ContentJson;           // normalized JSON payload
    int Version;                  // monotonic doc version
    DateTimeOffset UpdatedUtc;
    string UpdatedBy;             // audit principal
}

// Rules that mutate evaluation at/under a node
class Rule {
    Guid Id;  
    Guid ScopeId;                 // applicable subtree root
    string Expr;                  // CEL/expr-style (see below)
    RuleEffect Effect;            // Include/Exclude/Override
    string? OverrideJson;         // when Effect=Override
    int Priority;                 // for deterministic ordering
}

// Templates (JSON Schema + UI hints)
class Template {
    string Id;                    // e.g., "ui.theme"
    int SchemaVersion;
    string JsonSchema;
    string? UIMetadata;           // hints for editors (order, groups, display names)
}

// Git mirror index (when using Git provider)
class GitMirror {
    Guid Id;
    string RepoUrl;
    string Branch;
    string Path;                  // root path under repo
    string LastCommit;
}
```

**Notes**

* Use **owned types** in EF Core for `Labels` (JSON) and value objects.
* `Namespace` provides document bucketing and avoids key collisions.
* `Version` is per‑document (DB provider handles optimistic concurrency with `rowversion`).

---

## Scope & precedence

Default precedence (lowest → highest):

`Global < Org < Division < Site < Device < User < App < Environment < AdHocOverride`

Each layer contributes zero or more **documents** for a **namespace**. Precedence determines overwrite on identical keys. Final merged dictionary is **stable/immutable** for a given `(Scope, Revisions)` watermark.

Configurable precedence is represented as an ordered list of `LayerSpec` objects persisted in the DB and part of export bundles.

---

## Rule engine (expr‑based)

Rules use a lightweight expression language (CEL‑like) operating over a context:

```json
{
  "scope": {"kind":"site","labels":{"region":"us-central"}},
  "env": "Production",
  "app": {"name":"PosClient","version":"2.8.1"},
  "tags": ["kiosk","touch"],
  "doc": { /* current candidate doc */ }
}
```

**Examples**

* Include only kiosk sites: `scope.labels["profile"] == "kiosk"`
* Exclude for legacy devices: `tags.has("legacy")`
* Override theme for region: `scope.labels["region"] == "eu"`

Rules evaluate in priority order; first **Exclude** that matches drops the candidate; ordered **Override** rules deep‑merge (`OverrideJson`) into the candidate object.

Implementation options:

* Use [CEL‑C#](https://github.com/google/cel-spec) compatible engine *(or)* .NET `System.Linq.Dynamic` style parser.
* Keep the AST simple and whitelisted; supply intrinsic functions: `has()`, `contains()`, `semverCompare()`, `match()`.

---

## Templates & validation

* Templates are **JSON Schema Draft 2020‑12**.
* Each `ConfigDocument` references a template id+version.
* On write, validate `ContentJson` against the schema.
* UI renders form fields using schema + `UIMetadata` to control grouping/hints.

---

## Import/export bundles

* **Format**: YAML tarball (".strata.tgz"). Top‑level `manifest.yml` + `documents/*.json` + `rules/*.json` + `templates/*.json`.
* **Levels**: export at any `ScopeNode` (subtree), a `Namespace`, or entire org.
* **Semantics**: idempotent apply; server performs 3‑way merge when versions diverge (emit conflicts for manual resolution).

`manifest.yml` example:

```yaml
apiVersion: strata/v1
exportedAt: 2025-10-29T17:00:00Z
source: prod-west
layers:
  precedence: [global, org, division, site, device, user]
scopes:
  - id: 8f0f... kind: org name: Contoso labels: { billingTier: pro }
documents:
  - path: documents/ui.theme@site-49b1.json
  - path: documents/payments@org-8f0f.json
rules:
  - path: rules/region-eu.yml
```

---

## Abstractions (Core)

```csharp
public record ScopeContext(
    string Environment,
    string AppName,
    string? AppVersion,
    IReadOnlyDictionary<string,string> Dimensions, // orgId, siteId, userId, deviceId...
    IReadOnlyCollection<string> Tags);

public interface IConfigStore {
    Task<StoreSnapshot> ReadAsync(ScopeContext scope, string @namespace, CancellationToken ct);
    Task WriteAsync(ConfigDocument doc, CancellationToken ct);
    Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken ct);
    Task<IReadOnlyList<Rule>> GetRulesAsync(Guid scopeId, CancellationToken ct);
}

public interface ICacheLayer { // Redis (or in-memory for tests)
    Task<(bool hit, byte[]? payload)> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, byte[] payload, TimeSpan ttl, CancellationToken ct);
    Task PublishInvalidationAsync(string channel, string scopeKey, CancellationToken ct);
}

public interface IMergeEngine {
    JsonNode Merge(IReadOnlyList<JsonNode> orderedLayers);
}

public interface IRuleEngine {
    JsonNode? ApplyRules(JsonNode candidate, IEnumerable<Rule> rules, RuleContext ctx);
}

public interface ITemplateValidator { void Validate(JsonNode doc, Template template); }
```

---

## `IConfigurationProvider` (client package)

```csharp
public sealed class StrataConfigurationSource : IConfigurationSource {
    public required Func<ScopeContext> ScopeFactory { get; init; }
    public required string Namespace { get; init; }
    public required Uri ServiceBaseUrl { get; init; }
    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new StrataConfigurationProvider(this);
}

public sealed class StrataConfigurationProvider : ConfigurationProvider {
    private readonly StrataConfigurationSource _source;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;

    public StrataConfigurationProvider(StrataConfigurationSource source) {
        _source = source;
        _http = new HttpClient { BaseAddress = source.ServiceBaseUrl };
    }

    public override void Load() => LoadAsync().GetAwaiter().GetResult();

    private async Task LoadAsync() {
        var scope = _source.ScopeFactory();
        var res = await _http.PostAsJsonAsync("/api/config/resolve",
            new { scope, @namespace = _source.Namespace });
        res.EnsureSuccessStatusCode();
        var payload = await res.Content.ReadFromJsonAsync<Dictionary<string,string>>()
                     ?? new();
        Data = payload; // standard IConfigurationProvider backing
    }

    public void StartWatch(IAsyncEnumerable<string> invalidations) {
        _cts = new();
        _ = Task.Run(async () => {
            await foreach (var key in invalidations.WithCancellation(_cts.Token)) {
                if (MatchesCurrentScope(key)) { await LoadAsync(); OnReload(); }
            }
        });
    }
}
```

**Usage**

```csharp
builder.Configuration.Add(new StrataConfigurationSource {
    Namespace = "payments",
    ServiceBaseUrl = new("https://config.mycorp.local"),
    ScopeFactory = () => new(
        Environment: builder.Environment.EnvironmentName,
        AppName: "PosClient",
        AppVersion: ThisAssembly.AssemblyInformationalVersion,
        Dimensions: new Dictionary<string,string> {
            ["orgId"] = Env("ORG_ID"),
            ["siteId"] = Env("SITE_ID"),
            ["deviceId"] = Env("DEVICE_ID"),
            ["userId"] = CurrentUserIdOrNull(),
        },
        Tags: new [] { "kiosk", "touch" })
});
```

---

## API surface (Admin/API host)

* `POST /api/config/resolve` → merged K/V for a namespace & scope
* `GET /api/scopes/{id}` / `GET /api/scopes/{id}/children`
* `POST /api/documents` (create/update) with schema validation
* `POST /api/rules` / `DELETE /api/rules/{id}`
* `POST /api/export` / `POST /api/import`
* `GET /api/templates` / `POST /api/templates`
* `GET /api/git/status` / `POST /api/git/sync`

All writes emit **domain events** → background worker persists to DB, mirrors to Git (if configured), and publishes Redis invalidations (channels partitioned by namespace).

---

## Redis caching & invalidation

* Cache key: `strata:v1:{namespace}:{scopeHash}:{revision}`
* Invalidation channel: `strata:inval:{namespace}` with a compact payload `{ scopeMinKey, revision }`.
* Clients watch via SSE/WebSocket (server fan‑out) or Redis directly when colocated.

---

## Git provider

* Directory layout:

    * `templates/{id}@{version}.schema.json`
    * `scopes/{path}/documents/{namespace}/{doc}.json` (path mirrors tree)
    * `rules/{path}/{ruleId}.json`
* `git sync` service: bidirectional by policy. Default **DB → Git** (mirror); optional **PR import** path for Git‑authored changes.
* Commit message conventions include scope id, namespace, and doc ids.

---

## Blazor admin UI (Server)

**Pages**

* **Scope Explorer** (tree + search + labels)
* **Namespace View** (table of documents + status + effective preview)
* **Document Editor** (JSON Schema form, diff viewer, history)
* **Rules** (list, priority, test‑bench with fake scope contexts)
* **Templates** (schema editor + UI hints)
* **Bundles** (export/import wizards)
* **Settings** (precedence editor; provider configuration)

**Components**

* `JsonSchemaForm` (render via NJsonSchema)
* `DiffView` (text & structured object diff)
* `ScopeBreadcrumbs`, `LabelsEditor`, `RuleTester`

---

## .NET Aspire (AppHost) composition

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var postgres = builder.AddPostgres("pg").WithDataVolume().AddDatabase("strata");
var redis = builder.AddRedis("cache").WithDataVolume();

var api = builder.AddProject<Projects.StrataConfig_ASP>("api")
    .WithReference(postgres).WithReference(redis)
    .WithEnvironment("ASPNETCORE_URLS","http://+:8080")
    .WithEndpoint("http", e => e.Port = 8080);

var blazor = builder.AddProject<Projects.StrataConfig_Blazor>("ui").WithReference(api);

builder.Build().Run();
```

---

## Merge engine details

* Flatten JSON objects into dotted keys for `IConfiguration` (e.g., `ui.theme.primary = "#09f"`).
* Merge order follows precedence; later layers override scalar values; arrays use **replace** by default, with optional `mergeStrategy` metadata per key (`append`, `set`, `uniqueBy: id`).
* Deep objects merged recursively.

Pseudocode:

```csharp
foreach (var layer in orderedLayers) {
  foreach (var (key,val) in Flatten(layer)) {
    if (!result.ContainsKey(key) || ShouldOverride(key)) result[key] = val;
  }
}
```

---

## Security & tenancy

* **Tenant boundary**: every `ScopeNode` belongs to a `TenantId`; all queries filtered by tenant.
* **AuthN**: OpenID Connect (Auth0/Entra/Okta), roles + per‑namespace permissions.
* **Audit**: who/when for changes; export bundles include signer and checksum.

---

## Testing strategy

* **Core**: property tests for merge associativity & determinism; rule engine truth tables; schema round‑trips.
* **Providers**: contract tests (`IConfigStore` spec) shared across EF/Git/Redis variants.
* **E2E**: Aspire‑orchestrated test environment; playwright tests for the UI.

---

## Minimal POC backlog (2–3 days)

1. **Core**: `IConfigStore` (in‑memory stub), merge engine, rule engine (subset), template validator (NJsonSchema).
2. **API**: `/api/config/resolve` + `/api/documents` (create/update) with validation.
3. **Client**: `StrataConfigurationProvider` + sample ASP.NET app consuming `IOptions<T>`.
4. **Redis**: memory cache first; wire Redis pub/sub for invalidation.
5. **UI**: Scope explorer stub + JSON Schema form for a single template.

Stretch: EF provider with Postgres + migrations; Git mirror write‑only; Aspire apphost wiring.

---

## Package references (initial)

* Core: `System.Text.Json`, `NJsonSchema`, `FluentValidation`, `Microsoft.Extensions.Configuration.Abstractions`
* EF: `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` (or `SqlServer`)
* Git: `LibGit2Sharp`
* Cache: `StackExchange.Redis`
* API: `AspNetCore.MinimalApi`, `Swashbuckle.AspNetCore`
* UI: `Microsoft.AspNetCore.Components`, `MudBlazor` or `Blazorise`
* Observability: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.Otlp`

---

## Example: end‑to‑end resolve flow (pseudo)

```csharp
// API handler
app.MapPost("/api/config/resolve", async (ResolveRequest req, IConfigStore store,
    IRuleEngine rules, ITemplateValidator tv, IMergeEngine merge, ICacheLayer cache, ILogger log, CancellationToken ct) =>
{
    var cacheKey = CacheKey.Build(req.Scope, req.Namespace);
    if (await cache.TryGetAsync(cacheKey, ct) is (true, var bytes) && bytes is not null)
        return Results.File(bytes, "application/json");

    var snap = await store.ReadAsync(req.Scope, req.Namespace, ct);
    var docs = snap.Documents;
    var applicableRules = await store.GetRulesAsync(snap.RootScopeId, ct);

    var candidates = new List<JsonNode>();
    foreach (var d in docs) {
        var templ = snap.Templates.Single(t => t.Id == d.TemplateRef);
        tv.Validate(JsonNode.Parse(d.ContentJson)!, templ);
        var afterRules = rules.ApplyRules(JsonNode.Parse(d.ContentJson)!, applicableRules, RuleContext.From(req));
        if (afterRules is not null) candidates.Add(afterRules);
    }

    var merged = merge.Merge(candidates.OrderBy(c => c["_precedence"].GetValue<int>()).ToList());
    var flattened = FlattenToDictionary(merged);

    var payload = JsonSerializer.SerializeToUtf8Bytes(flattened);
    await cache.SetAsync(cacheKey, payload, TimeSpan.FromMinutes(5), ct);
    return Results.File(payload, "application/json");
});
```

---

## Naming & licensing

* Project: **StrataConfig** (MIT license).
* Secondary packages: `StrataConfig.Client`, `StrataConfig.Providers.*`.

---

## Execution status (v0 POC)

| Status | Area | Scope | Notes |
| --- | --- | --- | --- |
| ✅ | Architecture | Confirm precedence defaults, domain model, and core flows | Documented above; ready to translate into code |
| ✅ | Runtime skeleton | Aspire AppHost + ApiService + Web templates | Baseline weather template plus minimal API host bootstrapped |
| 🚧 | Core POC backend | In-memory store, merge engine, rule engine, template validation, resolve/upsert endpoints | Merge/rule/template plumbing in place; store still ignores scope layering and lacks tests |
| ✅ | Testing | Unit coverage for merge/rule/template + minimal integration smoke | TinyBDD specs cover core merge/rule/store and API endpoints |
| 🚧 | Admin UI | Razor Components admin surface for templates/documents | Scope explorer + namespace viewer scaffolded; needs editing flows |
| ⏭️ | Data provider | EF Core provider (Postgres) + migrations + seeding | Waiting on finalized domain types |
| ⏭️ | Cache & invalidation | Redis-backed cache layer + pub/sub | Not started |
| ⏭️ | Git workflows | Git provider + import/export bundles | Not started |

---

## Near-term backlog (order of attack)

Focus: finish the in-memory/core POC so API + UI can iterate.

1. **Structure solution for reuse**
   - Create `src/StrataConfig.Core` class library for abstractions/engines.
   - Move `IConfigStore`, merge/rule/template validators, and DTOs out of ApiService.
   - Add Directory.Packages.props (shared package management).
2. **Harden in-memory store + scope semantics**
   - Model sample scope hierarchy (org/site/device) with deterministic precedence.
   - Return ordered layers per scope/namespace; add support for per-scope rules list.
   - Track document version (+ watermark) and handle optimistic concurrency.
3. **Strengthen rule + merge behavior**
   - Extend rule expressions for simple boolean/label checks and add guard rails.
   - Support array merge strategies (replace vs append) flags in metadata.
   - Add fast-path skip when no matching rules.
4. **Add automated tests**
   - Unit tests for merge engine (associativity, override precedence).
   - Unit tests for rule engine expression coverage.
   - Minimal API tests exercising `/api/config/resolve` happy path + validation failures.

---

## API & UI MVP implementation plan

1. **Domain foundations (Core)**
   - Flesh out scope graph model (`ScopeNode`, `ScopePath`, tenant keys) and precedence helper utilities.
   - Expand `StoreSnapshot` contract to return ordered layers + rule sets + metadata (revision watermark).
   - Enrich `ConfigDocument` with `Precedence`/`Layer` info and introduce lightweight DTOs for API responses.
   - Implement deterministic ordering + merge metadata within the in-memory provider, including sample hierarchy + rules.
   - Leverage PatternKit-style fluent chains for rule processing to keep overrides/excludes composable.
2. **API surface (Minimal API)**
   - `/api/templates` (GET list, GET by id) – serve schema + UI metadata.
   - `/api/scopes` (GET tree, GET by id, POST create placeholder) – drive UI navigation.
   - `/api/namespaces/{ns}/documents` – list + paging; include merge preview and audit metadata.
   - `/api/documents/{id}` – GET/PUT/DELETE with validation + optimistic concurrency (etag/version).
   - `/api/config/resolve` – extend payload to include `flattened` + `raw` + `metadata` (cache key, revision).
   - Shared problem details + validation responses; ensure OpenAPI descriptions cover new endpoints.
3. **Admin UI slice (Blazor)**
   - Layout: Scope explorer (tree + search) and namespace/document panel.
   - Components: Template catalog viewer, document editor (JSON Schema driven), merge preview drawer.
   - Services: Typed API clients, state containers for selected scope/namespace, optimistic update flows.
   - Styling: baseline components (cards/tables) and loading/error UX.
4. **Testing & quality gates**
   - Unit tests for scope precedence helpers + in-memory store behaviors.
   - API integration tests for new endpoints (create/update/error paths).
   - Playwright (or bUnit) smoke for UI navigation once components land.

Sequencing guidance: finish domain+API layers first (items 1–2), wire UI against them (item 3), then harden with tests (item 4).

---

### Immediate implementation backlog (per project)

| Project | Short-term actions | Notes |
| --- | --- | --- |
| `StrataConfig.Core` | Add scope + hierarchy models, precedence calculator, enrich in-memory store with sample tree & rule fixtures | Unblock API projections and tests |
| `StrataConfig.ApiService` | Introduce DTO layer, map new endpoints (`/api/templates`, `/api/scopes`, `/api/namespaces/{ns}/documents`, `/api/documents/{id}`), wire validation + version headers | Ensure OpenAPI tags + summaries |
| `StrataConfig.Web` | Replace weather sample with scope explorer + namespace/document panes; scaffold API clients & state services | Build against new endpoints |
| `StrataConfig.Tests` | Create unit/integration test suites for core precedence, rules, and API endpoints | Leverage xUnit + Aspire test host |

Start with the `Core` work, then layer API endpoints, followed by the Blazor UI and tests.

---

## Upcoming milestones (post-core POC)

1. **UI slice** – Replace weather UI with scope/document explorer + JSON schema-driven editor.
2. **EF Core provider** – New project with migrations, seeding scripts, Aspire wiring to Postgres.
3. **Redis cache** – Plug StackExchange.Redis into API, implement cache invalidation hooks.
4. **Git mirror & bundles** – Git provider prototype, import/export CLI, tie-ins with EF provider.

---

If you want, we can tackle the core refactor (new `StrataConfig.Core` project + scoped in-memory store) next to unblock downstream work.
