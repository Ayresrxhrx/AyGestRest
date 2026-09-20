using AyGestRest.Data;
using AyGestRest.Models;
using AyGestRest.Views;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AyGestRest
{
    public partial class App : Application
    {
        private bool _isShuttingDown = false;

        // ⚠️ TRUE só em DEV, para recriar DB do zero
        private const bool FORCE_RECREATE_DB = false;

        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                base.OnStartup(e);

                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                Debug.WriteLine("🚀 APP INICIOU");

                ConfigureGlobalExceptionHandlers();

                await CheckForUpdatesAsync();
                ContinueNormalFlow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO CRÍTICO: {ex.Message}");
                ShowFatalErrorDialog(ex);
                Shutdown(1);
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                if (ShouldSkipThisVersion()) return;
                if (!ShouldCheckForUpdatesToday()) return;

                var (hasUpdate, latestVersion, downloadUrl, releaseNotes) =
                    await Updater.CheckForUpdatesAsync();

                if (hasUpdate)
                {
                    var updateWindow = new UpdateWindow(downloadUrl, latestVersion, releaseNotes);
                    bool? result = updateWindow.ShowDialog();

                    if (result == true)
                    {
                        MarkVersionAsUpdated(latestVersion);
                        await Task.Delay(1000);
                        Shutdown();
                        return;
                    }
                    else
                    {
                        MarkUpdateCheckForToday();
                    }
                }
                else
                {
                    MarkUpdateCheckForToday();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Error in update check: {ex.Message}");
            }
        }

        private void ContinueNormalFlow()
        {
            try
            {
                string appFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                Directory.CreateDirectory(appFolder);

                // ANTES DE QUALQUER COISA: Backup do banco existente
                BackupExistingDatabase();

                using (var db = new AyGestRestContext())
                {
                    bool recreate = FORCE_RECREATE_DB || ShouldRecreateDatabase();

                    if (recreate)
                    {
                        Debug.WriteLine("🧨 RECRIANDO DB (DEV MODE)");
                        db.Database.EnsureDeleted();
                        Debug.WriteLine("✅ DB recriado do zero");
                    }

                    Debug.WriteLine("🔍 Verificando estrutura do banco de dados...");

                    // ⚡ CHAMADA DO NOVO MÉTODO - cria/atualiza DB automaticamente
                    db.InitializeDatabase();

                    Debug.WriteLine("✅ Estrutura do banco verificada e atualizada");

                    // 🆕 VERIFICAÇÃO ESPECÍFICA PARA ASSOCIAÇÃO INGREDIENTE-PRODUTO
                    CheckAndFixProductIngredientAssociations(db);

                    // Garantir que o admin existe
                    db.EnsureAdminUser();

                    // ⚡ Checar setup inicial APÓS DB estar pronto
                    // 🆕 CORREÇÃO: Só mostrar wizard se não existir flag de configuração concluída
                    if (!IsInitialSetupCompleted())
                    {
                        Debug.WriteLine("🆕 Primeira execução - Mostrando wizard de configuração");
                        var wizard = new InitialSetupWizard();
                        if (wizard.ShowDialog() != true)
                        {
                            Shutdown();
                            return;
                        }

                        // Marcar configuração como concluída
                        MarkInitialSetupAsCompleted();
                    }
                    else
                    {
                        Debug.WriteLine("✅ Configuração inicial já concluída");
                    }
                }

                // Abrir login
                var login = new LoginWindow();
                if (login.ShowDialog() == true)
                {
                    var main = new MainWindow();
                    MainWindow = main;

                    main.Closed += (_, __) =>
                    {
                        if (!_isShuttingDown)
                        {
                            _isShuttingDown = true;
                            Shutdown();
                        }
                    };

                    main.Show();
                }
                else
                {
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ERRO NO FLUXO: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                ShowFatalErrorDialog(ex);
                Shutdown(1);
            }
        }

        // 🆕 MÉTODO: Verificar se a configuração inicial foi concluída
        private bool IsInitialSetupCompleted()
        {
            try
            {
                string flagPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "setup_completed.flag");

                return File.Exists(flagPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao verificar flag de setup: {ex.Message}");
                return false;
            }
        }

        // 🆕 MÉTODO: Marcar configuração inicial como concluída
        private void MarkInitialSetupAsCompleted()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                Directory.CreateDirectory(folder);

                string flagPath = Path.Combine(folder, "setup_completed.flag");

                // Criar arquivo vazio para marcar como concluído
                File.WriteAllText(flagPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                Debug.WriteLine($"✅ Configuração inicial marcada como concluída: {flagPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao marcar setup como concluído: {ex.Message}");
            }
        }

        // 🆕 MÉTODO: Remover flag de setup (para testes)
        private void ResetInitialSetupFlag()
        {
            try
            {
                string flagPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "setup_completed.flag");

                if (File.Exists(flagPath))
                {
                    File.Delete(flagPath);
                    Debug.WriteLine("🗑️ Flag de setup removida (para testes)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao remover flag de setup: {ex.Message}");
            }
        }

        // 🆕 REMOVER: O método antigo CheckIfInitialSetupCompleted que verifica no banco
        // O novo método usa apenas o arquivo .flag

        private void BackupExistingDatabase()
        {
            try
            {
                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                if (!File.Exists(dbPath))
                {
                    Debug.WriteLine("ℹ️ Nenhum banco existente para backup");
                    return;
                }

                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "Backups",
                    "PreMigration");

                Directory.CreateDirectory(backupDir);

                string backupPath = Path.Combine(
                    backupDir,
                    $"AyGestRest_PreMigration_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                File.Copy(dbPath, backupPath, true);
                Debug.WriteLine($"✅ Backup pré-migração criado: {backupPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao criar backup pré-migração: {ex.Message}");
            }
        }

        private void CheckAndFixProductIngredientAssociations(AyGestRestContext db)
        {
            try
            {
                Debug.WriteLine("🔍 Verificando associações Produto-Ingrediente...");

                // 1. Verificar se a tabela ProductIngredients existe
                var tableExists = db.Database.ExecuteSqlRaw(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='ProductIngredients'");

                if (tableExists > 0)
                {
                    // 2. Verificar se tem a estrutura correta
                    var columnsResult = db.Database.ExecuteSqlRaw(
                        "PRAGMA table_info(ProductIngredients)");

                    // 3. Verificar se há associações "órfãs" (sem produto ou ingrediente)
                    Debug.WriteLine("🔍 Verificando associações órfãs...");

                    var orphanedAssociations = db.ProductIngredients
                        .Where(pi => !db.Products.Any(p => p.Id == pi.ProductId) ||
                                    !db.Ingredients.Any(i => i.Id == pi.IngredientId))
                        .ToList();

                    if (orphanedAssociations.Any())
                    {
                        Debug.WriteLine($"⚠️ Removendo {orphanedAssociations.Count} associações órfãs");
                        db.ProductIngredients.RemoveRange(orphanedAssociations);
                        db.SaveChanges();
                    }

                    // 4. Verificar associações duplicadas
                    var duplicates = db.ProductIngredients
                        .GroupBy(pi => new { pi.ProductId, pi.IngredientId })
                        .Where(g => g.Count() > 1)
                        .SelectMany(g => g.Skip(1))
                        .ToList();

                    if (duplicates.Any())
                    {
                        Debug.WriteLine($"⚠️ Removendo {duplicates.Count} associações duplicadas");
                        db.ProductIngredients.RemoveRange(duplicates);
                        db.SaveChanges();
                    }

                    // 5. Contar associações válidas
                    var validAssociations = db.ProductIngredients
                        .Where(pi => db.Products.Any(p => p.Id == pi.ProductId) &&
                                   db.Ingredients.Any(i => i.Id == pi.IngredientId))
                        .Count();

                    Debug.WriteLine($"✅ {validAssociations} associações Produto-Ingrediente válidas");

                    // 6. Verificar produtos compostos sem receita
                    var compositeProducts = db.Products
                        .Where(p => p.IsComposite && !db.ProductIngredients.Any(pi => pi.ProductId == p.Id))
                        .ToList();

                    if (compositeProducts.Any())
                    {
                        Debug.WriteLine($"⚠️ {compositeProducts.Count} produtos compostos sem receita");
                    }
                }
                else
                {
                    Debug.WriteLine("ℹ️ Tabela ProductIngredients não existe, será criada");
                }

                Debug.WriteLine("✅ Verificação de associações concluída");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao verificar associações: {ex.Message}");
            }
        }

        private bool ShouldRecreateDatabase()
        {
            string flag = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest",
                "recreate_db.flag");

            if (File.Exists(flag))
            {
                try
                {
                    File.Delete(flag);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao deletar flag: {ex.Message}");
                }
            }

            return false;
        }

        private void ConfigureGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                LogException(e.ExceptionObject as Exception);
                ShowFatalErrorDialog(e.ExceptionObject as Exception);
                if (!Debugger.IsAttached)
                {
                    Shutdown(1);
                }
            };

            DispatcherUnhandledException += (_, e) =>
            {
                LogException(e.Exception);
                ShowFatalErrorDialog(e.Exception);
                e.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogException(e.Exception);
                e.SetObserved();
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                string dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "AyGestRest.db");

                if (!File.Exists(dbPath))
                {
                    base.OnExit(e);
                    return;
                }

                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AyGestRest Backups");

                Directory.CreateDirectory(backupDir);

                // Mantém apenas os últimos 7 backups
                var oldBackups = Directory.GetFiles(backupDir, "AutoBackup_*.db")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(7);

                foreach (var oldBackup in oldBackups)
                {
                    try { File.Delete(oldBackup); }
                    catch { }
                }

                string backup = Path.Combine(
                    backupDir,
                    $"AutoBackup_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                File.Copy(dbPath, backup);
                Debug.WriteLine($"✅ Backup criado: {backup}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Erro ao criar backup: {ex.Message}");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        private void ShowFatalErrorDialog(Exception ex)
        {
            try
            {
                string errorMessage = ex?.Message ?? "Erro desconhecido";
                string fullMessage = $"Ocorreu um erro crítico:\n\n{errorMessage}\n\n" +
                                   "A aplicação será encerrada.\n" +
                                   "Consulte o arquivo de log para mais detalhes.";

                MessageBox.Show(
                    fullMessage,
                    "Erro Fatal - AyGestRest",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Fallback se a MessageBox falhar
                try
                {
                    Debug.WriteLine($"FATAL ERROR: {ex?.Message}");
                }
                catch { }
            }
        }

        private void LogException(Exception ex)
        {
            try
            {
                string logFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "Logs");

                Directory.CreateDirectory(logFolder);

                string logFile = Path.Combine(
                    logFolder,
                    $"error_{DateTime.Now:yyyyMMdd}.txt");

                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR\n" +
                                $"Message: {ex?.Message}\n" +
                                $"Type: {ex?.GetType().FullName}\n" +
                                $"Stack Trace:\n{ex?.StackTrace}\n" +
                                $"Inner Exception: {ex?.InnerException?.Message}\n" +
                                "----------------------------------------\n\n";

                File.AppendAllText(logFile, logEntry);
            }
            catch
            {
                // Se falhar ao escrever no log, pelo menos escreve no Debug
                Debug.WriteLine($"Failed to log exception: {ex?.Message}");
            }
        }

        private bool ShouldSkipThisVersion()
        {
            try
            {
                string file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "skip_update.txt");

                if (File.Exists(file))
                {
                    string skippedVersion = File.ReadAllText(file);
                    var currentVersion = GetCurrentVersion();
                    return skippedVersion == currentVersion.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar versão ignorada: {ex.Message}");
            }
            return false;
        }

        private bool ShouldCheckForUpdatesToday()
        {
            try
            {
                string file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "last_update_check.txt");

                if (File.Exists(file))
                {
                    string lastCheckDate = File.ReadAllText(file);
                    if (DateTime.TryParse(lastCheckDate, out DateTime lastCheck))
                    {
                        return (DateTime.Now - lastCheck).TotalDays >= 1;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar data do último check: {ex.Message}");
                return true;
            }
        }

        private void MarkUpdateCheckForToday()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                Directory.CreateDirectory(folder);

                File.WriteAllText(
                    Path.Combine(folder, "last_update_check.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao marcar check de atualização: {ex.Message}");
            }
        }

        private void MarkVersionAsUpdated(string version)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest");

                Directory.CreateDirectory(folder);

                File.WriteAllText(
                    Path.Combine(folder, "last_checked_version.txt"),
                    version);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao marcar versão atualizada: {ex.Message}");
            }
        }

        private string GetCurrentVersion()
        {
            try
            {
                var version = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetName()
                    .Version;

                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }
}