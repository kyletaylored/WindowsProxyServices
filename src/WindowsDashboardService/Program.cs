using System.ServiceProcess;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Host setup
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(o => o.ServiceName = "WindowsDashboardService");
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
// GET /api/services  — list all proxy services with Windows service status
// ---------------------------------------------------------------------------
app.MapGet("/api/services", () =>
    instances.Select(inst =>
    {
        var svcName = $"WindowsProxyService.{inst.InstanceName}";
        string status;
        try   { using var sc = new ServiceController(svcName); status = sc.Status.ToString(); }
        catch { status = "NotFound"; }

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
    }));

// ---------------------------------------------------------------------------
// POST /api/services/{name}/test  — call the local proxy, return the response
// ---------------------------------------------------------------------------
app.MapPost("/api/services/{name}/test", async (string name, IHttpClientFactory cf) =>
{
    var inst = instances.FirstOrDefault(i =>
        string.Equals(i.InstanceName, name, StringComparison.OrdinalIgnoreCase));
    if (inst is null) return Results.NotFound();

    var path   = testPaths.GetValueOrDefault(name, "/");
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
