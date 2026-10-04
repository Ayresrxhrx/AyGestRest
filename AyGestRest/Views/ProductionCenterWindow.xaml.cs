using AyGestRest.Data;
using AyGestRest.Services;
using System.Diagnostics;
using System.Windows;

namespace AyGestRest.Views
{
    public partial class ProductionCenterWindow : Window
    {
        private readonly ReportsService _reports;
        private readonly ProductionPlatformService _platform = new();
        private readonly AppConfig _config;

        public ProductionCenterWindow()
        {
            InitializeComponent();
            _config = AppConfig.Load();
            using var db = new AyGestRestContext();
            _reports = new ReportsService(db);
            Loaded += async (_, _) => await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                var snapshot = await _reports.GetDashboardAsync(DateTime.Today, DateTime.Today);
                TxtSales.Text = $"{snapshot.Sales:N2} MT";
                TxtOrders.Text = snapshot.PaidOrders.ToString("N0");
                TxtLowStock.Text = snapshot.LowStockItems.ToString("N0");
                TxtCustomers.Text = snapshot.Customers.ToString("N0");
                TxtCancelled.Text = snapshot.CancelledOrders.ToString("N0");
                TxtTerminalName.Text = _config.TerminalName;
                TxtTerminalId.Text = _config.TerminalId.Length > 14 ? _config.TerminalId[..14] + "…" : _config.TerminalId;
                TxtTerminalMode.Text = _config.Mode.ToString();
                TxtCurrency.Text = $"{_config.CurrencyCode} ({_config.CurrencySymbol})";
                TopProductsGrid.ItemsSource = snapshot.TopProducts;
                TxtConnection.Text = "Ligado";
            }
            catch (Exception ex)
            {
                TxtConnection.Text = "Verificar";
                Debug.WriteLine($"Production Center: {ex}");
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async void Backup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = await _platform.CreateBackupAsync("manual-ui");
                MessageBox.Show(this, $"Backup criado com sucesso.\n\n{path}", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenBackups_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest", "Backups");
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Backups", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
