using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace StrataConfig.Web.Services;

public sealed class ConfigApiClient
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
}
