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
        public string MachineName { get; set; } = Environment.MachineName;
        public string TerminalId { get; set; } = Guid.NewGuid().ToString("N").ToUpperInvariant();
        public string TerminalName { get; set; } = Environment.MachineName;
        public string ApiBaseUrl { get; set; } = "";
        public bool AutoDiscoverServer { get; set; } = true;
        public bool OfflineQueueEnabled { get; set; } = true;
        public bool FirstRun { get; set; } = true;
        public string CurrencyCode { get; set; } = "MZN";
        public string CurrencySymbol { get; set; } = "MT";
        public string InvoiceSeries { get; set; } = "A";
        public string BusinessName { get; set; } = "AyGest POS";
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
                        config.TerminalName = string.IsNullOrWhiteSpace(config.TerminalName)
                            ? Environment.MachineName
                            : config.TerminalName;

                        if (string.IsNullOrWhiteSpace(config.TerminalId))
                            config.TerminalId = Guid.NewGuid().ToString("N").ToUpperInvariant();

                        config.Save();
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao carregar config: {ex.Message}");
            }

            var fresh = new AppConfig { MachineName = Environment.MachineName, TerminalName = Environment.MachineName };
            fresh.Save();
            return fresh;
        }

        public string GetApiBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(ApiBaseUrl))
                return ApiBaseUrl.TrimEnd('/');

            if (!string.IsNullOrWhiteSpace(ServerIp))
                return $"http://{ServerIp}:{ServerPort}";

            return $"http://127.0.0.1:{ServerPort}";
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath)!;
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
