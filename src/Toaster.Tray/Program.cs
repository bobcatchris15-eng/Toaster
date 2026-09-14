using System.Net.Http.Headers;
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
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 56, Text = "Checking Toaster service…", Padding = new Padding(8) };
    private readonly CheckBox _queueToasting = new() { Text = "Queue imported manuals for toasting by my connected model/provider", Checked = true, Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 4, 4, 4) };
    private readonly Label _sourceNote = new() { Text = "Import manuals and research into Toaster. PDFs are extracted page-by-page; text, Markdown, HTML, JSON and source files are sectioned locally. Scanned/image-only PDFs currently require OCR before import.", Dock = DockStyle.Top, Height = 72, Padding = new Padding(8) };
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:47321"), Timeout = TimeSpan.FromMinutes(10) };
    private readonly NotifyIcon _notify;
    private bool _reallyExit;

    public MainForm()
    {
        Text = "Toaster";
        Width = 800;
        Height = 540;
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
        var copyConfig = new Button { Text = "Copy configuration + agent instructions", Dock = DockStyle.Top, Height = 36 };
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
        var page = new TabPage("Manuals & Sources");
        var import = new Button { Text = "Import manual or research file…", Dock = DockStyle.Top, Height = 46 };
        import.Click += async (_, _) => await ImportSourceAsync();
        page.Controls.Add(import);
        page.Controls.Add(_queueToasting);
        page.Controls.Add(_sourceNote);
        return page;
    }

    private async Task ImportSourceAsync()
    {
        using var picker = new OpenFileDialog
        {
            Filter = "Manuals and research|*.pdf;*.txt;*.md;*.html;*.htm;*.json;*.cs;*.ps1;*.py;*.js;*.ts|PDF manuals|*.pdf|Text and source files|*.txt;*.md;*.html;*.htm;*.json;*.cs;*.ps1;*.py;*.js;*.ts|All files|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _sourceNote.Text = "Importing and indexing " + Path.GetFileName(picker.FileName) + "…";
            var bytes = await File.ReadAllBytesAsync(picker.FileName);
            using var body = new ByteArrayContent(bytes);
            body.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(picker.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : "text/plain");
            var url = "/api/v1/sources/file?fileName=" + Uri.EscapeDataString(Path.GetFileName(picker.FileName)) +
                      "&title=" + Uri.EscapeDataString(Path.GetFileName(picker.FileName)) +
                      "&origin=" + Uri.EscapeDataString(picker.FileName) +
                      "&queueToasting=" + (_queueToasting.Checked ? "true" : "false");
            var response = await _http.PostAsync(url, body);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(responseText);

            using var result = JsonDocument.Parse(responseText);
            var duplicate = result.RootElement.TryGetProperty("duplicate", out var d) && d.GetBoolean();
            var queued = result.RootElement.TryGetProperty("jobsQueued", out var jq) ? jq.GetInt32() : 0;
            MessageBox.Show(this,
                duplicate ? "That exact source is already in Toaster." : $"Source imported into Toaster.\n\nQueued toasting jobs: {queued}",
                "Toaster", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Import failed: " + ex.Message, "Toaster", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _sourceNote.Text = "Import manuals and research into Toaster. PDFs are extracted page-by-page; text, Markdown, HTML, JSON and source files are sectioned locally. Scanned/image-only PDFs currently require OCR before import.";
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var status = await _http.GetFromJsonAsync<JsonElement>("/api/v1/status");
            _status.Text = $"Service: {status.GetProperty("service").GetString()}    Toast: {status.GetProperty("toastCount").GetInt64()}    Sources: {status.GetProperty("sourceCount").GetInt64()}    Observations: {status.GetProperty("observationCount").GetInt64()}    Pending toasting: {status.GetProperty("pendingToastingJobs").GetInt64()}";
            var x = await _http.GetFromJsonAsync<JsonElement>("/api/v1/integrations");
            _endpoint.Text = x.GetProperty("mcpEndpoint").GetString()!;
            var instructions = x.GetProperty("agentInstructions").GetString();
            _config.Text = "Generic MCP connection:\r\n\r\nName: Toaster\r\nTransport: Streamable HTTP\r\nURL: " + _endpoint.Text +
                           "\r\n\r\nSuggested agent instructions:\r\n\r\n" + instructions +
                           "\r\n\r\nJSON-style configuration:\r\n\r\n" + JsonSerializer.Serialize(x.GetProperty("examples").GetProperty("json"), new JsonSerializerOptions { WriteIndented = true });
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
