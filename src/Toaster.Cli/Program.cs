using System.Net.Http.Json;
using System.Text.Json;

var endpoint=Environment.GetEnvironmentVariable("TOASTER_URL")?.TrimEnd('/') ?? "http://127.0.0.1:47321";
using var http=new HttpClient{BaseAddress=new Uri(endpoint)};
var json=args.Contains("--json"); var clean=args.Where(x=>x!="--json").ToArray();
if(clean.Length==0){Help();return;}
try{
 switch(clean[0].ToLowerInvariant()){
  case "status": await Print(await http.GetStringAsync("/api/v1/status")); break;
  case "integrations": await Print(await http.GetStringAsync("/api/v1/integrations")); break;
  case "query": if(clean.Length<2)throw new ArgumentException("query text required"); var response=await http.PostAsJsonAsync("/api/v1/query",new{query=string.Join(' ',clean.Skip(1)),limit=8}); await Print(await response.Content.ReadAsStringAsync()); break;
  case "toast": if(clean.Length<3||clean[1]!="add")throw new ArgumentException("usage: toaster toast add <title> <statement>"); var add=await http.PostAsJsonAsync("/api/v1/toast",new{title=clean[2],statement=string.Join(' ',clean.Skip(3)),confidence=.5}); await Print(await add.Content.ReadAsStringAsync()); break;
  default: Help(); break;
 }
}catch(Exception ex){Console.Error.WriteLine($"Toaster: {ex.Message}");Environment.ExitCode=1;}

async Task Print(string raw){ if(json){Console.WriteLine(raw);return;} try{using var d=JsonDocument.Parse(raw);Console.WriteLine(JsonSerializer.Serialize(d,new JsonSerializerOptions{WriteIndented=true}));}catch{Console.WriteLine(raw);} await Task.CompletedTask; }
static void Help(){Console.WriteLine("Toaster CLI\n  toaster status [--json]\n  toaster integrations [--json]\n  toaster query <text> [--json]\n  toaster toast add <title> <statement> [--json]\nEnvironment: TOASTER_URL overrides http://127.0.0.1:47321");}
