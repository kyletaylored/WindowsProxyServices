using System;
using System.Runtime.InteropServices;
using System.Web.Http;

namespace WindowsProxyService.Controllers
{
    [RoutePrefix("api")]
    public class StatusController : ApiController
    {
        [HttpGet]
        [Route("status")]
        public IHttpActionResult Status()
        {
            var s = AppState.Settings;

            return Ok(new
            {
                ok = true,
                serverTimeUtc = DateTime.UtcNow,
                instance = s?.InstanceName,
                host = s?.Host,
                port = s?.Port,
                proxyUrl = s?.ProxyUrl
            });
        }
    }
}