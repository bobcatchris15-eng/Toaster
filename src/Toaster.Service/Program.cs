using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Toaster.Core;
using Toaster.Service;
using Toaster.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "Toaster");
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 256L * 1024 * 1024);

var port = builder.Configuration.GetValue<int?>("Toaster:Port") ?? 47321;
var configuredPath = builder.Configuration["Toaster:DataPath"] ?? "%LOCALAPPDATA%\\Toaster";
var dataPath = Environment.ExpandEnvironmentVariables(configuredPath);
if (configuredPath.Contains("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase) && !Environment.UserInteractive)
    dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Toaster");
Directory.CreateDirectory(dataPath);
Directory.CreateDirectory(Path.Combine(dataPath, "sources", "objects"));

builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton<IKnowledgeStore>(_ => new SqliteKnowledgeStore(Path.Combine(dataPath, "toaster.db")));

var app = builder.Build();
var store = app.Services.GetRequiredService<IKnowledgeStore>();
await store.InitializeAsync();
var mcpEndpoint = $"http://127.0.0.1:{port}/mcp";

app.MapGet("/", () => Results.Redirect("/api/v1/status"));
app.MapGet("/api/v1/status", (CancellationToken ct) => store.GetStatusAsync(mcpEndpoint, ct));
app.MapGet("/api/v1/integrations", () => Results.Ok(new
{
    mcpEndpoint,
    transport = "streamable-http",
    agentInstructions = "Use Toaster for reusable expertise. Record meaningful implementation mistakes/lessons with toaster_add_observation. Periodically call toaster_get_toasting_jobs; distill each job into reusable operational lessons and submit them with toaster_submit_toasting_result. Do not turn project-specific state into toast.",
    examples = new
    {
        generic = new { name = "Toaster", url = mcpEndpoint },
        json = new { mcpServers = new Dictionary<string, object> { ["toaster"] = new { url = mcpEndpoint } } }
    }
}));

app.MapPost("/api/v1/toast", async (ToastInput i, CancellationToken ct) =>
{
    var now = DateTimeOffset.UtcNow;
    var toast = new Toast(Guid.NewGuid(), i.Title, i.Statement, i.Explanation, i.Domains ?? [], i.Tags ?? [], i.Conditions ?? [], i.Exceptions ?? [], Math.Clamp(i.Confidence ?? 0.5, 0, 1), ToastLifecycle.Provisional, now, now);
    return Results.Created($"/api/v1/toast/{toast.Id}", await store.AddToastAsync(toast, ct));
});

app.MapGet("/api/v1/toast/{id:guid}", async (Guid id, CancellationToken ct) =>
{
    var toast = await store.GetToastAsync(id, ct);
    return toast is null ? Results.NotFound() : Results.Ok(new { toast, provenance = await store.GetProvenanceAsync(id, ct) });
});

app.MapPost("/api/v1/query", async (QueryRequest q, CancellationToken ct) =>
{
    var toast = await store.SearchToastAsync(q.Query, q.Limit, ct);
    var sources = await store.SearchSourcesAsync(q.Query, Math.Min(q.Limit, 5), ct);
    return Results.Ok(new QueryResponse(q.Query, toast, sources, Math.Min(1.0, toast.Count * .15 + sources.Count * .08)));
});

app.MapPost("/api/v1/observations", async (ObservationInput i, CancellationToken ct) =>
{
    var observation = await store.AddObservationAsync(new Observation(Guid.NewGuid(), i.Activity, i.Attempt, i.Result, i.Resolution, i.EnvironmentJson, DateTimeOffset.UtcNow), ct);
    var job = (i.QueueForToasting ?? true) ? await ToastingCoordinator.QueueObservationJob(observation, store, ct) : null;
    return Results.Created("/api/v1/observations", new { observation, toastingJob = job });
});

app.MapPost("/api/v1/sources/text", async (TextSourceInput i, CancellationToken ct) =>
    await IngestSource(Encoding.UTF8.GetBytes(i.Content), i.Title, i.Origin, i.ContentType ?? "text/plain", i.QueueForToasting ?? false, dataPath, store, ct));

app.MapPost("/api/v1/sources/file", async (HttpRequest request, string? fileName, string? title, string? origin, bool? queueToasting, CancellationToken ct) =>
{
    fileName ??= "manual.bin";
    title ??= fileName;
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms, ct);
    if (ms.Length == 0) return Results.BadRequest(new { error = "The uploaded file was empty." });
    return await IngestSource(ms.ToArray(), title, origin ?? fileName, request.ContentType?.Split(';')[0] ?? GuessContentType(fileName), queueToasting ?? false, dataPath, store, ct);
});

app.MapGet("/api/v1/sources/{id:guid}", async (Guid id, CancellationToken ct) =>
    (await store.GetSourceAsync(id, ct)) is { } source ? Results.Ok(source) : Results.NotFound());
app.MapGet("/api/v1/sources/{id:guid}/sections", async (Guid id, CancellationToken ct) => Results.Ok(await store.GetSourceSectionsAsync(id, ct)));
app.MapGet("/api/v1/source-sections/{id:guid}", async (Guid id, CancellationToken ct) =>
    (await store.GetSourceSectionAsync(id, ct)) is { } section ? Results.Ok(section) : Results.NotFound());
app.MapPost("/api/v1/sources/{id:guid}/toast", async (Guid id, CancellationToken ct) =>
{
    var source = await store.GetSourceAsync(id, ct);
    if (source is null) return Results.NotFound();
    var jobs = await ToastingCoordinator.QueueSourceJobs(source, await store.GetSourceSectionsAsync(id, ct), store, ct);
    return Results.Ok(new { sourceId = id, jobsQueued = jobs.Count });
});

app.MapGet("/api/v1/toasting/jobs", async (int? limit, CancellationToken ct) => Results.Ok(await store.GetPendingToastingJobsAsync(limit ?? 5, ct)));
app.MapPost("/api/v1/toasting/jobs/{id:guid}/result", async (Guid id, ToastingResultInput input, CancellationToken ct) =>
{
    var result = await ToastingCoordinator.SubmitResult(id, input.Candidates ?? [], store, ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapPost("/mcp", async (HttpContext http, CancellationToken ct) =>
{
    using var doc = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: ct);
    var root = doc.RootElement;
    var id = root.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
    var method = root.GetProperty("method").GetString();
    object result;

    switch (method)
    {
        case "initialize":
            result = new { protocolVersion = "2025-06-18", capabilities = new { tools = new { listChanged = false } }, serverInfo = new { name = "Toaster", version = "0.2.0" } };
            break;
        case "notifications/initialized":
            return Results.NoContent();
        case "tools/list":
            result = new { tools = McpTools() };
            break;
        case "tools/call":
            result = await CallTool(root.GetProperty("params"), store, ct);
            break;
        default:
            return JsonRpcError(id, -32601, $"Method not found: {method}");
    }

    return Results.Json(new { jsonrpc = "2.0", id = id.ValueKind == JsonValueKind.Undefined ? null : id, result });
});

app.Run();

static object[] McpTools() =>
[
    Tool("toaster_query", "Search distilled expertise and likely source material before doing expensive research.", new { type="object", properties=new { query=new { type="string" }, limit=new { type="integer", minimum=1, maximum=50 } }, required=new[]{"query"} }),
    Tool("toaster_get_toast", "Retrieve one toast and its provenance by id.", new { type="object", properties=new { id=new { type="string" } }, required=new[]{"id"} }),
    Tool("toaster_search_sources", "Search ingested manuals and research sources. Returns cheap previews; retrieve a section only when useful.", new { type="object", properties=new { query=new { type="string" }, limit=new { type="integer", minimum=1, maximum=50 } }, required=new[]{"query"} }),
    Tool("toaster_get_source_sections", "List the sections/pages of an ingested manual or source.", new { type="object", properties=new { sourceId=new { type="string" } }, required=new[]{"sourceId"} }),
    Tool("toaster_get_source_section", "Retrieve one source/manual section by id.", new { type="object", properties=new { sectionId=new { type="string" } }, required=new[]{"sectionId"} }),
    Tool("toaster_queue_source_toasting", "Queue an ingested source for provider-neutral distillation into reusable toast.", new { type="object", properties=new { sourceId=new { type="string" } }, required=new[]{"sourceId"} }),
    Tool("toaster_add_observation", "Record an implementation attempt/result. By default this creates a live toasting job so the current or another model can generalize the lesson.", new { type="object", properties=new { activity=new { type="string" }, attempt=new { type="string" }, result=new { type="string" }, resolution=new { type="string" }, environmentJson=new { type="string" }, queueForToasting=new { type="boolean" } }, required=new[]{"activity","attempt","result"} }),
    Tool("toaster_get_toasting_jobs", "Get pending manual or live-learning distillation jobs. Process the evidence with your current model/provider and return only reusable operational lessons, not project-specific state.", new { type="object", properties=new { limit=new { type="integer", minimum=1, maximum=25 } } }),
    Tool("toaster_submit_toasting_result", "Submit generalized lessons produced by the current model/provider for a toasting job. Zero candidates is valid when the evidence contains no reusable lesson.", new { type="object", properties=new { jobId=new { type="string" }, candidates=new { type="array", items=new { type="object", properties=new { title=new { type="string" }, statement=new { type="string" }, explanation=new { type="string" }, domains=new { type="array", items=new { type="string" } }, tags=new { type="array", items=new { type="string" } }, conditions=new { type="array", items=new { type="string" } }, exceptions=new { type="array", items=new { type="string" } }, confidence=new { type="number", minimum=0, maximum=1 } }, required=new[]{"title","statement"} } } }, required=new[]{"jobId","candidates"} })
];

static object Tool(string name, string description, object inputSchema) => new { name, description, inputSchema };
static IResult JsonRpcError(JsonElement id, int code, string message) => Results.Json(new { jsonrpc = "2.0", id = id.ValueKind == JsonValueKind.Undefined ? null : id, error = new { code, message } });
static object McpText(object value) => new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(value) } }, isError = false };
static object McpError(string text) => new { content = new[] { new { type = "text", text } }, isError = true };

