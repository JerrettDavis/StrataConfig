using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using StrataConfig.Web.Components.Pages;
using StrataConfig.Web.Services;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Web;

[Feature("Home component interactivity (TinyBDD + bUnit)")]
public sealed partial class HomeComponentTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private sealed record TestState(BunitContext Context, FakeConfigApiClient Api, IRenderedComponent<Home> Cut);

    [Scenario("Selecting a sample scenario switches namespace and updates active banner")]
    [Fact]
    public Task ScenarioSelection_UpdatesNamespace()
        => Given("rendered Home component", RenderHome)
            .When("choosing the Berlin Ops scenario", state => SelectScenario(state, "Berlin Ops Dashboards"))
            .Then("observability namespace requested", state => state.Api.LastRequestedNamespace == "observability")
            .And("active scenario text updated", state => state.Cut.Markup.Contains("Berlin Ops Dashboards", StringComparison.Ordinal))
            .And("dispose context", state =>
            {
                state.Context.Dispose();
                return true;
            })
            .AssertPassed();

    [Scenario("Tag toggling pushes tags into resolve context")]
    [Fact]
    public Task TagToggle_ForwardsTags()
        => Given("rendered Home component", RenderHome)
            .When("activating the beta tag", state => ToggleTag(state, "beta"))
            .Then("resolve call contains beta tag", state => state.Api.ResolveCalls.Last().Tags.Contains("beta"))
            .And("dispose context", state =>
            {
                state.Context.Dispose();
                return true;
            })
            .AssertPassed();

    private static TestState RenderHome()
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConfigApiClient, FakeConfigApiClient>();
        ctx.Services.AddLogging();

        var cut = ctx.Render<Home>();
        cut.WaitForAssertion(() => { Assert.Contains("Layered Documents", cut.Markup, StringComparison.Ordinal); });

        var api = (FakeConfigApiClient)ctx.Services.GetRequiredService<IConfigApiClient>();
        return new TestState(ctx, api, cut);
    }

    private static TestState SelectScenario(TestState state, string scenarioName)
    {
        var button = state.Cut
            .FindAll(".scenario-chip")
            .First(b => b.TextContent.Contains(scenarioName, StringComparison.Ordinal));
        button.Click();

        state.Cut.WaitForAssertion(() =>
        {
            Assert.Equal("observability", state.Api.LastRequestedNamespace);
            Assert.Contains(scenarioName, state.Cut.Markup, StringComparison.Ordinal);
        });

        return state;
    }

    private static TestState ToggleTag(TestState state, string tag)
    {
        state.Cut.WaitForAssertion(() => Assert.NotEmpty(state.Api.ResolveCalls));

        var button = state.Cut
            .FindAll(".filter-group .chip-button")
            .First(b => b.TextContent.Contains(tag, StringComparison.OrdinalIgnoreCase));
        button.Click();

        state.Cut.WaitForAssertion(() =>
            Assert.Contains(tag, state.Api.ResolveCalls.Last().Tags, StringComparer.OrdinalIgnoreCase));

        return state;
    }

    private sealed class FakeConfigApiClient : IConfigApiClient
    {
        private readonly IReadOnlyList<string> _namespaces;
        private readonly IReadOnlyList<ScopeNodeDto> _scopeTree;
        private readonly Dictionary<string, ScopeNodeDto> _nodeByKey;
        private readonly Dictionary<string, NamespaceDocumentsDto> _documentsByNamespace;
        private readonly Dictionary<string, IDictionary<string, string>> _resolvedByNamespace;

        public FakeConfigApiClient()
        {
            _scopeTree = BuildScopeTree(out _nodeByKey);
            _namespaces = ["ui", "observability", "pricing"];
            _documentsByNamespace = BuildDocuments();
            _resolvedByNamespace = BuildResolved();
        }

        public string? LastRequestedNamespace { get; private set; }
        public IReadOnlyDictionary<string, string>? LastRequestedDimensions { get; private set; }
        public List<ResolveScopeContext> ResolveCalls { get; } = [];

        public Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TemplateDto>>([]);

        public Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_namespaces);

        public Task<IReadOnlyList<ScopeNodeDto>> GetScopeTreeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_scopeTree);

        public Task<NamespaceDocumentsDto?> GetNamespaceDocumentsAsync(
            string ns,
            IDictionary<string, string> dimensions,
            string environment,
            string appName,
            CancellationToken cancellationToken = default)
        {
            LastRequestedNamespace = ns;
            LastRequestedDimensions = new Dictionary<string, string>(dimensions, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<NamespaceDocumentsDto?>(_documentsByNamespace[ns]);
        }

        public Task<IDictionary<string, string>> ResolveAsync(
            string ns,
            ResolveScopeContext context,
            CancellationToken cancellationToken = default)
        {
            ResolveCalls.Add(context);
            return Task.FromResult(_resolvedByNamespace[ns]);
        }

        public Task<IReadOnlyList<ConfigDocumentDto>> ListDocumentsAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default)
        {
            var docs = _documentsByNamespace[ns].Layers
                .SelectMany(l => l.Documents)
                .Where(d => !scopeId.HasValue || d.ScopeId == scopeId.Value)
                .ToList();
            return Task.FromResult((IReadOnlyList<ConfigDocumentDto>)docs);
        }

        public Task<ConfigDocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var doc = _documentsByNamespace.Values
                .SelectMany(x => x.Layers)
                .SelectMany(l => l.Documents)
                .FirstOrDefault(d => d.Id == id);
            return Task.FromResult(doc);
        }

        public Task<ConfigDocumentDto> UpsertDocumentAsync(
            Guid? id,
            Guid scopeId,
            string ns,
            string templateRef,
            JsonNode content,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var snap = _documentsByNamespace[ns];
            var allDocs = snap.Layers.SelectMany(l => l.Documents).ToList();
            var existing = id.HasValue ? allDocs.FirstOrDefault(d => d.Id == id.Value) : null;
            var assignedId = existing?.Id ?? id ?? Guid.NewGuid();
            var version = existing is null ? 1 : existing.Version + 1;
            var newDoc = new ConfigDocumentDto(assignedId, scopeId, templateRef, version, now, updatedBy, content);

            // naive replace/insert in first layer matching scope or create a new layer
            var layer = snap.Layers.FirstOrDefault(l => l.Scope.Id == scopeId);
            if (layer is null)
            {
                var scope = _scopeTree.SelectMany(Flatten).First(x => x.Id == scopeId);
                var l = new ConfigLayerDto(snap.Layers.Count, scope, new List<ConfigDocumentDto> { newDoc });
                (snap.Layers as List<ConfigLayerDto>)!.Add(l);
            }
            else
            {
                var docs = (layer.Documents as List<ConfigDocumentDto>)!;
                var idx = docs.FindIndex(d => d.Id == assignedId);
                if (idx >= 0) docs[idx] = newDoc;
                else docs.Add(newDoc);
            }

            return Task.FromResult(newDoc);
        }

        public Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        {
            foreach (var layer in _documentsByNamespace.Values.SelectMany(v => v.Layers))
            {
                var list = (layer.Documents as List<ConfigDocumentDto>)!;
                var idx = list.FindIndex(d => d.Id == id);
                if (idx >= 0)
                {
                    list.RemoveAt(idx);
                    return Task.FromResult(true);
                }
            }

            return Task.FromResult(false);
        }

        public async Task<ConfigDocumentDto> CloneDocumentAsync(
            Guid sourceId,
            Guid destinationScopeId,
            string updatedBy,
            CancellationToken cancellationToken = default)
        {
            var src = await GetDocumentAsync(sourceId, cancellationToken) ?? throw new InvalidOperationException();
            var content = JsonNode.Parse(src.Content.ToJsonString()) ?? new JsonObject();
            return await UpsertDocumentAsync(null, destinationScopeId, GuessNamespace(src), src.TemplateRef, content, updatedBy, cancellationToken);
        }

        public Task<IReadOnlyList<ConfigDocumentDto>> ExportAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default)
            => ListDocumentsAsync(ns, scopeId, cancellationToken);

        public async Task<IReadOnlyList<ConfigDocumentDto>> ImportAsync(
            string ns,
            IReadOnlyList<ImportDocumentRequestDto> documents,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ConfigDocumentDto>();
            foreach (var d in documents)
            {
                var saved = await UpsertDocumentAsync(d.Id, d.ScopeId, ns, d.TemplateRef, d.Content, d.UpdatedBy ?? "tests", cancellationToken);
                results.Add(saved);
            }

            return results;
        }

        public Task<DiffResponseDto?> DiffAsync(
            JsonNode? aContent,
            Guid? aId,
            JsonNode? bContent,
            Guid? bId,
            CancellationToken cancellationToken = default)
        {
            // minimal stub: return empty diff when payloads are same string, else changed at root
            if (aId.HasValue && bId.HasValue && aId == bId)
                return Task.FromResult<DiffResponseDto?>(new(new List<string>(), new List<string>(), new List<DiffChangedEntryDto>()));
            if (aContent?.ToJsonString() == bContent?.ToJsonString())
                return Task.FromResult<DiffResponseDto?>(new(new List<string>(), new List<string>(), new List<DiffChangedEntryDto>()));
            return Task.FromResult<DiffResponseDto?>(new(new List<string>(), new List<string>(),
                new List<DiffChangedEntryDto> { new("$", aContent?.ToJsonString(), bContent?.ToJsonString()) }));
        }

        public Task<ScopeNodeDto> CreateScopeAsync(
            string kind,
            string name,
            Guid? parentId,
            IDictionary<string, string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid();
            var parent = parentId;
            var node = new ScopeNodeDto(id, $"{kind}:{name.ToLowerInvariant().Replace(' ', '-')}", kind, name, parent, new Dictionary<string, string>(),
                new List<ScopeNodeDto>());
            // naive: append under first root if no parent; not used by current tests
            return Task.FromResult(node);
        }

        private static IEnumerable<ScopeNodeDto> Flatten(ScopeNodeDto n)
        {
            yield return n;
            foreach (var c in n.Children)
            foreach (var i in Flatten(c))
                yield return i;
        }

        private static string GuessNamespace(ConfigDocumentDto doc)
            => doc.TemplateRef.Contains("ui", StringComparison.OrdinalIgnoreCase) ? "ui"
                : doc.TemplateRef.Contains("observability", StringComparison.OrdinalIgnoreCase) ? "observability"
                : "pricing";

        private Dictionary<string, NamespaceDocumentsDto> BuildDocuments()
        {
            var now = DateTimeOffset.UtcNow;

            NamespaceDocumentsDto Create(string ns, params (int precedence, string scopeKey, object content)[] docs)
            {
                var layers = docs
                    .Select(tuple =>
                    {
                        var scope = _nodeByKey[tuple.scopeKey];
                        var doc = new ConfigDocumentDto(
                            Guid.NewGuid(),
                            scope.Id,
                            tuple.scopeKey.StartsWith("device", StringComparison.OrdinalIgnoreCase) ? "ops.observability" : "ui.theme",
                            1,
                            now,
                            "test",
                            JsonNode.Parse(JsonSerializer.Serialize(tuple.content))!);
                        return new ConfigLayerDto(
                            tuple.precedence,
                            scope,
                            new List<ConfigDocumentDto> { doc });
                    })
                    .ToList();

                return new NamespaceDocumentsDto(ns, Revision: 1, layers);
            }

            var uiDocs = Create("ui",
                (0, "global", new { theme = new { primary = "#111111", font = "Inter" } }),
                (1, "division:retail", new { theme = new { font = "Public Sans" } }),
                (2, "org:northwind", new { theme = new { primary = "#0e8bff" } }),
                (3, "site:seattle-hq", new { theme = new { primary = "#ff4081" } }),
                (5, "env:Development", new { featureFlags = new { welcome = true } }));

            var observabilityDocs = new NamespaceDocumentsDto(
                "observability",
                1,
                new List<ConfigLayerDto>
                {
                    new(0, _nodeByKey["global"], new List<ConfigDocumentDto>
                    {
                        CreateDocument(_nodeByKey["global"], "ops.observability", new
                        {
                            dashboards = new[]
                            {
                                new { id = "global", title = "Global KPI", widgets = new[] { "traffic" } }
                            }
                        }, now)
                    }),
                    new(3, _nodeByKey["site:berlin-fulfillment"], new List<ConfigDocumentDto>
                    {
                        CreateDocument(_nodeByKey["site:berlin-fulfillment"], "ops.observability", new
                        {
                            alerts = new { conveyor = new { severity = "high" } }
                        }, now)
                    }),
                    new(4, _nodeByKey["device:line-ber-robot"], new List<ConfigDocumentDto>
                    {
                        CreateDocument(_nodeByKey["device:line-ber-robot"], "ops.observability", new
                        {
                            alerts = new { conveyor = new { severity = "critical" } }
                        }, now)
                    })
                });

            var pricingDocs = new NamespaceDocumentsDto(
                "pricing",
                1,
                new List<ConfigLayerDto>
                {
                    new(0, _nodeByKey["global"], new List<ConfigDocumentDto>
                    {
                        CreateDocument(_nodeByKey["global"], "commerce.pricing", new
                        {
                            currency = "USD",
                            tiers = new[]
                            {
                                new { sku = "standard", @base = 10.0m }
                            }
                        }, now)
                    }),
                    new(2, _nodeByKey["org:contoso"], new List<ConfigDocumentDto>
                    {
                        CreateDocument(_nodeByKey["org:contoso"], "commerce.pricing", new
                        {
                            currency = "GBP"
                        }, now)
                    })
                });

            return new Dictionary<string, NamespaceDocumentsDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["ui"] = uiDocs,
                ["observability"] = observabilityDocs,
                ["pricing"] = pricingDocs
            };
        }

        private Dictionary<string, IDictionary<string, string>> BuildResolved()
            => new(StringComparer.OrdinalIgnoreCase)
            {
                ["ui"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["theme.font"] = "Inter",
                    ["featureFlags.welcome"] = "true"
                },
                ["observability"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["alerts.conveyor.severity"] = "critical"
                },
                ["pricing"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

        private static ConfigDocumentDto CreateDocument(ScopeNodeDto scope, string templateRef, object content, DateTimeOffset timestamp)
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(content)) ?? new JsonObject();
            return new ConfigDocumentDto(
                Guid.NewGuid(),
                scope.Id,
                templateRef,
                1,
                timestamp,
                "test",
                json);
        }

        private static IReadOnlyList<ScopeNodeDto> BuildScopeTree(out Dictionary<string, ScopeNodeDto> index)
        {
            var nodes = new Dictionary<string, ScopeNodeDto>(StringComparer.OrdinalIgnoreCase);

            ScopeNodeDto Create(string key, string kind, string name, Guid id, Guid? parentId, IEnumerable<ScopeNodeDto>? children = null)
            {
                var node = new ScopeNodeDto(
                    id,
                    key,
                    kind,
                    name,
                    parentId,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    (children ?? []).ToList());
                nodes[key] = node;
                return node;
            }

            var globalId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var retailId = Guid.Parse("10000000-0000-0000-0000-000000000002");
            var operationsId = Guid.Parse("10000000-0000-0000-0000-000000000003");
            var northwindId = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var contosoId = Guid.Parse("20000000-0000-0000-0000-000000000002");
            var fabrikamId = Guid.Parse("20000000-0000-0000-0000-000000000003");
            var seattleId = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var berlinId = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var londonId = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var robotId = Guid.Parse("40000000-0000-0000-0000-000000000001");

            var robot = Create("device:line-ber-robot", "device", "Line Robot", robotId, berlinId);
            var berlin = Create("site:berlin-fulfillment", "site", "Berlin Fulfillment", berlinId, fabrikamId, [robot]);
            var seattle = Create("site:seattle-hq", "site", "Seattle HQ", seattleId, northwindId);
            var london = Create("site:london-hub", "site", "London Hub", londonId, contosoId);
            var northwind = Create("org:northwind", "org", "Northwind", northwindId, retailId, [seattle]);
            var contoso = Create("org:contoso", "org", "Contoso", contosoId, retailId, [london]);
            var fabrikam = Create("org:fabrikam", "org", "Fabrikam", fabrikamId, operationsId, [berlin]);
            var retail = Create("division:retail", "division", "Retail", retailId, globalId, [northwind, contoso]);
            var operations = Create("division:operations", "division", "Operations", operationsId, globalId, [fabrikam]);
            var environment = Create("env:Development", "environment", "Development", Guid.Parse("50000000-0000-0000-0000-000000000001"), null);

            var global = new ScopeNodeDto(
                globalId,
                "global",
                "global",
                "Global",
                null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                [retail, operations]);
            nodes["global"] = global;

            var app = Create("app:Strata.Admin", "app", "Strata.Admin", Guid.Parse("60000000-0000-0000-0000-000000000001"), null);

            index = nodes;
            return [global, environment, app];
        }

        public Task<string> CreateNamespaceAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(name);
    }
}
