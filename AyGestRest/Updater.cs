using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace AyGestRest
{
    public static class Updater
    {
        private const string GITHUB_API =
            "https://api.github.com/repos/Ayresrxhrx/AyGestRest/releases/latest";

        private static readonly string TempInstaller =
            Path.Combine(Path.GetTempPath(), "AyGestRest_Update.exe");

        private static readonly string AppDataFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest");

        private static readonly string VersionFile =
            Path.Combine(AppDataFolder, "last_checked_version.txt");

        /// <summary>
        /// Verifica atualizações apenas se a versão no site for maior que a atual
        /// </summary>
        public static async Task<(bool hasUpdate, string latestVersion, string downloadUrl, string releaseNotes)>
            CheckForUpdatesAsync(bool forceCheck = false)
        {
            try
            {
                // Obter versão atual
                var currentVersion = GetCurrentVersion();
                if (currentVersion == null)
                    return (false, null, null, null);

                // Verificar se já verificamos recentemente
                if (!forceCheck && ShouldSkipUpdateCheck(currentVersion.ToString()))
                {
                    Debug.WriteLine($"⏭️ Skipping update check - already at version {currentVersion}");
                    return (false, null, null, null);
                }

                // Buscar informações da release
                using var client = CreateHttpClient();
                var json = await client.GetStringAsync(GITHUB_API);
                using var doc = JsonDocument.Parse(json);

                // Extrair versão mais recente
                if (!doc.RootElement.TryGetProperty("tag_name", out var tagNameElement))
                    return (false, null, null, null);

                var latestVersionStr = tagNameElement.GetString()?.Replace("v", "")?.Trim();
                if (string.IsNullOrEmpty(latestVersionStr))
                    return (false, null, null, null);

                // Extrair release notes
                string releaseNotes = "";
                if (doc.RootElement.TryGetProperty("body", out var bodyElement))
                {
                    releaseNotes = bodyElement.GetString() ?? "";
                }

                // Converter e comparar versões
                if (!Version.TryParse(latestVersionStr, out var latestVersion))
                    return (false, null, null, null);

                // Só retornar atualização se a versão do site for MAIOR que a atual
                if (latestVersion > currentVersion)
                {
                    // Encontrar asset .exe
                    if (doc.RootElement.TryGetProperty("assets", out var assets) &&
                        assets.GetArrayLength() > 0)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("browser_download_url", out var urlElement))
                            {
                                var url = urlElement.GetString();
                                if (!string.IsNullOrEmpty(url) &&
                                    url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    Debug.WriteLine($"✅ Update available: {currentVersion} → {latestVersionStr}");
                                    return (true, latestVersionStr, url, releaseNotes);
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Salvar que já estamos na versão mais recente
                    SaveLastCheckedVersion(currentVersion.ToString());
                }

                return (false, null, null, null);
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"⚠️ HTTP error checking updates: {ex.Message}");
                return (false, null, null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Error checking updates: {ex.Message}");
                return (false, null, null, null);
            }
        }

        /// <summary>
        /// Baixa e instala a atualização
        /// </summary>
        public static async Task<(bool success, string errorMessage)> DownloadAndInstallAsync(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                    return (false, "URL inválida");

                // Criar diretório temporário
                string tempDir = Path.Combine(Path.GetTempPath(), "AyGestRest_Update");
                Directory.CreateDirectory(tempDir);

                string tempFile = Path.Combine(tempDir, "AyGestRest_Setup.exe");

                // Baixar com progresso
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5); // Tempo maior para downloads

                Debug.WriteLine($"⬇️ Downloading update from: {url}");

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var buffer = new byte[8192];
                var totalRead = 0L;
                var read = 0;

                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    // Reportar progresso se necessário
                    if (totalBytes > 0)
                    {
                        var progress = (double)totalRead / totalBytes * 100;
                        Debug.WriteLine($"📥 Download: {progress:F1}%");
                    }
                }

                await fileStream.FlushAsync();
                fileStream.Close();

                // Verificar arquivo
                var fileInfo = new FileInfo(tempFile);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    Debug.WriteLine("❌ Downloaded file is empty!");
                    return (false, "Arquivo baixado está vazio");
                }

                Debug.WriteLine($"✅ Download complete: {fileInfo.Length} bytes");

                // Executar instalador
                Debug.WriteLine($"🚀 Starting installer: {tempFile}");

                var processInfo = new ProcessStartInfo
                {
                    FileName = tempFile,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var process = Process.Start(processInfo);

                if (process == null)
                {
                    Debug.WriteLine("❌ Failed to start installer");
                    return (false, "Não foi possível iniciar o instalador");
                }

                // Aguardar um pouco e verificar se ainda está rodando
                await Task.Delay(2000);

                if (process.HasExited)
                {
                    Debug.WriteLine($"❌ Installer exited with code: {process.ExitCode}");
                    return (false, $"Instalador terminou com código {process.ExitCode}");
                }

                Debug.WriteLine("✅ Installer started successfully!");
                return (true, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Download/install error: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "AyGestRest-Updater");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.Timeout = TimeSpan.FromSeconds(15);
            return client;
        }

        private static Version GetCurrentVersion()
        {
            try
            {
                return System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version;
            }
            catch
            {
                return new Version(1, 0, 0);
            }
        }

        private static bool ShouldSkipUpdateCheck(string currentVersion)
        {
            try
            {
                if (!File.Exists(VersionFile))
                    return false;

                var lastChecked = File.ReadAllText(VersionFile).Trim();
                return lastChecked == currentVersion;
            }
            catch
            {
                return false;
            }
        }

        private static void SaveLastCheckedVersion(string version)
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);
                File.WriteAllText(VersionFile, version);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Error saving version: {ex.Message}");
            }
        }
    }
}