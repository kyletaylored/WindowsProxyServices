using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WindowsDashboardService.Tests;

public class EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ── GET /api/services ────────────────────────────────────────────────────

    [Fact]
    public async Task GetServices_Returns200WithArray()
    {
        var resp = await _client.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(body);
        Assert.True(body.Length > 0);
    }

    [Fact]
    public async Task GetServices_ContainsExpectedInstanceNames()
    {
        var body = await _client.GetFromJsonAsync<JsonElement[]>("/api/services");
        Assert.NotNull(body);

        var names = body.Select(e => e.GetProperty("instanceName").GetString()).ToArray();
        Assert.Contains("OpenMeteo",  names);
        Assert.Contains("SqlService", names);
    }

    [Fact]
    public async Task GetServices_EachEntryHasRequiredFields()
    {
        var body = await _client.GetFromJsonAsync<JsonElement[]>("/api/services");
        Assert.NotNull(body);

        foreach (var entry in body)
        {
            Assert.True(entry.TryGetProperty("instanceName",       out _), "missing instanceName");
            Assert.True(entry.TryGetProperty("serviceDescription", out _), "missing serviceDescription");
            Assert.True(entry.TryGetProperty("port",               out _), "missing port");
            Assert.True(entry.TryGetProperty("status",             out _), "missing status");
            Assert.True(entry.TryGetProperty("testPath",           out _), "missing testPath");
        }
    }

    // ── POST /api/services/{name}/test ───────────────────────────────────────

    [Fact]
    public async Task ServiceTest_UnknownName_Returns404()
    {
        var resp = await _client.PostAsJsonAsync("/api/services/NoSuchService/test", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── POST /api/custom-request ─────────────────────────────────────────────

    [Fact]
    public async Task CustomRequest_BlankUrl_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/custom-request", new { url = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("url is required", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CustomRequest_NullUrl_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/custom-request", new { url = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
