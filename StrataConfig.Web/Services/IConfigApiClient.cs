using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace StrataConfig.Web.Services;

public interface IConfigApiClient
{
    Task<IReadOnlyList<TemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScopeNodeDto>> GetScopeTreeAsync(CancellationToken cancellationToken = default);
    Task<NamespaceDocumentsDto?> GetNamespaceDocumentsAsync(
        string ns,
        IDictionary<string, string> dimensions,
        string environment,
        string appName,
        CancellationToken cancellationToken = default);
    Task<IDictionary<string, string>> ResolveAsync(
        string ns,
        ResolveScopeContext context,
        CancellationToken cancellationToken = default);

    // Document management
    Task<IReadOnlyList<ConfigDocumentDto>> ListDocumentsAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default);
    Task<ConfigDocumentDto?> GetDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConfigDocumentDto> UpsertDocumentAsync(Guid? id, Guid scopeId, string ns, string templateRef, JsonNode content, string updatedBy, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ConfigDocumentDto> CloneDocumentAsync(Guid sourceId, Guid destinationScopeId, string updatedBy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConfigDocumentDto>> ExportAsync(string ns, Guid? scopeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConfigDocumentDto>> ImportAsync(string ns, IReadOnlyList<ImportDocumentRequestDto> documents, CancellationToken cancellationToken = default);
    Task<DiffResponseDto?> DiffAsync(JsonNode? aContent, Guid? aId, JsonNode? bContent, Guid? bId, CancellationToken cancellationToken = default);

    // Scopes
    Task<ScopeNodeDto> CreateScopeAsync(string kind, string name, Guid? parentId, IDictionary<string, string>? labels = null, CancellationToken cancellationToken = default);
    Task<string> CreateNamespaceAsync(string name, CancellationToken cancellationToken = default);
}
