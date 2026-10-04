using System.Diagnostics;
using System.Windows;
using AyGestRest.Services;

namespace AyGestRest
{
    public partial class App
    {
        private readonly ProductionPlatformService _productionPlatform = new();
        private readonly AuthorizationService _authorizationService = new();

        public App()
        {
            Startup += ProductionStartupAsync;
        }

        private async void ProductionStartupAsync(object? sender, StartupEventArgs e)
        {
            try
            {
                await _productionPlatform.InitializeAsync();
                await _authorizationService.InitializeAsync();

                var config = AppConfig.Load();
                await _productionPlatform.RegisterTerminalAsync(
                    config.TerminalId,
                    config.TerminalName,
                    (int)config.Mode,
                    config.ServerIp);

                Debug.WriteLine("AyGest Production Platform inicializada.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Production Platform: {ex}");
            }
        }
    }
}
