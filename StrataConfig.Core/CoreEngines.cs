using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Linq;
using NJsonSchema;
using PatternKit.Behavioral.Strategy;

namespace StrataConfig.Core;

internal static class JsonNodeMerge
{
    public static void DeepMerge(JsonObject target, JsonNode source)
    {
        if (source is not JsonObject srcObj) return;

        foreach (var kvp in srcObj)
        {
            var key = kvp.Key;
            var srcChild = kvp.Value;
            if (srcChild is null)
            {
                target[key] = null;
                continue;
            }

            if (target[key] is JsonObject tgtObj && srcChild is JsonObject srcChildObj)
            {
                DeepMerge(tgtObj, srcChildObj);
            }
            else
            {
                target[key] = srcChild.DeepClone();
            }
        }
    }
}

public sealed class MergeEngine : IMergeEngine
{
    public JsonNode Merge(IReadOnlyList<JsonNode> orderedLayers)
    {
        var result = new JsonObject();
        foreach (var layer in orderedLayers)
        {
            JsonNodeMerge.DeepMerge(result, layer);
        }

        return result;
    }

    public IDictionary<string, string> Flatten(JsonNode node)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenInto(dict, node, prefix: string.Empty);
        return dict;
    }

    private static void FlattenInto(IDictionary<string, string> dest, JsonNode node, string prefix)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    if (kvp.Value is null) continue;
                    var next = string.IsNullOrEmpty(prefix) ? kvp.Key : prefix + "." + kvp.Key;
                    FlattenInto(dest, kvp.Value, next);
                }

                break;
            case JsonArray arr:
                dest[prefix] = arr.ToJsonString();
                break;
            default:
                dest[prefix] = node.ToJsonString().Trim('"');
                break;
        }
    }
}

public sealed class RuleEngine : IRuleEngine
{
    private static readonly Strategy<RuleProcessingContext, RuleProcessingContext> RuleStrategy = BuildStrategy();

    public JsonNode? ApplyRules(JsonNode candidate, IEnumerable<Rule> rules, RuleContext ctx)
    {
        var orderedRules = rules?
            .OrderBy(r => r.Priority)
            .ToList();

        if (orderedRules is null || orderedRules.Count == 0)
        {
            return candidate;
        }

        var working = candidate.DeepClone();

        foreach (var rule in orderedRules)
        {
            if (!IsMatch(rule, ctx, working))
            {
                continue;
            }

            var context = new RuleProcessingContext(rule, working, false);
            context = RuleStrategy.Execute(context);

            if (context.Excluded)
            {
                return null;
            }

            working = context.Working ?? working;
        }

        return working;
    }

    private static Strategy<RuleProcessingContext, RuleProcessingContext> BuildStrategy()
        => Strategy<RuleProcessingContext, RuleProcessingContext>.Create()
            .When(static (in RuleProcessingContext ctx) => ctx.Rule.Effect == RuleEffect.Exclude)
                .Then(static (in RuleProcessingContext ctx) => ctx with { Excluded = true, Working = null })
            .When(static (in RuleProcessingContext ctx) => ctx.Rule.Effect == RuleEffect.Override && ctx.Rule.OverrideJson is not null)
                .Then(static (in RuleProcessingContext ctx) => ctx with { Working = ApplyOverride(ctx) })
            .Default(static (in RuleProcessingContext ctx) => ctx)
            .Build();

    private static JsonNode? ApplyOverride(RuleProcessingContext ctx)
    {
        if (ctx.Rule.OverrideJson is null || ctx.Working is null)
        {
            return ctx.Working;
        }

        var overrideNode = JsonNode.Parse(ctx.Rule.OverrideJson);
        if (overrideNode is JsonObject overrideObj && ctx.Working is JsonObject workingObj)
        {
            var clone = (JsonObject)workingObj.DeepClone();
            JsonNodeMerge.DeepMerge(clone, overrideObj);
            return clone;
        }

        return overrideNode?.DeepClone();
    }

    private readonly record struct RuleProcessingContext(Rule Rule, JsonNode? Working, bool Excluded);

    private static bool IsMatch(Rule rule, RuleContext ctx, JsonNode current)
    {
        if (string.IsNullOrWhiteSpace(rule.Expr))
        {
            return true;
        }

        var expr = rule.Expr.Trim();

        if (expr.StartsWith("tags.has(", StringComparison.OrdinalIgnoreCase))
        {
            var raw = ExtractBetween(expr, "tags.has(", ")");
            var value = Unquote(raw);
            return ctx.Scope.Tags.Any(tag => string.Equals(tag, value, StringComparison.OrdinalIgnoreCase));
        }

        if (expr.Contains("==", StringComparison.Ordinal))
        {
            var parts = expr.Split("==", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                return false;
            }

            var left = parts[0];
            var right = Unquote(parts[1]);

            if (left.StartsWith("scope.labels[", StringComparison.OrdinalIgnoreCase))
            {
                var key = ExtractBetween(left, "[", "]");
                key = key.Trim('"');
                return ctx.Scope.Dimensions.TryGetValue(key, out var actual) &&
                       string.Equals(actual, right, StringComparison.OrdinalIgnoreCase);
            }

            if (left.Equals("env", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(ctx.Scope.Environment, right, StringComparison.OrdinalIgnoreCase);
            }

            if (left.Equals("app.name", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(ctx.Scope.AppName, right, StringComparison.OrdinalIgnoreCase);
            }

            if (left.Equals("app.version", StringComparison.OrdinalIgnoreCase) && ctx.Scope.AppVersion is not null)
            {
                return string.Equals(ctx.Scope.AppVersion, right, StringComparison.OrdinalIgnoreCase);
            }

            if (left.StartsWith("doc.", StringComparison.OrdinalIgnoreCase))
            {
                var path = left[4..];
                if (TryResolveDocumentPath(current, path, out var value))
                {
                    return string.Equals(value, right, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return false;
    }

    private static bool TryResolveDocumentPath(JsonNode node, string path, out string value)
    {
        value = string.Empty;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        JsonNode? current = node;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj)
            {
                current = obj[segment];
            }
            else
            {
                current = null;
            }

            if (current is null)
            {
                return false;
            }
        }

        value = current switch
        {
            JsonValue jv => jv.ToJsonString().Trim('"'),
            JsonArray arr => arr.ToJsonString(),
            JsonObject obj => obj.ToJsonString(),
            _ => current.ToJsonString()
        };

        return true;
    }

    private static string ExtractBetween(string source, string startToken, string endToken)
    {
        var start = source.IndexOf(startToken, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += startToken.Length;
        var end = source.IndexOf(endToken, start, StringComparison.Ordinal);
        if (end < 0 || end <= start)
        {
            return string.Empty;
        }

        return source[start..end];
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }
}

public sealed class NJsonSchemaTemplateValidator : ITemplateValidator
{
    private readonly ConcurrentDictionary<(string id, int version), JsonSchema> _schemaCache = new();

    public void Validate(JsonNode doc, Template template)
    {
        if (string.IsNullOrWhiteSpace(template.JsonSchema))
        {
            return;
        }

        var schema = _schemaCache.GetOrAdd(
            (template.Id, template.SchemaVersion),
            _ => JsonSchema.FromJsonAsync(template.JsonSchema!).GetAwaiter().GetResult());

        var errors = schema.Validate(doc.ToJsonString());
        if (errors.Count > 0)
        {
            var messages = errors.Select(e => e.ToString()).ToList();
            throw new TemplateValidationException(
                $"Document did not match template '{template.Id}@{template.SchemaVersion}'",
                messages);
        }
    }
}
