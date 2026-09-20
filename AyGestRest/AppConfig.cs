using System;
using System.IO;
using System.Text.Json;

namespace AyGestRest
{
    public enum AppMode
    {
        Local = 0,
        Server = 1,
        Client = 2
    }

    public class AppConfig
    {
        public AppMode Mode { get; set; } = AppMode.Local;
        public string ServerIp { get; set; } = "";
        public string ServerPort { get; set; } = "5050";
        public string DiscoveryPort { get; set; } = "50555";
        public string MachineName { get; set; }
        public bool FirstRun { get; set; } = true;
        public DateTime LastConfigUpdate { get; set; } = DateTime.Now;

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AyGestRest",
            "config.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        config.MachineName = Environment.MachineName;
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar config: {ex.Message}");
            }

            return new AppConfig { MachineName = Environment.MachineName };
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                LastConfigUpdate = DateTime.Now;
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao salvar config: {ex.Message}");
            }
        }
    }

}