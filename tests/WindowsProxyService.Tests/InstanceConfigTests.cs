using System.Text.Json;
using WindowsProxyService;

namespace WindowsProxyService.Tests;

public class InstanceConfigTests
{
    [Fact]
    public void Deserializes_AllFields()
    {
        const string json = """
            {
              "InstanceName":       "OpenMeteo",
              "ServiceDescription": "Weather API proxy",
              "Host":               "+",
              "Port":               5052,
              "ProxyUrl":           "https://api.open-meteo.com"
            }
            """;

        var cfg = JsonSerializer.Deserialize<InstanceConfig>(json, InstanceConfig.JsonOptions)!;

        Assert.Equal("OpenMeteo",               cfg.InstanceName);
        Assert.Equal("Weather API proxy",       cfg.ServiceDescription);
        Assert.Equal("+",                       cfg.Host);
        Assert.Equal(5052,                      cfg.Port);
        Assert.Equal("https://api.open-meteo.com", cfg.ProxyUrl);
    }

    [Fact]
    public void Deserializes_DefaultValues_WhenFieldsOmitted()
    {
        var cfg = JsonSerializer.Deserialize<InstanceConfig>(
            """{ "InstanceName": "Test" }""", InstanceConfig.JsonOptions)!;

        Assert.Equal("Test", cfg.InstanceName);
        Assert.Equal("+",    cfg.Host);
        Assert.Equal(5052,   cfg.Port);
        Assert.Equal("",     cfg.ServiceDescription);
        Assert.Equal("",     cfg.ProxyUrl);
    }

    [Fact]
    public void Deserializes_CaseInsensitiveKeys()
    {
        var cfg = JsonSerializer.Deserialize<InstanceConfig>(
            """{ "instancename": "Lowercase", "port": 9000 }""", InstanceConfig.JsonOptions)!;

        Assert.Equal("Lowercase", cfg.InstanceName);
        Assert.Equal(9000,        cfg.Port);
    }

    [Fact]
    public void Deserializes_Array_MatchingServicesJsonShape()
    {
        const string json = """
            [
              { "InstanceName": "OpenMeteo",  "Port": 5052, "ProxyUrl": "https://api.open-meteo.com",          "Host": "+" },
              { "InstanceName": "SqlService", "Port": 5055, "ProxyUrl": "sql://localhost\\SQLEXPRESS/WpsDemo",  "Host": "+" }
            ]
            """;

        var cfgs = JsonSerializer.Deserialize<List<InstanceConfig>>(json, InstanceConfig.JsonOptions)!;

        Assert.Equal(2,            cfgs.Count);
        Assert.Equal("OpenMeteo",  cfgs[0].InstanceName);
        Assert.Equal("SqlService", cfgs[1].InstanceName);
    }

    [Fact]
    public void ProxyUrlFilter_ExcludesNonHttpEntries()
    {
        // Replicates the filtering logic in Program.cs — sql:// entries must not
        // be started as YARP proxies.
        var instances = new[]
        {
            new InstanceConfig { InstanceName = "OpenMeteo",  ProxyUrl = "https://api.open-meteo.com" },
            new InstanceConfig { InstanceName = "SqlService", ProxyUrl = @"sql://localhost\SQLEXPRESS/WpsDemo" },
        };

        var proxyOnly = instances
            .Where(x => x.ProxyUrl.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
                     || x.ProxyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Single(proxyOnly);
        Assert.Equal("OpenMeteo", proxyOnly[0].InstanceName);
    }
}
