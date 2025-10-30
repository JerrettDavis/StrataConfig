namespace StrataConfig.Core;

/// <summary>
/// Represents a node within the configuration scope hierarchy (e.g. org, division, site).
/// </summary>
public sealed record ScopeNode(
    Guid Id,
    string Key,
    string Kind,
    string Name,
    Guid? ParentId,
    IReadOnlyDictionary<string, string> Labels)
{
    public static ScopeNode Create(
        Guid id,
        string key,
        string kind,
        string name,
        Guid? parentId = null,
        IReadOnlyDictionary<string, string>? labels = null)
        => new(
            id,
            key,
            kind,
            name,
            parentId,
            labels ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// Immutable representation of a scope path from the root to a target node.
/// </summary>
public sealed class ScopePath
{
    private readonly IReadOnlyList<ScopeNode> _segments;

    public ScopePath(IEnumerable<ScopeNode> segments)
    {
        _segments = segments?.ToArray() ?? Array.Empty<ScopeNode>();
    }

    public IReadOnlyList<ScopeNode> Segments => _segments;

    public ScopeNode? FindByKind(string kind)
        => _segments.FirstOrDefault(n => string.Equals(n.Kind, kind, StringComparison.OrdinalIgnoreCase));

    public ScopeNode? Leaf => _segments.Count == 0 ? null : _segments[^1];
}

/// <summary>
/// Maintains an in-memory graph of scope nodes and offers traversal helpers.
/// </summary>
public sealed class ScopeGraph
{
    private readonly Dictionary<Guid, ScopeNode> _nodes = new();
    private readonly Dictionary<Guid, List<ScopeNode>> _children = new();

    public ScopeGraph(IEnumerable<ScopeNode> nodes)
    {
        foreach (var node in nodes)
        {
            Add(node);
        }
    }

    public IReadOnlyDictionary<Guid, ScopeNode> Nodes => _nodes;

    public void Add(ScopeNode node)
    {
        _nodes[node.Id] = node;

        if (node.ParentId is { } parentId)
        {
            if (!_children.TryGetValue(parentId, out var list))
            {
                list = new List<ScopeNode>();
                _children[parentId] = list;
            }

            list.Add(node);
        }
    }

    public bool TryGet(Guid id, out ScopeNode node)
    {
        var found = _nodes.TryGetValue(id, out var result);
        node = result!;
        return found;
    }

    public ScopePath BuildPath(Guid nodeId)
    {
        var chain = new List<ScopeNode>();
        if (!_nodes.TryGetValue(nodeId, out var current))
        {
            return new ScopePath(chain);
        }

        chain.Add(current);
        while (current.ParentId is { } parentId && _nodes.TryGetValue(parentId, out current))
        {
            chain.Add(current);
        }

        chain.Reverse();
        return new ScopePath(chain);
    }

    public IReadOnlyList<ScopeNode> GetChildren(Guid parentId)
        => _children.TryGetValue(parentId, out var list)
            ? list.AsReadOnly()
            : Array.Empty<ScopeNode>();
}

/// <summary>
/// Utility for ordering scope layers according to the default precedence.
/// </summary>
public static class ScopePrecedence
{
    private static readonly string[] OrderedKinds =
    [
        "global",
        "org",
        "division",
        "site",
        "device",
        "user",
        "app",
        "environment",
        "adhoc"
    ];

    private static readonly Dictionary<string, int> OrderByKind =
        OrderedKinds
            .Select((kind, index) => new { kind, index })
            .ToDictionary(
                x => x.kind,
                x => x.index,
                StringComparer.OrdinalIgnoreCase);

    public static int GetOrder(string kind)
        => OrderByKind.TryGetValue(kind, out var order) ? order : int.MaxValue;

    public static IEnumerable<ScopeNode> Order(IEnumerable<ScopeNode> nodes)
        => nodes
            .OrderBy(n => GetOrder(n.Kind))
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
}
