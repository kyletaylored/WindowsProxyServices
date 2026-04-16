using System.Diagnostics;
using System.Security.Principal;
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
            Icon             = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
                               ?? SystemIcons.Application,
            Text             = "Windows Proxy Services",
            ContextMenuStrip = _menu,
            Visible          = true,
        };

        _tray.DoubleClick += (_, _) => OpenDashboard();
    }

    // ── Menu construction ─────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var isAdmin = IsAdministrator();
        var menu    = new ContextMenuStrip { ShowImageMargin = false };

        // Open Dashboard (bold — primary action)
        var openItem = new ToolStripMenuItem("Open Dashboard")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        };
        openItem.Click += (_, _) => OpenDashboard();
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "Start All", () => ControlAll("start"), isAdmin);
        AddItem(menu, "Stop All",  () => ControlAll("stop"),  isAdmin);
        menu.Items.Add(new ToolStripSeparator());

        // One sub-menu per service  (StatusOffset = 5 items above)
        foreach (var name in _names)
        {
            var sub = new ToolStripMenuItem(name);
            var n   = name; // capture for lambda

            var startItem = new ToolStripMenuItem("Start");
            startItem.Click   += (_, _) => ControlService(n, "start");
            startItem.Enabled  = isAdmin;

            var stopItem = new ToolStripMenuItem("Stop");
            stopItem.Click    += (_, _) => ControlService(n, "stop");
            stopItem.Enabled   = isAdmin;

            sub.DropDownItems.Add(startItem);
            sub.DropDownItems.Add(stopItem);
            menu.Items.Add(sub);
        }

        menu.Items.Add(new ToolStripSeparator());
        AddItem(menu, "Exit", () => { _tray.Visible = false; Application.Exit(); });
        return menu;
    }

    private static void AddItem(ToolStrip menu, string text, Action onClick, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
    }

    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

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
            return ["OpenMeteo", "JsonPlaceholder", "DatadogDemo"];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _tray.Dispose(); _http.Dispose(); _menu.Dispose(); }
        base.Dispose(disposing);
    }
}
