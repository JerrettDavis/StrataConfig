using System.Text.Json.Nodes;
using StrataConfig.Core;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Core;

[Feature("Rule engine tag and expression handling (TinyBDD)")]
public sealed class RuleEngineTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private sealed record RuleContextState(RuleEngine Engine, JsonNode Document, IReadOnlyList<Rule> Rules, RuleContext Context);

    [Scenario("Beta tag triggers override and include retains document")]
    [Fact]
    public Task ApplyRules_WithMatchingTag_OverridesFlag()
        => Given("rule engine with beta override", BuildOverrideState)
           .When("applying rules", Apply)
           .Then("document remains present", node => node is not null)
           .And("welcome flag overridden to true", node => node! ["featureFlags"]!["welcome"]!.GetValue<bool>() == true)
           .AssertPassed();

    [Scenario("Mismatch tag removes document when exclude is applied")]
    [Fact]
    public Task ApplyRules_WithExclude_RemovesDocument()
        => Given("rule engine with exclude rule", BuildExcludeState)
           .When("applying rules", Apply)
           .Then("result is null", node => node is null)
           .AssertPassed();

    [Scenario("Document expressions match and include leaves document untouched")]
    [Fact]
    public Task ApplyRules_WithDocExpression_ReturnsDocument()
        => Given("rule engine with doc expression", BuildDocExpressionState)
           .When("applying rules", Apply)
           .Then("document survives", node => node is not null && node["theme"]!["primary"]!.GetValue<string>() == "#ff4081")
           .AssertPassed();

    private static RuleContextState BuildOverrideState()
    {
        var doc = JsonNode.Parse("""{ "featureFlags": { "welcome": false } }""")!;
        var rules = new List<Rule>
        {
            new(
                Id: Guid.NewGuid(),
                ScopeId: Guid.NewGuid(),
                Expr: "tags.has(\"beta\")",
                Effect: RuleEffect.Override,
                OverrideJson: "{ \"featureFlags\": { \"welcome\": true } }",
                Priority: 10)
        };

        var ctx = new RuleContext(
            new ScopeContext(
                Environment: "Development",
                AppName: "Strata.Admin",
                AppVersion: null,
                Dimensions: new Dictionary<string, string>(),
                Tags: new[] { "beta" }),
            Namespace: "ui");

        return new RuleContextState(new RuleEngine(), doc, rules, ctx);
    }

    private static RuleContextState BuildExcludeState()
    {
        var doc = JsonNode.Parse("""{ "featureFlags": { "welcome": true } }""")!;
        var rules = new List<Rule>
        {
            new(
                Id: Guid.NewGuid(),
                ScopeId: Guid.NewGuid(),
                Expr: "env == \"Production\"",
                Effect: RuleEffect.Exclude,
                OverrideJson: null,
                Priority: 1)
        };

        var ctx = new RuleContext(
            new ScopeContext(
                Environment: "Production",
                AppName: "Strata.Admin",
                AppVersion: null,
                Dimensions: new Dictionary<string, string>(),
                Tags: Array.Empty<string>()),
            Namespace: "ui");

        return new RuleContextState(new RuleEngine(), doc, rules, ctx);
    }

    private static RuleContextState BuildDocExpressionState()
    {
        var doc = JsonNode.Parse("""{ "theme": { "primary": "#ff4081" } }""")!;
        var rules = new List<Rule>
        {
            new(
                Id: Guid.NewGuid(),
                ScopeId: Guid.NewGuid(),
                Expr: "doc.theme.primary == \"#ff4081\"",
                Effect: RuleEffect.Include,
                OverrideJson: null,
                Priority: 1)
        };

        var ctx = new RuleContext(
            new ScopeContext(
                Environment: "Development",
                AppName: "Strata.Admin",
                AppVersion: null,
                Dimensions: new Dictionary<string, string>(),
                Tags: Array.Empty<string>()),
            Namespace: "ui");

        return new RuleContextState(new RuleEngine(), doc, rules, ctx);
    }

    private static JsonNode? Apply(RuleContextState state)
        => state.Engine.ApplyRules(state.Document, state.Rules, state.Context);
}
