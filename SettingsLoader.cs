using System;
using System.IO;
using Newtonsoft.Json;

namespace WindowsProxyService
{
    public static class SettingsLoader
    {
        public static ServiceSettings LoadFromAppDirectory()
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(exeDir, "settings.json");

            var settings = new ServiceSettings();

            if (!File.Exists(path))
                return settings;

            var json = File.ReadAllText(path);
            var fromFile = JsonConvert.DeserializeObject<ServiceSettings>(json);

            if (fromFile == null)
                return settings;

            // Merge, keeping defaults where missing
            settings.Port = fromFile.Port != 0 ? fromFile.Port : settings.Port;
            settings.Host = string.IsNullOrWhiteSpace(fromFile.Host) ? settings.Host : fromFile.Host;
            settings.InstanceName = string.IsNullOrWhiteSpace(fromFile.InstanceName) ? settings.InstanceName : fromFile.InstanceName;
            settings.ProxyUrl = string.IsNullOrWhiteSpace(fromFile.ProxyUrl) ? settings.ProxyUrl : fromFile.ProxyUrl;

            return settings;
        }
    }
}