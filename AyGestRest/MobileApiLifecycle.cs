using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AyGestRest.Services;

namespace AyGestRest
{
    public partial class App
    {
        private EmbeddedApiServer? _mobileApiServer;
        private TableSaleStateCoordinator? _tableSaleStateCoordinator;

        private void OnApplicationStartup(object sender, StartupEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(StartMobileApi), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Mobile API] falha ao agendar inicialização: {ex.Message}");
            }
        }

        private void StartMobileApi()
        {
            if (_mobileApiServer != null) return;

            try
            {
                var config = AppConfig.Load();
                if (config.Mode == AppMode.Client)
                {
                    Debug.WriteLine("[Mobile API] terminal cliente: servidor local não será iniciado.");
                    return;
                }

                _tableSaleStateCoordinator ??= new TableSaleStateCoordinator();
                _mobileApiServer = new EmbeddedApiServer(config.ServerPort);
                _mobileApiServer.Start();
                Debug.WriteLine($"[Mobile API] servidor iniciado na porta {config.ServerPort}.");
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
                _tableSaleStateCoordinator?.Dispose();
                _tableSaleStateCoordinator = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Mobile API] falha ao encerrar: {ex.Message}");
            }
        }
    }
}
