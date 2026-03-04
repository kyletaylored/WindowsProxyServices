using System;
using System.Net.Http;
using System.Net;

namespace WindowsProxyService
{
    public static class HttpClientProvider
    {
        public static readonly HttpClient Instance;

        static HttpClientProvider()
        {
            // If you're behind a corporate proxy, HttpClientHandler will typically pick it up automatically.
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            Instance = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            Instance.DefaultRequestHeaders.UserAgent.ParseAdd("WindowsProxyService-Proxy/1.0");
        }
    }
}