using System.ServiceProcess;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// UseWindowsService (not AddWindowsService) also sets ContentRoot to
// AppContext.BaseDirectory so UseStaticFiles finds wwwroot next to the exe.
builder.Host.UseWindowsService(o => o.ServiceName = "WindowsDashboardService");
builder.Services.AddHttpClient();
builder.WebHost.UseUrls("http://+:5051");

// Load services.json from the same directory as this exe
var configPath = Path.Combine(AppContext.BaseDirectory, "services.json");
var instances  = JsonSerializer.Deserialize<InstanceConfig[]>(
    File.ReadAllText(configPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

// Default test path per service — a simple GET that returns interesting JSON
var testPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["OpenMeteo"]        = "/v1/forecast?latitude=40.7128&longitude=-74.0060&current_weather=true&forecast_days=1",
    ["CatFacts"]         = "/facts?limit=1",
    ["JsonPlaceholder"]  = "/todos/1",
    ["DogCeo"]           = "/api/breeds/image/random",
    ["ChuckNorris"]      = "/jokes/random",
};

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
var app = builder.Build();

// Serve wwwroot/index.html at "/"
app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------------------------------------------------------------------
// GET /api/services  — list all proxy services with Windows service status.
// When the service is not registered in SCM (e.g. running directly in dev),
// falls back to an HTTP probe on the port so the dashboard still works.
// ---------------------------------------------------------------------------
app.MapGet("/api/services", async (IHttpClientFactory cf) =>
{
    var probe  = cf.CreateClient();
    probe.Timeout = TimeSpan.FromSeconds(2);

    var tasks = instances.Select(async inst =>
    {
        var svcName = $"WindowsProxyService.{inst.InstanceName}";
        string status;
        try
        {
            using var sc = new ServiceController(svcName);
            status = sc.Status.ToString();
        }
        catch
        {
            // Not registered as a Windows service (dev mode) — probe the port instead.
            try
            {
                await probe.GetAsync($"http://localhost:{inst.Port}/");
                status = "Running";
            }
            catch
            {
                status = "Stopped";
            }
        }

        return new
        {
            inst.InstanceName,
            inst.ServiceDescription,
            inst.ProxyUrl,
            inst.Port,
            ServiceName = svcName,
            Status      = status,
            TestPath    = testPaths.GetValueOrDefault(inst.InstanceName, "/"),
        };
    });

    return await Task.WhenAll(tasks);
});

// ---------------------------------------------------------------------------
// POST /api/services/{name}/test  — call the local proxy, return the response.
// Optional JSON body: { "path": "/custom/path?foo=bar" }
// Omit body (or leave path blank) to use the default test path.
// ---------------------------------------------------------------------------
app.MapPost("/api/services/{name}/test", async (string name, TestRequest? req, IHttpClientFactory cf) =>
{
    var inst = instances.FirstOrDefault(i =>
        string.Equals(i.InstanceName, name, StringComparison.OrdinalIgnoreCase));
    if (inst is null) return Results.NotFound();

    var rawPath = req?.Path?.Trim();
    var path    = string.IsNullOrEmpty(rawPath) ? testPaths.GetValueOrDefault(name, "/") : rawPath;
    if (!path.StartsWith('/')) path = '/' + path;
    var url    = $"http://localhost:{inst.Port}{path}";
    var client = cf.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(10);

    try
    {
        var resp = await client.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();

        // Try to parse as JSON so the browser can pretty-print it
        object? parsed = null;
        try { parsed = JsonSerializer.Deserialize<JsonElement>(body); } catch { }

        return Results.Ok(new
        {
            Url        = url,
            StatusCode = (int)resp.StatusCode,
            Body       = parsed ?? (object)body,
            Error      = (string?)null,
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { Url = url, StatusCode = 0, Body = (object?)null, Error = ex.Message });
    }
});

// ---------------------------------------------------------------------------
// POST /api/services/{name}/start|stop  — control Windows service lifecycle
// (Dashboard service runs as LocalSystem which has SERVICE_START/STOP rights)
// ---------------------------------------------------------------------------
app.MapPost("/api/services/{name}/start", (string name) =>
{
    var svcName = $"WindowsProxyService.{name}";
    try
    {
        using var sc = new ServiceController(svcName);
        if (sc.Status != ServiceControllerStatus.Running)
            sc.Start();
        return Results.Ok(new { Status = "Starting" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/services/{name}/stop", (string name) =>
{
    var svcName = $"WindowsProxyService.{name}";
    try
    {
        using var sc = new ServiceController(svcName);
        if (sc.Status == ServiceControllerStatus.Running)
            sc.Stop();
        return Results.Ok(new { Status = "Stopping" });
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

await app.RunAsync();

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------
record InstanceConfig(
    string InstanceName,
    string ServiceDescription,
    string Host,
    int    Port,
    string ProxyUrl);

record TestRequest(string? Path);
