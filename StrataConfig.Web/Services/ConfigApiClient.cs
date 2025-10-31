using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;

namespace StrataConfig.Web.Services;

public sealed class ConfigApiClient : IConfigApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ConfigApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<TemplateDto>>(
            "/api/templates",
            SerializerOptions,
            cancellationToken);
        return items ?? new List<TemplateDto>();
    }

    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<string>>(
            "/api/namespaces",
            SerializerOptions,
            cancellationToken);
        return items ?? new List<string>();
    }

    public async Task<IReadOnlyList<ScopeNodeDto>> GetScopeTreeAsync(CancellationToken cancellationToken = default)
    {
        var items = await _httpClient.GetFromJsonAsync<List<ScopeNodeDto>>(
            "/api/scopes",
            SerializerOptions,
            cancellationToken);
        return items ?? new List<ScopeNodeDto>();
    }

    public async Task<NamespaceDocumentsDto?> GetNamespaceDocumentsAsync(
        string ns,
        IDictionary<string, string> dimensions,
        string environment,
        string appName,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["environment"] = environment,
            ["app"] = appName
        };

        foreach (var kvp in dimensions)
        {
            query[kvp.Key] = kvp.Value;
        }

        var uri = QueryHelpers.AddQueryString($"/api/namespaces/{ns}/documents", query);
        return await _httpClient.GetFromJsonAsync<NamespaceDocumentsDto>(
            uri,
            SerializerOptions,
            cancellationToken);
    }

    public async Task<IDictionary<string, string>> ResolveAsync(
        string ns,
        ResolveScopeContext context,
        CancellationToken cancellationToken = default)
    {
        var payload = new ResolveRequestDto(context, ns);
        var response = await _httpClient.PostAsJsonAsync(
            "/api/config/resolve",
            payload,
            SerializerOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var resolved = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(
            SerializerOptions,
            cancellationToken);
        return resolved ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ConfigDocumentDto>> ListDocumentsAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ns"] = ns,
            ["scopeId"] = scopeId?.ToString()
        };
        var uri = QueryHelpers.AddQueryString("/api/documents", query);
        var items = await _httpClient.GetFromJsonAsync<List<ConfigDocumentDto>>(uri, SerializerOptions, cancellationToken);
        return items ?? new List<ConfigDocumentDto>();
    }

    public async Task<ConfigDocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        => await _httpClient.GetFromJsonAsync<ConfigDocumentDto>($"/api/documents/{id}", SerializerOptions, cancellationToken);

    public async Task<ConfigDocumentDto> UpsertDocumentAsync(Guid? id, Guid scopeId, string ns, string templateRef, JsonNode content, string updatedBy, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Id = id,
            ScopeId = scopeId,
            Namespace = ns,
            TemplateRef = templateRef,
            Content = content,
            UpdatedBy = updatedBy
        };
        var response = await _httpClient.PostAsJsonAsync("/api/documents", payload, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<ConfigDocumentDto>(SerializerOptions, cancellationToken);
        return saved!;
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/documents/{id}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<ConfigDocumentDto> CloneDocumentAsync(Guid sourceId, Guid destinationScopeId, string updatedBy, CancellationToken cancellationToken = default)
    {
        var payload = new CloneDocumentRequestDto(sourceId, destinationScopeId, updatedBy);
        var response = await _httpClient.PostAsJsonAsync("/api/documents/clone", payload, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ConfigDocumentDto>(SerializerOptions, cancellationToken))!;
    }

    public async Task<IReadOnlyList<ConfigDocumentDto>> ExportAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["scopeId"] = scopeId?.ToString()
        };
        var uri = QueryHelpers.AddQueryString($"/api/namespaces/{ns}/export", query);
        var items = await _httpClient.GetFromJsonAsync<List<ConfigDocumentDto>>(uri, SerializerOptions, cancellationToken);
        return items ?? new List<ConfigDocumentDto>();
    }

    public async Task<IReadOnlyList<ConfigDocumentDto>> ImportAsync(string ns, IReadOnlyList<ImportDocumentRequestDto> documents, CancellationToken cancellationToken = default)
    {
        var payload = new ImportRequestDto(ns, documents);
        var response = await _httpClient.PostAsJsonAsync($"/api/namespaces/{ns}/import", payload, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ConfigDocumentDto>>(SerializerOptions, cancellationToken)) ?? new List<ConfigDocumentDto>();
    }

    public async Task<DiffResponseDto?> DiffAsync(JsonNode? aContent, Guid? aId, JsonNode? bContent, Guid? bId, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            A = new { Id = aId, Content = aContent },
            B = new { Id = bId, Content = bContent }
        };
        var response = await _httpClient.PostAsJsonAsync("/api/documents/diff", payload, SerializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiffResponseDto>(SerializerOptions, cancellationToken);
    }
}
