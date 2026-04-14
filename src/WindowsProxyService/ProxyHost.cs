using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace WindowsProxyService;

internal static class ProxyHost
{
    internal static async Task RunAsync(InstanceConfig config, string[] args, bool isSingleService)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Set content root explicitly so the service always finds its files regardless of
        // what Directory.GetCurrentDirectory() returns when started by SCM.
        builder.Host.UseContentRoot(AppContext.BaseDirectory);

        if (isSingleService)
            builder.Host.UseWindowsService(o => o.ServiceName = $"WindowsProxyService.{config.InstanceName}");

        builder.WebHost.UseUrls($"http://{config.Host}:{config.Port}");

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes     = true;
            options.TimestampFormat   = "yyyy-MM-dd HH:mm:ss ";
            options.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
        });

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

        builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);
        builder.Services.AddSingleton(config);

        var app    = builder.Build();
        var logger = app.Services.GetRequiredService<ILoggerFactory>()
                       .CreateLogger($"WindowsProxyService.{config.InstanceName}");

        app.Use(async (context, next) =>
        {
            using var _ = logger.BeginScope(
                new Dictionary<string, object?> { ["Instance"] = config.InstanceName });
            await next(context);
        });

        app.MapGet("/api/status", (InstanceConfig cfg) => Results.Ok(new
        {
            ok            = true,
            serverTimeUtc = DateTime.UtcNow,
            instance      = cfg.InstanceName,
            host          = cfg.Host,
            port          = cfg.Port,
            proxyUrl      = cfg.ProxyUrl
        }));

        app.MapReverseProxy();

        logger.LogInformation(
            "Proxy '{InstanceName}' starting on {Host}:{Port}, forwarding to {ProxyUrl}",
            config.InstanceName, config.Host, config.Port, config.ProxyUrl);

        await app.RunAsync();
    }
}
