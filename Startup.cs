using System.Web.Http;
using Owin;

namespace WindowsProxyService
{
    public static class AppState
    {
        public static ServiceSettings Settings { get; set; }
    }

    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();

            config.MapHttpAttributeRoutes();

            // Make sure errors include useful content for debugging (you can remove later)
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            app.UseWebApi(config);
        }
    }
}