using System.Text.Json.Nodes;
using StrataConfig.Core;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Core;

[Feature("Merge engine layer precedence (TinyBDD)")]
public sealed partial class MergeEngineTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private sealed record MergeContext(MergeEngine Engine, IReadOnlyList<JsonNode> Layers);
    private sealed record MergeResult(MergeEngine Engine, JsonNode Node);

    [Scenario("Higher precedence layer overrides lower layers and flattening yields dotted keys")]
    [Fact]
    public Task Merge_FavorsHigherPrecedence()
        => Given("merge engine with two layers", CreateContext)
           .When("merging ordered layers", Merge)
           .Then("primary color comes from second layer", r => r.Engine.Flatten(r.Node)["theme.primary"] == "#ff4081")
           .And("font comes from base layer", r => r.Engine.Flatten(r.Node)["theme.font"] == "Inter")
           .And("feature flag flattened to bool text", r => r.Engine.Flatten(r.Node)["featureFlags.welcome"] == "false")
           .AssertPassed();

    [Scenario("Array values are serialized during flattening")]
    [Fact]
    public Task Merge_ArraySerialization()
        => Given("engine with array layers", CreateArrayContext)
           .When("flattening", ctx => ctx.Engine.Flatten(ctx.Node))
           .Then("array serialized", dict => dict["items"] == "[\"a\",\"b\"]")
           .AssertPassed();

    private static MergeContext CreateContext()
    {
        var baseLayer = JsonNode.Parse("""
        {
            "theme": { "primary": "#1166dd", "font": "Inter" },
            "featureFlags": { "welcome": true }
        }
        """)!;

        var overrideLayer = JsonNode.Parse("""
        {
            "theme": { "primary": "#ff4081" },
            "featureFlags": { "welcome": false }
        }
        """)!;

        return new MergeContext(new MergeEngine(), [baseLayer, overrideLayer]);
    }

    private static MergeResult Merge(MergeContext ctx)
        => new(ctx.Engine, ctx.Engine.Merge(ctx.Layers));

    private static MergeResult CreateArrayContext()
    {
        var engine = new MergeEngine();
        var first = JsonNode.Parse("{ \"items\": [\"a\"] }")!;
        var second = JsonNode.Parse("{ \"items\": [\"a\", \"b\"] }")!;
        var merged = engine.Merge([first, second]);
        return new MergeResult(engine, merged);
    }
}
