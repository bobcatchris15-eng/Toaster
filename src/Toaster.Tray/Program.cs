using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

ApplicationConfiguration.Initialize();
Application.Run(new MainForm());

sealed class MainForm : Form
{
    private readonly TextBox _endpoint = new() { ReadOnly = true, Dock = DockStyle.Top };
    private readonly TextBox _config = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 36, Text = "Checking Toaster service…", Padding = new Padding(8) };
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:47321") };
    private readonly NotifyIcon _notify;
    private bool _reallyExit;

    public MainForm()
    {
        Text = "Toaster";
        Width = 760;
        Height = 500;
        StartPosition = FormStartPosition.CenterScreen;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Toaster", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _reallyExit = true; _notify.Visible = false; Close(); });
        _notify = new NotifyIcon { Text = "Toaster", Icon = SystemIcons.Application, Visible = true, ContextMenuStrip = menu };
        _notify.DoubleClick += (_, _) => ShowFromTray();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildStatusPage());
        tabs.TabPages.Add(BuildIntegrationsPage());
        tabs.TabPages.Add(BuildSourcesPage());
        Controls.Add(tabs);

        Shown += async (_, _) => await RefreshAsync();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) => { if (!_reallyExit) { e.Cancel = true; Hide(); } };
    }

    private TabPage BuildStatusPage()
    {
        var page = new TabPage("Status");
        var refresh = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 38 };
        refresh.Click += async (_, _) => await RefreshAsync();
        page.Controls.Add(_status);
        page.Controls.Add(refresh);
        return page;
    }

    private TabPage BuildIntegrationsPage()
    {
        var page = new TabPage("Integrations");
        var copyConfig = new Button { Text = "Copy configuration", Dock = DockStyle.Top, Height = 36 };
        copyConfig.Click += (_, _) => Clipboard.SetText(_config.Text);
        var copy = new Button { Text = "Copy MCP address", Dock = DockStyle.Top, Height = 36 };
        copy.Click += (_, _) => Clipboard.SetText(_endpoint.Text);
        page.Controls.Add(_config);
        page.Controls.Add(copyConfig);
        page.Controls.Add(copy);
        page.Controls.Add(_endpoint);
        return page;
    }

    private TabPage BuildSourcesPage()
    {
        var page = new TabPage("Sources");
        var note = new Label { Text = "Import a text-based research file into the local Encyclopedia index. PDF/DOCX parsing is planned next.", Dock = DockStyle.Top, Height = 52, Padding = new Padding(8) };
        var import = new Button { Text = "Import text / Markdown / code file…", Dock = DockStyle.Top, Height = 42 };
        import.Click += async (_, _) => await ImportSourceAsync();
        page.Controls.Add(import);
        page.Controls.Add(note);
        return page;
    }

    private async Task ImportSourceAsync()
    {
        using var picker = new OpenFileDialog { Filter = "Text and source files|*.txt;*.md;*.html;*.htm;*.json;*.cs;*.ps1;*.py;*.js;*.ts|All files|*.*", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var content = await File.ReadAllTextAsync(picker.FileName, Encoding.UTF8);
            var response = await _http.PostAsJsonAsync("/api/v1/sources/text", new { title = Path.GetFileName(picker.FileName), content, origin = picker.FileName, contentType = "text/plain" });
            response.EnsureSuccessStatusCode();
            MessageBox.Show(this, "Source imported into Toaster.", "Toaster", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, "Toaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var status = await _http.GetFromJsonAsync<JsonElement>("/api/v1/status");
            _status.Text = $"Service: {status.GetProperty("service").GetString()}    Toast: {status.GetProperty("toastCount").GetInt64()}    Sources: {status.GetProperty("sourceCount").GetInt64()}    Observations: {status.GetProperty("observationCount").GetInt64()}";
            var x = await _http.GetFromJsonAsync<JsonElement>("/api/v1/integrations");
            _endpoint.Text = x.GetProperty("mcpEndpoint").GetString()!;
            _config.Text = "Generic MCP connection:\r\n\r\nName: Toaster\r\nTransport: Streamable HTTP\r\nURL: " + _endpoint.Text + "\r\n\r\nJSON-style configuration:\r\n\r\n" + JsonSerializer.Serialize(x.GetProperty("examples").GetProperty("json"), new JsonSerializerOptions { WriteIndented = true });
            _notify.Text = "Toaster — service healthy";
        }
        catch (Exception ex)
        {
            _status.Text = "Service unavailable: " + ex.Message;
            _endpoint.Text = "http://127.0.0.1:47321/mcp";
            _config.Text = "Start the Toaster service, then refresh.\r\n\r\nExpected MCP URL: " + _endpoint.Text;
            _notify.Text = "Toaster — service unavailable";
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _notify.Dispose(); _http.Dispose(); }
        base.Dispose(disposing);
    }
}
