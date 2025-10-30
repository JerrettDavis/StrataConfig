using System.Linq;
using StrataConfig.Core;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Core;

[Feature("In-memory store layering and revision semantics (TinyBDD)")]
public sealed class InMemoryConfigStoreTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private sealed record StoreState(InMemoryConfigStore Store, ScopeContext Scope, StoreSnapshot Snapshot);

    [Scenario("Reading seeded namespace produces ordered layers and revision watermark")]
    [Fact]
    public Task Read_ReturnsOrderedLayers()
        => Given("store with default seed", BuildState)
           .Then("revision watermark is non-zero", s => s.Snapshot.Revision >= 3)
           .And("layers are ordered from global to environment", s => s.Snapshot.Layers.Select(l => l.Scope.Kind).SequenceEqual(new[] { "global", "org", "site", "app", "environment" }))
           .And("site layer overrides welcome flag", s => s.Snapshot.Layers.First(l => l.Scope.Kind == "site").Documents.First().ContentJson.Contains("\"welcome\":false"))
           .AssertPassed();

    [Scenario("Upserting document increments revision and surfaces in snapshot")]
    [Fact]
    public Task Upsert_BumpsRevision()
        => Given("store and scope", BuildState)
           .When("upserting a new doc", Upsert)
           .Then("revision increased", s => s.NewRevision > s.OriginalRevision)
           .AssertPassed();

    [Scenario("Namespaces and scope nodes enumerate correctly")]
    [Fact]
    public Task Store_MetadataQueries()
        => Given("store", BuildState)
           .When("querying metadata", QueryMetadata)
           .Then("namespaces include ui", meta => meta.Namespaces.Contains("ui"))
           .And("scope node count", meta => meta.ScopeNodes.Count >= 3)
           .AssertPassed();

    private static StoreState BuildState()
    {
        var store = new InMemoryConfigStore();
        var scope = new ScopeContext(
            Environment: "Development",
            AppName: "Strata.Admin",
            AppVersion: null,
            Dimensions: new Dictionary<string, string>
            {
                ["org"] = "northwind",
                ["site"] = "seattle-hq"
            },
            Tags: Array.Empty<string>());

        var snapshot = store.ReadAsync(scope, "ui", CancellationToken.None).GetAwaiter().GetResult();
        return new StoreState(store, scope, snapshot);
    }

    private static (StoreState State, long OriginalRevision, long NewRevision) Upsert(StoreState state)
    {
        var before = state.Snapshot.Revision;
        state.Store.UpsertAsync(
            new ConfigDocumentWrite(
                Id: null,
                ScopeId: state.Snapshot.Layers.First(l => l.Scope.Kind == "org").Scope.Id,
                Namespace: "ui",
                TemplateRef: "ui.theme",
                Content: System.Text.Json.Nodes.JsonNode.Parse("{ \"theme\": { \"secondary\": \"#123456\" } }")!,
                UpdatedBy: "tests"),
            CancellationToken.None).GetAwaiter().GetResult();

        var afterSnapshot = state.Store.ReadAsync(state.Scope, "ui", CancellationToken.None).GetAwaiter().GetResult();
        return (state with { Snapshot = afterSnapshot }, before, afterSnapshot.Revision);
    }

    private static (IReadOnlyList<string> Namespaces, IReadOnlyList<ScopeNode> ScopeNodes) QueryMetadata(StoreState state)
    {
        var namespaces = state.Store.GetNamespacesAsync(CancellationToken.None).GetAwaiter().GetResult();
        var nodes = state.Store.GetScopeNodesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (namespaces, nodes);
    }
}
