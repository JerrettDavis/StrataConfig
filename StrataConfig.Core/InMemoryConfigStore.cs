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

    public InMemoryConfigStore()
    {
        var global = ScopeNode.Create(_rootScopeId, "global", "global", "Global");
        var orgScope = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000010"),
            "org:northwind",
            "org",
            "Northwind",
            parentId: global.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["region"] = "NA"
            });
        var siteScope = ScopeNode.Create(
            Guid.Parse("00000000-0000-0000-0000-000000000020"),
            "site:seattle-hq",
            "site",
            "Seattle HQ",
            parentId: orgScope.Id,
            labels: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["timezone"] = "PST"
            });

        var nodes = new[]
        {
            global,
            orgScope,
            siteScope
        };

        _graph = new ScopeGraph(nodes);
        foreach (var node in nodes)
        {
            _scopeByKey[node.Key] = node;
        }

        _templates.Add(new Template(
            Id: "ui.theme",
            SchemaVersion: 1,
            JsonSchema: UiThemeSchema,
            UIMetadata: null));

        var now = DateTimeOffset.UtcNow;

        Add("ui", new ConfigDocument(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000001"),
            ScopeId: global.Id,
            Namespace: "ui",
            TemplateRef: "ui.theme",
            ContentJson: JsonSerializer.Serialize(new
            {
                theme = new { primary = "#1166dd", secondary = "#0aa", font = "Inter" },
                featureFlags = new { welcome = true }
            }),
            Version: 1,
            UpdatedUtc: now,
            UpdatedBy: "seed"));

        Add("ui", new ConfigDocument(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000002"),
            ScopeId: orgScope.Id,
            Namespace: "ui",
            TemplateRef: "ui.theme",
            ContentJson: JsonSerializer.Serialize(new
            {
                theme = new { primary = "#0e8bff" },
                featureFlags = new { welcome = true }
            }),
            Version: 1,
            UpdatedUtc: now,
            UpdatedBy: "seed"));

        Add("ui", new ConfigDocument(
            Id: Guid.Parse("10000000-0000-0000-0000-000000000003"),
            ScopeId: siteScope.Id,
            Namespace: "ui",
            TemplateRef: "ui.theme",
            ContentJson: JsonSerializer.Serialize(new
            {
                theme = new { primary = "#ff4081" },
                featureFlags = new { welcome = false }
            }),
            Version: 1,
            UpdatedUtc: now,
            UpdatedBy: "seed"));

        _rules.Add(new Rule(
            Id: Guid.Parse("00000000-0000-0000-0000-000000000030"),
            ScopeId: siteScope.Id,
            Expr: "tags.has(\"beta\")",
            Effect: RuleEffect.Override,
            OverrideJson: JsonSerializer.Serialize(new
            {
                featureFlags = new { welcome = true }
            }),
            Priority: 100));
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
