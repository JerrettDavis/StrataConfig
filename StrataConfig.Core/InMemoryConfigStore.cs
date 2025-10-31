using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StrataConfig.Core;

public sealed class InMemoryConfigStore : IConfigStore
{
    private readonly ConcurrentDictionary<string, List<ConfigDocument>> _docsByNamespace = new();
    private readonly List<Template> _templates = new();
    private readonly List<Rule> _rules = new();
    private readonly Dictionary<string, ScopeNode> _scopeByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Guid _rootScopeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly ScopeGraph _graph;
    private readonly object _gate = new();
    private long _revision;

    private const string UiThemeSchema = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "theme": {
      "type": "object",
      "properties": {
        "primary": { "type": "string" },
        "secondary": { "type": "string" },
        "font": { "type": "string" }
      },
      "required": [ "primary" ],
      "additionalProperties": false
    },
    "featureFlags": {
      "type": "object",
      "properties": {
        "welcome": { "type": "boolean" }
      },
      "additionalProperties": true
    }
  },
  "required": [ "theme" ],
  "additionalProperties": true
}
""";

    private const string ObservabilitySchema = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "dashboards": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "id": { "type": "string" },
          "title": { "type": "string" },
          "widgets": {
            "type": "array",
            "items": { "type": "string" }
          }
        },
        "required": [ "id", "title" ],
        "additionalProperties": false
      }
    },
    "alerts": {
      "type": "object",
      "additionalProperties": {
        "type": "object",
        "properties": {
          "severity": { "type": "string" },
          "channel": { "type": "string" }
        },
        "additionalProperties": false
      }
    }
  },
  "additionalProperties": false
}
""";

    private const string PricingSchema = """
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "properties": {
    "currency": { "type": "string" },
    "tiers": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "sku": { "type": "string" },
          "base": { "type": "number" },
          "adjustments": {
            "type": "object",
            "additionalProperties": { "type": "number" }
          }
        },
        "required": [ "sku", "base" ],
        "additionalProperties": false
      }
    },
    "fees": {
      "type": "object",
      "additionalProperties": { "type": "number" }
    }
  },
  "required": [ "currency", "tiers" ],
  "additionalProperties": false
}
""";

    public InMemoryConfigStore()
    {
        var seedNodes = SeedScopes();
        _graph = new ScopeGraph(seedNodes);
        foreach (var node in seedNodes)
        {
            _scopeByKey[node.Key] = node;
        }

        SeedTemplates();
        SeedDocuments();
        SeedRules();
    }

    private IReadOnlyList<ScopeNode> SeedScopes()
    {
        var nodes = new List<ScopeNode>();

        var global = ScopeNode.Create(_rootScopeId, "global", "global", "Global");
        nodes.Add(global);

        var retailDivision = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000005"),
            "division:retail",
            "division",
            "Retail",
            parentId: global.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["segment"] = "consumer"
            });

        var operationsDivision = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000006"),
            "division:operations",
            "division",
            "Operations",
            parentId: global.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["segment"] = "logistics"
            });

        nodes.Add(retailDivision);
        nodes.Add(operationsDivision);

        var northwindOrg = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000010"),
            "org:northwind",
            "org",
            "Northwind",
            parentId: retailDivision.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "NA",
                ["industry"] = "Retail"
            });

        var fabrikamOrg = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000011"),
            "org:fabrikam",
            "org",
            "Fabrikam",
            parentId: operationsDivision.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "EU",
                ["industry"] = "Fulfillment"
            });

        var contosoOrg = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000012"),
            "org:contoso",
            "org",
            "Contoso",
            parentId: retailDivision.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "UK",
                ["industry"] = "Retail"
            });

        nodes.Add(northwindOrg);
        nodes.Add(fabrikamOrg);
        nodes.Add(contosoOrg);

        var seattleSite = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000020"),
            "site:seattle-hq",
            "site",
            "Seattle HQ",
            parentId: northwindOrg.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "PST"
            });

        var portlandSite = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000021"),
            "site:portland-field",
            "site",
            "Portland Field Ops",
            parentId: northwindOrg.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "PST"
            });

        var berlinSite = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000022"),
            "site:berlin-fulfillment",
            "site",
            "Berlin Fulfillment",
            parentId: fabrikamOrg.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "CET"
            });

        var dublinSite = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000023"),
            "site:dublin-lab",
            "site",
            "Dublin Lab",
            parentId: fabrikamOrg.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "GMT"
            });

        var londonSite = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000024"),
            "site:london-hub",
            "site",
            "London Hub",
            parentId: contosoOrg.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "GMT"
            });

        nodes.Add(seattleSite);
        nodes.Add(portlandSite);
        nodes.Add(berlinSite);
        nodes.Add(dublinSite);
        nodes.Add(londonSite);

        var seattleDevice = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000031"),
            "device:pos-sea-01",
            "device",
            "POS SEA-01",
            parentId: seattleSite.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["role"] = "pos"
            });

        var berlinDevice = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000032"),
            "device:line-ber-robot",
            "device",
            "Line Robot BER-1",
            parentId: berlinSite.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["role"] = "robotics"
            });

        nodes.Add(seattleDevice);
        nodes.Add(berlinDevice);

        return nodes;
    }

    private void SeedTemplates()
    {
        _templates.AddRange(new[]
        {
            new Template(
                Id: "ui.theme",
                SchemaVersion: 1,
                JsonSchema: UiThemeSchema,
                UIMetadata: null),
            new Template(
                Id: "ops.observability",
                SchemaVersion: 1,
                JsonSchema: ObservabilitySchema,
                UIMetadata: null),
            new Template(
                Id: "commerce.pricing",
                SchemaVersion: 1,
                JsonSchema: PricingSchema,
                UIMetadata: null)
        });
    }

    private void SeedDocuments()
    {
        var timestamp = DateTimeOffset.UtcNow;

        SeedUiDocuments(timestamp);
        SeedObservabilityDocuments(timestamp);
        SeedPricingDocuments(timestamp);
    }

    private void SeedUiDocuments(DateTimeOffset timestamp)
    {
        var global = _scopeByKey["global"];
        var retailDivision = _scopeByKey["division:retail"];
        var northwind = _scopeByKey["org:northwind"];
        var seattle = _scopeByKey["site:seattle-hq"];
        var portland = _scopeByKey["site:portland-field"];
        var fabrikam = _scopeByKey["org:fabrikam"];
        var berlin = _scopeByKey["site:berlin-fulfillment"];
        var seattleDevice = _scopeByKey["device:pos-sea-01"];

        AddDocument(
            "ui",
            global,
            "ui.theme",
            new
            {
                theme = new { primary = "#1166dd", secondary = "#0aa3d6", font = "Inter" },
                featureFlags = new { welcome = true, quicklinks = true }
            },
            timestamp,
            "global-base");

        AddDocument(
            "ui",
            retailDivision,
            "ui.theme",
            new
            {
                theme = new { primary = "#1166dd", font = "Public Sans" },
                featureFlags = new { storefinder = true }
            },
            timestamp,
            "retail-font");

        AddDocument(
            "ui",
            northwind,
            "ui.theme",
            new
            {
                theme = new { primary = "#0e8bff", secondary = "#1dd3b0" },
                featureFlags = new { welcome = true, quicklinks = false }
            },
            timestamp,
            "northwind-theme");

        AddDocument(
            "ui",
            seattle,
            "ui.theme",
            new
            {
                theme = new { primary = "#ff4081", secondary = "#ffe082" },
                featureFlags = new { welcome = false, kiosk = true }
            },
            timestamp,
            "seattle-site");

        AddDocument(
            "ui",
            portland,
            "ui.theme",
            new
            {
                theme = new { primary = "#32a852", secondary = "#0f766e" },
                featureFlags = new { welcome = true, fieldOps = true }
            },
            timestamp,
            "portland-site");

        AddDocument(
            "ui",
            fabrikam,
            "ui.theme",
            new
            {
                theme = new { primary = "#34d399", secondary = "#2563eb" },
                featureFlags = new { robotics = true }
            },
            timestamp,
            "fabrikam-theme");

        AddDocument(
            "ui",
            berlin,
            "ui.theme",
            new
            {
                theme = new { primary = "#1f2937", secondary = "#facc15" },
                featureFlags = new { darkmode = true }
            },
            timestamp,
            "berlin-site");

        AddDocument(
            "ui",
            seattleDevice,
            "ui.theme",
            new
            {
                theme = new { primary = "#ff4081", font = "IBM Plex Mono" },
                featureFlags = new { kiosk = true, offline = true }
            },
            timestamp,
            "seattle-device",
            updatedBy: "iot-seed");
    }

    private void SeedObservabilityDocuments(DateTimeOffset timestamp)
    {
        var global = _scopeByKey["global"];
        var operationsDivision = _scopeByKey["division:operations"];
        var fabrikam = _scopeByKey["org:fabrikam"];
        var berlin = _scopeByKey["site:berlin-fulfillment"];
        var seattle = _scopeByKey["site:seattle-hq"];
        var berlinDevice = _scopeByKey["device:line-ber-robot"];

        AddDocument(
            "observability",
            global,
            "ops.observability",
            new
            {
                dashboards = new[]
                {
                    new
                    {
                        id = "global-kpi",
                        title = "Global KPI",
                        widgets = new[] { "revenue", "latency", "conversion" }
                    }
                },
                alerts = new
                {
                    revenueDip = new { severity = "high", channel = "slack" }
                }
            },
            timestamp,
            "global-ops");

        AddDocument(
            "observability",
            operationsDivision,
            "ops.observability",
            new
            {
                dashboards = new[]
                {
                    new
                    {
                        id = "ops-oncall",
                        title = "Ops On-call",
                        widgets = new[] { "uptime", "incidents", "error-budget" }
                    }
                },
                alerts = new
                {
                    pager = new { severity = "critical", channel = "pagerduty" }
                }
            },
            timestamp,
            "operations-dashboard");

        AddDocument(
            "observability",
            fabrikam,
            "ops.observability",
            new
            {
                dashboards = new[]
                {
                    new
                    {
                        id = "fab-warehouse",
                        title = "Fabrikam Warehouse",
                        widgets = new[] { "throughput", "error-rate", "cycle-time" }
                    }
                },
                alerts = new
                {
                    shift = new { severity = "medium", channel = "teams" }
                }
            },
            timestamp,
            "fabrikam-warehouse");

        AddDocument(
            "observability",
            berlin,
            "ops.observability",
            new
            {
                dashboards = new[]
                {
                    new
                    {
                        id = "berlin-line",
                        title = "Berlin Fulfillment Line",
                        widgets = new[] { "pick-speed", "orders", "downtime" }
                    }
                },
                alerts = new
                {
                    conveyor = new { severity = "high", channel = "sms" }
                }
            },
            timestamp,
            "berlin-line");

        AddDocument(
            "observability",
            seattle,
            "ops.observability",
            new
            {
                dashboards = new[]
                {
                    new
                    {
                        id = "seattle-site",
                        title = "Seattle Storefront",
                        widgets = new[] { "checkouts", "aov", "traffic" }
                    }
                },
                alerts = new
                {
                    checkout = new { severity = "medium", channel = "email" }
                }
            },
            timestamp,
            "seattle-observability");

        AddDocument(
            "observability",
            berlinDevice,
            "ops.observability",
            new
            {
                dashboards = Array.Empty<object>(),
                alerts = new
                {
                    conveyor = new { severity = "high", channel = "pagerduty" },
                    motors = new { severity = "high", channel = "sms" }
                }
            },
            timestamp,
            "berlin-device");
    }

    private void SeedPricingDocuments(DateTimeOffset timestamp)
    {
        var global = _scopeByKey["global"];
        var retailDivision = _scopeByKey["division:retail"];
        var northwind = _scopeByKey["org:northwind"];
        var seattle = _scopeByKey["site:seattle-hq"];
        var contoso = _scopeByKey["org:contoso"];
        var london = _scopeByKey["site:london-hub"];

        AddDocument(
            "pricing",
            global,
            "commerce.pricing",
            new
            {
                currency = "USD",
                tiers = new[]
                {
                    new
                    {
                        sku = "standard",
                        @base = 10.0m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["global"] = 0m
                        }
                    },
                    new
                    {
                        sku = "premium",
                        @base = 25.0m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["subscription"] = -2.5m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["handling"] = 1.25m
                }
            },
            timestamp,
            "global-pricing");

        AddDocument(
            "pricing",
            retailDivision,
            "commerce.pricing",
            new
            {
                currency = "USD",
                tiers = new[]
                {
                    new
                    {
                        sku = "standard",
                        @base = 9.5m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["loyalty"] = -1.0m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["state-tax"] = 0.85m
                }
            },
            timestamp,
            "retail-pricing");

        AddDocument(
            "pricing",
            northwind,
            "commerce.pricing",
            new
            {
                currency = "USD",
                tiers = new[]
                {
                    new
                    {
                        sku = "vip",
                        @base = 18.0m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["promo"] = -2.0m,
                            ["shipping"] = 0.5m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["pickup"] = 0m
                }
            },
            timestamp,
            "northwind-pricing");

        AddDocument(
            "pricing",
            seattle,
            "commerce.pricing",
            new
            {
                currency = "USD",
                tiers = new[]
                {
                    new
                    {
                        sku = "vip",
                        @base = 17.5m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["local-tax"] = 1.2m,
                            ["stadium-fee"] = 0.4m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["delivery"] = 4.5m
                }
            },
            timestamp,
            "seattle-pricing");

        AddDocument(
            "pricing",
            contoso,
            "commerce.pricing",
            new
            {
                currency = "GBP",
                tiers = new[]
                {
                    new
                    {
                        sku = "standard",
                        @base = 12.0m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["seasonal"] = 1.5m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["fulfillment"] = 1.1m
                }
            },
            timestamp,
            "contoso-pricing");

        AddDocument(
            "pricing",
            london,
            "commerce.pricing",
            new
            {
                currency = "GBP",
                tiers = new[]
                {
                    new
                    {
                        sku = "standard",
                        @base = 11.5m,
                        adjustments = new Dictionary<string, decimal>
                        {
                            ["holiday"] = -1.0m
                        }
                    }
                },
                fees = new Dictionary<string, decimal>
                {
                    ["delivery"] = 3.25m
                }
            },
            timestamp,
            "london-pricing");
    }

    private void SeedRules()
    {
        var seattleSite = _scopeByKey["site:seattle-hq"];
        var berlinDevice = _scopeByKey["device:line-ber-robot"];

        _rules.Add(new Rule(
            Id: Guid.Parse("00000000-0000-0000-0000-000000000030"),
            ScopeId: seattleSite.Id,
            Expr: "tags.has(\"beta\")",
            Effect: RuleEffect.Override,
            OverrideJson: JsonSerializer.Serialize(new
            {
                featureFlags = new { welcome = true, experimentalCheckout = true }
            }),
            Priority: 100));

        _rules.Add(new Rule(
            Id: Guid.Parse("00000000-0000-0000-0000-000000000031"),
            ScopeId: berlinDevice.Id,
            Expr: "env == \"Production\"",
            Effect: RuleEffect.Override,
            OverrideJson: JsonSerializer.Serialize(new
            {
                alerts = new
                {
                    conveyor = new { severity = "critical", channel = "pagerduty" }
                }
            }),
            Priority: 110));
    }

    public Task<StoreSnapshot> ReadAsync(ScopeContext scope, string @namespace, CancellationToken ct)
    {
        _docsByNamespace.TryGetValue(@namespace, out var docs);
        var layers = BuildLayers(scope, docs ?? Enumerable.Empty<ConfigDocument>());
        var snapshot = new StoreSnapshot(
            _rootScopeId,
            layers,
            _templates,
            Interlocked.Read(ref _revision));
        return Task.FromResult(snapshot);
    }

    public Task<ConfigDocument> UpsertAsync(ConfigDocumentWrite document, CancellationToken ct)
    {
        var list = _docsByNamespace.GetOrAdd(document.Namespace, _ => new List<ConfigDocument>());

        ConfigDocument stored;
        lock (_gate)
        {
            var existing = document.Id.HasValue
                ? list.FirstOrDefault(d => d.Id == document.Id.Value)
                : null;

            var id = existing?.Id ?? document.Id ?? Guid.NewGuid();
            var version = existing is null ? 1 : existing.Version + 1;
            if (existing is not null)
            {
                list.Remove(existing);
            }

            stored = new ConfigDocument(
                Id: id,
                ScopeId: document.ScopeId,
                Namespace: document.Namespace,
                TemplateRef: document.TemplateRef,
                ContentJson: document.Content.ToJsonString(),
                Version: version,
                UpdatedUtc: DateTimeOffset.UtcNow,
                UpdatedBy: document.UpdatedBy);

            list.Add(stored);
            Interlocked.Increment(ref _revision);
        }

        return Task.FromResult(stored);
    }

    public Task<ConfigDocument?> GetDocumentAsync(Guid id, CancellationToken ct)
    {
        foreach (var list in _docsByNamespace.Values)
        {
            var match = list.FirstOrDefault(d => d.Id == id);
            if (match is not null)
            {
                return Task.FromResult<ConfigDocument?>(CloneDocument(match));
            }
        }
        return Task.FromResult<ConfigDocument?>(null);
    }

    public Task<IReadOnlyList<ConfigDocument>> GetDocumentsAsync(string @namespace, Guid? scopeId, CancellationToken ct)
    {
        _docsByNamespace.TryGetValue(@namespace, out var list);
        var result = (list ?? new List<ConfigDocument>())
            .Where(d => !scopeId.HasValue || d.ScopeId == scopeId.Value)
            .Select(CloneDocument)
            .ToList();
        return Task.FromResult((IReadOnlyList<ConfigDocument>)result);
    }

    public Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct)
    {
        lock (_gate)
        {
            foreach (var kvp in _docsByNamespace)
            {
                var list = kvp.Value;
                var idx = list.FindIndex(d => d.Id == id);
                if (idx >= 0)
                {
                    list.RemoveAt(idx);
                    Interlocked.Increment(ref _revision);
                    return Task.FromResult(true);
                }
            }
        }
        return Task.FromResult(false);
    }

    public async Task<ConfigDocument> CloneDocumentAsync(Guid id, Guid newScopeId, string updatedBy, CancellationToken ct)
    {
        var existing = await GetDocumentAsync(id, ct) ?? throw new InvalidOperationException("Document not found.");
        var content = JsonNode.Parse(existing.ContentJson) ?? new JsonObject();
        var write = new ConfigDocumentWrite(null, newScopeId, existing.Namespace, existing.TemplateRef, content, updatedBy);
        return await UpsertAsync(write, ct);
    }

    public async Task<IReadOnlyList<ConfigDocument>> ImportAsync(string @namespace, IEnumerable<ConfigDocumentWrite> documents, CancellationToken ct)
    {
        var results = new List<ConfigDocument>();
        foreach (var d in documents)
        {
            var normalized = new ConfigDocumentWrite(d.Id, d.ScopeId, @namespace, d.TemplateRef, d.Content, d.UpdatedBy);
            var saved = await UpsertAsync(normalized, ct);
            results.Add(saved);
        }
        return results;
    }

    public Task<IReadOnlyList<ConfigDocument>> ExportAsync(string @namespace, Guid? scopeId, CancellationToken ct)
    {
        _docsByNamespace.TryGetValue(@namespace, out var list);
        var items = (list ?? new List<ConfigDocument>())
            .Where(d => !scopeId.HasValue || d.ScopeId == scopeId.Value)
            .Select(CloneDocument)
            .ToList();
        return Task.FromResult((IReadOnlyList<ConfigDocument>)items);
    }

    public Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken ct)
        => Task.FromResult((IReadOnlyList<Template>)_templates);

    public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct)
    {
        var keys = _docsByNamespace.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult((IReadOnlyList<string>)keys);
    }

    public Task<IReadOnlyList<ScopeNode>> GetScopeNodesAsync(CancellationToken ct)
        => Task.FromResult((IReadOnlyList<ScopeNode>)_graph.Nodes.Values.ToList());

    public Task<IReadOnlyList<Rule>> GetRulesAsync(Guid scopeId, CancellationToken ct)
        => Task.FromResult((IReadOnlyList<Rule>)_rules);

    private IReadOnlyList<ConfigLayer> BuildLayers(ScopeContext scope, IEnumerable<ConfigDocument> documents)
    {
        var ordered = GatherScopeNodes(scope).ToList();
        var byScope = documents
            .GroupBy(d => d.ScopeId)
            .ToDictionary(g => g.Key, g => g.Select(CloneDocument).ToList());

        var layers = new List<ConfigLayer>(ordered.Count);
        var index = 0;
        foreach (var node in ScopePrecedence.Order(ordered))
        {
            byScope.TryGetValue(node.Id, out var docList);
            layers.Add(new ConfigLayer(
                node,
                docList ?? new List<ConfigDocument>(),
                index++));
        }

        return layers;
    }

    private IEnumerable<ScopeNode> GatherScopeNodes(ScopeContext scope)
    {
        var seen = new HashSet<Guid>();

        foreach (var segment in ExpandPath("global", seen))
        {
            yield return segment;
        }

        if (scope.Dimensions.TryGetValue("division", out var divisionKey))
        {
            foreach (var segment in ExpandPath($"division:{divisionKey}", seen))
            {
                yield return segment;
            }
        }

        if (scope.Dimensions.TryGetValue("org", out var orgKey))
        {
            foreach (var segment in ExpandPath($"org:{orgKey}", seen))
            {
                yield return segment;
            }
        }

        if (scope.Dimensions.TryGetValue("site", out var siteKey))
        {
            foreach (var segment in ExpandPath($"site:{siteKey}", seen))
            {
                yield return segment;
            }
        }

        if (scope.Dimensions.TryGetValue("device", out var deviceKey))
        {
            foreach (var segment in ExpandPath($"device:{deviceKey}", seen))
            {
                yield return segment;
            }
        }

        if (!string.IsNullOrWhiteSpace(scope.AppName))
        {
            var appNode = ScopeNode.Create(
                CreateDeterministicGuid($"app:{scope.AppName}"),
                $"app:{scope.AppName}",
                "app",
                scope.AppName,
                parentId: null);
            if (seen.Add(appNode.Id))
            {
                yield return appNode;
            }
        }

        if (!string.IsNullOrWhiteSpace(scope.Environment))
        {
            var envNode = ScopeNode.Create(
                CreateDeterministicGuid($"env:{scope.Environment}"),
                $"env:{scope.Environment}",
                "environment",
                scope.Environment,
                parentId: null);
            if (seen.Add(envNode.Id))
            {
                yield return envNode;
            }
        }
    }

    private IEnumerable<ScopeNode> ExpandPath(string key, HashSet<Guid> seen)
    {
        if (!_scopeByKey.TryGetValue(key, out var node))
        {
            yield break;
        }

        foreach (var segment in _graph.BuildPath(node.Id).Segments)
        {
            if (seen.Add(segment.Id))
            {
                yield return segment;
            }
        }
    }

    private void Add(string ns, ConfigDocument doc)
    {
        var list = _docsByNamespace.GetOrAdd(ns, _ => new List<ConfigDocument>());
        list.Add(doc);
        Interlocked.Increment(ref _revision);
    }

    private void AddDocument(
        string ns,
        ScopeNode scope,
        string templateRef,
        object content,
        DateTimeOffset timestamp,
        string discriminator,
        string updatedBy = "seed")
    {
        var json = JsonSerializer.Serialize(content);
        var doc = new ConfigDocument(
            Id: CreateDeterministicGuid($"{ns}:{scope.Id}:{templateRef}:{discriminator}"),
            ScopeId: scope.Id,
            Namespace: ns,
            TemplateRef: templateRef,
            ContentJson: json,
            Version: 1,
            UpdatedUtc: timestamp,
            UpdatedBy: updatedBy);

        Add(ns, doc);
    }

    private static ConfigDocument CloneDocument(ConfigDocument doc)
        => doc with { ContentJson = doc.ContentJson };

    private static Guid CreateDeterministicGuid(string value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 indicator
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variant RFC4122
        return new Guid(bytes);
    }
}
