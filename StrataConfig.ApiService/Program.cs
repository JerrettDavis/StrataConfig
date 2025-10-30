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
