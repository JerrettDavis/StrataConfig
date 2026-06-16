using System.Text.Json.Nodes;
using StrataConfig.Core;
using TinyBDD;
using TinyBDD.Xunit;

namespace StrataConfig.Tests.Core;

[Feature("Template validation errors (TinyBDD)")]
public sealed partial class TemplateValidatorTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    [Scenario("Invalid document raises TemplateValidationException with errors populated")]
    [Fact]
    public Task TemplateValidation_InvalidDocument()
        => Given("validator and template", CreateValidator)
           .When("validating bad document", ValidateInvalid)
           .Then("exception thrown", result => result.Exception is TemplateValidationException)
           .And("errors collection populated", result => result.Exception is TemplateValidationException tve && tve.Errors.Count > 0)
           .AssertPassed();

    private static ValidatorContext CreateValidator()
    {
        var validator = new NJsonSchemaTemplateValidator();
        var template = new Template("sample", 1, "{ \"type\": \"object\", \"required\": [\"name\"] }", null);
        return new ValidatorContext(validator, template);
    }

    private static ValidationResult ValidateInvalid(ValidatorContext ctx)
    {
        try
        {
            ctx.Validator.Validate(JsonNode.Parse("{ }")!, ctx.Template);
            return new ValidationResult(ctx, null);
        }
        catch (TemplateValidationException ex)
        {
            return new ValidationResult(ctx, ex);
        }
    }

    private sealed record ValidatorContext(NJsonSchemaTemplateValidator Validator, Template Template);

    private sealed record ValidationResult(ValidatorContext Context, TemplateValidationException? Exception);
}
