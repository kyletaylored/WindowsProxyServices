using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace WindowsProxyService;

internal static class ProxyHost
{
    internal static async Task RunAsync(InstanceConfig config, string[] args, bool isSingleService)
    {
        // ContentRootPath passed at construction — setting it via builder.Host.UseContentRoot()
        // after CreateBuilder throws NotSupportedException in ASP.NET Core 8.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args            = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        if (isSingleService)
            builder.Host.UseWindowsService(o => o.ServiceName = $"WindowsProxyService.{config.InstanceName}");

        builder.WebHost.UseUrls($"http://{config.Host}:{config.Port}");

        // Do NOT call ClearProviders() here — UseWindowsService adds the Windows
        // Event Log sink when running under SCM, and clearing removes it.
        // JSON console is added on top for structured output in dev/interactive mode.
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

        var sanitizedProxyUrl = config.ProxyUrl;
        if (Uri.TryCreate(config.ProxyUrl, UriKind.Absolute, out var uri))
        {
            sanitizedProxyUrl = uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped);
        }

        logger.LogInformation(
            "Proxy '{InstanceName}' starting on {Host}:{Port}, forwarding to {ProxyUrl}",
            config.InstanceName, config.Host, config.Port, sanitizedProxyUrl);

        await app.RunAsync();
    }
}
