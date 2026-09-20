using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Drawing.Printing;

namespace AyGestRest.Views
{
    public partial class InitialSetupWizard : Window, INotifyPropertyChanged
    {
        private readonly AyGestRestContext _context;
        private RestaurantConfig _config;
        private VFDConfig _vfdConfig;

        private int _currentStep = 1;
        public int CurrentStep
        {
            get => _currentStep;
            set
            {
                if (value >= 1 && value <= 6)
                {
                    _currentStep = value;
                    OnPropertyChanged(nameof(CurrentStep));
                    UpdateStepProperties();
                }
            }
        }

        // Propriedades para visibilidade dos passos
        public bool IsStep1Visible => CurrentStep == 1;
        public bool IsStep2Visible => CurrentStep == 2;
        public bool IsStep3Visible => CurrentStep == 3;
        public bool IsStep4Visible => CurrentStep == 4;
        public bool IsStep5Visible => CurrentStep == 5;
        public bool IsStep6Visible => CurrentStep == 6;

        // Propriedades para o rodapé
        private string _currentStepTitle = "Informações do Restaurante";
        public string CurrentStepTitle
        {
            get => _currentStepTitle;
            set { _currentStepTitle = value; OnPropertyChanged(nameof(CurrentStepTitle)); }
        }

        private string _stepDescription = "Configure as informações básicas do seu estabelecimento";
        public string StepDescription
        {
            get => _stepDescription;
            set { _stepDescription = value; OnPropertyChanged(nameof(StepDescription)); }
        }

        // Navegação
        public bool CanGoBack => CurrentStep > 1;
        public bool IsLastStep => CurrentStep == 6;
        public string NextButtonText => IsLastStep ? "Concluir" : "Próximo";

        // Listas para ComboBoxes
        public List<string> AvailablePrinters { get; set; } = new List<string>();
        public List<string> AvailableCOMPorts { get; set; } = new List<string>();
        public List<int> BaudRates { get; set; } = new List<int> { 9600, 19200, 38400, 57600, 115200 };
        public List<int> DisplayLines { get; set; } = new List<int> { 1, 2, 4 };
        public List<int> DisplayColumns { get; set; } = new List<int> { 16, 20, 24, 32, 40 };

        // Propriedades VFD
        private bool _isVFDEnabled;
        public bool IsVFDEnabled
        {
            get => _isVFDEnabled;
            set { _isVFDEnabled = value; OnPropertyChanged(nameof(IsVFDEnabled)); UpdateVFDPreview(); }
        }

        private string _selectedCOMPort = "COM1";
        public string SelectedCOMPort
        {
            get => _selectedCOMPort;
            set { _selectedCOMPort = value; OnPropertyChanged(nameof(SelectedCOMPort)); UpdateVFDPreview(); }
        }

        private int _selectedBaudRate = 9600;
        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set { _selectedBaudRate = value; OnPropertyChanged(nameof(SelectedBaudRate)); }
        }

        private int _selectedDisplayLines = 2;
        public int SelectedDisplayLines
        {
            get => _selectedDisplayLines;
            set { _selectedDisplayLines = value; OnPropertyChanged(nameof(SelectedDisplayLines)); UpdateVFDPreview(); }
        }

        private int _selectedDisplayColumns = 20;
        public int SelectedDisplayColumns
        {
            get => _selectedDisplayColumns;
            set { _selectedDisplayColumns = value; OnPropertyChanged(nameof(SelectedDisplayColumns)); UpdateVFDPreview(); }
        }

        private bool _showRealTimeTotal = true;
        public bool ShowRealTimeTotal
        {
            get => _showRealTimeTotal;
            set { _showRealTimeTotal = value; OnPropertyChanged(nameof(ShowRealTimeTotal)); }
        }

        private string _vfdPreviewText = "Tela VFD Desativada";
        public string VFDPreviewText
        {
            get => _vfdPreviewText;
            set { _vfdPreviewText = value; OnPropertyChanged(nameof(VFDPreviewText)); }
        }

