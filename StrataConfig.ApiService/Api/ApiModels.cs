using System.Linq;
using System.Text.Json.Nodes;
using StrataConfig.Core;

namespace StrataConfig.ApiService.Api;

public sealed record TemplateResponse(
    string Id,
    int SchemaVersion,
    string? JsonSchema,
    string? UIMetadata);

public sealed record ScopeNodeResponse(
    Guid Id,
    string Key,
    string Kind,
    string Name,
    Guid? ParentId,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<ScopeNodeResponse> Children);

public sealed record ConfigDocumentResponse(
    Guid Id,
    Guid ScopeId,
    string TemplateRef,
    int Version,
    DateTimeOffset UpdatedUtc,
    string UpdatedBy,
    JsonNode Content);

public sealed record ConfigLayerResponse(
    int PrecedenceOrder,
    ScopeNodeResponse Scope,
    IReadOnlyList<ConfigDocumentResponse> Documents);

public sealed record NamespaceDocumentsResponse(
    string Namespace,
    long Revision,
    IReadOnlyList<ConfigLayerResponse> Layers);

internal static class ApiModelMapper
{
    public static TemplateResponse ToResponse(this Template template)
        => new(template.Id, template.SchemaVersion, template.JsonSchema, template.UIMetadata);

    public static ScopeNodeResponse ToFlatResponse(this ScopeNode node)
        => node.ToResponse(Array.Empty<ScopeNodeResponse>());

    public static ScopeNodeResponse ToResponse(this ScopeNode node, IReadOnlyList<ScopeNodeResponse> children)
        => new(node.Id, node.Key, node.Kind, node.Name, node.ParentId, node.Labels, children);

    public static ConfigDocumentResponse ToResponse(this ConfigDocument doc)
    {
        var content = JsonNode.Parse(doc.ContentJson) ?? new JsonObject();
        return new ConfigDocumentResponse(
            doc.Id,
            doc.ScopeId,
            doc.TemplateRef,
            doc.Version,
            doc.UpdatedUtc,
            doc.UpdatedBy,
            content);
    }

    public static NamespaceDocumentsResponse ToResponse(this StoreSnapshot snapshot, string ns)
    {
        var layers = snapshot.Layers
            .OrderBy(l => l.PrecedenceOrder)
            .Select(layer => new ConfigLayerResponse(
                layer.PrecedenceOrder,
                layer.Scope.ToFlatResponse(),
                layer.Documents.Select(d => d.ToResponse()).ToList()))
            .ToList();

        return new NamespaceDocumentsResponse(ns, snapshot.Revision, layers);
    }

    public static IReadOnlyList<ScopeNodeResponse> BuildScopeTree(IEnumerable<ScopeNode> nodes)
    {
        var orderedNodes = nodes
            .OrderBy(n => ScopePrecedence.GetOrder(n.Kind))
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var childrenMap = orderedNodes.ToDictionary(n => n.Id, _ => new List<ScopeNode>(), EqualityComparer<Guid>.Default);
        foreach (var node in orderedNodes)
        {
            if (node.ParentId is Guid parent && childrenMap.TryGetValue(parent, out var list))
            {
                list.Add(node);
            }
        }

        IReadOnlyList<ScopeNodeResponse> Recurse(ScopeNode parent)
        {
            var children = childrenMap[parent.Id]
                .OrderBy(n => ScopePrecedence.GetOrder(n.Kind))
                .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => child.ToResponse(Recurse(child)))
                .ToList();

            return children;
        }

        var roots = orderedNodes.Where(n => n.ParentId is null).ToList();
        return roots
            .Select(root => root.ToResponse(Recurse(root)))
            .ToList();
    }
}
