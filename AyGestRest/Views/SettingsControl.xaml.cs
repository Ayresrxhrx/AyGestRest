using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AyGestRest.Views
{
    public partial class SettingsControl : UserControl, INotifyPropertyChanged
    {
        private readonly AyGestRestContext _context;
        private RestaurantConfig _currentConfig;
        public static event Action ConfigSaved;
        private VFDConfig _vfdConfig;

        // Propriedade para config do restaurante
        public RestaurantConfig Config
        {
            get => _currentConfig;
            set
            {
                _currentConfig = value;
                OnPropertyChanged(nameof(Config));
            }
        }

        private bool _secondScreenEnabled;
        public bool SecondScreenEnabled
        {
            get => _secondScreenEnabled;
            set
            {
                _secondScreenEnabled = value;
                OnPropertyChanged(nameof(SecondScreenEnabled));
            }
        }

        // Propriedades para VFD
        private bool _isVFDEenabled;
        public bool IsVFDEenabled
        {
            get => _isVFDEenabled;
            set
            {
                _isVFDEenabled = value;
                OnPropertyChanged(nameof(IsVFDEenabled));
                UpdateVFDPreview();
            }
        }

        private string _selectedCOMPort = "COM1";
        public string SelectedCOMPort
        {
            get => _selectedCOMPort;
            set
            {
                _selectedCOMPort = value;
                OnPropertyChanged(nameof(SelectedCOMPort));
                UpdateVFDPreview();
            }
        }

        private int _selectedBaudRate = 9600;
        public int SelectedBaudRate
        {
            get => _selectedBaudRate;
            set
            {
                _selectedBaudRate = value;
                OnPropertyChanged(nameof(SelectedBaudRate));
            }
        }

        private int _selectedDisplayLines = 2;
        public int SelectedDisplayLines
        {
            get => _selectedDisplayLines;
            set
            {
                _selectedDisplayLines = value;
                OnPropertyChanged(nameof(SelectedDisplayLines));
                UpdateVFDPreview();
            }
        }

        private int _selectedDisplayColumns = 20;
        public int SelectedDisplayColumns
        {
            get => _selectedDisplayColumns;
            set
            {
                _selectedDisplayColumns = value;
                OnPropertyChanged(nameof(SelectedDisplayColumns));
                UpdateVFDPreview();
            }
        }

        private bool _showRealTimeTotal = true;
        public bool ShowRealTimeTotal
        {
            get => _showRealTimeTotal;
            set
            {
                _showRealTimeTotal = value;
                OnPropertyChanged(nameof(ShowRealTimeTotal));
            }
        }

        private string _vfdPreviewText = "Tela VFD Desativada";
        public string VFDPreviewText
        {
            get => _vfdPreviewText;
            set
            {
                _vfdPreviewText = value;
                OnPropertyChanged(nameof(VFDPreviewText));
            }
        }

        // Propriedades para impressão
        private string _selectedPrintMode = "Sequential";
        public string SelectedPrintMode
        {
            get => _selectedPrintMode;
            set
            {
                _selectedPrintMode = value;
                OnPropertyChanged(nameof(SelectedPrintMode));
                if (Config != null)
                    Config.PrintMode = value;
            }
        }

        // Listas para comboboxes
        public List<string> AvailablePrinters { get; set; }
        public List<string> AvailableCOMPorts { get; set; }
        public List<int> BaudRates { get; set; }
        public List<int> DisplayLines { get; set; }
        public List<int> DisplayColumns { get; set; }
        public List<string> PrintModes { get; set; }

        // Propriedades para estado de validação de email
        private bool _isEmailValid = true;
        private bool _isRecipientEmailValid = true;

        public event PropertyChangedEventHandler PropertyChanged;

        public SettingsControl()
        {
            try
            {
                InitializeComponent();
                DataContext = this;
                _context = new AyGestRestContext();

                // Inicializar listas
                InitializePrintModes();

                LoadSettings();
                InitializePrinters();
                InitializeCOMPorts();
                InitializeBaudRates();
                InitializeDisplayOptions();
                LoadVFDConfig();
                LoadRadioButtonStates();
                // Carregar informações do sistema
                LoadSystemInfo();
                LoadBackupList();
                LoadPrintSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no construtor: {ex.Message}");
            }
        }

        private void LoadSystemInfo()
        {
            try
            {
                // Pasta de dados
                string dataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");
                TxtDataPath.Text = dataPath;

                // Tamanho do banco
                string dbPath = Path.Combine(dataPath, "AyGestRest.db");
                if (File.Exists(dbPath))
                {
                    long size = new FileInfo(dbPath).Length;
                    TxtDatabaseSize.Text = FormatFileSize(size);
                }
                else
                {
                    TxtDatabaseSize.Text = "Não encontrado";
                }

                // Último backup
                string backupPath = Config?.BackupPath;
                if (string.IsNullOrEmpty(backupPath))
                {
                    backupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AyGestRest Backups");
                }

                if (Directory.Exists(backupPath))
                {
                    var backupFiles = Directory.GetFiles(backupPath, "*.zip")
                        .Union(Directory.GetFiles(backupPath, "*.aybackup"))
                        .Union(Directory.GetFiles(backupPath, "*.db"))
                        .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                        .FirstOrDefault();

                    if (backupFiles != null)
                    {
                        var info = new FileInfo(backupFiles);
                        TxtLastBackup.Text = $"{info.LastWriteTime:dd/MM/yyyy HH:mm} ({FormatFileSize(info.Length)})";
                    }
                    else
                    {
                        TxtLastBackup.Text = "Nenhum backup encontrado";
                    }
                }
                else
                {
                    TxtLastBackup.Text = "Pasta de backup não existe";
                }
            }
            catch (Exception ex)
            {
                TxtDatabaseSize.Text = "Erro ao carregar";
                TxtLastBackup.Text = "Erro ao carregar";
                Debug.WriteLine($"Erro ao carregar informações: {ex.Message}");
            }
        }

        private void RefreshSystemInfo_Click(object sender, RoutedEventArgs e)
        {
            LoadSystemInfo();
            LoadBackupList();
            ShowBackupResult("ℹ️ Informações atualizadas", "Dados do sistema atualizados com sucesso.", true);
        }

        private void LoadBackupList()
        {
            try
            {
                string backupPath = Config?.BackupPath;
                if (string.IsNullOrEmpty(backupPath))
                {
                    backupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AyGestRest Backups");
                }

                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                    LstBackups.ItemsSource = new List<BackupFileInfo>();
                    return;
                }

                var backupList = new List<BackupFileInfo>();

                // ZIP files
                foreach (var file in Directory.GetFiles(backupPath, "*.zip"))
                {
                    var info = new FileInfo(file);
                    backupList.Add(new BackupFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Date = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        Size = FormatFileSize(info.Length),
                        Type = "Backup ZIP"
                    });
                }

                // AYBACKUP files
                foreach (var file in Directory.GetFiles(backupPath, "*.aybackup"))
                {
                    var info = new FileInfo(file);
                    backupList.Add(new BackupFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Date = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        Size = FormatFileSize(info.Length),
                        Type = "Backup Completo"
                    });
                }

                // DB files (backups rápidos)
                foreach (var file in Directory.GetFiles(backupPath, "*.db"))
                {
                    var info = new FileInfo(file);
                    backupList.Add(new BackupFileInfo
                    {
                        FileName = Path.GetFileName(file),
                        FullPath = file,
                        Date = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        Size = FormatFileSize(info.Length),
                        Type = "Backup Rápido"
                    });
                }

                LstBackups.ItemsSource = backupList.OrderByDescending(b => b.Date).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar lista de backups: {ex.Message}");
            }
        }

        private void RefreshBackupList_Click(object sender, RoutedEventArgs e)
        {
            LoadBackupList();
            ShowBackupResult("📋 Lista atualizada", "Lista de backups atualizada.", true);
        }

        private void ViewBackupDetails_Click(object sender, RoutedEventArgs e)
        {
            if (LstBackups.SelectedItem is BackupFileInfo backup)
            {
                try
                {
                    string details = $"📁 Nome: {backup.FileName}\n" +
                                   $"📅 Data: {backup.Date}\n" +
                                   $"📦 Tamanho: {backup.Size}\n" +
                                   $"🔧 Tipo: {backup.Type}\n" +
                                   $"📍 Local: {backup.FullPath}";

                    MessageBox.Show(details, "Detalhes do Backup",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao exibir detalhes: {ex.Message}", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Selecione um backup para ver os detalhes.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            if (LstBackups.SelectedItem is BackupFileInfo backup)
            {
                var result = MessageBox.Show(
                    $"Tem certeza que deseja excluir o backup '{backup.FileName}'?\n\n" +
                    "Esta ação não pode ser desfeita.",
                    "Confirmar Exclusão",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(backup.FullPath);
                        LoadBackupList();
                        LoadSystemInfo();
                        ShowBackupResult("🗑️ Backup excluído", $"O backup '{backup.FileName}' foi excluído com sucesso.", true);
                    }
                    catch (Exception ex)
                    {
                        ShowBackupResult("❌ Erro ao excluir", $"Não foi possível excluir o backup: {ex.Message}", false);
                    }
                }
            }
            else
            {
                MessageBox.Show("Selecione um backup para excluir.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ========== EXPORTAR BACKUP COMPLETO ==========

        private async void ExportBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "Arquivo de Backup AyGestRest (*.aybackup)|*.aybackup|Arquivo ZIP (*.zip)|*.zip",
                    FileName = $"AyGestRest_Backup_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".aybackup",
                    Title = "Exportar Backup Completo"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    // Mostrar progresso
                    BackupProgressBar.Visibility = Visibility.Visible;
                    BackupProgressBar.IsIndeterminate = true;
                    ShowBackupResult("⏳ Criando backup...", "Preparando arquivos...", true);

                    await Task.Run(() => CreateCompleteBackup(saveDialog.FileName));

                    BackupProgressBar.Visibility = Visibility.Collapsed;
                    Mouse.OverrideCursor = null;

                    // Atualizar lista
                    LoadBackupList();
                    LoadSystemInfo();

                    // Configurar botão de ação
                    BtnBackupAction.Content = "Abrir Pasta";
                    BtnBackupAction.Tag = Path.GetDirectoryName(saveDialog.FileName);
                    BtnBackupAction.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                BackupProgressBar.Visibility = Visibility.Collapsed;
                ShowBackupResult("❌ Erro ao exportar backup",
                    $"Detalhes: {ex.Message}", false);
            }
        }

        private void CreateCompleteBackup(string outputPath)
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                string dbPath = Path.Combine(appDataPath, "AyGestRest.db");

                if (!File.Exists(dbPath))
                {
                    throw new FileNotFoundException("Banco de dados não encontrado");
                }

                // Criar pasta temporária
                string tempBackupPath = Path.Combine(Path.GetTempPath(), $"AyGestRest_Backup_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempBackupPath);

                // 1. Copiar banco de dados
                string backupDbPath = Path.Combine(tempBackupPath, "database.db");
                File.Copy(dbPath, backupDbPath, true);

                // 2. Copiar logos
                string logosPath = Path.Combine(appDataPath, "Logos");
                if (Directory.Exists(logosPath))
                {
                    string backupLogosPath = Path.Combine(tempBackupPath, "Logos");
                    Directory.CreateDirectory(backupLogosPath);

                    foreach (var file in Directory.GetFiles(logosPath))
                    {
                        string fileName = Path.GetFileName(file);
                        string destPath = Path.Combine(backupLogosPath, fileName);
                        File.Copy(file, destPath, true);
                    }
                }

                // 3. Copiar configurações
                var settings = new
                {
                    BackupDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    DatabaseSize = new FileInfo(backupDbPath).Length,
                    ContainsLogos = Directory.Exists(logosPath) && Directory.GetFiles(logosPath).Length > 0
                };

                string settingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Path.Combine(tempBackupPath, "settings.json"), settingsJson);

                // 4. Criar arquivo de informações
                File.WriteAllText(Path.Combine(tempBackupPath, "INFO.txt"),
                    $"AYGESTREST BACKUP COMPLETO\n" +
                    $"==========================\n" +
                    $"Data do Backup: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n" +
                    $"Versão do Sistema: 1.0\n" +
                    $"Conteúdo Incluído:\n" +
                    $"  • Base de dados completa\n" +
                    $"  • Logos e imagens\n" +
                    $"  • Configurações do sistema\n\n" +
                    $"PARA RESTAURAR:\n" +
                    $"1. No menu Configurações, vá para a aba 'Backup'\n" +
                    $"2. Clique em 'Restaurar do Backup'\n" +
                    $"3. Selecione este arquivo\n\n" +
                    $"⚠️ AVISO: A restauração substituirá todos os dados atuais!");

                // 5. Comprimir
                System.IO.Compression.ZipFile.CreateFromDirectory(tempBackupPath, outputPath);

                // 6. Limpar
                Directory.Delete(tempBackupPath, true);

                // Atualizar UI na thread principal
                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowBackupResult("✅ Backup exportado com sucesso!",
                        $"Arquivo: {Path.GetFileName(outputPath)}\n" +
                        $"Tamanho: {FormatFileSize(new FileInfo(outputPath).Length)}\n" +
                        $"Local: {Path.GetDirectoryName(outputPath)}",
                        true);
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao criar backup: {ex.Message}", ex);
            }
        }

        // ========== RESTAURAR BACKUP ==========

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "Arquivos de Backup (*.aybackup;*.zip)|*.aybackup;*.zip",
                    Title = "Selecionar Backup para Restaurar",
                    Multiselect = false
                };

                if (openDialog.ShowDialog() != true) return;

                string backupFilePath = openDialog.FileName;

                if (!File.Exists(backupFilePath))
                {
                    MessageBox.Show("Arquivo de backup não encontrado!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Confirmar
                var confirmResult = MessageBox.Show(
                    "⚠️ ATENÇÃO: RESTAURAÇÃO COMPLETA\n\n" +
                    "Esta operação irá:\n" +
                    "1. Substituir TODOS os dados atuais\n" +
                    "2. Substituir todas as imagens/logos\n" +
                    "3. O sistema será reiniciado\n\n" +
                    "✅ RECOMENDAÇÃO: Faça um backup atual antes de continuar!\n\n" +
                    "Deseja prosseguir com a restauração?",
                    "Confirmar Restauração",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult != MessageBoxResult.Yes) return;

                // Fazer backup atual primeiro
                string currentBackupPath = await CreateEmergencyBackup();

                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;
                    BackupProgressBar.Visibility = Visibility.Visible;
                    BackupProgressBar.IsIndeterminate = true;
                    ShowBackupResult("⏳ Restaurando backup...", "Extraindo arquivos...", true);

                    await Task.Run(() => RestoreFromBackupFile(backupFilePath));

                    BackupProgressBar.Visibility = Visibility.Collapsed;
                    Mouse.OverrideCursor = null;

                    ShowBackupResult("✅ Backup restaurado com sucesso!",
                        "O sistema será reiniciado para aplicar as alterações.\n" +
                        "Clique em OK para continuar.",
                        true);

                    // Perguntar sobre reinício
                    var restartResult = MessageBox.Show(
                        "✅ Restauração concluída com sucesso!\n\n" +
                        "O sistema precisa ser reiniciado para aplicar as alterações.\n\n" +
                        "Deseja reiniciar agora?",
                        "Reiniciar Sistema",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (restartResult == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                catch (Exception ex)
                {
                    // Tentar restaurar backup de emergência
                    try
                    {
                        MessageBox.Show(
                            "Ocorreu um erro durante a restauração. Revertendo para o estado anterior...",
                            "Erro na Restauração",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        await Task.Run(() => RestoreFromBackupFile(currentBackupPath));

                        MessageBox.Show(
                            "Sistema restaurado para o estado anterior.",
                            "Reversão Concluída",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception revertEx)
                    {
                        MessageBox.Show(
                            $"❌ ERRO CRÍTICO!\n\n" +
                            $"Não foi possível restaurar o sistema.\n\n" +
                            $"Entre em contato com o suporte técnico.\n\n" +
                            $"Erro original: {ex.Message}\n" +
                            $"Erro na reversão: {revertEx.Message}",
                            "Erro Crítico",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    Mouse.OverrideCursor = null;
                    BackupProgressBar.Visibility = Visibility.Collapsed;
                }
                finally
                {
                    // Limpar backup de emergência
                    if (File.Exists(currentBackupPath))
                    {
                        try { File.Delete(currentBackupPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowBackupResult("❌ Erro ao restaurar backup",
                    $"Detalhes: {ex.Message}", false);
            }
        }

        private async Task<string> CreateEmergencyBackup()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"Emergency_Backup_{Guid.NewGuid()}.zip");

            await Task.Run(() =>
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                string tempDir = Path.Combine(Path.GetTempPath(), $"Temp_Emergency_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                // Copiar banco
                string dbPath = Path.Combine(appDataPath, "AyGestRest.db");
                if (File.Exists(dbPath))
                {
                    File.Copy(dbPath, Path.Combine(tempDir, "database.db"), true);
                }

                // Comprimir
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, tempPath);

                // Limpar
                Directory.Delete(tempDir, true);
            });

            return tempPath;
        }

        private void RestoreFromBackupFile(string backupPath)
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest");

            // Extrair backup
            string extractPath = Path.Combine(Path.GetTempPath(), $"Restore_{Guid.NewGuid()}");
            Directory.CreateDirectory(extractPath);

            System.IO.Compression.ZipFile.ExtractToDirectory(backupPath, extractPath);

            // Fechar conexão com banco
            _context?.Dispose();

            // Aguardar liberação do arquivo
            System.Threading.Thread.Sleep(1000);

            // Restaurar banco
            string backupDbPath = Path.Combine(extractPath, "database.db");
            if (File.Exists(backupDbPath))
            {
                string targetDbPath = Path.Combine(appDataPath, "AyGestRest.db");

                // Tentar várias vezes se o arquivo estiver em uso
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        File.Copy(backupDbPath, targetDbPath, true);
                        break;
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(500);
                    }
                }
            }

            // Restaurar logos
            string backupLogosPath = Path.Combine(extractPath, "Logos");
            if (Directory.Exists(backupLogosPath))
            {
                string targetLogosPath = Path.Combine(appDataPath, "Logos");

                if (Directory.Exists(targetLogosPath))
                {
                    Directory.Delete(targetLogosPath, true);
                }

                Directory.CreateDirectory(targetLogosPath);
                foreach (var file in Directory.GetFiles(backupLogosPath))
                {
                    string fileName = Path.GetFileName(file);
                    string destPath = Path.Combine(targetLogosPath, fileName);
                    File.Copy(file, destPath, true);
                }
            }

            // Limpar
            Directory.Delete(extractPath, true);
        }

        // ========== BACKUP RÁPIDO ==========

        private void QuickBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupPath = Config?.BackupPath;
                if (string.IsNullOrEmpty(backupPath))
                {
                    backupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AyGestRest Backups");
                }

                Directory.CreateDirectory(backupPath);

                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                if (!File.Exists(dbPath))
                {
                    ShowBackupResult("❌ Erro no backup", "Banco de dados não encontrado.", false);
                    return;
                }

                string backupFile = Path.Combine(
                    backupPath,
                    $"Backup_Rapido_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                Mouse.OverrideCursor = Cursors.Wait;

                File.Copy(dbPath, backupFile, true);

                Mouse.OverrideCursor = null;

                LoadBackupList();
                LoadSystemInfo();

                ShowBackupResult("✅ Backup rápido criado!",
                    $"Arquivo: {Path.GetFileName(backupFile)}\n" +
                    $"Tamanho: {FormatFileSize(new FileInfo(backupFile).Length)}\n" +
                    $"Local: {backupPath}",
                    true);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowBackupResult("❌ Erro no backup rápido", $"Detalhes: {ex.Message}", false);
            }
        }

        private async void QuickRestore_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openDialog = new OpenFileDialog
                {
                    Filter = "Backup Rápido (*.db)|*.db",
                    Title = "Selecionar Backup Rápido",
                    Multiselect = false
                };

                if (openDialog.ShowDialog() != true) return;

                string backupPath = openDialog.FileName;

                if (!File.Exists(backupPath))
                {
                    MessageBox.Show("Arquivo de backup não encontrado!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Verificar se é um banco válido
                try
                {
                    using (var testConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backupPath}"))
                    {
                        await testConn.OpenAsync();
                        testConn.Close();
                    }
                }
                catch
                {
                    MessageBox.Show("O arquivo selecionado não é um banco de dados válido!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var confirmResult = MessageBox.Show(
                    "⚠️ Restaurar backup rápido?\n\n" +
                    "Esta operação substituirá o banco de dados atual.\n" +
                    "Certifique-se de ter feito um backup recente.\n\n" +
                    "Deseja continuar?",
                    "Confirmar Restauração",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult != MessageBoxResult.Yes) return;

                Mouse.OverrideCursor = Cursors.Wait;

                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                // Fechar conexão
                _context?.Dispose();

                // Aguardar
                await Task.Delay(1000);

                // Copiar arquivo
                File.Copy(backupPath, dbPath, true);

                Mouse.OverrideCursor = null;

                ShowBackupResult("✅ Backup rápido restaurado!",
                    "O banco de dados foi restaurado com sucesso.\n" +
                    "Reinicie o sistema para aplicar as alterações.",
                    true);

                MessageBox.Show("Backup restaurado com sucesso! Reinicie o sistema.",
                    "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                ShowBackupResult("❌ Erro ao restaurar", $"Detalhes: {ex.Message}", false);
            }
        }

        // ========== MÉTODOS AUXILIARES ==========

        private void ShowBackupResult(string title, string details, bool isSuccess)
        {
            if (BackupResultPanel != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    BackupResultPanel.Visibility = Visibility.Visible;
                    BackupResultPanel.Background = isSuccess
                        ? new SolidColorBrush(Color.FromArgb(255, 232, 245, 233))
                        : new SolidColorBrush(Color.FromArgb(255, 255, 235, 238));

                    BackupResultPanel.BorderBrush = isSuccess
                        ? new SolidColorBrush(Color.FromArgb(255, 165, 214, 167))
                        : new SolidColorBrush(Color.FromArgb(255, 239, 154, 154));

                    TxtBackupResultIcon.Text = isSuccess ? "✅" : "❌";
                    TxtBackupResult.Text = title;
                    TxtBackupDetails.Text = details;

                    // Auto-esconder após 10 segundos
                    if (isSuccess)
                    {
                        var timer = new System.Windows.Threading.DispatcherTimer();
                        timer.Interval = TimeSpan.FromSeconds(10);
                        timer.Tick += (s, e) =>
                        {
                            BackupResultPanel.Visibility = Visibility.Collapsed;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                });
            }
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private void RestartApplication()
        {
            try
            {
                // Fechar aplicação atual
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(currentExe);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao reiniciar: {ex.Message}\n\nPor favor, reinicie manualmente.",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== MÉTODOS EXISTENTES MODIFICADOS ==========

        private async void BackupNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupPath = Config.BackupPath;
                if (string.IsNullOrWhiteSpace(backupPath))
                {
                    backupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AyGestRest Backups");
                }

                Directory.CreateDirectory(backupPath);

                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                if (!File.Exists(dbPath))
                {
                    ShowBackupResult("❌ Erro no backup", "Base de dados não encontrada.", false);
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                BackupProgressBar.Visibility = Visibility.Visible;
                BackupProgressBar.IsIndeterminate = true;

                string backupFile = Path.Combine(
                    backupPath,
                    $"AyGestRest_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

                await Task.Run(() => CreateCompleteBackup(backupFile));

                BackupProgressBar.Visibility = Visibility.Collapsed;
                Mouse.OverrideCursor = null;

                LoadBackupList();
                LoadSystemInfo();

                // Configurar botão de ação
                BtnBackupAction.Content = "Abrir Pasta";
                BtnBackupAction.Tag = backupPath;
                BtnBackupAction.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                BackupProgressBar.Visibility = Visibility.Collapsed;
                ShowBackupResult("❌ Erro no backup", $"Detalhes: {ex.Message}", false);
            }
        }

        private void OpenBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string backupPath = Config.BackupPath;
                if (string.IsNullOrEmpty(backupPath))
                {
                    backupPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "AyGestRest Backups");
                }

                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                Process.Start("explorer.exe", backupPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir pasta: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Adicione este método ao construtor para inicializar
        private void InitializeBackupTab()
        {
            // Carregar lista de backups
            LoadBackupList();

            // Configurar auto-atualização
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(30);
            timer.Tick += (s, e) =>
            {
                if (IsVisible)
                {
                    LoadSystemInfo();
                }
            };
            timer.Start();
        }

        private void InitializePrintModes()
        {
            PrintModes = new List<string>
            {
                "Sequential",      // Uma cópia completa após a outra
                "Collated"         // Tudo em uma página se possível
            };
            OnPropertyChanged(nameof(PrintModes));
        }

        private void LoadSettings()
        {
            Config = _context.RestaurantConfigs.FirstOrDefault();
            if (Config == null)
            {
                Config = new RestaurantConfig
                {
                    CopiesCount = 1, // Valor padrão
                    PrintMode = "Sequential" // Modo padrão
                };
                _context.RestaurantConfigs.Add(Config);
                _context.SaveChanges();
            }

            // Garantir valores válidos para cópias
            if (Config.CopiesCount < 1 || Config.CopiesCount > 10)
            {
                Config.CopiesCount = 1;
            }

            if (string.IsNullOrEmpty(Config.PrintMode))
            {
                Config.PrintMode = "Sequential";
            }

            SelectedPrintMode = Config.PrintMode;

            UpdateLogoPreview();
            SecondScreenEnabled = Config.SecondScreenEnabled;

            // Carregar senha diretamente do campo simples
            if (!string.IsNullOrEmpty(Config.EmailPassword))
            {
                PwdEmailPassword.Password = Config.EmailPassword;
            }
            else
            {
                PwdEmailPassword.Password = "";
            }
        }

        private void LoadPrintSettings()
        {
            if (Config == null) return;

            // Configurar número de cópias
            if (Config.CopiesCount < 1 || Config.CopiesCount > 10)
            {
                Config.CopiesCount = 1;
            }

            // Configurar modo de impressão
            if (!string.IsNullOrEmpty(Config.PrintMode))
            {
                SelectedPrintMode = Config.PrintMode;
            }
            else
            {
                SelectedPrintMode = "Sequential";
            }

            // Atualizar radio buttons do modo de impressão
            UpdatePrintModeRadioButtons();
        }

        private void UpdatePrintModeRadioButtons()
        {
            if (string.IsNullOrEmpty(SelectedPrintMode)) return;

            if (PrintModeSequential != null)
                PrintModeSequential.IsChecked = SelectedPrintMode == "Sequential";

            if (PrintModeCollated != null)
                PrintModeCollated.IsChecked = SelectedPrintMode == "Collated";
        }

        private void LoadVFDConfig()
        {
            _vfdConfig = _context.VFDConfigs.FirstOrDefault();
            if (_vfdConfig == null)
            {
                _vfdConfig = new VFDConfig
                {
                    IsEnabled = false,
                    COMPort = "COM1",
                    BaudRate = 9600,
                    DisplayLines = 2,
                    DisplayColumns = 20
                };
                _context.VFDConfigs.Add(_vfdConfig);
                _context.SaveChanges();
            }

            IsVFDEenabled = _vfdConfig.IsEnabled;
            SelectedCOMPort = _vfdConfig.COMPort;
            SelectedBaudRate = _vfdConfig.BaudRate;
            SelectedDisplayLines = _vfdConfig.DisplayLines;
            SelectedDisplayColumns = _vfdConfig.DisplayColumns;
            UpdateVFDPreview();
        }

        // ============================================
        // MÉTODOS PARA CONTROLE DE CÓPIAS
        // ============================================

        private void BtnDecreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null || TxtCopiesCount == null) return;

                int currentCopies = Config.CopiesCount;
                if (currentCopies > 1)
                {
                    Config.CopiesCount = currentCopies - 1;
                    TxtCopiesCount.Text = Config.CopiesCount.ToString();
                    OnPropertyChanged(nameof(Config));

                    // Feedback visual
                    ShowCopiesChangeFeedback($"Cópias reduzidas para {Config.CopiesCount}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao diminuir cópias: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnIncreaseCopies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null || TxtCopiesCount == null) return;

                int currentCopies = Config.CopiesCount;
                if (currentCopies < 10)
                {
                    Config.CopiesCount = currentCopies + 1;
                    TxtCopiesCount.Text = Config.CopiesCount.ToString();
                    OnPropertyChanged(nameof(Config));

                    // Feedback visual
                    ShowCopiesChangeFeedback($"Cópias aumentadas para {Config.CopiesCount}");
                }
                else
                {
                    MessageBox.Show("O número máximo de cópias é 10.", "Limite Máximo",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aumentar cópias: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowCopiesChangeFeedback(string message)
        {
            // Feedback simples no console
            Debug.WriteLine($"📋 {message}");
        }

        // ============================================
        // MÉTODOS PARA PERFIS RÁPIDOS
        // ============================================

        private void ApplyRestaurantProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null) return;

                Config.CopiesCount = 1;
                Config.PrintMode = "Sequential";
                Config.PrintOrderToKitchen = true;

                SelectedPrintMode = "Sequential";

                if (TxtCopiesCount != null)
                    TxtCopiesCount.Text = "1";

                UpdatePrintModeRadioButtons();
                OnPropertyChanged(nameof(Config));

                ShowProfileAppliedMessage("🏨 Restaurante",
                    "Configurações aplicadas:\n• 1 cópia por talão\n• Modo sequencial\n• Comanda para cozinha: Ativada");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar perfil: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyBarProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null) return;

                Config.CopiesCount = 1;
                Config.PrintMode = "Collated";
                Config.PrintOrderToKitchen = false;

                SelectedPrintMode = "Collated";

                if (TxtCopiesCount != null)
                    TxtCopiesCount.Text = "1";

                UpdatePrintModeRadioButtons();
                OnPropertyChanged(nameof(Config));

                ShowProfileAppliedMessage("☕ Bar/Café",
                    "Configurações aplicadas:\n• 1 cópia por talão\n• Modo intercalado\n• Comanda para cozinha: Desativada");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar perfil: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyPartyProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null) return;

                Config.CopiesCount = 2;
                Config.PrintMode = "Sequential";
                Config.PrintOrderToKitchen = true;

                SelectedPrintMode = "Sequential";

                if (TxtCopiesCount != null)
                    TxtCopiesCount.Text = "2";

                UpdatePrintModeRadioButtons();
                OnPropertyChanged(nameof(Config));

                ShowProfileAppliedMessage("🎉 Festas/Eventos",
                    "Configurações aplicadas:\n• 2 cópias por talão\n• Modo sequencial\n• Comanda para cozinha: Ativada\n\nIdeal para: cliente + arquivo");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar perfil: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyDeliveryProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null) return;

                Config.CopiesCount = 2;
                Config.PrintMode = "Sequential";
                Config.PrintOrderToKitchen = true;

                SelectedPrintMode = "Sequential";

                if (TxtCopiesCount != null)
                    TxtCopiesCount.Text = "2";

                UpdatePrintModeRadioButtons();
                OnPropertyChanged(nameof(Config));

                ShowProfileAppliedMessage("🚚 Delivery",
                    "Configurações aplicadas:\n• 2 cópias por talão\n• Modo sequencial\n• Comanda para cozinha: Ativada\n\nIdeal para: cliente + motoboy");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar perfil: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyHotelProfile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null) return;

                Config.CopiesCount = 3;
                Config.PrintMode = "Sequential";
                Config.PrintOrderToKitchen = true;

                SelectedPrintMode = "Sequential";

                if (TxtCopiesCount != null)
                    TxtCopiesCount.Text = "3";

                UpdatePrintModeRadioButtons();
                OnPropertyChanged(nameof(Config));

                ShowProfileAppliedMessage("🏨 Hotel",
                    "Configurações aplicadas:\n• 3 cópias por talão\n• Modo sequencial\n• Comanda para cozinha: Ativada\n\nIdeal para: cliente + cozinha + arquivo");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar perfil: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowProfileAppliedMessage(string profileName, string description)
        {
            try
            {
                // Mostrar no painel de resultado
                if (TestCopiesResultPanel != null && TxtCopiesTestResult != null && TxtCopiesTestDetails != null)
                {
                    TestCopiesResultPanel.Visibility = Visibility.Visible;
                    TestCopiesResultPanel.Background = new SolidColorBrush(
                        Color.FromArgb(255, 232, 245, 233));
                    TestCopiesResultPanel.BorderBrush = new SolidColorBrush(
                        Color.FromArgb(255, 165, 214, 167));

                    TxtCopiesTestResult.Text = $"✅ Perfil '{profileName}' aplicado";
                    TxtCopiesTestResult.Foreground = new SolidColorBrush(
                        Color.FromArgb(255, 46, 125, 50));
                    TxtCopiesTestDetails.Text = description;
                }

                // Mostrar mensagem de confirmação
                MessageBox.Show($"✅ Perfil '{profileName}' aplicado com sucesso!\n\n{description}",
                                "Perfil Aplicado",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao mostrar mensagem de perfil: {ex.Message}");
            }
        }

        // ============================================
        // MÉTODOS EXISTENTES
        // ============================================

        private void RemoveLogo_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Deseja remover o logo atual?", "Remover Logo",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Config.LogoPath = null;
                UpdateLogoPreview();
                OnPropertyChanged(nameof(Config));

                string logosFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "Logos");

                if (Directory.Exists(logosFolder))
                {
                    foreach (var file in Directory.GetFiles(logosFolder, "restaurant_logo.*"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }

                MessageBox.Show("Logo removido com sucesso.", "Sucesso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void UpdateLogoPreview()
        {
            if (string.IsNullOrWhiteSpace(Config?.LogoPath))
            {
                LogoPreviewImage.Source = null;
                return;
            }

            string logoPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest",
                "Logos",
                Config.LogoPath);

            if (File.Exists(logoPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(logoPath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                LogoPreviewImage.Source = bitmap;
            }
            else
            {
                LogoPreviewImage.Source = null;
            }
        }

        private void InitializePrinters()
        {
            AvailablePrinters = new List<string> { "(Nenhuma)" };
            foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                AvailablePrinters.Add(printer);
            }
            OnPropertyChanged(nameof(AvailablePrinters));
        }

        private void InitializeCOMPorts()
        {
            AvailableCOMPorts = new List<string> { "(Nenhuma)" };
            AvailableCOMPorts.AddRange(SerialPort.GetPortNames());
            OnPropertyChanged(nameof(AvailableCOMPorts));
        }

        private void InitializeBaudRates()
        {
            BaudRates = new List<int> { 9600, 19200, 38400, 57600, 115200 };
            OnPropertyChanged(nameof(BaudRates));
        }

        private void InitializeDisplayOptions()
        {
            DisplayLines = new List<int> { 1, 2, 4 };
            DisplayColumns = new List<int> { 16, 20, 24, 32, 40 };
            OnPropertyChanged(nameof(DisplayLines));
            OnPropertyChanged(nameof(DisplayColumns));
        }

        private void LoadRadioButtonStates()
        {
            SetRadioButton("RequiresLoginYes", "RequiresLoginNo", Config.RequiresLogin == true);
            SetRadioButton("AutoSaveYes", "AutoSaveNo", Config.AutoSave);
            SetRadioButton("SoundEffectsYes", "SoundEffectsNo", Config.SoundEffects == true);
            SetRadioButton("DarkModeYes", "DarkModeNo", Config.DarkMode);
            SetRadioButton("ShowNotificationsYes", "ShowNotificationsNo", Config.ShowNotifications);
            SetRadioButton("BackupDiarioYes", "BackupDiarioNo", Config.BackupDiario == true);
            SetRadioButton("AutoBackupYes", "AutoBackupNo", Config.AutoBackup);
            SetRadioButton("PrintOrderToKitchenYes", "PrintOrderToKitchenNo", Config.PrintOrderToKitchen);
            SetRadioButton("VFDEenabledYes", "VFDEenabledNo", IsVFDEenabled);
            SetRadioButton("ShowRealTimeTotalYes", "ShowRealTimeTotalNo", ShowRealTimeTotal);
            SetRadioButton("SecondScreenYes", "SecondScreenNo", Config.SecondScreenEnabled);
            SetRadioButton("SSLYes", "SSLNo", Config.UseSSL);
            SetRadioButton("SendDailyReportYes", "SendDailyReportNo", Config.SendDailyReport);
            SetRadioButton("SendReportsYes", "SendReportsNo", Config.SendReports);
        }

        private void SetRadioButton(string yesName, string noName, bool value)
        {
            var yesRadio = this.FindName(yesName) as RadioButton;
            var noRadio = this.FindName(noName) as RadioButton;

            if (yesRadio != null && noRadio != null)
            {
                yesRadio.IsChecked = value;
                noRadio.IsChecked = !value;
            }
        }

        private void BooleanRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                string radioName = radioButton.Name;
                bool isChecked = radioButton.IsChecked == true;
                UpdatePropertyFromRadioButton(radioName, isChecked);
            }
        }

        private void PrintModeRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is RadioButton radioButton && radioButton.IsChecked == true && Config != null)
                {
                    switch (radioButton.Name)
                    {
                        case "PrintModeSequential":
                            SelectedPrintMode = "Sequential";
                            Config.PrintMode = "Sequential";
                            Debug.WriteLine("✅ Modo de impressão alterado para: Sequencial");
                            break;

                        case "PrintModeCollated":
                            SelectedPrintMode = "Collated";
                            Config.PrintMode = "Collated";
                            Debug.WriteLine("✅ Modo de impressão alterado para: Intercalado");
                            break;
                    }

                    OnPropertyChanged(nameof(Config));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao alterar modo de impressão: {ex.Message}");
            }
        }

        private void UpdatePropertyFromRadioButton(string radioName, bool isChecked)
        {
            switch (radioName)
            {
                case "RequiresLoginYes":
                    Config.RequiresLogin = isChecked;
                    break;
                case "RequiresLoginNo":
                    Config.RequiresLogin = !isChecked;
                    break;
                case "AutoSaveYes":
                    Config.AutoSave = isChecked;
                    break;
                case "AutoSaveNo":
                    Config.AutoSave = !isChecked;
                    break;
                case "SecondScreenYes":
                    Config.SecondScreenEnabled = isChecked;
                    SecondScreenEnabled = isChecked;
                    break;
                case "SecondScreenNo":
                    Config.SecondScreenEnabled = !isChecked;
                    SecondScreenEnabled = !isChecked;
                    break;
                case "SoundEffectsYes":
                    Config.SoundEffects = isChecked;
                    break;
                case "SoundEffectsNo":
                    Config.SoundEffects = !isChecked;
                    break;
                case "DarkModeYes":
                    Config.DarkMode = isChecked;
                    break;
                case "DarkModeNo":
                    Config.DarkMode = !isChecked;
                    break;
                case "ShowNotificationsYes":
                    Config.ShowNotifications = isChecked;
                    break;
                case "ShowNotificationsNo":
                    Config.ShowNotifications = !isChecked;
                    break;
                case "BackupDiarioYes":
                    Config.BackupDiario = isChecked;
                    break;
                case "BackupDiarioNo":
                    Config.BackupDiario = !isChecked;
                    break;
                case "AutoBackupYes":
                    Config.AutoBackup = isChecked;
                    break;
                case "AutoBackupNo":
                    Config.AutoBackup = !isChecked;
                    break;
                case "PrintOrderToKitchenYes":
                    Config.PrintOrderToKitchen = isChecked;
                    break;
                case "PrintOrderToKitchenNo":
                    Config.PrintOrderToKitchen = !isChecked;
                    break;
                case "VFDEenabledYes":
                    IsVFDEenabled = isChecked;
                    break;
                case "VFDEenabledNo":
                    IsVFDEenabled = !isChecked;
                    break;
                case "ShowRealTimeTotalYes":
                    ShowRealTimeTotal = isChecked;
                    break;
                case "ShowRealTimeTotalNo":
                    ShowRealTimeTotal = !isChecked;
                    break;
                case "SSLYes":
                    Config.UseSSL = isChecked;
                    break;
                case "SSLNo":
                    Config.UseSSL = !isChecked;
                    break;
                case "SendDailyReportYes":
                    Config.SendDailyReport = isChecked;
                    break;
                case "SendDailyReportNo":
                    Config.SendDailyReport = !isChecked;
                    break;
                case "SendReportsYes":
                    Config.SendReports = isChecked;
                    break;
                case "SendReportsNo":
                    Config.SendReports = !isChecked;
                    break;
            }
        }

        private void UpdateVFDPreview()
        {
            if (!IsVFDEenabled)
            {
                VFDPreviewText = "Tela VFD Desativada";
                return;
            }

            var lines = new List<string>();
            string restaurantName = Config?.RestaurantName ?? "Meu Restaurante";
            if (restaurantName.Length > SelectedDisplayColumns)
                restaurantName = restaurantName.Substring(0, SelectedDisplayColumns);
            lines.Add(CenterText(restaurantName, SelectedDisplayColumns));

            if (SelectedDisplayLines >= 2)
            {
                string datetime = DateTime.Now.ToString("dd/MM HH:mm");
                lines.Add(CenterText(datetime, SelectedDisplayColumns));
            }

            if (SelectedDisplayLines >= 3)
            {
                string portInfo = $"COM:{SelectedCOMPort}";
                lines.Add(CenterText(portInfo, SelectedDisplayColumns));
            }

            if (SelectedDisplayLines >= 4)
            {
                string displayInfo = $"{SelectedDisplayLines}x{SelectedDisplayColumns}";
                lines.Add(CenterText(displayInfo, SelectedDisplayColumns));
            }

            VFDPreviewText = string.Join(Environment.NewLine, lines);
        }

        private string CenterText(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return new string(' ', width);
            if (text.Length >= width) return text.Substring(0, width);

            int spaces = width - text.Length;
            int leftSpaces = spaces / 2;
            int rightSpaces = spaces - leftSpaces;

            return new string(' ', leftSpaces) + text + new string(' ', rightSpaces);
        }

        private void BrowseLogo_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Imagens (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos (*.*)|*.*",
                Title = "Selecionar Logo do Restaurante"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos");
                    Directory.CreateDirectory(appDataPath);

                    string extension = Path.GetExtension(openFileDialog.FileName).ToLowerInvariant();
                    string fileName = "restaurant_logo" + extension;
                    string destinationPath = Path.Combine(appDataPath, fileName);

                    File.Copy(openFileDialog.FileName, destinationPath, overwrite: true);
                    Config.LogoPath = fileName;
                    UpdateLogoPreview();
                    OnPropertyChanged(nameof(Config));

                    MessageBox.Show("Logo carregado com sucesso!\nA imagem será exibida após salvar as configurações.",
                        "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao carregar o logo: {ex.Message}", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BrowseBackupFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Config.BackupPath = dialog.SelectedPath;
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

        private void TestCopies_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null || TxtCopiesCount == null) return;

                if (!int.TryParse(TxtCopiesCount.Text, out int copies) || copies < 1 || copies > 10)
                {
                    MessageBox.Show("Por favor, insira um número válido de cópias (1 a 10).",
                                    "Valor Inválido",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                    return;
                }

                // Atualizar o valor no Config
                Config.CopiesCount = copies;

                // Mostrar painel de resultado
                if (TestCopiesResultPanel != null && TxtCopiesTestResult != null && TxtCopiesTestDetails != null)
                {
                    TestCopiesResultPanel.Visibility = Visibility.Visible;
                    TestCopiesResultPanel.Background = new SolidColorBrush(
                        Color.FromArgb(255, 255, 243, 224));
                    TestCopiesResultPanel.BorderBrush = new SolidColorBrush(
                        Color.FromArgb(255, 255, 204, 128));

                    TxtCopiesTestResult.Text = "⏳ Testando impressão...";
                    TxtCopiesTestResult.Foreground = new SolidColorBrush(
                        Color.FromArgb(255, 230, 81, 0));
                    TxtCopiesTestDetails.Text = $"Preparando {copies} cópias no modo {SelectedPrintMode}...";
                }

                // Executar o teste
                TestMultipleCopies(Config.PrinterName, copies);

                // Atualizar resultado no painel (se o teste não lançou exceção)
                if (TestCopiesResultPanel != null && TxtCopiesTestResult != null && TxtCopiesTestDetails != null)
                {
                    TxtCopiesTestResult.Text = "✅ Teste enviado!";
                    TxtCopiesTestResult.Foreground = new SolidColorBrush(
                        Color.FromArgb(255, 46, 125, 50));
                    TxtCopiesTestDetails.Text = $"{copies} cópias enviadas para impressão.\nModo: {SelectedPrintMode}\nVerifique a impressora.";
                }
            }
            catch (Exception ex)
            {
                // Atualizar resultado de erro no painel
                if (TestCopiesResultPanel != null && TxtCopiesTestResult != null && TxtCopiesTestDetails != null)
                {
                    TxtCopiesTestResult.Text = "❌ Erro no teste";
                    TxtCopiesTestResult.Foreground = new SolidColorBrush(
                        Color.FromArgb(255, 211, 47, 47));
                    TxtCopiesTestDetails.Text = $"Erro: {ex.Message}";
                }

                MessageBox.Show($"Erro ao testar cópias: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestPrinter(string printerName, string printerType)
        {
            if (string.IsNullOrWhiteSpace(printerName) || printerName == "(Nenhuma)")
            {
                MessageBox.Show($"Selecione uma {printerType.ToLower()} primeiro.", "Aviso",
                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var printDoc = new System.Drawing.Printing.PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;

                if (!printDoc.PrinterSettings.IsValid)
                {
                    MessageBox.Show("A impressora selecionada não é válida.", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                printDoc.PrintPage += (s, e) =>
                {
                    var font = new System.Drawing.Font("Consolas", 10);
                    float y = 10;
                    e.Graphics.DrawString("=== TESTE DE IMPRESSÃO ===", font, System.Drawing.Brushes.Black, 10, y);
                    y += 25;
                    e.Graphics.DrawString($"Impressora: {printerName}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 25;
                    e.Graphics.DrawString($"Data: {DateTime.Now}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 40;
                    e.Graphics.DrawString("IMPRESSÃO OK ✅", font, System.Drawing.Brushes.Black, 10, y);
                };

                printDoc.Print();

                MessageBox.Show("Teste de impressão enviado com sucesso!", "Sucesso",
                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro real ao imprimir: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestMultipleCopies(string printerName, int copies)
        {
            if (string.IsNullOrWhiteSpace(printerName) || printerName == "(Nenhuma)")
            {
                MessageBox.Show("Selecione uma impressora de talões primeiro.", "Aviso",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (copies < 1 || copies > 10)
            {
                MessageBox.Show("Número de cópias deve estar entre 1 e 10.", "Aviso",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var printDoc = new System.Drawing.Printing.PrintDocument();
                printDoc.PrinterSettings.PrinterName = printerName;

                // Configurar número de cópias
                printDoc.PrinterSettings.Copies = (short)copies;

                if (!printDoc.PrinterSettings.IsValid)
                {
                    MessageBox.Show("A impressora selecionada não é válida.", "Erro",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int copyCount = 0;
                printDoc.PrintPage += (s, e) =>
                {
                    copyCount++;
                    var font = new System.Drawing.Font("Consolas", 10);
                    float y = 10;

                    e.Graphics.DrawString($"=== TESTE DE IMPRESSÃO ===", font, System.Drawing.Brushes.Black, 10, y);
                    y += 25;
                    e.Graphics.DrawString($"Impressora: {printerName}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 20;
                    e.Graphics.DrawString($"Cópia: {copyCount} de {copies}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 20;
                    e.Graphics.DrawString($"Modo: {SelectedPrintMode}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 20;
                    e.Graphics.DrawString($"Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss}", font, System.Drawing.Brushes.Black, 10, y);
                    y += 40;
                    e.Graphics.DrawString("IMPRESSÃO DE MÚLTIPLAS CÓPIAS OK ✅", font, System.Drawing.Brushes.Black, 10, y);

                    // Se for modo sequencial e ainda houver cópias, continua
                    if (SelectedPrintMode == "Sequential" && copyCount < copies)
                    {
                        e.HasMorePages = true;
                    }
                    else
                    {
                        e.HasMorePages = false;
                    }
                };

                printDoc.EndPrint += (s, e) =>
                {
                    if (e.PrintAction == System.Drawing.Printing.PrintAction.PrintToPrinter)
                    {
                        MessageBox.Show($"Teste de {copies} cópias enviado com sucesso!\n\nImpressora: {printerName}\nModo: {SelectedPrintMode}",
                                        "Teste Concluído",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                    }
                };

                printDoc.Print();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao imprimir múltiplas cópias: {ex.Message}", "Erro",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DetectCOMPorts_Click(object sender, RoutedEventArgs e)
        {
            AvailableCOMPorts = new List<string> { "(Nenhuma)" };
            foreach (var port in SerialPort.GetPortNames())
            {
                try
                {
                    using (var sp = new SerialPort(port))
                    {
                        sp.Open();
                        sp.Close();
                        AvailableCOMPorts.Add(port);
                    }
                }
                catch { }
            }

            OnPropertyChanged(nameof(AvailableCOMPorts));
            MessageBox.Show($"{AvailableCOMPorts.Count - 1} porta(s) COM funcionais detectadas.",
            "COM Ports", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void TestVFDConnection_Click(object sender, RoutedEventArgs e)
        {
            if (!IsVFDEenabled || string.IsNullOrEmpty(SelectedCOMPort) || SelectedCOMPort == "(Nenhuma)")
            {
                MessageBox.Show("Ative a tela VFD e selecione uma porta COM válida.", "Aviso",
                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    using (var serialPort = new SerialPort(SelectedCOMPort, SelectedBaudRate))
                    {
                        serialPort.Open();
                        byte[] clear = { 0x0C };
                        serialPort.Write(clear, 0, clear.Length);
                        byte[] text = System.Text.Encoding.ASCII.GetBytes("TESTE VFD OK");
                        serialPort.Write(text, 0, text.Length);
                        serialPort.Close();
                    }
                });

                MessageBox.Show("Texto enviado para o VFD com sucesso!", "Sucesso",
                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro REAL no VFD: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateVFDPreview_Click(object sender, RoutedEventArgs e)
        {
            UpdateVFDPreview();

            if (!IsVFDEenabled) return;

            try
            {
                using (var serialPort = new SerialPort(SelectedCOMPort, SelectedBaudRate))
                {
                    serialPort.Open();
                    byte[] clear = { 0x0C };
                    serialPort.Write(clear, 0, clear.Length);
                    var bytes = System.Text.Encoding.ASCII.GetBytes(VFDPreviewText);
                    serialPort.Write(bytes, 0, bytes.Length);
                    serialPort.Close();
                }

                MessageBox.Show("Preview enviado para o VFD!", "VFD",
                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao enviar preview: {ex.Message}", "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================
        // FUNÇÕES DE EMAIL - VERSÃO SIMPLIFICADA
        // ============================================

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void EmailField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string email = textBox.Text.Trim();
                bool isValid = IsValidEmail(email);

                if (textBox.Name == "TxtEmail")
                {
                    _isEmailValid = isValid;
                    TxtEmailError.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
                }
                else if (textBox.Name == "TxtRecipientEmail")
                {
                    _isRecipientEmailValid = isValid;
                    TxtRecipientEmailError.Visibility = isValid ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        }

        private async void TestEmail_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateEmailConfig())
                {
                    MessageBox.Show("Por favor, preencha todos os campos obrigatórios corretamente.",
                        "Configuração Incompleta", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TestResultPanel.Visibility = Visibility.Visible;
                TxtTestResult.Text = "🔧 Preparando envio...";
                TxtTestResult.Foreground = System.Windows.Media.Brushes.Orange;
                TxtTestDetails.Text = "Verificando configurações...";

                string emailPassword = PwdEmailPassword.Password;
                if (string.IsNullOrEmpty(emailPassword))
                {
                    TxtTestResult.Text = "❌ Senha não fornecida";
                    TxtTestResult.Foreground = System.Windows.Media.Brushes.Red;
                    TxtTestDetails.Text = "Digite a senha do email para testar.";
                    return;
                }

                TxtTestResult.Text = "📧 Conectando ao servidor...";
                TxtTestDetails.Text = $"Servidor: {Config.SMTPServer}:{Config.SMTPPort}";

                bool success = await Task.Run(() => SendTestEmailImproved(emailPassword));

                if (success)
                {
                    TxtTestResult.Text = "✅ Email enviado com sucesso!";
                    TxtTestResult.Foreground = System.Windows.Media.Brushes.Green;
                    TxtTestDetails.Text = $"Email de teste enviado para:\n{Config.RecipientEmail}\n\nVerifique sua caixa de entrada.";
                }
                else
                {
                    TxtTestResult.Text = "❌ Falha no envio";
                    TxtTestResult.Foreground = System.Windows.Media.Brushes.Red;
                    TxtTestDetails.Text = "Verifique as configurações e tente novamente.";
                }
            }
            catch (Exception ex)
            {
                TxtTestResult.Text = "💥 Erro no teste";
                TxtTestResult.Foreground = System.Windows.Media.Brushes.Red;
                TxtTestDetails.Text = GetFriendlyErrorMessage(ex);
            }
        }

        private bool SendTestEmailImproved(string emailPassword)
        {
            try
            {
                Console.WriteLine($"=== TENTANDO ENVIAR EMAIL ===");
                Console.WriteLine($"From: {Config.EmailAddress}");
                Console.WriteLine($"To: {Config.RecipientEmail}");
                Console.WriteLine($"Server: {Config.SMTPServer}:{Config.SMTPPort}");
                Console.WriteLine($"SSL: {Config.UseSSL}");
                Console.WriteLine($"Password Length: {emailPassword.Length}");

                string server = string.IsNullOrEmpty(Config.SMTPServer) ? "smtp.gmail.com" : Config.SMTPServer;
                int port = Config.SMTPPort <= 0 ? 587 : Config.SMTPPort;
                bool ssl = Config.UseSSL;

                using (SmtpClient client = new(server, port))
                {
                    client.EnableSsl = ssl;
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;
                    client.Timeout = 30000;
                    client.Credentials = new NetworkCredential(Config.EmailAddress, emailPassword);
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

                    ServicePointManager.ServerCertificateValidationCallback =
                        (s, certificate, chain, sslPolicyErrors) =>
                        {
                            return true;
                        };

                    MailMessage mail = new MailMessage
                    {
                        From = new MailAddress(Config.EmailAddress, Config.RestaurantName ?? "AyGestRest")
                    };
                    mail.To.Add(Config.RecipientEmail);
                    mail.Subject = $"Teste de Email - {Config.RestaurantName ?? "AyGestRest"}";
                    mail.Body = $@"
Este é um email de teste do sistema AyGestRest.

📋 Detalhes do Teste:
• Data/Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}
• Restaurante: {Config.RestaurantName ?? "Não configurado"}
• Servidor SMTP: {server}:{port}
• SSL: {(ssl ? "Ativado" : "Desativado")}

✅ Se recebeu esta mensagem, a configuração de email está funcionando corretamente.

---
Mensagem automática do sistema AyGestRest
";
                    mail.IsBodyHtml = false;

                    client.Send(mail);
                    Console.WriteLine("✅ EMAIL ENVIADO COM SUCESSO!");
                    return true;
                }
            }
            catch (SmtpException smtpEx)
            {
                Console.WriteLine($"❌ ERRO SMTP: {smtpEx.StatusCode} - {smtpEx.Message}");
                throw new Exception($"Erro SMTP: {smtpEx.Message}\nStatus: {smtpEx.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERRO GERAL: {ex.Message}");
                throw;
            }
        }

        private string GetFriendlyErrorMessage(Exception ex)
        {
            string errorMessage = ex.Message;

            if (errorMessage.Contains("Authentication Required") || errorMessage.Contains("5.7.0") || errorMessage.Contains("5.7.8"))
            {
                return $"🔒 ERRO DE AUTENTICAÇÃO\n\n" +
                       $"O Gmail requer autenticação especial.\n\n" +
                       $"📌 SOLUÇÃO PARA GMAIL:\n" +
                       $"1. Acesse: https://myaccount.google.com/apppasswords\n" +
                       $"2. Crie uma 'Senha de App' para 'AyGestRest'\n" +
                       $"3. Copie os 16 caracteres (ex: xxxxxxxxxxxxxxxx)\n" +
                       $"4. Cole no campo 'Senha do Email'\n\n" +
                       $"⚠️ NÃO use sua senha normal do Gmail!";
            }
            else if (errorMessage.Contains("The operation has timed out"))
            {
                return $"⏱️ TIMEOUT DE CONEXÃO\n\n" +
                       $"O servidor não respondeu a tempo.\n" +
                       $"Verifique sua conexão com a internet.";
            }
            else if (errorMessage.Contains("Unable to connect"))
            {
                return $"🔌 SEM CONEXÃO\n\n" +
                       $"Não foi possível conectar ao servidor.\n" +
                       $"Verifique:\n" +
                       $"1. Servidor SMTP correto\n" +
                       $"2. Porta correta\n" +
                       $"3. Conexão com internet\n" +
                       $"4. Firewall/antivírus";
            }
            else
            {
                return $"Erro técnico:\n{errorMessage}";
            }
        }

        private void ValidateEmailConfig_Click(object sender, RoutedEventArgs e)
        {
            bool isValid = ValidateEmailConfig();

            if (isValid)
            {
                MessageBox.Show("✅ Configuração de email válida!\n\nTodos os campos obrigatórios estão preenchidos corretamente.\n\nAgora pode testar o envio clicando em 'Testar Email'.",
                    "Validação Bem-sucedida", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("⚠️ Configuração de email incompleta ou inválida.\n\nVerifique os campos destacados em vermelho.\n\nPara Gmail, lembre-se de usar a SENHA DE APP (16 caracteres).",
                    "Validação Falhou", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool ValidateEmailConfig()
        {
            bool allValid = true;

            _isEmailValid = IsValidEmail(Config.EmailAddress);
            TxtEmailError.Visibility = _isEmailValid ? Visibility.Collapsed : Visibility.Visible;
            if (!_isEmailValid) allValid = false;

            _isRecipientEmailValid = IsValidEmail(Config.RecipientEmail);
            TxtRecipientEmailError.Visibility = _isRecipientEmailValid ? Visibility.Collapsed : Visibility.Visible;
            if (!_isRecipientEmailValid) allValid = false;

            if (string.IsNullOrWhiteSpace(Config.SMTPServer))
            {
                allValid = false;
            }

            if (Config.SMTPPort <= 0)
            {
                allValid = false;
            }

            if (!string.IsNullOrEmpty(Config.EmailAddress))
            {
                if (string.IsNullOrWhiteSpace(PwdEmailPassword.Password))
                {
                    allValid = false;
                }
            }

            return allValid;
        }

        // ============================================
        // FUNÇÃO PARA ENVIAR EMAIL DE OUTRAS TELAS
        // ============================================

        public static bool SendEmail(string subject, string body, bool isHtml = true, string attachmentPath = null)
        {
            try
            {
                using (var context = new AyGestRestContext())
                {
                    var config = context.RestaurantConfigs.FirstOrDefault();
                    if (config == null || string.IsNullOrEmpty(config.EmailAddress))
                        return false;

                    if (!config.SendReports)
                        return false;

                    string emailPassword = config.EmailPassword;
                    if (string.IsNullOrEmpty(emailPassword))
                    {
                        Console.WriteLine("❌ SENHA DO EMAIL NÃO ENCONTRADA OU VAZIA");
                        return false;
                    }

                    using (SmtpClient client = new SmtpClient())
                    {
                        client.Host = config.SMTPServer ?? "smtp.gmail.com";
                        client.Port = config.SMTPPort > 0 ? config.SMTPPort : 587;
                        client.EnableSsl = config.UseSSL;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.UseDefaultCredentials = false;
                        client.Credentials = new NetworkCredential(config.EmailAddress, emailPassword);
                        client.Timeout = 30000;

                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                        ServicePointManager.ServerCertificateValidationCallback =
                            (sender, certificate, chain, sslPolicyErrors) => true;

                        MailMessage mail = new MailMessage();
                        mail.From = new MailAddress(config.EmailAddress, config.RestaurantName ?? "AyGestRest");
                        mail.To.Add(config.RecipientEmail ?? config.EmailAddress);
                        mail.Subject = subject;
                        mail.Body = body;
                        mail.IsBodyHtml = isHtml;

                        if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                        {
                            mail.Attachments.Add(new Attachment(attachmentPath));
                        }

                        client.Send(mail);
                        Console.WriteLine("✅ EMAIL ENVIADO COM SUCESSO DE OUTRA TELA");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERRO AO ENVIAR EMAIL DE OUTRA TELA: {ex.Message}");
                return false;
            }
        }

        // ============================================
        // SALVAR CONFIGURAÇÕES - VERSÃO SIMPLIFICADA
        // ============================================

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Config == null)
                {
                    MessageBox.Show("Configuração não carregada.", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(Config.RestaurantName))
                {
                    MessageBox.Show("O nome do restaurante é obrigatório.", "Validação",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validar número de cópias
                if (!ValidateCopiesCount())
                {
                    MessageBox.Show("Por favor, corrija o número de cópias (1-10) antes de salvar.",
                                    "Validação",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                    return;
                }

                // Salvar modo de impressão
                Config.PrintMode = SelectedPrintMode;

                // 🔥 SIMPLIFICADO: Salvar senha diretamente
                Config.EmailPassword = PwdEmailPassword.Password;

                // PERMISSÕES
                Config.FuncionarioPodeFechoDiario = ChkFechoDia?.IsChecked == true;
                Config.FuncionarioPodeReservas = ChkReservas?.IsChecked == true;
                Config.FuncionarioPodeHistoricoVendas = ChkHistorico?.IsChecked == true;

                // VFD
                if (_vfdConfig != null)
                {
                    _vfdConfig.IsEnabled = IsVFDEenabled;
                    _vfdConfig.COMPort = SelectedCOMPort;
                    _vfdConfig.BaudRate = SelectedBaudRate;
                    _vfdConfig.DisplayLines = SelectedDisplayLines;
                    _vfdConfig.DisplayColumns = SelectedDisplayColumns;
                    _vfdConfig.ShowRealTimeTotal = ShowRealTimeTotal;
                    _context.VFDConfigs.Update(_vfdConfig);
                }

                _context.RestaurantConfigs.Update(Config);
                await _context.SaveChangesAsync();

                if (AppSession.RestaurantConfig != null)
                {
                    AppSession.RestaurantConfig.EmailPassword = Config.EmailPassword;
                    AppSession.RestaurantConfig.CopiesCount = Config.CopiesCount;
                    AppSession.RestaurantConfig.PrintMode = Config.PrintMode;
                }

                ConfigSaved?.Invoke();

                MessageBox.Show($"✅ Configurações salvas com sucesso!\n\n" +
                               $"Impressão configurada para {Config.CopiesCount} cópias por talão.\n" +
                               $"Modo de impressão: {(SelectedPrintMode == "Sequential" ? "Sequencial" : "Intercalado")}.",
                "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Erro ao salvar configurações: {ex.Message}\n\nDetalhes: {ex.InnerException?.Message}",
                "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateCopiesCount()
        {
            if (TxtCopiesCount == null || string.IsNullOrEmpty(TxtCopiesCount.Text))
            {
                Config.CopiesCount = 1;
                return true;
            }

            if (!int.TryParse(TxtCopiesCount.Text, out int copies))
            {
                MessageBox.Show("Número de cópias inválido. Usando valor padrão (1).",
                                "Valor Inválido",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                Config.CopiesCount = 1;
                TxtCopiesCount.Text = "1";
                return false;
            }

            if (copies < 1 || copies > 10)
            {
                MessageBox.Show("Número de cópias deve estar entre 1 e 10. Usando valor padrão (1).",
                                "Valor Inválido",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                Config.CopiesCount = 1;
                TxtCopiesCount.Text = "1";
                return false;
            }

            Config.CopiesCount = copies;
            return true;
        }

        private void TxtCopiesCount_LostFocus(object sender, RoutedEventArgs e)
        {
            ValidateCopiesCount();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            LoadVFDConfig();
            UpdateLogoPreview();
            LoadRadioButtonStates();
            SecondScreenEnabled = Config.SecondScreenEnabled;
            TestResultPanel.Visibility = Visibility.Collapsed;
            LoadPrintSettings();

            if (ChkFechoDia != null) ChkFechoDia.IsChecked = Config.FuncionarioPodeFechoDiario;
            if (ChkReservas != null) ChkReservas.IsChecked = Config.FuncionarioPodeReservas;
            if (ChkHistorico != null) ChkHistorico.IsChecked = Config.FuncionarioPodeHistoricoVendas;

            MessageBox.Show("Alterações canceladas.",
            "Cancelar", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Tem certeza que deseja restaurar todas as configurações para os valores padrão?\n\n⚠️ ATENÇÃO: A senha do email também será apagada!\n\nEsta ação não pode ser desfeita.",
            "Restaurar Padrões",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Config = new RestaurantConfig
                {
                    CopiesCount = 1,
                    PrintMode = "Sequential"
                };
                IsVFDEenabled = false;
                SelectedCOMPort = "COM1";
                SelectedBaudRate = 9600;
                SelectedDisplayLines = 2;
                SelectedDisplayColumns = 20;
                ShowRealTimeTotal = true;
                Config.SecondScreenEnabled = false;
                SecondScreenEnabled = false;
                PwdEmailPassword.Password = "";
                SelectedPrintMode = "Sequential";
                TestResultPanel.Visibility = Visibility.Collapsed;
                LoadRadioButtonStates();
                UpdatePrintModeRadioButtons();
                UpdateVFDPreview();
                MessageBox.Show("Configurações restauradas para os valores padrão.",
                "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                string password = passwordBox.Password;

                // Validação básica da senha em tempo real
                if (string.IsNullOrEmpty(password))
                {
                    // Senha vazia - mostrar aviso se email estiver configurado
                    if (!string.IsNullOrEmpty(Config?.EmailAddress))
                    {
                        TxtPasswordError.Visibility = Visibility.Visible;
                        TxtPasswordError.Text = "⚠️ Senha necessária para o email configurado";
                    }
                    else
                    {
                        TxtPasswordError.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    // Senha preenchida - esconder erro
                    TxtPasswordError.Visibility = Visibility.Collapsed;

                    // Validação para Gmail (senhas de app têm 16 caracteres)
                    if (!string.IsNullOrEmpty(Config?.EmailAddress) &&
                        Config.EmailAddress.ToLower().Contains("gmail.com"))
                    {
                        if (password.Length == 16 && password.All(c => char.IsLetterOrDigit(c)))
                        {
                            // Senha de app do Gmail válida
                            TxtPasswordHelp.Visibility = Visibility.Visible;
                            TxtPasswordHelp.Text = "✅ Senha de App do Gmail válida";
                            TxtPasswordHelp.Foreground = System.Windows.Media.Brushes.Green;
                        }
                        else if (password.Length < 16)
                        {
                            // Possível senha de app muito curta
                            TxtPasswordHelp.Visibility = Visibility.Visible;
                            TxtPasswordHelp.Text = "⚠️ Para Gmail use SENHA DE APP (16 caracteres)";
                            TxtPasswordHelp.Foreground = System.Windows.Media.Brushes.Orange;
                        }
                    }
                }
            }
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            if (!char.IsDigit(e.Text, 0))
            {
                e.Handled = true;
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class BackupFileInfo
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public string Date { get; set; }
        public string Size { get; set; }
        public string Type { get; set; }
    }
}