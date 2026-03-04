using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Http;

namespace WindowsProxyService.Controllers
{
    [RoutePrefix("api")]
    public class ProxyController : ApiController
    {
        private static readonly HttpClient Http = HttpClientProvider.Instance;

        // ANY http://<host>:5052/api/proxy/{*path}
        // Examples:
        //   GET /api/proxy/v1/forecast?latitude=32.7767&longitude=-96.7970&current_weather=true
        // Proxies to:
        //   {ProxyUrl}/v1/forecast?...
        [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
        [Route("proxy/{*path}")]
        public async Task<HttpResponseMessage> Proxy(string path = "")
        {
            var settings = AppState.Settings;
            if (settings == null || string.IsNullOrWhiteSpace(settings.ProxyUrl))
            {
                return Request.CreateErrorResponse(
                    System.Net.HttpStatusCode.BadRequest,
                    "ProxyUrl is not configured. Set ProxyUrl in settings.json."
                );
            }

            // Build target URL: ProxyUrl + "/" + path + querystring
            var baseUri = new Uri(settings.ProxyUrl.TrimEnd('/') + "/");
            var relative = (path ?? "").TrimStart('/');
            var query = Request.RequestUri.Query; // includes leading '?', or empty

            var targetUri = new Uri(baseUri, relative + query);

            var upstreamRequest = new HttpRequestMessage(Request.Method, targetUri);

            // Copy body if present
            if (Request.Content != null && Request.Method != HttpMethod.Get && Request.Method != HttpMethod.Head)
            {
                var bodyBytes = await Request.Content.ReadAsByteArrayAsync();
                upstreamRequest.Content = new ByteArrayContent(bodyBytes);

                // Copy content headers (Content-Type etc.)
                foreach (var h in Request.Content.Headers)
                    upstreamRequest.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            // Copy request headers (avoid hop-by-hop headers)
            foreach (var header in Request.Headers)
            {
                if (IsHopByHopHeader(header.Key)) continue;
                upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Optional: add X-Forwarded headers
            upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Host", Request.RequestUri.Host);
            upstreamRequest.Headers.TryAddWithoutValidation("X-Forwarded-Proto", Request.RequestUri.Scheme);

            HttpResponseMessage upstreamResponse;
            try
            {
                upstreamResponse = await Http.SendAsync(
                    upstreamRequest,
                    HttpCompletionOption.ResponseHeadersRead
                );
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(
                    System.Net.HttpStatusCode.BadGateway,
                    "Proxy request failed: " + ex.Message
                );
            }

            // Build response
            var clientResponse = Request.CreateResponse(upstreamResponse.StatusCode);

            // Copy response headers (avoid hop-by-hop)
            foreach (var h in upstreamResponse.Headers)
            {
                if (IsHopByHopHeader(h.Key)) continue;
                clientResponse.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            if (upstreamResponse.Content != null)
            {
                var respBytes = await upstreamResponse.Content.ReadAsByteArrayAsync();
                clientResponse.Content = new ByteArrayContent(respBytes);

                foreach (var h in upstreamResponse.Content.Headers)
                {
                    if (IsHopByHopHeader(h.Key)) continue;
                    clientResponse.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
            }

            return clientResponse;
        }

        private static bool IsHopByHopHeader(string headerName)
        {
            // hop-by-hop headers that should not be forwarded by proxies
            string[] hopByHop =
            {
                "Connection",
                "Keep-Alive",
                "Proxy-Authenticate",
                "Proxy-Authorization",
                "TE",
                "Trailer",
                "Transfer-Encoding",
                "Upgrade",
                "Host"
            };

            return hopByHop.Any(h => headerName.Equals(h, StringComparison.OrdinalIgnoreCase));
        }
    }
}