using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

sealed class MainForm : Form
{
    private readonly TextBox _endpoint=new(){ReadOnly=true,Dock=DockStyle.Top};
    private readonly TextBox _config=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
    private readonly Label _status=new(){Dock=DockStyle.Top,Height=32,Text="Checking Toaster service…"};
    private readonly HttpClient _http=new(){BaseAddress=new Uri("http://127.0.0.1:47321")};
    public MainForm(){ Text="Toaster";Width=720;Height=470;StartPosition=FormStartPosition.CenterScreen; var tabs=new TabControl{Dock=DockStyle.Fill}; var integration=new TabPage("Integrations"); var statusPage=new TabPage("Status"); var copy=new Button{Text="Copy MCP address",Dock=DockStyle.Top,Height=36}; copy.Click+=(s,e)=>Clipboard.SetText(_endpoint.Text); integration.Controls.Add(_config);integration.Controls.Add(copy);integration.Controls.Add(_endpoint); statusPage.Controls.Add(_status); tabs.TabPages.Add(statusPage);tabs.TabPages.Add(integration);Controls.Add(tabs);Shown+=async(_,_)=>await RefreshAsync(); }
    private async Task RefreshAsync(){ try{var status=await _http.GetFromJsonAsync<JsonElement>("/api/v1/status");_status.Text=$"Service: {status.GetProperty("service").GetString()}   Toast: {status.GetProperty("toastCount").GetInt64()}   Sources: {status.GetProperty("sourceCount").GetInt64()}"; var x=await _http.GetFromJsonAsync<JsonElement>("/api/v1/integrations"); _endpoint.Text=x.GetProperty("mcpEndpoint").GetString()!; _config.Text="Generic MCP connection:\r\n\r\nName: Toaster\r\nTransport: Streamable HTTP\r\nURL: "+_endpoint.Text+"\r\n\r\nJSON-style configuration:\r\n\r\n"+JsonSerializer.Serialize(x.GetProperty("examples").GetProperty("json"),new JsonSerializerOptions{WriteIndented=true}); }catch(Exception ex){_status.Text="Service unavailable: "+ex.Message;_endpoint.Text="http://127.0.0.1:47321/mcp";_config.Text="Start the Toaster service, then reopen this window.\r\n\r\nExpected MCP URL: "+_endpoint.Text;} }
}
