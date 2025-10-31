using System.Text.Json.Nodes;

namespace StrataConfig.Web.Services;

public sealed record TemplateDto(
    string Id,
    int SchemaVersion,
    string? JsonSchema,
    string? UIMetadata);

public sealed record ScopeNodeDto(
    Guid Id,
    string Key,
    string Kind,
    string Name,
    Guid? ParentId,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<ScopeNodeDto> Children);

public sealed record ConfigDocumentDto(
    Guid Id,
    Guid ScopeId,
    string TemplateRef,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy,
    JsonNode Content);

public sealed record ConfigLayerDto(
    int PrecedenceOrder,
    ScopeNodeDto Scope,
    IReadOnlyList<ConfigDocumentDto> Documents);

public sealed record NamespaceDocumentsDto(
    string Namespace,
    long Revision,
    IReadOnlyList<ConfigLayerDto> Layers);

public sealed record ResolveScopeContext(
    string Environment,
    string AppName,
    string? AppVersion,
    IDictionary<string, string> Dimensions,
    IReadOnlyCollection<string> Tags);

public sealed record ResolveRequestDto(
    ResolveScopeContext Scope,
    string Namespace);

public sealed record DiffChangedEntryDto(string Key, string? From, string? To);
public sealed record DiffResponseDto(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<DiffChangedEntryDto> Changed);

public sealed record CloneDocumentRequestDto(Guid SourceId, Guid DestinationScopeId, string? UpdatedBy);

public sealed record ImportDocumentRequestDto(
    Guid? Id,
    Guid ScopeId,
    string TemplateRef,
    JsonNode Content,
    string? UpdatedBy);

public sealed record ImportRequestDto(string Namespace, IReadOnlyList<ImportDocumentRequestDto> Documents);
