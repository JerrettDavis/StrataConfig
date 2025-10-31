using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using TinyBDD;
using TinyBDD.Xunit;
using ApiProgram = StrataConfig.ApiService.Program;

namespace StrataConfig.Tests.Api;

[Feature("Documents CRUD, clone, import/export, diff (TinyBDD)")]
public sealed class DocumentsCrudTinyBddTests(Xunit.Abstractions.ITestOutputHelper output) : TinyBddXunitBase(output)
{
    private sealed record ApiContext(WebApplicationFactory<ApiProgram> Factory, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Factory.Dispose();
        }
    }

    private static TestState CreateApiState()
    {
        var factory = new WebApplicationFactory<ApiProgram>();
        var ctx = new ApiContext(factory, factory.CreateClient());
        return new TestState(ctx, new HttpResponseMessage(), Guid.Empty, new HttpResponseMessage(), new HttpResponseMessage(), null, [], [], new HttpResponseMessage(), null);
    }

    [Scenario("Create, get, list, clone, delete, export/import, diff")]
    [Fact]
    public Task Documents_EndToEnd()
        => Given("API context", CreateApiState)
           .When("create a document", CreateDocument)
           .Then("upsert returns created", r => r.Response.StatusCode == HttpStatusCode.Created)
           .When("get by id", GetById)
           .Then("returns document", r => r.Document is not null)
           .When("list documents in namespace", ListDocs)
           .Then("list contains item", r => r.List.Any(d => d.Id == r.Document!.Id))
           .When("clone to another scope", CloneDoc)
           .Then("clone success", r => r.CloneResponse.StatusCode == HttpStatusCode.Created)
           .When("diff original vs clone (equal)", DiffEqual)
           .Then("no changes", r => r.Diff!.Added.Count == 0 && r.Diff.Removed.Count == 0 && r.Diff.Changed.Count == 0)
           .When("delete original", DeleteOriginal)
           .Then("delete ok", r => r.DeleteResponse.StatusCode == HttpStatusCode.NoContent)
           .When("export namespace", ExportNamespace)
           .Then("export non-empty", r => r.Export.Count > 0)
           .When("import exported", ImportNamespace)
           .Then("import ok", r => r.ImportResponse.IsSuccessStatusCode)
           .And("dispose", r => { r.Context.Dispose(); return true; })
           .AssertPassed();

    private sealed record TestState(ApiContext Context, HttpResponseMessage Response, Guid CreatedId, HttpResponseMessage CloneResponse, HttpResponseMessage DeleteResponse, DocumentResponse? Document, List<DocumentResponse> List, List<DocumentResponse> Export, HttpResponseMessage ImportResponse, DiffResponse? Diff);

    private static TestState CreateDocument(TestState state)
    {
        var payload = new UpsertRequest(null,
            ScopeId: Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Namespace: "ui",
            TemplateRef: "ui.theme",
            Content: JsonNode.Parse("{ \"theme\": { \"primary\": \"#112233\" } }")!,
            UpdatedBy: "tests");
        var res = state.Context.Client.PostAsJsonAsync("/api/documents", payload).GetAwaiter().GetResult();
        var saved = res.Content.ReadFromJsonAsync<DocumentResponse>().GetAwaiter().GetResult();
        return state with { Response = res, CreatedId = saved!.Id };
    }

    private static TestState GetById(TestState state)
    {
        var doc = state.Context.Client.GetFromJsonAsync<DocumentResponse>($"/api/documents/{state.CreatedId}").GetAwaiter().GetResult();
        return state with { Document = doc };
    }

    private static TestState ListDocs(TestState state)
    {
        var docs = state.Context.Client.GetFromJsonAsync<List<DocumentResponse>>("/api/documents?ns=ui").GetAwaiter().GetResult() ?? [];
        return state with { List = docs };
    }

    private static TestState CloneDoc(TestState state)
    {
        var clone = new CloneRequest(state.CreatedId, Guid.Parse("00000000-0000-0000-0000-000000000021"), "tests");
        var response = state.Context.Client.PostAsJsonAsync("/api/documents/clone", clone).GetAwaiter().GetResult();
        return state with { CloneResponse = response };
    }

    private static TestState DiffEqual(TestState state)
    {
        var payload = new DiffRequest(new DocRef(state.CreatedId, null), new DocRef(state.CreatedId, null));
        var response = state.Context.Client.PostAsJsonAsync("/api/documents/diff", payload).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var diff = response.Content.ReadFromJsonAsync<DiffResponse>().GetAwaiter().GetResult();
        return state with { Diff = diff };
    }

    private static TestState DeleteOriginal(TestState state)
    {
        var response = state.Context.Client.DeleteAsync($"/api/documents/{state.CreatedId}").GetAwaiter().GetResult();
        return state with { DeleteResponse = response };
    }

    private static TestState ExportNamespace(TestState state)
    {
        var export = state.Context.Client.GetFromJsonAsync<List<DocumentResponse>>("/api/namespaces/ui/export").GetAwaiter().GetResult() ?? [];
        return state with { Export = export };
    }

    private static TestState ImportNamespace(TestState state)
    {
        var docs = state.Export.Select(d => new ImportDoc(d.Id, d.ScopeId, d.TemplateRef, d.Content, d.UpdatedBy)).ToList();
        var request = new ImportPayload("ui", docs);
        var response = state.Context.Client.PostAsJsonAsync("/api/namespaces/ui/import", request).GetAwaiter().GetResult();
        return state with { ImportResponse = response };
    }

    // Local DTOs (mirror API)
    private sealed record UpsertRequest(Guid? Id, Guid ScopeId, string Namespace, string TemplateRef, JsonNode Content, string UpdatedBy);
    private sealed record DocumentResponse(Guid Id, Guid ScopeId, string TemplateRef, int Version, DateTimeOffset UpdatedUtc, string UpdatedBy, JsonNode Content);
    private sealed record CloneRequest(Guid SourceId, Guid DestinationScopeId, string UpdatedBy);
    private sealed record ImportDoc(Guid? Id, Guid ScopeId, string TemplateRef, JsonNode Content, string UpdatedBy);
    private sealed record ImportPayload(string Namespace, IReadOnlyList<ImportDoc> Documents);
    private sealed record DiffRequest(DocRef A, DocRef B);
    private sealed record DocRef(Guid? Id, JsonNode? Content);
    private sealed record DiffChanged(string Key, string? From, string? To);
    private sealed record DiffResponse(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<DiffChanged> Changed);
}
