using System.Linq;
using StrataConfig.Core;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Core;

[Feature("Scope model utilities (TinyBDD)")]
public sealed class ScopeModelsTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    [Scenario("ScopeGraph builds path and children collections")]
    [Fact]
    public Task ScopeGraph_PathAndChildren()
        => Given("a small scope graph", BuildGraph)
           .Then("root children count", g => g.Graph.GetChildren(g.Root.Id).Count == 1)
           .And("site path includes root", g => g.Graph.BuildPath(g.Site.Id).Segments.First().Id == g.Root.Id)
           .And("site leaf equals node", g => g.Graph.BuildPath(g.Site.Id).Leaf?.Id == g.Site.Id)
           .And("nodes property exposes entries", g => g.Graph.Nodes.Count == 3)
           .And("try get fails for unknown", g => !g.Graph.TryGet(Guid.NewGuid(), out _))
           .And("try get succeeds for org", g => g.Graph.TryGet(g.Org.Id, out var node) && node.Id == g.Org.Id)
           .AssertPassed();

    [Scenario("Scope precedence orders kinds predictably")]
    [Fact]
    public Task ScopePrecedence_Sorting()
        => Given("unordered nodes", BuildNodes)
           .When("ordering", nodes => ScopePrecedence.Order(nodes.Input))
           .Then("global first", ordered => ordered.First().Kind == "global")
           .And("environment last", ordered => ordered.Last().Kind == "environment")
           .AssertPassed();

    [Scenario("ScopeContext exposes nullable AppVersion")]
    [Fact]
    public Task ScopeContext_AppVersionAccessor()
        => Given("scope context with version", () => new ScopeContext("Prod", "App", "1.2.3", new Dictionary<string, string>(), []))
           .Then("app version returns value", ctx => ctx.AppVersion == "1.2.3")
           .AssertPassed();

    private static ScopeGraphContext BuildGraph()
    {
        var root = ScopeNode.Create(Guid.NewGuid(), "global", "global", "Global");
        var org = ScopeNode.Create(Guid.NewGuid(), "org:northwind", "org", "Northwind", parentId: root.Id);
        var site = ScopeNode.Create(Guid.NewGuid(), "site:seattle", "site", "Seattle", parentId: org.Id);
        var graph = new ScopeGraph([root, org, site]);
        return new ScopeGraphContext(graph, root, org, site);
    }

    private static ScopePrecedenceContext BuildNodes()
    {
        var nodes = new[]
        {
            ScopeNode.Create(Guid.NewGuid(), "site", "site", "Site", parentId: null),
            ScopeNode.Create(Guid.NewGuid(), "global", "global", "Global", parentId: null),
            ScopeNode.Create(Guid.NewGuid(), "environment", "environment", "Prod", parentId: null)
        };
        return new ScopePrecedenceContext(nodes);
    }

    private sealed record ScopeGraphContext(ScopeGraph Graph, ScopeNode Root, ScopeNode Org, ScopeNode Site);

    private sealed record ScopePrecedenceContext(IReadOnlyList<ScopeNode> Input);
}
