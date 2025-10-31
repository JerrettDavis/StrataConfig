using System.Linq;
using System.Text.Json.Nodes;
using StrataConfig.ApiService.Api;
using StrataConfig.Core;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// StrataConfig core services (POC)
builder.Services.AddSingleton<IConfigStore, InMemoryConfigStore>();
builder.Services.AddSingleton<IMergeEngine, MergeEngine>();
builder.Services.AddSingleton<IRuleEngine, RuleEngine>();
builder.Services.AddSingleton<ITemplateValidator, NJsonSchemaTemplateValidator>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithSummary("Sample weather data")
    .WithTags("Diagnostics");

// Templates
app.MapGet("/api/templates", async (
        IConfigStore store,
        CancellationToken ct) =>
    {
        var templates = await store.GetTemplatesAsync(ct);
        return Results.Ok(templates.Select(t => t.ToResponse()));
    })
    .WithName("ListTemplates")
    .WithSummary("List available configuration templates")
    .WithDescription("Returns all templates the configuration service knows about.")
    .WithTags("Templates");

app.MapGet("/api/namespaces", async (
        IConfigStore store,
        CancellationToken ct) =>
    {
        var namespaces = await store.GetNamespacesAsync(ct);
        return Results.Ok(namespaces);
    })
    .WithName("ListNamespaces")
    .WithSummary("List namespaces")
    .WithDescription("Return available namespaces backed by the in-memory store.")
    .WithTags("Documents");

app.MapGet("/api/templates/{id}", async (
        string id,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var templates = await store.GetTemplatesAsync(ct);
        var template = templates.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        return template is null
            ? Results.NotFound()
            : Results.Ok(template.ToResponse());
    })
    .WithName("GetTemplateById")
    .WithSummary("Get template by id")
    .WithDescription("Fetch a single template, if present.")
    .WithTags("Templates");

// Scope hierarchy
app.MapGet("/api/scopes", async (
        IConfigStore store,
        CancellationToken ct) =>
    {
        var nodes = await store.GetScopeNodesAsync(ct);
        var tree = ApiModelMapper.BuildScopeTree(nodes);
        return Results.Ok(tree);
    })
    .WithName("ListScopes")
    .WithSummary("Get scope tree")
    .WithDescription("Returns the seeded scope hierarchy for exploration.")
    .WithTags("Scopes");

app.MapGet("/api/scopes/{id:guid}", async (
        Guid id,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var nodes = await store.GetScopeNodesAsync(ct);
        var tree = ApiModelMapper.BuildScopeTree(nodes);
        ScopeNodeResponse? FindById(IEnumerable<ScopeNodeResponse> items)
        {
            foreach (var node in items)
            {
                if (node.Id == id)
                {
                    return node;
                }

                var child = FindById(node.Children);
                if (child is not null)
                {
                    return child;
                }
            }

            return null;
        }

        var match = FindById(tree);
        return match is null ? Results.NotFound() : Results.Ok(match);
    })
    .WithName("GetScopeById")
    .WithSummary("Get scope details")
    .WithDescription("Returns a single scope plus its child hierarchy, if found.")
    .WithTags("Scopes");

// Namespace documents explorer
app.MapGet("/api/namespaces/{ns}/documents", async (
        string ns,
        HttpContext http,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var scopeContext = BuildScopeContext(http.Request);
        var snapshot = await store.ReadAsync(scopeContext, ns, ct);
        var response = snapshot.ToResponse(ns);
        return Results.Ok(response);
    })
    .WithName("GetNamespaceDocuments")
    .WithSummary("Inspect layered documents")
    .WithDescription("Returns the layered documents and revision metadata for the requested namespace.")
    .WithTags("Documents");

