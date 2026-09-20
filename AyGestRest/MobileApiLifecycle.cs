using System;
using System.Diagnostics;
using System.Windows;

namespace AyGestRest
{
    public partial class App
    {
        private EmbeddedApiServer? _mobileApiServer;

        private void OnApplicationStartup(object sender, StartupEventArgs e)
        {
            try
            {
                var port = Environment.GetEnvironmentVariable("AYGEST_MOBILE_API_PORT");
                _mobileApiServer = new EmbeddedApiServer(string.IsNullOrWhiteSpace(port) ? "5050" : port);
                _mobileApiServer.Start();
                Debug.WriteLine("[Mobile API] servidor iniciado.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Mobile API] falha ao iniciar: {ex.Message}");
            }
        }

        private void OnApplicationExit(object sender, ExitEventArgs e)
        {
            try
            {
                _mobileApiServer?.Dispose();
                _mobileApiServer = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Mobile API] falha ao encerrar: {ex.Message}");
            }
        }
    }
}
