using System;
using System.IO;
using System.ServiceProcess;
using Microsoft.Owin.Hosting;

namespace WindowsProxyService
{
    public class Service1 : ServiceBase
    {
        private IDisposable _webApp;
        private string _baseAddress;

        public Service1()
        {
            ServiceName = "WindowsProxyService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                var settings = SettingsLoader.LoadFromAppDirectory();
                AppState.Settings = settings;

                _baseAddress = $"http://{settings.Host}:{settings.Port}/";

                Log($"Starting '{settings.InstanceName}' at {_baseAddress} with ProxyUrl='{settings.ProxyUrl ?? "(null)"}'");

                _webApp = WebApp.Start<Startup>(_baseAddress);

                Log("Started web host.");
            }
            catch (Exception ex)
            {
                var fnf = ex as FileNotFoundException;
                var extra = fnf != null ? (" Missing: " + fnf.FileName) : "";
                Log("FAILED OnStart: " + ex + extra);
                throw;
            }
        }

        protected override void OnStop()
        {
            try
            {
                _webApp?.Dispose();
                _webApp = null;
                Log("Stopped.");
            }
            catch (Exception ex)
            {
                Log("FAILED OnStop: " + ex);
                throw;
            }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "WindowsProxyService",
                    "logs"
                );
                Directory.CreateDirectory(dir);

                var path = Path.Combine(dir, "service.log");
                File.AppendAllText(path, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
            catch
            {
                // swallow logging failures
            }
        }
    }
}