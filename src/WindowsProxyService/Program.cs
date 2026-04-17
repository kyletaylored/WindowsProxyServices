using System.Text.Json;
using WindowsProxyService;

// ---------------------------------------------------------------------------
// 1. Parse --name arguments
//    Supports one or many names:
//      --name OpenMeteo
//      --name OpenMeteo JsonPlaceholder
//      --name OpenMeteo --name JsonPlaceholder
//      --name * | --all              (every service in services.json)
// ---------------------------------------------------------------------------
var parsed   = ArgParser.Parse(args);
var startAll = parsed.StartAll;
var names    = parsed.Names;

if (!startAll && names.Count == 0)
{
    Console.Error.WriteLine("ERROR: --name <InstanceName> [<InstanceName>...] is required.");
    Console.Error.WriteLine("       Use '--name *' or '--all' to start every service.");
    Console.Error.WriteLine("       Example: WindowsProxyService.exe --name OpenMeteo JsonPlaceholder");
    return 1;
}

// ---------------------------------------------------------------------------
// 2. Load services.json and resolve the requested configs
// ---------------------------------------------------------------------------
var servicesJsonPath = Path.Combine(AppContext.BaseDirectory, "services.json");
if (!File.Exists(servicesJsonPath))
{
    Console.Error.WriteLine($"ERROR: services.json not found at: {servicesJsonPath}");
    return 1;
}

var allInstances = JsonSerializer.Deserialize<List<InstanceConfig>>(
    File.ReadAllText(servicesJsonPath), InstanceConfig.JsonOptions);

if (allInstances is null || allInstances.Count == 0)
{
    Console.Error.WriteLine("ERROR: services.json is empty or could not be parsed.");
    return 1;
}

// Entries whose ProxyUrl is not an HTTP/HTTPS address (e.g. SqlService uses
// "sql://...") are listed in services.json only so the dashboard can enumerate
// them.  They must not be started as YARP proxies.
var proxyInstances = allInstances
    .Where(x => x.ProxyUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
             || x.ProxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    .ToList();

List<InstanceConfig> configs = startAll
    ? proxyInstances
    : [.. proxyInstances.Where(x => names.Contains(x.InstanceName, StringComparer.OrdinalIgnoreCase))];

var missing = names
    .Except(configs.Select(c => c.InstanceName), StringComparer.OrdinalIgnoreCase)
    .ToList();

if (missing.Count > 0)
{
    Console.Error.WriteLine($"ERROR: Unknown service name(s): {string.Join(", ", missing)}");
    Console.Error.WriteLine($"       Available: {string.Join(", ", proxyInstances.Select(x => x.InstanceName))}");
    return 1;
}

// ---------------------------------------------------------------------------
// 3. Run one WebApplication per selected service, all concurrently.
//    UseWindowsService is only wired when a single instance is started
//    (the normal Windows service deployment path). Multi-instance mode is
//    a convenience for local development and runs as a plain console app.
// ---------------------------------------------------------------------------
var isSingle = configs.Count == 1;
await Task.WhenAll(configs.Select(cfg => ProxyHost.RunAsync(cfg, args, isSingle)));
return 0;

// Required for WebApplicationFactory<Program> in test projects.
public partial class Program { }