static async Task<object> CallTool(JsonElement p, IKnowledgeStore store, CancellationToken ct)
{
    var name = p.GetProperty("name").GetString();
    var a = p.TryGetProperty("arguments", out var x) ? x : default;

    switch (name)
    {
        case "toaster_query":
        {
            var query = a.GetProperty("query").GetString()!;
            var limit = a.TryGetProperty("limit", out var l) ? l.GetInt32() : 8;
            var toast = await store.SearchToastAsync(query, limit, ct);
            var sources = await store.SearchSourcesAsync(query, Math.Min(limit, 5), ct);
            return McpText(new QueryResponse(query, toast, sources, Math.Min(1.0, toast.Count * .15 + sources.Count * .08)));
        }
        case "toaster_get_toast":
        {
            var id = Guid.Parse(a.GetProperty("id").GetString()!);
            return McpText(new { toast = await store.GetToastAsync(id, ct), provenance = await store.GetProvenanceAsync(id, ct) });
        }
        case "toaster_search_sources":
            return McpText(await store.SearchSourcesAsync(a.GetProperty("query").GetString()!, a.TryGetProperty("limit", out var sl) ? sl.GetInt32() : 8, ct));
        case "toaster_get_source_sections":
            return McpText(await store.GetSourceSectionsAsync(Guid.Parse(a.GetProperty("sourceId").GetString()!), ct));
        case "toaster_get_source_section":
            return McpText(await store.GetSourceSectionAsync(Guid.Parse(a.GetProperty("sectionId").GetString()!), ct));
        case "toaster_queue_source_toasting":
        {
            var sourceId = Guid.Parse(a.GetProperty("sourceId").GetString()!);
            var source = await store.GetSourceAsync(sourceId, ct);
            if (source is null) return McpError("Source not found.");
            var jobs = await ToastingCoordinator.QueueSourceJobs(source, await store.GetSourceSectionsAsync(sourceId, ct), store, ct);
            return McpText(new { sourceId, jobsQueued = jobs.Count });
        }
        case "toaster_add_observation":
        {
            var observation = await store.AddObservationAsync(new Observation(Guid.NewGuid(), a.GetProperty("activity").GetString()!, a.GetProperty("attempt").GetString()!, a.GetProperty("result").GetString()!, OptionalString(a, "resolution"), OptionalString(a, "environmentJson"), DateTimeOffset.UtcNow), ct);
            var queue = !a.TryGetProperty("queueForToasting", out var q) || q.ValueKind != JsonValueKind.False;
            var job = queue ? await ToastingCoordinator.QueueObservationJob(observation, store, ct) : null;
            return McpText(new { observation, toastingJob = job });
        }
        case "toaster_get_toasting_jobs":
            return McpText(await store.GetPendingToastingJobsAsync(a.TryGetProperty("limit", out var jl) ? jl.GetInt32() : 5, ct));
        case "toaster_submit_toasting_result":
        {
            var jobId = Guid.Parse(a.GetProperty("jobId").GetString()!);
            var candidates = a.GetProperty("candidates").EnumerateArray().Select(CandidateFromJson).ToArray();
            var submitted = await ToastingCoordinator.SubmitResult(jobId, candidates, store, ct);
            return submitted is null ? McpError("Toasting job not found.") : McpText(submitted);
        }
        default:
            return McpError($"Unknown tool: {name}");
    }
}