// Minimal POC resolve endpoint (returns flattened dictionary)
app.MapPost("/api/config/resolve", async (
        ResolveRequest req,
        IConfigStore store,
        IRuleEngine rules,
        ITemplateValidator tv,
        IMergeEngine merge,
        CancellationToken ct) =>
    {
        var snap = await store.ReadAsync(req.Scope, req.Namespace, ct);
        var applicableRules = await store.GetRulesAsync(snap.RootScopeId, ct);

        var candidates = new List<(int precedence, JsonNode node)>();
        foreach (var layer in snap.Layers.OrderBy(l => l.PrecedenceOrder))
        {
            foreach (var d in layer.Documents)
            {
                var templ = snap.Templates.FirstOrDefault(t => t.Id == d.TemplateRef) ?? new("unknown", 1, null, null);
                var parsed = JsonNode.Parse(d.ContentJson)!;
                tv.Validate(parsed, templ);
                var afterRules = rules.ApplyRules(parsed, applicableRules, new(req.Scope, req.Namespace));
                if (afterRules is not null)
                {
                    candidates.Add((layer.PrecedenceOrder, afterRules));
                }
            }
        }

        var merged = merge.Merge(
            candidates
                .OrderBy(c => c.precedence)
                .Select(c => c.node)
                .ToList());
        var flattened = merge.Flatten(merged);
        return Results.Json(flattened);
    })
    .WithName("ResolveConfig")
    .WithSummary("Resolve configuration")
    .WithDescription("Runs merge/rule/template validation and returns flattened key/value configuration for the scope.")
    .WithTags("Resolve");

// Minimal POC document upsert endpoint
app.MapPost("/api/documents", async (
        UpsertDocumentRequest req,
        IConfigStore store,
        ITemplateValidator validator,
        CancellationToken ct) =>
    {
        if (req.Content is null)
        {
            return Results.BadRequest(new { error = "Content is required." });
        }

        var templates = await store.GetTemplatesAsync(ct);
        var template = templates.FirstOrDefault(t => t.Id == req.TemplateRef);
        if (template is null)
        {
            return Results.BadRequest(new { error = $"Template '{req.TemplateRef}' was not found." });
        }

        try
        {
            validator.Validate(req.Content, template);
        }
        catch (TemplateValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, details = ex.Errors });
        }

        var saved = await store.UpsertAsync(
            new ConfigDocumentWrite(
                req.Id,
                req.ScopeId,
                req.Namespace,
                req.TemplateRef,
                req.Content,
                req.UpdatedBy ?? "unknown"),
            ct);

        return Results.Created($"/api/documents/{saved.Id}", saved);
    })
    .WithName("UpsertDocument")
    .WithSummary("Create or update a document")
    .WithDescription("Validates content against the template then writes it into the in-memory store.")
    .WithTags("Documents");

// Documents: get by id
app.MapGet("/api/documents/{id:guid}", async (
        Guid id,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var doc = await store.GetDocumentAsync(id, ct);
        return doc is null ? Results.NotFound() : Results.Ok(doc.ToResponse());
    })
    .WithName("GetDocumentById")
    .WithSummary("Get document by id")
    .WithTags("Documents");

// Documents: list
app.MapGet("/api/documents", async (
        string ns,
        Guid? scopeId,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var docs = await store.GetDocumentsAsync(ns, scopeId, ct);
        return Results.Ok(docs.Select(d => d.ToResponse()));
    })
    .WithName("ListDocuments")
    .WithSummary("List documents by namespace and optional scope")
    .WithTags("Documents");

