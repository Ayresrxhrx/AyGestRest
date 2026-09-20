using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AyGestRest.Views
{
    public partial class ServerSelectorWindow : Window, INotifyPropertyChanged
    {
        private readonly NetworkDiscovery _discovery;
        private readonly AppConfig _config;
        private List<ServerInfo> _discoveredServers = new List<ServerInfo>();
        private ServerInfo _selectedServer;
        private string _manualIpAddress;
        private string _manualPort = "5050";
        private string _manualConnectionStatus;
        private Brush _manualConnectionStatusColor = Brushes.Gray;
        private string _statusMessage = "Aguardando seleção...";

        public List<ServerInfo> DiscoveredServers
        {
            get => _discoveredServers;
            set { _discoveredServers = value; OnPropertyChanged(nameof(DiscoveredServers)); OnPropertyChanged(nameof(ServersCount)); }
        }

        public int ServersCount => _discoveredServers?.Count ?? 0;

        public ServerInfo SelectedServer
        {
            get => _selectedServer;
            set { _selectedServer = value; OnPropertyChanged(nameof(SelectedServer)); UpdateCanConnect(); }
        }

        public string ManualIpAddress
        {
            get => _manualIpAddress;
            set { _manualIpAddress = value; OnPropertyChanged(nameof(ManualIpAddress)); UpdateCanConnect(); }
        }

        public string ManualPort
        {
            get => _manualPort;
            set { _manualPort = value; OnPropertyChanged(nameof(ManualPort)); UpdateCanConnect(); }
        }

        public string ManualConnectionStatus
        {
            get => _manualConnectionStatus;
            set { _manualConnectionStatus = value; OnPropertyChanged(nameof(ManualConnectionStatus)); }
        }

        public Brush ManualConnectionStatusColor
        {
            get => _manualConnectionStatusColor;
            set { _manualConnectionStatusColor = value; OnPropertyChanged(nameof(ManualConnectionStatusColor)); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public bool CanConnect =>
            (SelectedServer != null) ||
            (!string.IsNullOrWhiteSpace(ManualIpAddress) && IsValidIp(ManualIpAddress));

        public bool CanTestManualConnection =>
            !string.IsNullOrWhiteSpace(ManualIpAddress) && IsValidIp(ManualIpAddress);

        public string SelectedServerIp => SelectedServer?.IpAddress ?? ManualIpAddress;

        public event PropertyChangedEventHandler PropertyChanged;

        public ServerSelectorWindow(NetworkDiscovery discovery, AppConfig config)
        {
            InitializeComponent();
            DataContext = this;

            _discovery = discovery;
            _config = config;

            _discovery.ServersUpdated += OnServersUpdated;

            // Procurar servidores automaticamente ao abrir
            Task.Run(async () => await RefreshServers());
        }

        private void OnServersUpdated(List<ServerInfo> servers)
        {
            Dispatcher.Invoke(() =>
            {
                DiscoveredServers = servers;
                StatusMessage = $"Encontrados {servers.Count} servidor(es) na rede";

                // Se já havia um servidor selecionado, manter seleção
                if (SelectedServer != null)
                {
                    var stillExists = servers.FirstOrDefault(s => s.IpAddress == SelectedServer.IpAddress);
                    SelectedServer = stillExists ?? SelectedServer;
                }
            });
        }

        private async Task RefreshServers()
        {
            StatusMessage = "Procurando servidores...";
            await _discovery.BroadcastDiscovery();
        }

        private async void RefreshServers_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServers();
        }

        private async void TestManualConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ManualConnectionStatus = "Testando conexão...";
                ManualConnectionStatusColor = Brushes.Gray;

                using (var client = new ApiClient(ManualIpAddress, ManualPort))
                {
                    if (await client.TestConnection())
                    {
                        ManualConnectionStatus = "✅ Conexão bem-sucedida! Pode conectar.";
                        ManualConnectionStatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    }
                    else
                    {
                        ManualConnectionStatus = "❌ Não foi possível conectar ao servidor";
                        ManualConnectionStatusColor = Brushes.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                ManualConnectionStatus = $"❌ Erro: {ex.Message}";
                ManualConnectionStatusColor = Brushes.Red;
            }
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            string ipToConnect = SelectedServer?.IpAddress ?? ManualIpAddress;
            string portToConnect = ManualPort;

            if (string.IsNullOrEmpty(ipToConnect))
            {
                MessageBox.Show("Selecione um servidor ou digite um IP manual.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusMessage = $"Conectando a {ipToConnect}:{portToConnect}...";
                ConnectButton.IsEnabled = false;

                using (var client = new ApiClient(ipToConnect, portToConnect))
                {
                    if (await client.TestConnection())
                    {
                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show($"Não foi possível conectar ao servidor {ipToConnect}", "Erro de Conexão",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusMessage = "Falha na conexão";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao conectar: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Falha na conexão";
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private bool IsValidIp(string ip)
        {
            return IPAddress.TryParse(ip, out _);
        }

        private void UpdateCanConnect()
        {
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(CanTestManualConnection));
        }

        protected override void OnClosed(EventArgs e)
        {
            _discovery.ServersUpdated -= OnServersUpdated;
            base.OnClosed(e);
        }

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}