        public RestaurantConfig Config
        {
            get => _config;
            set { _config = value; OnPropertyChanged(nameof(Config)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public InitialSetupWizard()
        {
            InitializeComponent();
            DataContext = this;

            _context = new AyGestRestContext();
            LoadConfiguration();
            InitializePrinters();
            InitializeCOMPorts();
            UpdateStepProperties();
            UpdateVFDPreview();
        }

        private void LoadConfiguration()
        {
            try
            {
                Config = _context.RestaurantConfigs.FirstOrDefault() ?? new RestaurantConfig { CurrencySymbol = "MT" };
                _vfdConfig = _context.VFDConfigs.FirstOrDefault() ?? new VFDConfig();

                // Carregar valores VFD
                IsVFDEnabled = _vfdConfig.IsEnabled;
                SelectedCOMPort = string.IsNullOrEmpty(_vfdConfig.COMPort) ? "COM1" : _vfdConfig.COMPort;
                SelectedBaudRate = _vfdConfig.BaudRate;
                SelectedDisplayLines = _vfdConfig.DisplayLines;
                SelectedDisplayColumns = _vfdConfig.DisplayColumns;
                ShowRealTimeTotal = _vfdConfig.ShowRealTimeTotal;

                // Carregar permissões de funcionários
                LoadEmployeePermissions();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configurações: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadEmployeePermissions()
        {
            // Carregar valores das permissões
            if (FindName("ChkFechoDia") is CheckBox chkFecho)
                chkFecho.IsChecked = Config.FuncionarioPodeFechoDiario;
            if (FindName("ChkReservas") is CheckBox chkRes)
                chkRes.IsChecked = Config.FuncionarioPodeReservas;
            if (FindName("ChkHistorico") is CheckBox chkHist)
                chkHist.IsChecked = Config.FuncionarioPodeHistoricoVendas;
        }

        private void UpdateStepProperties()
        {
            // Atualizar título e descrição do passo atual
            switch (CurrentStep)
            {
                case 1:
                    CurrentStepTitle = "Informações do Restaurante";
                    StepDescription = "Configure as informações básicas do seu estabelecimento";
                    break;
                case 2:
                    CurrentStepTitle = "Configurações de Impressão";
                    StepDescription = "Configure as impressoras e formatos de recibos";
                    break;
                case 3:
                    CurrentStepTitle = "Segurança e Permissões";
                    StepDescription = "Configure segurança e permissões de usuários";
                    break;
                case 4:
                    CurrentStepTitle = "Configurações de Backup";
                    StepDescription = "Configure backups automáticos e localização";
                    break;
                case 5:
                    CurrentStepTitle = "Tela VFD (Display do Cliente)";
                    StepDescription = "Configure o display para clientes";
                    break;
                case 6:
                    CurrentStepTitle = "Revisão Final";
                    StepDescription = "Pronto para começar!";
                    break;
            }

            // Atualizar visibilidade dos passos
            OnPropertyChanged(nameof(IsStep1Visible));
            OnPropertyChanged(nameof(IsStep2Visible));
            OnPropertyChanged(nameof(IsStep3Visible));
            OnPropertyChanged(nameof(IsStep4Visible));
            OnPropertyChanged(nameof(IsStep5Visible));
            OnPropertyChanged(nameof(IsStep6Visible));

            // Atualizar navegação
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(NextButtonText));
        }

        private void InitializePrinters()
        {
            AvailablePrinters.Clear();
            AvailablePrinters.Add("(Nenhuma)");
            foreach (string printer in PrinterSettings.InstalledPrinters)
                AvailablePrinters.Add(printer);
            OnPropertyChanged(nameof(AvailablePrinters));
        }

        private void InitializeCOMPorts()
        {
            AvailableCOMPorts.Clear();
            AvailableCOMPorts.Add("(Nenhuma)");
            AvailableCOMPorts.AddRange(SerialPort.GetPortNames());
            OnPropertyChanged(nameof(AvailableCOMPorts));
        }

        // Navegação
        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentStep > 1) CurrentStep--;
        }

        private void NextOrFinish_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateCurrentStep())
                return;

            if (CurrentStep < 6)
            {
                CurrentStep++;
            }
            else
            {
                FinishSetup();
            }
        }

