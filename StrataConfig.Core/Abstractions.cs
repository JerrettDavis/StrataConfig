using System.Text.Json.Nodes;

namespace StrataConfig.Core;

public enum RuleEffect
{
    Include,
    Exclude,
    Override
}

public sealed record ScopeContext(
    string Environment,
    string AppName,
    string? AppVersion,
    IReadOnlyDictionary<string, string> Dimensions,
    IReadOnlyCollection<string> Tags);

public sealed record ResolveRequest(
    ScopeContext Scope,
    string Namespace);

public sealed record Template(
    string Id,
    int SchemaVersion,
    string? JsonSchema,
    string? UIMetadata);

public sealed record ConfigDocument(
    Guid Id,
    Guid ScopeId,
    string Namespace,
    string TemplateRef,
    string ContentJson,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy);

public sealed record Rule(
    Guid Id,
    Guid ScopeId,
    string Expr,
    RuleEffect Effect,
    string? OverrideJson,
    int Priority);

public sealed record ConfigLayer(
    ScopeNode Scope,
    IReadOnlyList<ConfigDocument> Documents,
    int PrecedenceOrder);

public sealed record StoreSnapshot(
    Guid RootScopeId,
    IReadOnlyList<ConfigLayer> Layers,
    IReadOnlyList<Template> Templates,
    long Revision);

public sealed record ConfigDocumentWrite(
    Guid? Id,
    Guid ScopeId,
    string Namespace,
    string TemplateRef,
    JsonNode Content,
    string UpdatedBy);

public interface IConfigStore
{
    Task<StoreSnapshot> ReadAsync(ScopeContext scope, string @namespace, CancellationToken ct);
    Task<ConfigDocument> UpsertAsync(ConfigDocumentWrite document, CancellationToken ct);
    Task<IReadOnlyList<Template>> GetTemplatesAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct);
    Task<IReadOnlyList<ScopeNode>> GetScopeNodesAsync(CancellationToken ct);
    Task<IReadOnlyList<Rule>> GetRulesAsync(Guid scopeId, CancellationToken ct);
}

public interface IMergeEngine
{
    JsonNode Merge(IReadOnlyList<JsonNode> orderedLayers);
    IDictionary<string, string> Flatten(JsonNode node);
}

public sealed record RuleContext(
    ScopeContext Scope,
    string Namespace);

public interface IRuleEngine
{
    JsonNode? ApplyRules(JsonNode candidate, IEnumerable<Rule> rules, RuleContext ctx);
}

public interface ITemplateValidator
{
    void Validate(JsonNode doc, Template template);
}

public sealed class TemplateValidationException : Exception
{
    public TemplateValidationException(string message, IReadOnlyList<string> errors)
        : base(message)
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
