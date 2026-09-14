using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Toaster.Core;
using Toaster.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "Toaster");
builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
var port = builder.Configuration.GetValue<int?>("Toaster:Port") ?? 47321;
var configuredPath = builder.Configuration["Toaster:DataPath"] ?? "%LOCALAPPDATA%\\Toaster";
var dataPath = Environment.ExpandEnvironmentVariables(configuredPath);
if (configuredPath.Contains("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase) && Environment.UserInteractive == false)
    dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Toaster");
Directory.CreateDirectory(dataPath);
var dbPath = Path.Combine(dataPath, "toaster.db");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Services.AddSingleton<IKnowledgeStore>(_ => new SqliteKnowledgeStore(dbPath));
var app = builder.Build();
var store = app.Services.GetRequiredService<IKnowledgeStore>();
await store.InitializeAsync();
var mcpEndpoint = $"http://127.0.0.1:{port}/mcp";

app.MapGet("/", () => Results.Redirect("/api/v1/status"));
app.MapGet("/api/v1/status", (CancellationToken ct) => store.GetStatusAsync(mcpEndpoint, ct));
app.MapGet("/api/v1/integrations", () => Results.Ok(new {
    mcpEndpoint,
    transport = "streamable-http",
    examples = new {
      generic = new { name = "Toaster", url = mcpEndpoint },
      json = new { mcpServers = new Dictionary<string, object> { ["toaster"] = new { url = mcpEndpoint } } }
    }
}));

app.MapPost("/api/v1/toast", async (ToastInput i, CancellationToken ct) => {
    var now=DateTimeOffset.UtcNow; var toast=new Toast(Guid.NewGuid(),i.Title,i.Statement,i.Explanation,i.Domains??[],i.Tags??[],i.Conditions??[],i.Exceptions??[],Math.Clamp(i.Confidence??0.5,0,1),ToastLifecycle.Provisional,now,now);
    return Results.Created($"/api/v1/toast/{toast.Id}", await store.AddToastAsync(toast,ct));
});
app.MapGet("/api/v1/toast/{id:guid}", async (Guid id,CancellationToken ct) => (await store.GetToastAsync(id,ct)) is { } t ? Results.Ok(t) : Results.NotFound());
app.MapPost("/api/v1/query", async (QueryRequest q,CancellationToken ct) => {
    var toast=await store.SearchToastAsync(q.Query,q.Limit,ct); var src=await store.SearchSourcesAsync(q.Query,Math.Min(q.Limit,5),ct); var coverage=Math.Min(1.0,(toast.Count*0.15)+(src.Count*0.08)); return Results.Ok(new QueryResponse(q.Query,toast,src,coverage));
});
app.MapPost("/api/v1/observations", async (ObservationInput i,CancellationToken ct) => Results.Created("/api/v1/observations",await store.AddObservationAsync(new Observation(Guid.NewGuid(),i.Activity,i.Attempt,i.Result,i.Resolution,i.EnvironmentJson,DateTimeOffset.UtcNow),ct)));
app.MapPost("/api/v1/sources/text", async (TextSourceInput i,CancellationToken ct) => {
    var bytes=Encoding.UTF8.GetBytes(i.Content); var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); var sourceId=Guid.NewGuid(); var source=new SourceDocument(sourceId,i.Title,i.Origin,i.ContentType??"text/plain",sha,DateTimeOffset.UtcNow); var sections=SplitSections(sourceId,i.Content); return Results.Created($"/api/v1/sources/{sourceId}",await store.AddSourceAsync(source,sections,ct));
});

app.MapPost("/mcp", async (HttpContext http, CancellationToken ct) => {
    using var doc=await JsonDocument.ParseAsync(http.Request.Body,cancellationToken:ct); var root=doc.RootElement; var id=root.TryGetProperty("id",out var idEl)?idEl.Clone():default; var method=root.GetProperty("method").GetString(); object result;
    switch(method){
      case "initialize": result=new { protocolVersion="2025-06-18", capabilities=new { tools=new { listChanged=false } }, serverInfo=new { name="Toaster", version="0.1.0" } }; break;
      case "notifications/initialized": return Results.NoContent();
      case "tools/list": result=new { tools=new object[]{
        Tool("toaster_query","Search distilled expertise and likely source material",new { type="object",properties=new { query=new { type="string" },limit=new { type="integer",minimum=1,maximum=50 } },required=new[]{"query"} }),
        Tool("toaster_get_toast","Retrieve one toast by id",new { type="object",properties=new { id=new { type="string" } },required=new[]{"id"} }),
        Tool("toaster_add_observation","Record an implementation observation or failure lesson candidate",new { type="object",properties=new { activity=new { type="string" },attempt=new { type="string" },result=new { type="string" },resolution=new { type="string" } },required=new[]{"activity","attempt","result"} })
      }}; break;
      case "tools/call": result=await CallTool(root.GetProperty("params"),store,ct); break;
      default: return JsonRpcError(id,-32601,$"Method not found: {method}");
    }
    return Results.Json(new { jsonrpc="2.0", id= id.ValueKind==JsonValueKind.Undefined ? null : id, result });
});

app.Run();

static object Tool(string name,string description,object inputSchema)=>new {name,description,inputSchema};
static IResult JsonRpcError(JsonElement id,int code,string message)=>Results.Json(new {jsonrpc="2.0",id=id.ValueKind==JsonValueKind.Undefined?null:id,error=new{code,message}});
static async Task<object> CallTool(JsonElement p,IKnowledgeStore store,CancellationToken ct){ var name=p.GetProperty("name").GetString(); var a=p.TryGetProperty("arguments",out var x)?x:default; return name switch {
 "toaster_query" => McpText(JsonSerializer.Serialize(new QueryResponse(a.GetProperty("query").GetString()!,await store.SearchToastAsync(a.GetProperty("query").GetString()!,a.TryGetProperty("limit",out var l)?l.GetInt32():8,ct),await store.SearchSourcesAsync(a.GetProperty("query").GetString()!,5,ct),0.5))),
 "toaster_get_toast" => McpText(JsonSerializer.Serialize(await store.GetToastAsync(Guid.Parse(a.GetProperty("id").GetString()!),ct))),
 "toaster_add_observation" => McpText(JsonSerializer.Serialize(await store.AddObservationAsync(new Observation(Guid.NewGuid(),a.GetProperty("activity").GetString()!,a.GetProperty("attempt").GetString()!,a.GetProperty("result").GetString()!,a.TryGetProperty("resolution",out var r)?r.GetString():null,null,DateTimeOffset.UtcNow),ct))),
 _ => new { content=new[]{new {type="text",text=$"Unknown tool: {name}"}},isError=true }
}; }
static object McpText(string text)=>new {content=new[]{new {type="text",text}},isError=false};
static List<SourceSection> SplitSections(Guid sourceId,string content){ var chunks=new List<SourceSection>(); var lines=content.Replace("\r\n","\n").Split('\n'); var current=new StringBuilder(); var heading="Document"; var ordinal=0; void Flush(){ if(current.Length==0)return; chunks.Add(new SourceSection(Guid.NewGuid(),sourceId,heading,current.ToString().Trim(),ordinal++)); current.Clear(); } foreach(var line in lines){ if(line.StartsWith("#")){Flush();heading=line.TrimStart('#',' ');} else { current.AppendLine(line); if(current.Length>5000)Flush(); }} Flush(); if(chunks.Count==0)chunks.Add(new SourceSection(Guid.NewGuid(),sourceId,"Document",content,0)); return chunks; }

public sealed record ToastInput(string Title,string Statement,string? Explanation,string[]? Domains,string[]? Tags,string[]? Conditions,string[]? Exceptions,double? Confidence);
public sealed record ObservationInput(string Activity,string Attempt,string Result,string? Resolution,string? EnvironmentJson);
public sealed record TextSourceInput(string Title,string Content,string? Origin,string? ContentType);
