using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WindowsSqlService.Tests;

/// <summary>
/// Tests the /api/status endpoint's graceful-degradation behaviour.
/// These tests override WPS_SQL_CONNECTION_STRING to an unreachable address so
/// no real SQL Server is required — they verify that the service stays healthy
/// and returns a structured error response rather than crashing.
/// </summary>
public class StatusEndpointTests : IDisposable
{
    // Port 19433 is not expected to be listening anywhere, and Connect Timeout=1
    // ensures the SqlConnection attempt fails within ~1 second rather than 30.
    private const string BadConnStr =
        "Server=tcp:127.0.0.1,19433;Database=NoSuchDb;" +
        "Integrated Security=true;TrustServerCertificate=true;Connect Timeout=1;";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StatusEndpointTests()
    {
        Environment.SetEnvironmentVariable("WPS_SQL_CONNECTION_STRING", BadConnStr);
        _factory = new WebApplicationFactory<Program>();
        _client  = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        Environment.SetEnvironmentVariable("WPS_SQL_CONNECTION_STRING", null);
    }

    [Fact]
    public async Task Status_WhenDatabaseUnreachable_Returns200()
    {
        // The endpoint must always return HTTP 200, even when SQL is down.
        var resp = await _client.GetAsync("/api/status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Status_WhenDatabaseUnreachable_ReturnsErrorStatus()
    {
        var body = await _client.GetFromJsonAsync<JsonElement>("/api/status");

        Assert.Equal("error", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("error", out var errProp),
            "Response should include an 'error' field describing the failure.");
        Assert.False(string.IsNullOrEmpty(errProp.GetString()),
            "The 'error' field should contain the exception message.");
    }

    [Fact]
    public async Task Status_WhenDatabaseUnreachable_DoesNotExposeStack()
    {
        var raw = await _client.GetStringAsync("/api/status");
        Assert.DoesNotContain("at WindowsSqlService", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace",            raw, StringComparison.OrdinalIgnoreCase);
    }
}