static async Task<IResult> IngestSource(byte[] bytes, string title, string? origin, string contentType, bool queueToasting, string dataPath, IKnowledgeStore store, CancellationToken ct)
{
    var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (await store.FindSourceByShaAsync(sha, ct) is { } existing)
        return Results.Ok(new { source = existing, duplicate = true, message = "This exact source is already in Toaster." });

    var sourceId = Guid.NewGuid();
    IReadOnlyList<SourceSection> sections;
    try { sections = ManualParser.Parse(sourceId, bytes, contentType, title); }
    catch (Exception ex) { return Results.BadRequest(new { error = $"Could not extract this source: {ex.Message}" }); }
    if (sections.Count == 0)
        return Results.BadRequest(new { error = "No readable text was extracted. This may be a scanned/image-only PDF; OCR ingestion is not implemented yet." });

    var source = new SourceDocument(sourceId, title, origin, contentType, sha, DateTimeOffset.UtcNow);
    await store.AddSourceAsync(source, sections, ct);

    var extension = Path.GetExtension(title);
    if (string.IsNullOrWhiteSpace(extension)) extension = contentType == "application/pdf" ? ".pdf" : ".bin";
    var objectPath = Path.Combine(dataPath, "sources", "objects", sha + extension.ToLowerInvariant());
    if (!File.Exists(objectPath)) await File.WriteAllBytesAsync(objectPath, bytes, ct);

    var jobs = queueToasting ? await ToastingCoordinator.QueueSourceJobs(source, sections, store, ct) : new List<ToastingJob>();
    return Results.Created($"/api/v1/sources/{sourceId}", new { source, sectionCount = sections.Count, rawObject = objectPath, jobsQueued = jobs.Count });
}

static ToastCandidateInput CandidateFromJson(JsonElement c) => new(
    c.GetProperty("title").GetString() ?? "Untitled lesson",
    c.GetProperty("statement").GetString() ?? "",
    OptionalString(c, "explanation"),
    OptionalStrings(c, "domains"), OptionalStrings(c, "tags"), OptionalStrings(c, "conditions"), OptionalStrings(c, "exceptions"),
    c.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number ? confidence.GetDouble() : null);

static string? OptionalString(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
static string[]? OptionalStrings(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : null;
static string GuessContentType(string fileName) => fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "text/plain";

public sealed record ToastInput(string Title, string Statement, string? Explanation, string[]? Domains, string[]? Tags, string[]? Conditions, string[]? Exceptions, double? Confidence);
public sealed record ToastingResultInput(ToastCandidateInput[]? Candidates);
public sealed record ObservationInput(string Activity, string Attempt, string Result, string? Resolution, string? EnvironmentJson, bool? QueueForToasting = true);
public sealed record TextSourceInput(string Title, string Content, string? Origin, string? ContentType, bool? QueueForToasting = false);
