namespace WindowsProxyService;

/// <summary>
/// Represents one entry in services.json.
/// </summary>
public sealed class InstanceConfig
{
    public string InstanceName { get; init; } = "WindowsProxyService";
    public string ServiceDescription { get; init; } = string.Empty;
    public string Host { get; init; } = "+";
    public int Port { get; init; } = 5052;
    public string ProxyUrl { get; init; } = string.Empty;
}
