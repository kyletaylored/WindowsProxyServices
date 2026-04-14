using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ServiceProcess;
using System.Text.Json;

namespace WindowsTrayApp;

public sealed class TrayApplicationContext : ApplicationContext
{
    private const string DashboardUrl  = "http://localhost:5051";
    private const int    StatusOffset  = 5; // menu index where per-service items begin

    private readonly NotifyIcon      _tray;
    private readonly HttpClient      _http    = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly List<string>    _names   = LoadServiceNames();
    private readonly ContextMenuStrip _menu;

    public TrayApplicationContext()
    {
        _menu = BuildMenu();
        _menu.Opening += (_, _) => RefreshStatus();

        _tray = new NotifyIcon
        {
            Icon             = CreateIcon(),
            Text             = "Windows Proxy Services",
            ContextMenuStrip = _menu,
            Visible          = true,
        };

        _tray.DoubleClick += (_, _) => OpenDashboard();
    }

    // ── Menu construction ─────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };

        // Open Dashboard (bold — primary action)
        var openItem = new ToolStripMenuItem("Open Dashboard")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        };
        openItem.Click += (_, _) => OpenDashboard();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "Start All", () => ControlAll("start"));
        AddItem(menu, "Stop All",  () => ControlAll("stop"));
        menu.Items.Add(new ToolStripSeparator());

        // One sub-menu per service  (StatusOffset = 5 items above)
        foreach (var name in _names)
        {
            var sub = new ToolStripMenuItem(name);
            var n   = name; // capture for lambda
            sub.DropDownItems.Add("Start", null, (_, _) => ControlService(n, "start"));
            sub.DropDownItems.Add("Stop",  null, (_, _) => ControlService(n, "stop"));
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "Exit", () => { _tray.Visible = false; Application.Exit(); });
        return menu;
    }

    private static void AddItem(ToolStrip menu, string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    // ── Status refresh ────────────────────────────────────────────────────────

    private void RefreshStatus()
    {
        for (var i = 0; i < _names.Count; i++)
        {
            var svcName = $"WindowsProxyService.{_names[i]}";
            string dot;
            try
            {
                using var sc = new ServiceController(svcName);
                dot = sc.Status == ServiceControllerStatus.Running ? "● " : "○ ";
            }
            catch { dot = "? "; }

            if (StatusOffset + i < _menu.Items.Count)
                ((ToolStripMenuItem)_menu.Items[StatusOffset + i]).Text = dot + _names[i];
        }
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void OpenDashboard() =>
        Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });

    private async void ControlAll(string action)
    {
        foreach (var name in _names)
            await PostAsync($"/api/services/{name}/{action}");
    }

    private async void ControlService(string name, string action) =>
        await PostAsync($"/api/services/{name}/{action}");

    private async Task PostAsync(string path)
    {
        try
        {
            var r = await _http.PostAsync(DashboardUrl + path, null);
            if (!r.IsSuccessStatusCode)
            {
                var body = await r.Content.ReadAsStringAsync();
                MessageBox.Show($"Request failed ({(int)r.StatusCode}):\n{body}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (HttpRequestException)
        {
            MessageBox.Show(
                "Cannot reach the Dashboard Service.\n" +
                "Make sure WindowsDashboardService is running\n" +
                $"({DashboardUrl})",
                "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> LoadServiceNames()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "services.json");
            var docs = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(path))!;
            return [.. docs.Select(d => d.GetProperty("InstanceName").GetString()!)];
        }
        catch
        {
            // Fall back to the known static list so the tray works even when run standalone
            return ["OpenMeteo", "CatFacts", "JsonPlaceholder", "DogCeo", "ChuckNorris"];
        }
    }

    private static Icon CreateIcon()
    {
        const int S = 16;
        using var bmp = new Bitmap(S, S);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Blue filled circle
            g.FillEllipse(new SolidBrush(Color.FromArgb(59, 130, 246)), 0, 0, S - 1, S - 1);
            // "WP" label centred
            using var font = new Font("Segoe UI", 5.5f, FontStyle.Bold);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("WP", font, Brushes.White, new RectangleF(0, 0, S, S), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tray.Dispose(); _http.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
