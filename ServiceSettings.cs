namespace WindowsProxyService
{
    public class ServiceSettings
    {
        public int Port { get; set; } = 5052;
        public string Host { get; set; } = "+";
        public string InstanceName { get; set; } = "WindowsProxyService";
        public string ProxyUrl { get; set; } = null;
    }
}