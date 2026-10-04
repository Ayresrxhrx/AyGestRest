using System.Diagnostics;
using System.Windows;
using AyGestRest.Services;

namespace AyGestRest
{
    public partial class App
    {
        private readonly ProductionPlatformService _productionPlatform = new();

        public App()
        {
            Startup += ProductionStartupAsync;
        }

        private async void ProductionStartupAsync(object? sender, StartupEventArgs e)
        {
            try
            {
                await _productionPlatform.InitializeAsync();
                var config = AppConfig.Load();
                await _productionPlatform.RegisterTerminalAsync(
                    config.TerminalId,
                    config.TerminalName,
                    (int)config.Mode,
                    config.ServerIp);

                Debug.WriteLine("AyGest Production Platform inicializada com sucesso.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Production Platform: {ex}");
                // A aplicação já possui o seu próprio mecanismo de migração e
                // tratamento de erros. O núcleo complementar nunca deve impedir
                // uma instalação existente de iniciar.
            }
        }
    }
}