        private bool ValidateCurrentStep()
        {
            switch (CurrentStep)
            {
                case 1:
                    if (string.IsNullOrWhiteSpace(Config.RestaurantName))
                    {
                        MessageBox.Show("O nome do restaurante é obrigatório para continuar.", "Validação",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    break;

                case 2:
                    // Validação opcional para passo 2
                    break;

                case 5 when IsVFDEnabled:
                    if (SelectedCOMPort == "(Nenhuma)" || string.IsNullOrEmpty(SelectedCOMPort))
                    {
                        MessageBox.Show("Selecione uma porta COM válida para a Tela VFD.", "Validação",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    break;
            }
            return true;
        }

        private async void FinishSetup()
        {
            try
            {
                // Buscar valores dos RadioButtons no XAML
                LoadRadioButtonValues();

                // Atualizar permissões dos funcionários
                UpdateEmployeePermissions();

                // CORREÇÃO CRÍTICA: Salvar primeiro se for novo registro
                await SaveConfigurationAsync();

                MessageBox.Show("Configuração inicial concluída com sucesso!\nO AyGestRest está pronto para usar.",
                    "Bem-vindo!", MessageBoxButton.OK, MessageBoxImage.Information);

                // Marcar configuração inicial como concluída
                CreateInitialSetupFlag();

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar as configurações: {ex.Message}\n\nDetalhes: {ex.InnerException?.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveConfigurationAsync()
        {
            // Atualizar VFD
            _vfdConfig.IsEnabled = IsVFDEnabled;
            _vfdConfig.COMPort = SelectedCOMPort == "(Nenhuma)" ? null : SelectedCOMPort;
            _vfdConfig.BaudRate = SelectedBaudRate;
            _vfdConfig.DisplayLines = SelectedDisplayLines;
            _vfdConfig.DisplayColumns = SelectedDisplayColumns;
            _vfdConfig.ShowRealTimeTotal = ShowRealTimeTotal;

            // CORREÇÃO: Abordagem mais robusta para salvar
            await SaveRestaurantConfigAsync();
            await SaveVFDConfigAsync();
        }

        private async Task SaveRestaurantConfigAsync()
        {
            try
            {
                // Verificar se já existe no banco
                var existingConfig = await _context.RestaurantConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (existingConfig == null)
                {
                    // Não existe, adicionar novo
                    _context.RestaurantConfigs.Add(Config);
                }
                else
                {
                    // Já existe, atualizar
                    Config.Id = existingConfig.Id; // Manter o mesmo ID
                    _context.RestaurantConfigs.Update(Config);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao salvar configuração do restaurante: {ex.Message}", ex);
            }
        }

        private async Task SaveVFDConfigAsync()
        {
            try
            {
                // Verificar se já existe no banco
                var existingVFD = await _context.VFDConfigs
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (existingVFD == null)
                {
                    // Não existe, adicionar novo
                    _vfdConfig.Id = 0; // Garantir que é novo
                    _context.VFDConfigs.Add(_vfdConfig);
                }
                else
                {
                    // Já existe, atualizar
                    _vfdConfig.Id = existingVFD.Id; // Manter o mesmo ID
                    _context.VFDConfigs.Update(_vfdConfig);
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao salvar configuração VFD: {ex.Message}", ex);
            }
        }

        private void UpdateEmployeePermissions()
        {
            // Atualizar permissões dos funcionários
            if (FindName("ChkFechoDia") is CheckBox chkFecho)
                Config.FuncionarioPodeFechoDiario = chkFecho.IsChecked == true;
            if (FindName("ChkReservas") is CheckBox chkRes)
                Config.FuncionarioPodeReservas = chkRes.IsChecked == true;
            if (FindName("ChkHistorico") is CheckBox chkHist)
                Config.FuncionarioPodeHistoricoVendas = chkHist.IsChecked == true;
        }

        private void LoadRadioButtonValues()
        {
            // Buscar valores dos RadioButtons pelo nome
            Config.RequiresLogin = GetRadioButtonValue("RequiresLoginYes", "RequiresLoginNo");
            Config.AutoSave = GetRadioButtonValue("AutoSaveYes", "AutoSaveNo");
            Config.SoundEffects = GetRadioButtonValue("SoundEffectsYes", "SoundEffectsNo");
            Config.DarkMode = GetRadioButtonValue("DarkModeYes", "DarkModeNo");
            Config.ShowNotifications = GetRadioButtonValue("ShowNotificationsYes", "ShowNotificationsNo");
            Config.BackupDiario = GetRadioButtonValue("BackupDiarioYes", "BackupDiarioNo");
            Config.AutoBackup = GetRadioButtonValue("AutoBackupYes", "AutoBackupNo");
            Config.PrintOrderToKitchen = GetRadioButtonValue("PrintOrderToKitchenYes", "PrintOrderToKitchenNo");
            Config.SecondScreenEnabled = GetRadioButtonValue("SecondScreenYes", "SecondScreenNo");

            // VFD - usando propriedades diretas já vinculadas
            // Não precisamos buscar, já estão nas propriedades IsVFDEnabled e ShowRealTimeTotal
        }

        private bool GetRadioButtonValue(string yesName, string noName)
        {
            if (FindName(yesName) is RadioButton yes && yes.IsChecked == true)
                return true;
            if (FindName(noName) is RadioButton no && no.IsChecked == true)
                return false;

            // Valor padrão se não encontrar
            return false;
        }

        private void CreateInitialSetupFlag()
        {
            try
            {
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string appFolder = Path.Combine(appDataPath, "AyGestRest");
                string flagFile = Path.Combine(appFolder, "initial_setup_completed.flag");

                Directory.CreateDirectory(appFolder);
                File.WriteAllText(flagFile, "true");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Erro ao criar flag de configuração: {ex.Message}");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Deseja mesmo cancelar a configuração inicial?\nA aplicação será fechada.",
                "Cancelar", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                Application.Current.Shutdown();
        }

        // ==================== MÉTODOS DE CONFIGURAÇÃO ====================

        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os ficheiros (*.*)|*.*",
                Title = "Selecionar Logo do Restaurante"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Definir pasta interna para armazenar o logo (na pasta de AppData)
                    string appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos");

                    Directory.CreateDirectory(appDataPath); // Cria se não existir

                    // Nome do arquivo final (sempre o mesmo para sobrescrever o anterior)
                    string fileName = "restaurant_logo" + Path.GetExtension(openFileDialog.FileName).ToLower();
                    string destinationPath = Path.Combine(appDataPath, fileName);

                    // Copiar o arquivo selecionado para a pasta interna
                    File.Copy(openFileDialog.FileName, destinationPath, overwrite: true);

                    // Salvar apenas o nome do arquivo (ou caminho relativo) no banco
                    Config.LogoPath = fileName; // Ex: "restaurant_logo.png"

                    // Atualizar a UI (se você tem um Image control chamado imgLogo no XAML)
                    OnPropertyChanged(nameof(Config)); // Para atualizar o binding

                    MessageBox.Show("Logo carregado e salvo com sucesso!", "Sucesso",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao salvar o logo: {ex.Message}", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void LoadLogoPreview()
        {
            if (!string.IsNullOrEmpty(Config.LogoPath) && File.Exists(Config.LogoPath))
            {
                try
                {
                    // Criar uma cópia da imagem na pasta da aplicação para referência futura
                    string appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logo");

                    Directory.CreateDirectory(appDataPath);

                    string destinationPath = Path.Combine(appDataPath, "restaurant_logo" + Path.GetExtension(Config.LogoPath));

                    // Copiar imagem para pasta da aplicação
                    File.Copy(Config.LogoPath, destinationPath, true);

                    // Atualizar caminho para o relativo
                    Config.LogoPath = destinationPath;

                    // Forçar atualização da propriedade
                    OnPropertyChanged(nameof(Config));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao processar logo: {ex.Message}", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        private byte[] ImageToByteArray(string imagePath)
        {
            if (!File.Exists(imagePath)) return null;

            using (FileStream fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
            {
                byte[] imageData = new byte[fs.Length];
                fs.Read(imageData, 0, imageData.Length);
                return imageData;
            }
        }

        private void SaveLogoToDatabase(string imagePath)
        {
            try
            {
                byte[] logoBytes = ImageToByteArray(imagePath);
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    Config.LogoBytes = logoBytes;
                    Config.LogoMimeType = Path.GetExtension(imagePath).ToLower();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao converter logo: {ex.Message}");
            }
        }

        private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                Config.BackupPath = dialog.SelectedPath;
        }

        private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = string.IsNullOrEmpty(Config.BackupPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AyGestRest Backups")
                    : Config.BackupPath;

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupPath = string.IsNullOrEmpty(Config.BackupPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AyGestRest Backups")
                    : Config.BackupPath;

                Directory.CreateDirectory(backupPath);

                string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest", "AyGestRest.db");
                if (!File.Exists(dbPath))
                {
                    MessageBox.Show("Base de dados não encontrada.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string backupFile = Path.Combine(backupPath, $"AyGestRest_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                await Task.Run(() => File.Copy(dbPath, backupFile, true));

                MessageBox.Show("Backup criado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao fazer backup: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestMainPrinter_Click(object sender, RoutedEventArgs e)
        {
            TestPrinter(Config.PrinterName, "Impressora de Talões");
        }

        private void TestKitchenPrinter_Click(object sender, RoutedEventArgs e)
        {
            TestPrinter(Config.KitchenPrinterName, "Impressora da Cozinha");
        }

        private void TestPrinter(string printerName, string type)
        {
            if (string.IsNullOrWhiteSpace(printerName) || printerName == "(Nenhuma)")
            {
                MessageBox.Show($"Selecione uma {type} primeiro.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var doc = new PrintDocument { PrinterSettings = new PrinterSettings { PrinterName = printerName } };
                if (!doc.PrinterSettings.IsValid)
                {
                    MessageBox.Show("Impressora inválida.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                doc.PrintPage += (s, args) =>
                {
                    var font = new System.Drawing.Font("Consolas", 10);
                    float y = 10;
                    args.Graphics.DrawString("=== TESTE DE IMPRESSÃO ===", font, System.Drawing.Brushes.Black, 10, y);
                    y += 25;
                    args.Graphics.DrawString($"Impressora: {printerName}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 25;
                    args.Graphics.DrawString($"Data: {DateTime.Now}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 40;
                    args.Graphics.DrawString("IMPRESSÃO OK ✅", font, System.Drawing.Brushes.Black, 10, y);
                };

                doc.Print();
                MessageBox.Show("Teste enviado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao imprimir: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DetectCOMPorts_Click(object sender, RoutedEventArgs e)
        {
            AvailableCOMPorts.Clear();
            AvailableCOMPorts.Add("(Nenhuma)");

            foreach (string port in SerialPort.GetPortNames())
            {
                try
                {
                    using (var sp = new SerialPort(port)) { sp.Open(); sp.Close(); AvailableCOMPorts.Add(port); }
                }
                catch { /* Porta ocupada */ }
            }

            OnPropertyChanged(nameof(AvailableCOMPorts));
            MessageBox.Show($"{AvailableCOMPorts.Count - 1} porta(s) COM detetadas.", "Detecção COM",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void TestVFDConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!IsVFDEnabled || SelectedCOMPort == "(Nenhuma)")
            {
                MessageBox.Show("Ative a Tela VFD e selecione uma porta válida.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    using (var sp = new SerialPort(SelectedCOMPort, SelectedBaudRate))
                    {
                        sp.Open();
                        sp.Write(new byte[] { 0x0C }, 0, 1); // Clear
                        sp.Write(System.Text.Encoding.ASCII.GetBytes("TESTE VFD OK"), 0, 12);
                        sp.Close();
                    }
                });

                MessageBox.Show("Teste enviado ao VFD com sucesso!", "Sucesso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro na conexão VFD: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVFDPreview_Click(object sender, RoutedEventArgs e)
        {
            UpdateVFDPreview();

            if (!IsVFDEnabled) return;

            try
            {
                using (var sp = new SerialPort(SelectedCOMPort, SelectedBaudRate))
                {
                    sp.Open();
                    sp.Write(new byte[] { 0x0C }, 0, 1);
                    sp.Write(System.Text.Encoding.ASCII.GetBytes(VFDPreviewText), 0, VFDPreviewText.Length);
                    sp.Close();
                }
                MessageBox.Show("Preview enviado ao VFD!", "VFD",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao enviar preview: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVFDPreview()
        {
            if (!IsVFDEnabled)
            {
                VFDPreviewText = "Tela VFD Desativada";
                return;
            }

            var lines = new List<string>();
            string name = string.IsNullOrEmpty(Config?.RestaurantName) ? "RESTAURANTE" : Config.RestaurantName;
            if (name.Length > SelectedDisplayColumns) name = name.Substring(0, SelectedDisplayColumns);
            lines.Add(CenterText(name, SelectedDisplayColumns));

            if (SelectedDisplayLines >= 2)
                lines.Add(CenterText(DateTime.Now.ToString("dd/MM HH:mm"), SelectedDisplayColumns));

            VFDPreviewText = string.Join(Environment.NewLine, lines);
        }

        private string CenterText(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return new string(' ', width);
            if (text.Length >= width) return text.Substring(0, width);
            int left = (width - text.Length) / 2;
            return new string(' ', left) + text + new string(' ', width - text.Length - left);
        }

        private void BooleanRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                string name = rb.Name;

                switch (name)
                {
                    case "RequiresLoginYes": Config.RequiresLogin = true; break;
                    case "RequiresLoginNo": Config.RequiresLogin = false; break;
                    case "AutoSaveYes": Config.AutoSave = true; break;
                    case "AutoSaveNo": Config.AutoSave = false; break;
                    case "SoundEffectsYes": Config.SoundEffects = true; break;
                    case "SoundEffectsNo": Config.SoundEffects = false; break;
                    case "DarkModeYes": Config.DarkMode = true; break;
                    case "DarkModeNo": Config.DarkMode = false; break;
                    case "ShowNotificationsYes": Config.ShowNotifications = true; break;
                    case "ShowNotificationsNo": Config.ShowNotifications = false; break;
                    case "BackupDiarioYes": Config.BackupDiario = true; break;
                    case "BackupDiarioNo": Config.BackupDiario = false; break;
                    case "AutoBackupYes": Config.AutoBackup = true; break;
                    case "AutoBackupNo": Config.AutoBackup = false; break;
                    case "PrintOrderToKitchenYes": Config.PrintOrderToKitchen = true; break;
                    case "PrintOrderToKitchenNo": Config.PrintOrderToKitchen = false; break;
                    case "VFDEenabledYes": IsVFDEnabled = true; break;
                    case "VFDEenabledNo": IsVFDEnabled = false; break;
                    case "ShowRealTimeTotalYes": ShowRealTimeTotal = true; break;
                    case "ShowRealTimeTotalNo": ShowRealTimeTotal = false; break;
                    case "SecondScreenYes": Config.SecondScreenEnabled = true; break;
                    case "SecondScreenNo": Config.SecondScreenEnabled = false; break;
                }
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text[0]);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}