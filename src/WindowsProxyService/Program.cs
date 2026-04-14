using System.Text.Json;
using WindowsProxyService;

// ---------------------------------------------------------------------------
// 1. Parse --name arguments
//    Supports one or many names:
//      --name OpenMeteo
//      --name OpenMeteo CatFacts DogCeo
//      --name OpenMeteo --name CatFacts
//      --name * | --all              (every service in services.json)
// ---------------------------------------------------------------------------
var names    = new List<string>();
var startAll = false;

for (var i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--all", StringComparison.OrdinalIgnoreCase))
    {
        startAll = true;
    }
    else if (args[i].Equals("--name", StringComparison.OrdinalIgnoreCase))
    {
        // Consume all following values that don't begin with '--'
        for (var j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++, i++)
        {
            if (args[j] is "*" or "all") startAll = true;
            else names.Add(args[j]);
        }
    }
}

if (!startAll && names.Count == 0)
{
    Console.Error.WriteLine("ERROR: --name <InstanceName> [<InstanceName>...] is required.");
    Console.Error.WriteLine("       Use '--name *' or '--all' to start every service.");
    Console.Error.WriteLine("       Example: WindowsProxyService.exe --name OpenMeteo CatFacts");
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
    File.ReadAllText(servicesJsonPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

if (allInstances is null || allInstances.Count == 0)
{
    Console.Error.WriteLine("ERROR: services.json is empty or could not be parsed.");
    return 1;
}

var configs = startAll
    ? allInstances
    : allInstances
        .Where(x => names.Contains(x.InstanceName, StringComparer.OrdinalIgnoreCase))
        .ToList();

var missing = names
    .Except(configs.Select(c => c.InstanceName), StringComparer.OrdinalIgnoreCase)
    .ToList();

if (missing.Count > 0)
{
    Console.Error.WriteLine($"ERROR: Unknown service name(s): {string.Join(", ", missing)}");
    Console.Error.WriteLine($"       Available: {string.Join(", ", allInstances.Select(x => x.InstanceName))}");
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
