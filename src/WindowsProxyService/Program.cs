using System.Text.Json;
using Yarp.ReverseProxy.Configuration;
using WindowsProxyService;

// ---------------------------------------------------------------------------
// 1. Parse --name argument
// ---------------------------------------------------------------------------
var instanceName = string.Empty;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i].Equals("--name", StringComparison.OrdinalIgnoreCase))
    {
        instanceName = args[i + 1];
        break;
    }
}

if (string.IsNullOrWhiteSpace(instanceName))
{
    Console.Error.WriteLine("ERROR: --name <InstanceName> is required.");
    Console.Error.WriteLine("       Example: WindowsProxyService.exe --name OpenMeteo");
    return 1;
}

// ---------------------------------------------------------------------------
// 2. Load services.json and find matching instance config
// ---------------------------------------------------------------------------
var servicesJsonPath = Path.Combine(AppContext.BaseDirectory, "services.json");

if (!File.Exists(servicesJsonPath))
{
    Console.Error.WriteLine($"ERROR: services.json not found at: {servicesJsonPath}");
    return 1;
}

var allInstances = JsonSerializer.Deserialize<List<InstanceConfig>>(
    File.ReadAllText(servicesJsonPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

if (allInstances is null || allInstances.Count == 0)
{
    Console.Error.WriteLine("ERROR: services.json is empty or could not be parsed.");
    return 1;
}

var config = allInstances.FirstOrDefault(x =>
    x.InstanceName.Equals(instanceName, StringComparison.OrdinalIgnoreCase));

if (config is null)
{
    Console.Error.WriteLine($"ERROR: No instance named '{instanceName}' found in services.json.");
    Console.Error.WriteLine($"       Available: {string.Join(", ", allInstances.Select(x => x.InstanceName))}");
    return 1;
}

// ---------------------------------------------------------------------------
// 3. Build the WebApplication host
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// Windows Service lifecycle (no-op on non-Windows)
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = $"WindowsProxyService.{config.InstanceName}";
});

// Listen on the instance-specific port
builder.WebHost.UseUrls($"http://{config.Host}:{config.Port}");

// ---------------------------------------------------------------------------
// 4. JSON structured logging
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
});

// ---------------------------------------------------------------------------
// 5. YARP — one route, one cluster, destination = ProxyUrl
// ---------------------------------------------------------------------------
var routes = new[]
{
    new RouteConfig
    {
        RouteId   = "catch-all",
        ClusterId = "upstream",
        Match     = new RouteMatch { Path = "{**catch-all}" }
    }
};

var clusters = new[]
{
    new ClusterConfig
    {
        ClusterId    = "upstream",
        Destinations = new Dictionary<string, DestinationConfig>
        {
            ["primary"] = new DestinationConfig { Address = config.ProxyUrl }
        }
    }
};

builder.Services.AddReverseProxy()
    .LoadFromMemory(routes, clusters);

// Make instance config available for the status endpoint
builder.Services.AddSingleton(config);

// ---------------------------------------------------------------------------
// 6. Build and configure the pipeline
// ---------------------------------------------------------------------------
var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

// Scope every request log entry with the instance name
app.Use(async (context, next) =>
{
    using var _ = logger.BeginScope(new Dictionary<string, object?> { ["Instance"] = config.InstanceName });
    await next(context);
});

// Lightweight status endpoint — does not go through YARP
app.MapGet("/api/status", (InstanceConfig cfg) => Results.Ok(new
{
    ok              = true,
    serverTimeUtc   = DateTime.UtcNow,
    instance        = cfg.InstanceName,
    host            = cfg.Host,
    port            = cfg.Port,
    proxyUrl        = cfg.ProxyUrl
}));

// All other traffic is proxied upstream
app.MapReverseProxy();

logger.LogInformation(
    "Proxy '{InstanceName}' starting on {Host}:{Port}, forwarding to {ProxyUrl}",
    config.InstanceName, config.Host, config.Port, config.ProxyUrl);

await app.RunAsync();
return 0;
