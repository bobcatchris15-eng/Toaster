using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

var endpoint = Environment.GetEnvironmentVariable("TOASTER_URL")?.TrimEnd('/') ?? "http://127.0.0.1:47321";
using var http = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(10) };
var json = args.Contains("--json");
var clean = args.Where(x => x != "--json").ToArray();
if (clean.Length == 0) { Help(); return; }

try
{
    switch (clean[0].ToLowerInvariant())
    {
        case "status":
            await Print(await http.GetStringAsync("/api/v1/status"));
            break;
        case "integrations":
            await Print(await http.GetStringAsync("/api/v1/integrations"));
            break;
        case "query":
            if (clean.Length < 2) throw new ArgumentException("query text required");
            var queryResponse = await http.PostAsJsonAsync("/api/v1/query", new { query = string.Join(' ', clean.Skip(1)), limit = 8 });
            await Print(await queryResponse.Content.ReadAsStringAsync());
            break;
        case "toast":
            if (clean.Length < 3 || clean[1] != "add") throw new ArgumentException("usage: toaster toast add <title> <statement>");
            var add = await http.PostAsJsonAsync("/api/v1/toast", new { title = clean[2], statement = string.Join(' ', clean.Skip(3)), confidence = .5 });
            await Print(await add.Content.ReadAsStringAsync());
            break;
        case "ingest":
            if (clean.Length < 2) throw new ArgumentException("usage: toaster ingest <file> [--toast]");
            await Ingest(clean[1], clean.Contains("--toast"));
            break;
        case "toasting":
            if (clean.Length < 2 || clean[1] != "pending") throw new ArgumentException("usage: toaster toasting pending");
            await Print(await http.GetStringAsync("/api/v1/toasting/jobs?limit=10"));
            break;
        default:
            Help();
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Toaster: {ex.Message}");
    Environment.ExitCode = 1;
}

async Task Ingest(string path, bool queueToasting)
{
    path = Path.GetFullPath(path);
    if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);
    var bytes = await File.ReadAllBytesAsync(path);
    using var body = new ByteArrayContent(bytes);
    body.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "text/plain");
    var name = Path.GetFileName(path);
    var url = "/api/v1/sources/file?fileName=" + Uri.EscapeDataString(name) + "&title=" + Uri.EscapeDataString(name) + "&origin=" + Uri.EscapeDataString(path) + "&queueToasting=" + (queueToasting ? "true" : "false");
    var response = await http.PostAsync(url, body);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException(text);
    await Print(text);
}

async Task Print(string raw)
{
    if (json) { Console.WriteLine(raw); return; }
    try { using var d = JsonDocument.Parse(raw); Console.WriteLine(JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true })); }
    catch { Console.WriteLine(raw); }
    await Task.CompletedTask;
}

static void Help()
{
    Console.WriteLine("Toaster CLI\n  toaster status [--json]\n  toaster integrations [--json]\n  toaster query <text> [--json]\n  toaster toast add <title> <statement> [--json]\n  toaster ingest <manual.pdf|file> [--toast] [--json]\n  toaster toasting pending [--json]\n\n--toast queues ingested sections for whichever connected model/provider processes Toaster's toasting jobs.\nEnvironment: TOASTER_URL overrides http://127.0.0.1:47321");
}
