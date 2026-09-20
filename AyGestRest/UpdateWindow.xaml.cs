using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AyGestRest
{
    public partial class UpdateWindow : Window
    {
        private string _downloadUrl;
        private string _latestVersion;
        private string _releaseNotes;
        private bool _updateInProgress = false;

        public UpdateWindow(string downloadUrl, string latestVersion, string releaseNotes)
        {
            InitializeComponent();
            _downloadUrl = downloadUrl;
            _latestVersion = latestVersion;
            _releaseNotes = releaseNotes;

            Loaded += OnWindowLoaded;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Configurar versão
            txtVersion.Text = $"v{_latestVersion}";

            // Configurar release notes
            if (!string.IsNullOrWhiteSpace(_releaseNotes))
            {
                txtReleaseNotes.Text = _releaseNotes;
            }
            else
            {
                txtReleaseNotes.Text = "Melhorias de performance e correções de bugs.";
            }

            // Animar entrada
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)));
        }

        private async void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInProgress)
                return;

            _updateInProgress = true;

            // Desabilitar botões
            btnUpdate.IsEnabled = false;
            btnLater.IsEnabled = false;

            // Mostrar status
            ShowStatus("⏬", "#4361EE", "Baixando atualização...",
                      "Aguarde enquanto baixamos os arquivos necessários.");

            // Iniciar download
            var result = await Updater.DownloadAndInstallAsync(_downloadUrl);

            if (result.success)
            {
                ShowStatus("✅", "#4CAF50", "Instalação iniciada!",
                          "O instalador foi iniciado. O sistema será fechado automaticamente.");

                // Salvar preferência do usuário
                if (chkSkipUpdate.IsChecked == true)
                {
                    SaveUpdatePreference();
                }

                // Aguardar e fechar
                await Task.Delay(2000);
                DialogResult = true;
                Close();
            }
            else
            {
                ShowStatus("❌", "#F44336", "Falha na atualização",
                          result.errorMessage ?? "Ocorreu um erro durante o processo.");

                // Perguntar se quer tentar novamente
                var retryResult = MessageBox.Show(
                    "Não foi possível completar a atualização.\n\nDeseja tentar novamente?",
                    "Erro na Atualização",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (retryResult == MessageBoxResult.Yes)
                {
                    // Resetar para tentar novamente
                    _updateInProgress = false;
                    btnUpdate.IsEnabled = true;
                    btnLater.IsEnabled = true;
                    statusCard.Visibility = Visibility.Collapsed;
                }
                else
                {
                    DialogResult = false;
                    Close();
                }
            }
        }

        private void btnLater_Click(object sender, RoutedEventArgs e)
        {
            // Salvar preferência se marcado
            if (chkSkipUpdate.IsChecked == true)
            {
                SaveUpdatePreference();
            }

            DialogResult = false;
            Close();
        }

        private void ShowStatus(string icon, string color, string status, string details)
        {
            Dispatcher.Invoke(() =>
            {
                statusCard.Visibility = Visibility.Visible;
                txtStatusIcon.Text = icon;
                txtStatus.Text = status;
                txtStatusDetails.Text = details;

                // Atualizar cor do ícone
                var brush = new BrushConverter().ConvertFromString(color) as SolidColorBrush;
                if (brush != null)
                {
                    statusIcon.Background = brush;
                }

                // Mostrar progresso se estiver baixando
                if (status.Contains("Baixando"))
                {
                    progressGrid.Visibility = Visibility.Visible;
                    txtProgress.Text = "Preparando download...";
                }
                else
                {
                    progressGrid.Visibility = Visibility.Collapsed;
                }

                // Scroll para baixo para mostrar status
                var scrollViewer = FindVisualChild<ScrollViewer>(this);
                scrollViewer?.ScrollToEnd();
            });
        }

        private void SaveUpdatePreference()
        {
            try
            {
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                System.IO.Directory.CreateDirectory(folder);

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(folder, "skip_update.txt"),
                    _latestVersion);
            }
            catch
            {
                // Ignorar erros ao salvar preferência
            }
        }

        private T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child != null && child is T)
                    return (T)child;
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }
    }
}