using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TinyBDD;
using TinyBDD.Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using ApiProgram = StrataConfig.ApiService.Program;

namespace StrataConfig.Tests.Api;

[Feature("Config API surface (TinyBDD)")]
public sealed partial class ConfigApiTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private sealed record ApiContext(WebApplicationFactory<ApiProgram> Factory, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private sealed record TemplatesResult(ApiContext Context, IReadOnlyList<TemplateContract> Templates);
    private sealed record NamespaceResult(ApiContext Context, NamespaceDocumentsContract Documents, IDictionary<string, string> Resolved);
    private sealed record ObservabilityResult(ApiContext Context, NamespaceDocumentsContract Documents, IDictionary<string, string> Resolved);
    private sealed record TemplateNotFoundResult(ApiContext Context, HttpResponseMessage Response);
    private sealed record ScopesResult(ApiContext Context, IReadOnlyList<ScopeNodeContract> Tree, ScopeNodeContract Target, ScopeNodeContract? ScopeById);
    private sealed record UpsertResult(ApiContext Context, HttpResponseMessage Response);
    private sealed record DocumentSuccessResult(ApiContext Context, HttpResponseMessage Response, IDictionary<string, string> Resolved);
    private sealed record WeatherResult(ApiContext Context, HttpResponseMessage Response, WeatherForecastContract[] Forecasts);

    [Scenario("Templates endpoint returns seeded metadata")]
    [Fact]
    public Task Templates_ReturnSeeded()
        => Given("API client", CreateApiContext)
           .When("requesting templates", FetchTemplates)
           .Then("at least one template exists", r => r.Templates.Count > 0)
           .And("ui.theme template present", r => r.Templates.Any(t => t.Id == "ui.theme"))
           .And("dispose factory", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Namespace documents reflect layered store and resolve output")]
    [Fact]
    public Task NamespaceDocuments_ReturnLayers()
        => Given("API context", CreateApiContext)
           .When("fetching namespace documents", FetchNamespace)
           .Then("layers ordered", r => r.Documents.Layers.Select(l => l.Scope.Kind).SequenceEqual(["global", "division", "org", "site", "app", "environment"
           ]))
           .And("site layer flips welcome flag", r => r.Documents.Layers.Single(l => l.Scope.Kind == "site").Documents.First().Content["featureFlags"]! ["welcome"]!.GetValue<bool>() == false)
           .And("resolve output honors overrides", r => r.Resolved["featureFlags.welcome"] == "true")
           .And("dispose factory", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Observability namespace surfaces device layers and production overrides")]
    [Fact]
    public Task Observability_ReturnsDeviceLayer()
        => Given("API context", CreateApiContext)
           .When("fetching observability dataset", FetchObservability)
           .Then("device layer present", r => r.Documents.Layers.Select(l => l.Scope.Kind).Contains("device"))
           .And("resolve escalates conveyor severity", r => r.Resolved["alerts.conveyor.severity"] == "critical")
           .And("dispose factory", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Template lookup returns not found when missing")]
    [Fact]
    public Task Template_NotFound_Returns404()
        => Given("API client", CreateApiContext)
           .When("requesting missing template", FetchMissingTemplate)
           .Then("response is 404", r => r.Response.StatusCode == HttpStatusCode.NotFound)
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Scope endpoints return tree and allow lookup")]
    [Fact]
    public Task Scopes_ReturnTree()
        => Given("API context", CreateApiContext)
           .When("fetching scope tree", FetchScopes)
           .Then("tree has nodes", r => r.Tree.Count > 0)
           .And("lookup by id returns same node", r => r.ScopeById?.Id == r.Target.Id)
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Document upsert validation errors are surfaced")]
    [Fact]
    public Task Documents_UpsertValidationFailures()
        => Given("API context", CreateApiContext)
           .When("posting missing content", PostDocumentMissingContent)
           .Then("returns bad request", r => r.Response.StatusCode == HttpStatusCode.BadRequest)
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Document upsert succeeds and resolve sees changes")]
    [Fact]
    public Task Documents_UpsertSuccess()
        => Given("API context", CreateApiContext)
           .When("posting valid document", PostDocumentSuccess)
           .Then("status is 201", r => r.Response.StatusCode == HttpStatusCode.Created)
           .And("resolve picks up override", r => r.Resolved["theme.font"] == "Test Sans")
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    [Scenario("Weather endpoint returns sample payload")]
    [Fact]
    public Task Weather_ReturnsForecast()
        => Given("API context", CreateApiContext)
           .When("requesting weather", FetchWeather)
           .Then("response OK", r => r.Response.IsSuccessStatusCode)
           .And("payload non-empty", r => r.Forecasts.Length == 5)
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    private static ApiContext CreateApiContext()
    {
        var factory = new WebApplicationFactory<ApiProgram>();
        return new ApiContext(factory, factory.CreateClient());
    }

    private static TemplatesResult FetchTemplates(ApiContext context)
    {
        var templates = context.Client.GetFromJsonAsync<List<TemplateContract>>("/api/templates", SerializerOptions).GetAwaiter().GetResult()
                        ?? [];
        return new TemplatesResult(context, templates);
    }

    private static NamespaceResult FetchNamespace(ApiContext context)
    {
        var dims = new Dictionary<string, string?>
        {
            ["org"] = "northwind",
            ["site"] = "seattle-hq",
            ["dim.extra"] = "value"
        };

        var doc = context.Client.GetFromJsonAsync<NamespaceDocumentsContract>(
            QueryHelpers.AddQueryString("/api/namespaces/ui/documents", dims),
            SerializerOptions,
            CancellationToken.None).GetAwaiter().GetResult()!;

        var resolvePayload = new ResolveRequestContract(
            new ResolveScopeContextContract(
                Environment: "Development",
                AppName: "Strata.Admin",
                AppVersion: "1.0",
                Dimensions: new Dictionary<string, string>
                {
                    ["org"] = "northwind",
                    ["site"] = "seattle-hq",
                    ["extra"] = "value"
                },
                Tags: ["beta", "canary"]),
            Namespace: "ui");

        var resolveResponse = context.Client.PostAsJsonAsync("/api/config/resolve", resolvePayload, SerializerOptions).GetAwaiter().GetResult();
        resolveResponse.EnsureSuccessStatusCode();
        var resolved = resolveResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>(SerializerOptions).GetAwaiter().GetResult()
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new NamespaceResult(context, doc, resolved);
    }

    private static ObservabilityResult FetchObservability(ApiContext context)
    {
        var query = new Dictionary<string, string?>
        {
            ["environment"] = "Production",
            ["app"] = "Strata.Admin",
            ["org"] = "fabrikam",
            ["site"] = "berlin-fulfillment",
            ["device"] = "line-ber-robot"
        };

        var doc = context.Client.GetFromJsonAsync<NamespaceDocumentsContract>(
            QueryHelpers.AddQueryString("/api/namespaces/observability/documents", query),
            SerializerOptions,
            CancellationToken.None).GetAwaiter().GetResult()!;

        var resolvePayload = new ResolveRequestContract(
            new ResolveScopeContextContract(
                Environment: "Production",
                AppName: "Strata.Admin",
                AppVersion: null,
                Dimensions: new Dictionary<string, string>
                {
                    ["org"] = "fabrikam",
                    ["site"] = "berlin-fulfillment",
                    ["device"] = "line-ber-robot"
                },
                Tags: []),
            Namespace: "observability");

        var resolveResponse = context.Client.PostAsJsonAsync("/api/config/resolve", resolvePayload, SerializerOptions).GetAwaiter().GetResult();
        resolveResponse.EnsureSuccessStatusCode();
        var resolved = resolveResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>(SerializerOptions).GetAwaiter().GetResult()
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new ObservabilityResult(context, doc, resolved);
    }

    private static TemplateNotFoundResult FetchMissingTemplate(ApiContext context)
    {
        var response = context.Client.GetAsync("/api/templates/missing-template").GetAwaiter().GetResult();
        return new TemplateNotFoundResult(context, response);
    }

    private static ScopesResult FetchScopes(ApiContext context)
    {
        var tree = context.Client.GetFromJsonAsync<List<ScopeNodeContract>>("/api/scopes", SerializerOptions).GetAwaiter().GetResult() ?? [];
        var flattened = tree.SelectMany(Flatten).ToList();
        var target = flattened.First();
        var scopeById = context.Client.GetFromJsonAsync<ScopeNodeContract>($"/api/scopes/{target.Id}", SerializerOptions).GetAwaiter().GetResult();
        return new ScopesResult(context, tree, target, scopeById);

        static IEnumerable<ScopeNodeContract> Flatten(ScopeNodeContract node)
        {
            yield return node;
            foreach (var child in node.Children)
            {
                foreach (var inner in Flatten(child))
                {
                    yield return inner;
                }
            }
        }
    }

    private static UpsertResult PostDocumentMissingContent(ApiContext context)
    {
        var payload = new UpsertDocumentContract(null, Guid.Empty, "ui", "unknown", null, "tests");
        var response = context.Client.PostAsJsonAsync("/api/documents", payload, SerializerOptions).GetAwaiter().GetResult();
        return new UpsertResult(context, response);
    }

    private static DocumentSuccessResult PostDocumentSuccess(ApiContext context)
    {
        var scopeId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var payload = new UpsertDocumentContract(null, scopeId, "ui", "ui.theme", JsonNode.Parse("{ \"theme\": { \"primary\": \"#ff4081\", \"font\": \"Test Sans\" } }")!, "tests");
        var response = context.Client.PostAsJsonAsync("/api/documents", payload, SerializerOptions).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        var resolvePayload = new ResolveRequestContract(
            new ResolveScopeContextContract(
                Environment: "Development",
                AppName: "Strata.Admin",
                AppVersion: null,
                Dimensions: new Dictionary<string, string>
                {
                    ["org"] = "northwind",
                    ["site"] = "seattle-hq"
                },
                Tags: ["beta"]),
            Namespace: "ui");

        var resolveResponse = context.Client.PostAsJsonAsync("/api/config/resolve", resolvePayload, SerializerOptions).GetAwaiter().GetResult();
        resolveResponse.EnsureSuccessStatusCode();
        var resolved = resolveResponse.Content.ReadFromJsonAsync<Dictionary<string, string>>(SerializerOptions).GetAwaiter().GetResult()
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return new DocumentSuccessResult(context, response, resolved);
    }

    private static WeatherResult FetchWeather(ApiContext context)
    {
        var response = context.Client.GetAsync("/weatherforecast").GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var forecasts = response.Content.ReadFromJsonAsync<WeatherForecastContract[]>(SerializerOptions).GetAwaiter().GetResult()
                        ?? [];
        return new WeatherResult(context, response, forecasts);
    }

    private sealed record TemplateContract(string Id, int SchemaVersion, string? JsonSchema, string? UIMetadata);

    private sealed record NamespaceDocumentsContract(string Namespace, long Revision, IReadOnlyList<ConfigLayerContract> Layers);

    private sealed record ConfigLayerContract(int PrecedenceOrder, ScopeNodeContract Scope, IReadOnlyList<ConfigDocumentContract> Documents);

    private sealed record ScopeNodeContract(Guid Id, string Key, string Kind, string Name, Guid? ParentId, IReadOnlyDictionary<string, string> Labels, IReadOnlyList<ScopeNodeContract> Children);

    private sealed record ConfigDocumentContract(Guid Id, Guid ScopeId, string TemplateRef, int Version, DateTimeOffset UpdatedUtc, string UpdatedBy, JsonNode Content);

    private sealed record ResolveRequestContract(ResolveScopeContextContract Scope, string Namespace);

    private sealed record ResolveScopeContextContract(string Environment, string AppName, string? AppVersion, IDictionary<string, string> Dimensions, IReadOnlyCollection<string> Tags);

    private sealed record UpsertDocumentContract(Guid? Id, Guid ScopeId, string Namespace, string TemplateRef, JsonNode? Content, string? UpdatedBy);

    private sealed record WeatherForecastContract(DateOnly Date, int TemperatureC, string? Summary, int TemperatureF);
}