// Documents: delete
app.MapDelete("/api/documents/{id:guid}", async (
        Guid id,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var ok = await store.DeleteDocumentAsync(id, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteDocument")
    .WithSummary("Delete a document")
    .WithTags("Documents");

// Documents: clone
app.MapPost("/api/documents/clone", async (
        CloneDocumentRequest req,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var saved = await store.CloneDocumentAsync(req.SourceId, req.DestinationScopeId, req.UpdatedBy ?? "unknown", ct);
        return Results.Created($"/api/documents/{saved.Id}", saved.ToResponse());
    })
    .WithName("CloneDocument")
    .WithSummary("Clone a document to a different scope")
    .WithTags("Documents");

// Namespace export
app.MapGet("/api/namespaces/{ns}/export", async (
        string ns,
        Guid? scopeId,
        IConfigStore store,
        CancellationToken ct) =>
    {
        var docs = await store.ExportAsync(ns, scopeId, ct);
        return Results.Ok(docs.Select(d => d.ToResponse()));
    })
    .WithName("ExportNamespace")
    .WithSummary("Export documents for a namespace (optionally filtered by scope)")
    .WithTags("Documents");

// Namespace import
app.MapPost("/api/namespaces/{ns}/import", async (
        string ns,
        ImportRequest req,
        IConfigStore store,
        ITemplateValidator validator,
        CancellationToken ct) =>
    {
        if (!string.Equals(ns, req.Namespace, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Namespace mismatch." });
        }

        var writes = new List<ConfigDocumentWrite>();
        foreach (var d in req.Documents)
        {
            if (d.Content is null) return Results.BadRequest(new { error = "Content is required." });
            writes.Add(new ConfigDocumentWrite(d.Id, d.ScopeId, req.Namespace, d.TemplateRef, d.Content, d.UpdatedBy ?? "unknown"));
        }
        var saved = await store.ImportAsync(req.Namespace, writes, ct);
        return Results.Ok(saved.Select(s => s.ToResponse()));
    })
    .WithName("ImportNamespace")
    .WithSummary("Import documents for a namespace")
    .WithTags("Documents");

// Diff two documents (by id or by inline content)
app.MapPost("/api/documents/diff", async (
        DiffRequest req,
        IConfigStore store,
        IMergeEngine merge,
        CancellationToken ct) =>
    {
        async Task<JsonNode?> ResolveAsync(DocumentRefRequest r)
        {
            if (r.Id is Guid id)
            {
                var doc = await store.GetDocumentAsync(id, ct);
                return doc is null ? null : JsonNode.Parse(doc.ContentJson);
            }
            return r.Content;
        }

        var a = await ResolveAsync(req.A);
        var b = await ResolveAsync(req.B);
        if (a is null || b is null) return Results.BadRequest(new { error = "Both documents must be resolvable." });

        var fa = merge.Flatten(a);
        var fb = merge.Flatten(b);
        var added = fb.Keys.Except(fa.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var removed = fa.Keys.Except(fb.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(k => k).ToList();
        var changed = fa.Keys.Intersect(fb.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(k => !string.Equals(fa[k], fb[k], StringComparison.Ordinal))
            .OrderBy(k => k)
            .Select(k => new DiffChangedEntry(k, fa[k], fb[k]))
            .ToList();
        return Results.Ok(new DiffResponse(added, removed, changed));
    })
    .WithName("DiffDocuments")
    .WithSummary("Compute a diff between two document contents")
    .WithTags("Documents");

app.MapDefaultEndpoints();

app.Run();

static ScopeContext BuildScopeContext(HttpRequest request)
{
    var environment = request.Query.TryGetValue("environment", out var envValues)
        ? envValues.FirstOrDefault() ?? "Development"
        : "Development";

    var appName = request.Query.TryGetValue("app", out var appValues)
        ? appValues.FirstOrDefault() ?? "Strata.Admin"
        : "Strata.Admin";

    var appVersion = request.Query.TryGetValue("appVersion", out var versionValues)
        ? versionValues.FirstOrDefault()
        : null;

    var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    if (request.Query.TryGetValue("org", out var orgValues))
    {
        var orgValue = orgValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(orgValue))
        {
            dimensions["org"] = orgValue!;
        }
    }

    if (request.Query.TryGetValue("site", out var siteValues))
    {
        var siteValue = siteValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(siteValue))
        {
            dimensions["site"] = siteValue!;
        }
    }

    if (request.Query.TryGetValue("division", out var divisionValues))
    {
        var divisionValue = divisionValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(divisionValue))
        {
            dimensions["division"] = divisionValue!;
        }
    }

    if (request.Query.TryGetValue("device", out var deviceValues))
    {
        var deviceValue = deviceValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(deviceValue))
        {
            dimensions["device"] = deviceValue!;
        }
    }

    if (request.Query.TryGetValue("user", out var userValues))
    {
        var userValue = userValues.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(userValue))
        {
            dimensions["user"] = userValue!;
        }
    }

    foreach (var kvp in request.Query)
    {
        if (kvp.Key.StartsWith("dim.", StringComparison.OrdinalIgnoreCase))
        {
            var key = kvp.Key[4..];
            var value = kvp.Value.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                dimensions[key] = value!;
            }
        }
    }

    var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (request.Query.TryGetValue("tag", out var rawTags))
    {
        foreach (var raw in rawTags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var piece in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(piece))
                {
                    tagSet.Add(piece);
                }
            }
        }
    }

    var tags = tagSet.Count == 0
        ? Array.Empty<string>()
        : tagSet.ToArray();

    return new ScopeContext(
        environment,
        appName,
        string.IsNullOrWhiteSpace(appVersion) ? null : appVersion,
        dimensions,
        tags);
}

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public sealed record UpsertDocumentRequest(
    Guid? Id,
    Guid ScopeId,
    string Namespace,
    string TemplateRef,
    JsonNode? Content,
    string? UpdatedBy);
