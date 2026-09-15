using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    public static void Main()
    {
        ApplicationConfiguration.Initialize();
        DarkMode.Enable();
        Application.Run(new MainForm());
    }
}

sealed class MainForm : Form
{
    private const string SourceHelp =
        "Everything below runs on this machine. Nothing leaves the box until a connected model asks for it.";

    private static readonly string Pipeline = string.Join("\r\n\r\n", new[]
    {
        "01  EXTRACT    PDFs page-by-page; scanned or image-heavy pages fall back to local OCR",
        "02  SECTION    text, Markdown, HTML, JSON and source files split into retrievable sections",
        "03  QUEUE      sections queued for toasting by your connected model or provider",
        "04  DISTILL    results return as Toast — compact, reusable technical memory",
        "ACCEPTED   pdf · txt · md · html · json · cs · ps1 · py · js · ts"
    });

    private readonly HeaderBar _header = new() { Dock = DockStyle.Fill };
    private readonly NavStrip _nav = new("Status", "Integrations", "Manuals & Sources") { Dock = DockStyle.Fill };
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };

    private readonly StatusPill _pill = new() { Dock = DockStyle.Fill };
    private readonly MetricTile _toast = new() { Caption = "Toast", Rule = Theme.Accent, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    private readonly MetricTile _sources = new() { Caption = "Sources", Rule = Theme.Cyan, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    private readonly MetricTile _observations = new() { Caption = "Observations", Rule = Theme.Violet, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
    private readonly MetricTile _pending = new() { Caption = "Pending toasting", Rule = Theme.Ok, Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly Label _readout = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.Transparent,
        ForeColor = Theme.Muted,
        Font = Theme.Mono(8.5f),
        Text = ""
    };

    private readonly TextBox _endpoint = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface,
        ForeColor = Theme.Cyan,
        Font = Theme.Mono(10f, FontStyle.Bold)
    };

    private readonly ConsoleBox _config = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill
    };

    private readonly FlatCheck _queueToasting =
        new("Queue imported manuals for toasting by my connected model or provider") { Checked = true, Dock = DockStyle.Fill };

    private readonly Label _sourceNote = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.Transparent,
        ForeColor = Theme.Muted,
        Font = Theme.Display(9f, FontStyle.Regular),
        Text = SourceHelp
    };

    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:47321"), Timeout = TimeSpan.FromMinutes(10) };
    private readonly Icon _appIcon = LoadAppIcon();
    private readonly Icon _trayIcon;
    private readonly NotifyIcon _notify;
    private bool _reallyExit;

    /// <summary>
    /// The mark ships as an embedded multi-resolution icon, so the window, the taskbar
    /// and the tray all draw the frame that fits rather than rescaling one size.
    /// </summary>
    private static Icon LoadAppIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Toaster.ico");
        if (stream is not null) return new Icon(stream);
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }

    public MainForm()
    {
        Text = "Toaster";
        Icon = _appIcon;
        ShowIcon = true;
        Width = 880;
        Height = 600;
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.BgBottom;
        ForeColor = Theme.Text;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Toaster", null, (_, _) => ShowFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _reallyExit = true; _notify!.Visible = false; Close(); });
        _trayIcon = new Icon(_appIcon, SystemInformation.SmallIconSize);
        _notify = new NotifyIcon
        {
            Text = "Toaster",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notify.DoubleClick += (_, _) => ShowFromTray();

        var root = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 18) };
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(_header, 0, 0);
        shell.Controls.Add(_nav, 0, 1);
        shell.Controls.Add(_pages, 0, 2);
        root.Controls.Add(shell);
        Controls.Add(root);

        var status = BuildStatusPage();
        var integrations = BuildIntegrationsPage();
        var sources = BuildSourcesPage();
        var pages = new[] { status, integrations, sources };
        _pages.Controls.AddRange(pages);
        void ShowPage()
        {
            for (var i = 0; i < pages.Length; i++) pages[i].Visible = i == _nav.SelectedIndex;
            pages[_nav.SelectedIndex].BringToFront();
        }
        _nav.SelectedChanged += (_, _) => ShowPage();
        ShowPage();

        Shown += async (_, _) => await RefreshAsync();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosing += (_, e) => { if (!_reallyExit) { e.Cancel = true; Hide(); } };
    }

    // Dark chrome so the system title bar matches the glass body.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TrySetWindowAttribute(20, 1);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        TrySetWindowAttribute(33, 1);   // DWMWA_WINDOW_CORNER_PREFERENCE = do not round
        TrySetWindowAttribute(34, 0x00202A36); // DWMWA_BORDER_COLOR (BGR)
        TrySetWindowAttribute(35, 0x00131C25); // DWMWA_CAPTION_COLOR (BGR)
        TrySetWindowAttribute(36, 0x00C5A88B); // DWMWA_TEXT_COLOR (BGR)
    }

    private void TrySetWindowAttribute(int attribute, int value)
    {
        try { DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int)); }
        catch (DllNotFoundException) { /* pre-Vista shells only; cosmetic */ }
        catch (EntryPointNotFoundException) { }
    }

    private static Control Page() => new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 16, 0, 0) };

    private static TableLayoutPanel Rows(params float[] heights)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = heights.Length };
        foreach (var h in heights)
            table.RowStyles.Add(h < 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, h));
        return table;
    }

    private Control BuildStatusPage()
    {
        var page = Page();
        var rows = Rows(40, 14, 112, 22, -1);

        var head = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        var refresh = new FlatButton("Refresh") { Dock = DockStyle.Fill, Margin = new Padding(12, 0, 0, 0) };
        refresh.Click += async (_, _) => await RefreshAsync();
        head.Controls.Add(_pill, 0, 0);
        head.Controls.Add(refresh, 1, 0);

        var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 4, RowCount = 1 };
        for (var i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        tiles.Controls.Add(_toast, 0, 0);
        tiles.Controls.Add(_sources, 1, 0);
        tiles.Controls.Add(_observations, 2, 0);
        tiles.Controls.Add(_pending, 3, 0);

        var readoutCard = new CardPanel { Dock = DockStyle.Fill, Fill = Theme.SurfaceSunken, Padding = new Padding(16, 14, 16, 14) };
        readoutCard.Controls.Add(_readout);

        rows.Controls.Add(head, 0, 0);
        rows.Controls.Add(new Panel { BackColor = Color.Transparent, Dock = DockStyle.Fill }, 0, 1);
        rows.Controls.Add(tiles, 0, 2);
        rows.Controls.Add(new SectionLabel("Live readout") { Dock = DockStyle.Fill }, 0, 3);
        rows.Controls.Add(readoutCard, 0, 4);
        page.Controls.Add(rows);
        return page;
    }

    private Control BuildIntegrationsPage()
    {
        var page = Page();
        var rows = Rows(22, 46, 12, 40, 26, -1);

        var endpointCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 14, 12) };
        endpointCard.Controls.Add(_endpoint);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var copyAddress = new FlatButton("Copy MCP address") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
        copyAddress.Click += (_, _) => CopyToClipboard(_endpoint.Text);
        var copyConfig = new FlatButton("Copy configuration", primary: true) { Dock = DockStyle.Fill };
        copyConfig.Click += (_, _) => CopyToClipboard(_config.Text);
        buttons.Controls.Add(copyAddress, 0, 0);
        buttons.Controls.Add(copyConfig, 1, 0);

        var configCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 14, 10, 14) };
        configCard.Controls.Add(_config);

        rows.Controls.Add(new SectionLabel("Endpoint") { Dock = DockStyle.Fill }, 0, 0);
        rows.Controls.Add(endpointCard, 0, 1);
        rows.Controls.Add(new Panel { BackColor = Color.Transparent, Dock = DockStyle.Fill }, 0, 2);
        rows.Controls.Add(buttons, 0, 3);
        rows.Controls.Add(new SectionLabel("Configuration + agent instructions") { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) }, 0, 4);
        rows.Controls.Add(configCard, 0, 5);
        page.Controls.Add(rows);
        return page;
    }

    private Control BuildSourcesPage()
    {
        var page = Page();
        var rows = Rows(22, 50, 44, 24, -1);

        var importRow = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        importRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        importRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var import = new FlatButton("Import manual or research file", primary: true) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        import.Click += async (_, _) => await ImportSourceAsync();
        importRow.Controls.Add(import, 0, 0);

        var noteCard = new CardPanel { Dock = DockStyle.Fill, Fill = Theme.SurfaceSunken, Padding = new Padding(18, 14, 18, 16) };
        var noteRows = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2 };
        noteRows.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        noteRows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        noteRows.Controls.Add(_sourceNote, 0, 0);
        noteRows.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ForeColor = Theme.Muted,
            Font = Theme.Mono(8.5f),
            Text = Pipeline
        }, 0, 1);
        noteCard.Controls.Add(noteRows);

        rows.Controls.Add(new SectionLabel("Ingest") { Dock = DockStyle.Fill }, 0, 0);
        rows.Controls.Add(importRow, 0, 1);
        rows.Controls.Add(_queueToasting, 0, 2);
        rows.Controls.Add(new SectionLabel("How ingestion works") { Dock = DockStyle.Fill }, 0, 3);
        rows.Controls.Add(noteCard, 0, 4);
        page.Controls.Add(rows);
        return page;
    }

    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Clipboard.SetText(text);
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
            _sourceNote.Text = SourceHelp;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var status = await _http.GetFromJsonAsync<JsonElement>("/api/v1/status");
            var service = status.GetProperty("service").GetString() ?? "unknown";
            var pendingJobs = status.GetProperty("pendingToastingJobs").GetInt64();

            _toast.Value = Format(status.GetProperty("toastCount").GetInt64());
            _sources.Value = Format(status.GetProperty("sourceCount").GetInt64());
            _observations.Value = Format(status.GetProperty("observationCount").GetInt64());
            _pending.Value = Format(pendingJobs);
            _pending.Rule = pendingJobs > 0 ? Theme.Accent : Theme.Ok;
            _pending.Suffix = pendingJobs > 0 ? "queued" : "idle";

            var healthy = service.Equals("ok", StringComparison.OrdinalIgnoreCase);
            _pill.Set(healthy ? "Service online" : "Service " + service, healthy ? Theme.Ok : Theme.Accent, "127.0.0.1:47321");

            var x = await _http.GetFromJsonAsync<JsonElement>("/api/v1/integrations");
            _endpoint.Text = x.GetProperty("mcpEndpoint").GetString()!;
            var instructions = x.GetProperty("agentInstructions").GetString();
            _config.Text = "Generic MCP connection:\r\n\r\nName: Toaster\r\nTransport: Streamable HTTP\r\nURL: " + _endpoint.Text +
                           "\r\n\r\nSuggested agent instructions:\r\n\r\n" + instructions +
                           "\r\n\r\nJSON-style configuration:\r\n\r\n" + JsonSerializer.Serialize(x.GetProperty("examples").GetProperty("json"), new JsonSerializerOptions { WriteIndented = true });

            _header.Endpoint = _endpoint.Text;
            _header.Invalidate();
            _readout.Text = Readout(
                ("endpoint", _endpoint.Text),
                ("transport", "streamable http"),
                ("version", Read(status, "version")),
                ("data", Read(status, "dataPath")),
                ("service", service),
                ("queue", pendingJobs > 0 ? pendingJobs + " job(s) awaiting a connected model" : "drained"),
                ("checked", DateTime.Now.ToString("HH:mm:ss")));
            _notify.Text = "Toaster — service healthy";
        }
        catch (Exception ex)
        {
            foreach (var tile in new[] { _toast, _sources, _observations, _pending }) tile.Value = "—";
            _pending.Suffix = "";
            _pill.Set("Service offline", Theme.Bad, "127.0.0.1:47321");
            _endpoint.Text = "http://127.0.0.1:47321/mcp";
            _config.Text = "Start the Toaster service, then refresh.\r\n\r\nExpected MCP URL: " + _endpoint.Text;
            _header.Endpoint = _endpoint.Text;
            _header.Invalidate();
            _readout.Text = Readout(
                ("endpoint", _endpoint.Text),
                ("service", "unreachable"),
                ("reason", ex.Message),
                ("action", "start the Toaster service, then press refresh"),
                ("checked", DateTime.Now.ToString("HH:mm:ss")));
            _notify.Text = "Toaster — service unavailable";
        }
    }

    private static string Format(long value) => value.ToString("#,0");

    private static string Read(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? "—" : "—";

    private static string Readout(params (string Key, string Value)[] lines) =>
        string.Join("\r\n", lines.Select(l => l.Key.ToUpperInvariant().PadRight(10) + "  " + l.Value));

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _notify.Dispose(); _trayIcon.Dispose(); _appIcon.Dispose(); _http.Dispose(); }
        base.Dispose(disposing);
    }
}
