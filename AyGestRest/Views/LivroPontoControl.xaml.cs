using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace AyGestRest.Views
{
    public partial class LivroPontoControl : UserControl
    {
        private AyGestRestContext _context;
        private RestaurantConfig _config;
        private List<FuncionarioTurno> _registrosHoje;
        private List<FuncionarioTurno> _registrosRelatorio;
        private int _funcionarioSelecionadoId = 0;
        private string _currencySymbol = "MTn";

        // Classe auxiliar para exibição no DataGrid
        public class RegistroPontoView
        {
            public int Id { get; set; }
            public int UserId { get; set; }
            public string NomeFuncionario { get; set; }
            public DateTime Data { get; set; }
            public TimeSpan? HoraEntrada { get; set; }
            public TimeSpan? HoraSaida { get; set; }
            public TimeSpan? HorasTrabalhadas { get; set; }
            public TimeSpan? HorarioPrevistoEntrada { get; set; }
            public TimeSpan? HorarioPrevistoSaida { get; set; }
            public TimeSpan? Atraso { get; set; }
            public TimeSpan? HorasExtras { get; set; }
            public bool HoraExtraAutorizada { get; set; }
            public string Turno { get; set; }
            public bool Ausente { get; set; }
            public string Status
            {
                get
                {
                    if (Ausente) return "Ausente";
                    if (HoraEntrada == null) return "Aguardando Entrada";
                    if (HoraSaida == null) return "Em Trabalho";
                    return "Finalizado";
                }
            }
        }

        // Classe para resumo do relatório
        private class RelatorioResumo
        {
            public TimeSpan TotalHorasTrabalhadas { get; set; }
            public TimeSpan TotalAtrasos { get; set; }
            public TimeSpan TotalHorasEfetivas { get; set; } // Horas trabalhadas - Atrasos
            public TimeSpan TotalHorasExtras { get; set; }
            public int TotalRegistros { get; set; }
            public int TotalFuncionarios { get; set; }
            public int TotalAusencias { get; set; }
            public double PercentualPresenca { get; set; }
        }

        // Classe para dados do gráfico
        private class ChartData
        {
            public string Label { get; set; }
            public double Value { get; set; }
            public SolidColorBrush Color { get; set; }
        }

        public LivroPontoControl()
        {
            InitializeComponent();

            try
            {
                // Inicializar contexto
                _context = new AyGestRestContext();
                _config = _context.RestaurantConfigs.FirstOrDefault() ?? new RestaurantConfig();
                _currencySymbol = string.IsNullOrWhiteSpace(_config.CurrencySymbol) ? "MTn" : _config.CurrencySymbol;

                // Verificar e criar tabelas necessárias
                VerificarEAtualizarBancoDados();

                // Carregar dados iniciais
                CarregarFuncionarios();
                CarregarRegistrosHoje();
                AtualizarEstatisticas();

                // Inicializar DatePickers
                dpDataInicio.SelectedDate = DateTime.Today.AddDays(-30);
                dpDataFim.SelectedDate = DateTime.Today;

                // Carregar funcionários para o filtro de relatório
                CarregarFuncionariosFiltro();

                // Carregar gráficos iniciais
                CarregarGraficosIniciais();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar: {ex.Message}\n\nDetalhes: {ex.StackTrace}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Garantir que o contexto está configurado
            if (_context == null)
            {
                _context = new AyGestRestContext();
            }

            // Recarregar funcionários para o filtro de relatório
            CarregarFuncionariosFiltro();
        }

        #region Verificação e Atualização do Banco de Dados

        private void VerificarEAtualizarBancoDados()
        {
            try
            {
                Debug.WriteLine("🔍 Verificando estrutura do banco de dados para Livro de Ponto...");

                // Verificar se as tabelas existem
                var tablesExistentes = ObterTabelasExistentes();

                // Verificar e criar tabela FuncionarioTurnos se necessário
                if (!tablesExistentes.Contains("FuncionarioTurnos"))
                {
                    Debug.WriteLine("⚠️ Tabela FuncionarioTurnos não encontrada. Criando...");
                    CriarTabelaFuncionarioTurnos();
                }
                else
                {
                    Debug.WriteLine("✅ Tabela FuncionarioTurnos encontrada");
                    VerificarEAdicionarColunasFuncionarioTurno();
                    CorrigirTodosCamposNulos();
                }

                // Verificar e criar tabela HorarioUsuarios se necessário
                if (!tablesExistentes.Contains("HorarioUsuarios"))
                {
                    Debug.WriteLine("⚠️ Tabela HorarioUsuarios não encontrada. Criando...");
                    CriarTabelaHorarioUsuarios();
                }
                else
                {
                    Debug.WriteLine("✅ Tabela HorarioUsuarios encontrada");
                    VerificarEAdicionarColunasHorarioUsuario();
                }

                CorrigirRegistrosComTurnoNulo();
                CorrigirRegistrosComObservacoesNulas();

                Debug.WriteLine("✅ Verificação do banco de dados concluída");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao verificar banco de dados: {ex.Message}");

                try
                {
                    CriarBancoPontoSeparado();
                }
                catch (Exception innerEx)
                {
                    Debug.WriteLine($"❌ Erro crítico: {innerEx.Message}");
                    MessageBox.Show("Não foi possível configurar o banco de dados para o livro de ponto. " +
                                   "Verifique as permissões e tente novamente.\n\nErro: " + innerEx.Message,
                                   "Erro de Banco de Dados",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private List<string> ObterTabelasExistentes()
        {
            var tables = new List<string>();
            try
            {
                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                    _context.Database.OpenConnection();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tables.Add(reader.GetString(0));
                        }
                    }
                    _context.Database.CloseConnection();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao obter tabelas: {ex.Message}");
            }
            return tables;
        }

        private List<string> ObterColunasTabela(string tableName)
        {
            var colunas = new List<string>();
            try
            {
                using (var command = _context.Database.GetDbConnection().CreateCommand())
                {
                    command.CommandText = $"PRAGMA table_info({tableName})";
                    _context.Database.OpenConnection();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            colunas.Add(reader.GetString(1));
                        }
                    }
                    _context.Database.CloseConnection();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao obter colunas: {ex.Message}");
            }
            return colunas;
        }

        private void CriarTabelaFuncionarioTurnos()
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS FuncionarioTurnos (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Data TEXT NOT NULL,
                    HoraEntrada TEXT,
                    HoraSaida TEXT,
                    HorasExtras TEXT,
                    HorasTrabalhadas TEXT,
                    Atraso TEXT,
                    HoraExtraAutorizada INTEGER DEFAULT 0,
                    HorarioPrevistoEntrada TEXT,
                    HorarioPrevistoSaida TEXT,
                    Turno TEXT DEFAULT 'Integral',
                    Observacoes TEXT DEFAULT '',
                    Ausente INTEGER DEFAULT 0,
                    TipoAusencia TEXT DEFAULT '',
                    JustificativaAusencia TEXT DEFAULT '',
                    JustificativaHorasExtras TEXT DEFAULT '',
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                )";

            _context.Database.ExecuteSqlRaw(sql);
            Debug.WriteLine("✅ Tabela FuncionarioTurnos criada com sucesso");
        }

        private void VerificarEAdicionarColunasFuncionarioTurno()
        {
            var colunasExistentes = ObterColunasTabela("FuncionarioTurnos");

            var colunasNecessarias = new Dictionary<string, string>
            {
                { "HorasTrabalhadas", "ALTER TABLE FuncionarioTurnos ADD COLUMN HorasTrabalhadas TEXT" },
                { "Atraso", "ALTER TABLE FuncionarioTurnos ADD COLUMN Atraso TEXT" },
                { "HoraExtraAutorizada", "ALTER TABLE FuncionarioTurnos ADD COLUMN HoraExtraAutorizada INTEGER DEFAULT 0" },
                { "HorarioPrevistoEntrada", "ALTER TABLE FuncionarioTurnos ADD COLUMN HorarioPrevistoEntrada TEXT" },
                { "HorarioPrevistoSaida", "ALTER TABLE FuncionarioTurnos ADD COLUMN HorarioPrevistoSaida TEXT" },
                { "Observacoes", "ALTER TABLE FuncionarioTurnos ADD COLUMN Observacoes TEXT DEFAULT ''" },
                { "TipoAusencia", "ALTER TABLE FuncionarioTurnos ADD COLUMN TipoAusencia TEXT DEFAULT ''" },
                { "JustificativaAusencia", "ALTER TABLE FuncionarioTurnos ADD COLUMN JustificativaAusencia TEXT DEFAULT ''" },
                { "JustificativaHorasExtras", "ALTER TABLE FuncionarioTurnos ADD COLUMN JustificativaHorasExtras TEXT DEFAULT ''" }
            };

            foreach (var coluna in colunasNecessarias)
            {
                if (!colunasExistentes.Contains(coluna.Key))
                {
                    try
                    {
                        _context.Database.ExecuteSqlRaw(coluna.Value);
                        Debug.WriteLine($"✅ Coluna {coluna.Key} adicionada à tabela FuncionarioTurnos");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Erro ao adicionar coluna {coluna.Key}: {ex.Message}");
                    }
                }
            }
        }

        private void CriarTabelaHorarioUsuarios()
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS HorarioUsuarios (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Segunda INTEGER DEFAULT 1,
                    Terca INTEGER DEFAULT 1,
                    Quarta INTEGER DEFAULT 1,
                    Quinta INTEGER DEFAULT 1,
                    Sexta INTEGER DEFAULT 1,
                    Sabado INTEGER DEFAULT 0,
                    Domingo INTEGER DEFAULT 0,
                    DiasTrabalhoSemana INTEGER DEFAULT 5,
                    HorasPorDia INTEGER DEFAULT 8,
                    SegundaEntrada TEXT,
                    SegundaSaida TEXT,
                    TercaEntrada TEXT,
                    TercaSaida TEXT,
                    QuartaEntrada TEXT,
                    QuartaSaida TEXT,
                    QuintaEntrada TEXT,
                    QuintaSaida TEXT,
                    SextaEntrada TEXT,
                    SextaSida TEXT,
                    SabadoEntrada TEXT,
                    SabadoSaida TEXT,
                    DomingoEntrada TEXT,
                    DomingoSaida TEXT,
                    HorarioPadraoEntrada TEXT DEFAULT '09:00',
                    HorarioPadraoSaida TEXT DEFAULT '18:00',
                    VigenciaInicio TEXT DEFAULT CURRENT_DATE,
                    VigenciaFim TEXT,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    Ativo INTEGER DEFAULT 1,
                    FOREIGN KEY (UserId) REFERENCES Users(Id)
                )";

            _context.Database.ExecuteSqlRaw(sql);
            Debug.WriteLine("✅ Tabela HorarioUsuarios criada com sucesso");
        }

        private void VerificarEAdicionarColunasHorarioUsuario()
        {
            var colunasExistentes = ObterColunasTabela("HorarioUsuarios");

            var colunasNecessarias = new Dictionary<string, string>
            {
                { "SegundaEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN SegundaEntrada TEXT" },
                { "SegundaSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN SegundaSaida TEXT" },
                { "TercaEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN TercaEntrada TEXT" },
                { "TercaSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN TercaSaida TEXT" },
                { "QuartaEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN QuartaEntrada TEXT" },
                { "QuartaSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN QuartaSaida TEXT" },
                { "QuintaEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN QuintaEntrada TEXT" },
                { "QuintaSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN QuintaSaida TEXT" },
                { "SextaEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN SextaEntrada TEXT" },
                { "SextaSida", "ALTER TABLE HorarioUsuarios ADD COLUMN SextaSida TEXT" },
                { "SabadoEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN SabadoEntrada TEXT" },
                { "SabadoSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN SabadoSaida TEXT" },
                { "DomingoEntrada", "ALTER TABLE HorarioUsuarios ADD COLUMN DomingoEntrada TEXT" },
                { "DomingoSaida", "ALTER TABLE HorarioUsuarios ADD COLUMN DomingoSaida TEXT" }
            };

            foreach (var coluna in colunasNecessarias)
            {
                if (!colunasExistentes.Contains(coluna.Key))
                {
                    try
                    {
                        _context.Database.ExecuteSqlRaw(coluna.Value);
                        Debug.WriteLine($"✅ Coluna {coluna.Key} adicionada à tabela HorarioUsuarios");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Erro ao adicionar coluna {coluna.Key}: {ex.Message}");
                    }
                }
            }
        }

        private void CriarBancoPontoSeparado()
        {
            try
            {
                string dbPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AyGestRest",
                    "Ponto.db");

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dbPath));

                var optionsBuilder = new DbContextOptionsBuilder<AyGestRestContext>();
                optionsBuilder.UseSqlite($"Data Source={dbPath}");

                _context = new AyGestRestContext(optionsBuilder.Options);

                CriarTabelaFuncionarioTurnos();
                CriarTabelaHorarioUsuarios();

                _context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS Users (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Username TEXT NOT NULL UNIQUE,
                        PasswordHash TEXT NOT NULL,
                        FullName TEXT,
                        Role INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER DEFAULT 1,
                        CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                    )");

                _context.Database.ExecuteSqlRaw(@"
                    INSERT OR IGNORE INTO Users (Username, PasswordHash, FullName, Role, IsActive)
                    VALUES ('Admin', '1304', 'Administrador', 0, 1)");

                Debug.WriteLine("✅ Banco de ponto separado criado com sucesso");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao criar banco separado: {ex.Message}");
                throw;
            }
        }

        private void CorrigirRegistrosComTurnoNulo()
        {
            try
            {
                Debug.WriteLine("🔧 Corrigindo registros com Turno NULL...");

                var registrosParaCorrigir = _context.FuncionarioTurnos
                    .Where(f => string.IsNullOrEmpty(f.Turno))
                    .ToList();

                foreach (var registro in registrosParaCorrigir)
                {
                    if (registro.HoraEntrada.HasValue)
                    {
                        registro.Turno = DeterminarTurno(registro.HoraEntrada.Value);
                    }
                    else
                    {
                        registro.Turno = "Integral";
                    }
                    registro.UpdatedAt = DateTime.Now;
                }

                if (registrosParaCorrigir.Any())
                {
                    _context.SaveChanges();
                    Debug.WriteLine($"✅ Corrigidos {registrosParaCorrigir.Count} registros com Turno NULL");
                }
                else
                {
                    Debug.WriteLine("✅ Nenhum registro com Turno NULL encontrado");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao corrigir registros: {ex.Message}");
            }
        }

        private void CorrigirRegistrosComObservacoesNulas()
        {
            try
            {
                Debug.WriteLine("🔧 Corrigindo registros com Observacoes NULL...");

                var registrosParaCorrigir = _context.FuncionarioTurnos
                    .Where(f => f.Observacoes == null)
                    .ToList();

                foreach (var registro in registrosParaCorrigir)
                {
                    registro.Observacoes = "";
                    registro.UpdatedAt = DateTime.Now;
                }

                if (registrosParaCorrigir.Any())
                {
                    _context.SaveChanges();
                    Debug.WriteLine($"✅ Corrigidos {registrosParaCorrigir.Count} registros com Observacoes NULL");
                }
                else
                {
                    Debug.WriteLine("✅ Nenhum registro com Observacoes NULL encontrado");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao corrigir registros: {ex.Message}");
            }
        }

        private void CorrigirTodosCamposNulos()
        {
            try
            {
                Debug.WriteLine("🔧 Corrigindo todos os campos NULL na tabela FuncionarioTurnos...");

                _context.Database.ExecuteSqlRaw(@"
                    UPDATE FuncionarioTurnos 
                    SET 
                        Turno = COALESCE(Turno, 'Integral'),
                        Observacoes = COALESCE(Observacoes, ''),
                        TipoAusencia = COALESCE(TipoAusencia, ''),
                        JustificativaAusencia = COALESCE(JustificativaAusencia, ''),
                        JustificativaHorasExtras = COALESCE(JustificativaHorasExtras, '')
                    WHERE 
                        Turno IS NULL OR 
                        Observacoes IS NULL OR 
                        TipoAusencia IS NULL OR 
                        JustificativaAusencia IS NULL OR 
                        JustificativaHorasExtras IS NULL");

                Debug.WriteLine("✅ Campos NULL corrigidos");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao corrigir campos NULL: {ex.Message}");
            }
        }

        #endregion

        #region Carregamento de Dados

        private void CarregarFuncionarios()
        {
            try
            {
                var funcionarios = _context.Users
                    .Where(u => u.IsActive)
                    .OrderBy(u => u.FullName ?? u.Username)
                    .ToList();

                cmbFuncionarios.ItemsSource = funcionarios;
                cmbFuncionariosHorario.ItemsSource = funcionarios;

                if (funcionarios.Any())
                {
                    cmbFuncionarios.SelectedIndex = 0;
                    cmbFuncionariosHorario.SelectedIndex = 0;
                }

                txtFuncionariosAtivos.Text = $"Funcionários Ativos: {funcionarios.Count}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar funcionários: {ex.Message}");
            }
        }

        private void CarregarFuncionariosFiltro()
        {
            try
            {
                var funcionarios = _context.Users
                    .Where(u => u.IsActive)
                    .OrderBy(u => u.FullName ?? u.Username)
                    .ToList();

                cmbFiltroFuncionario.Items.Clear();

                var todosItem = new ComboBoxItem { Content = "👤 Todos os Funcionários", Tag = 0 };
                cmbFiltroFuncionario.Items.Add(todosItem);

                foreach (var func in funcionarios)
                {
                    var item = new ComboBoxItem
                    {
                        Content = func.FullName ?? func.Username,
                        Tag = func.Id
                    };
                    cmbFiltroFuncionario.Items.Add(item);
                }

                cmbFiltroFuncionario.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar funcionários para filtro: {ex.Message}");
            }
        }

        private void CarregarRegistrosHoje()
        {
            try
            {
                var hoje = DateTime.Today;

                var registros = _context.FuncionarioTurnos
                    .Where(f => f.Data.Date == hoje)
                    .Include(f => f.User)
                    .ToList();

                var registrosView = registros.Select(r => new RegistroPontoView
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    NomeFuncionario = r.User?.FullName ?? r.User?.Username ?? "Desconhecido",
                    Data = r.Data,
                    HoraEntrada = r.HoraEntrada,
                    HoraSaida = r.HoraSaida,
                    HorasTrabalhadas = r.HorasTrabalhadas,
                    HorarioPrevistoEntrada = r.HorarioPrevistoEntrada,
                    HorarioPrevistoSaida = r.HorarioPrevistoSaida,
                    Atraso = r.Atraso,
                    HorasExtras = r.HorasExtras,
                    HoraExtraAutorizada = r.HoraExtraAutorizada,
                    Turno = r.Turno ?? "Integral",
                    Ausente = r.Ausente
                }).ToList();

                dgRegistrosHoje.ItemsSource = registrosView;
                _registrosHoje = registros;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar registros de hoje: {ex.Message}");
                dgRegistrosHoje.ItemsSource = new List<RegistroPontoView>();
            }
        }

        private void AtualizarEstatisticas()
        {
            try
            {
                var hoje = DateTime.Today;
                var todosFuncionarios = _context.Users.Where(u => u.IsActive).ToList();
                var registrosHoje = _context.FuncionarioTurnos
                    .Where(f => f.Data.Date == hoje)
                    .ToList();

                int presentes = registrosHoje.Count(r => !r.Ausente && r.HoraEntrada != null);
                int atrasados = registrosHoje.Count(r => r.Atraso > TimeSpan.Zero);
                int ausentes = registrosHoje.Count(r => r.Ausente);

                txtPresentesHoje.Text = $"Presentes Hoje: {presentes}";
                txtAtrasadosHoje.Text = $"Atrasados: {atrasados}";
                txtAusentesHoje.Text = $"Ausentes: {ausentes}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar estatísticas: {ex.Message}");
            }
        }

        private void CarregarHorarioFuncionario(int userId)
        {
            try
            {
                var horario = _context.HorarioUsuarios
                    .FirstOrDefault(h => h.UserId == userId && h.Ativo);

                if (horario != null)
                {
                    txtHorarioPadraoEntrada.Text = horario.HorarioPadraoEntrada.ToString(@"hh\:mm");
                    txtHorarioPadraoSaida.Text = horario.HorarioPadraoSaida.ToString(@"hh\:mm");
                    txtHorasPorDia.Text = horario.HorasPorDia.ToString();

                    chkSegunda.IsChecked = horario.Segunda;
                    chkTerca.IsChecked = horario.Terca;
                    chkQuarta.IsChecked = horario.Quarta;
                    chkQuinta.IsChecked = horario.Quinta;
                    chkSexta.IsChecked = horario.Sexta;
                    chkSabado.IsChecked = horario.Sabado;
                    chkDomingo.IsChecked = horario.Domingo;

                    if (horario.SegundaEntrada.HasValue)
                        txtSegundaEntrada.Text = horario.SegundaEntrada.Value.ToString(@"hh\:mm");
                    if (horario.SegundaSaida.HasValue)
                        txtSegundaSaida.Text = horario.SegundaSaida.Value.ToString(@"hh\:mm");

                    if (horario.TercaEntrada.HasValue)
                        txtTercaEntrada.Text = horario.TercaEntrada.Value.ToString(@"hh\:mm");
                    if (horario.TercaSaida.HasValue)
                        txtTercaSaida.Text = horario.TercaSaida.Value.ToString(@"hh\:mm");

                    if (horario.QuartaEntrada.HasValue)
                        txtQuartaEntrada.Text = horario.QuartaEntrada.Value.ToString(@"hh\:mm");
                    if (horario.QuartaSaida.HasValue)
                        txtQuartaSaida.Text = horario.QuartaSaida.Value.ToString(@"hh\:mm");

                    if (horario.QuintaEntrada.HasValue)
                        txtQuintaEntrada.Text = horario.QuintaEntrada.Value.ToString(@"hh\:mm");
                    if (horario.QuintaSaida.HasValue)
                        txtQuintaSaida.Text = horario.QuintaSaida.Value.ToString(@"hh\:mm");

                    if (horario.SextaEntrada.HasValue)
                        txtSextaEntrada.Text = horario.SextaEntrada.Value.ToString(@"hh\:mm");
                    if (horario.SextaSida.HasValue)
                        txtSextaSaida.Text = horario.SextaSida.Value.ToString(@"hh\:mm");

                    if (horario.SabadoEntrada.HasValue)
                        txtSabadoEntrada.Text = horario.SabadoEntrada.Value.ToString(@"hh\:mm");
                    if (horario.SabadoSaida.HasValue)
                        txtSabadoSaida.Text = horario.SabadoSaida.Value.ToString(@"hh\:mm");

                    if (horario.DomingoEntrada.HasValue)
                        txtDomingoEntrada.Text = horario.DomingoEntrada.Value.ToString(@"hh\:mm");
                    if (horario.DomingoSaida.HasValue)
                        txtDomingoSaida.Text = horario.DomingoSaida.Value.ToString(@"hh\:mm");
                }
                else
                {
                    txtHorarioPadraoEntrada.Text = "09:00";
                    txtHorarioPadraoSaida.Text = "18:00";
                    txtHorasPorDia.Text = "8";

                    chkSegunda.IsChecked = true;
                    chkTerca.IsChecked = true;
                    chkQuarta.IsChecked = true;
                    chkQuinta.IsChecked = true;
                    chkSexta.IsChecked = true;
                    chkSabado.IsChecked = false;
                    chkDomingo.IsChecked = false;

                    txtSegundaEntrada.Text = "09:00";
                    txtSegundaSaida.Text = "18:00";
                    txtTercaEntrada.Text = "09:00";
                    txtTercaSaida.Text = "18:00";
                    txtQuartaEntrada.Text = "09:00";
                    txtQuartaSaida.Text = "18:00";
                    txtQuintaEntrada.Text = "09:00";
                    txtQuintaSaida.Text = "18:00";
                    txtSextaEntrada.Text = "09:00";
                    txtSextaSaida.Text = "18:00";
                    txtSabadoEntrada.Text = "";
                    txtSabadoSaida.Text = "";
                    txtDomingoEntrada.Text = "";
                    txtDomingoSaida.Text = "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar horário: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Cálculo

        private string DeterminarTurno(TimeSpan hora)
        {
            if (hora >= TimeSpan.FromHours(5) && hora < TimeSpan.FromHours(12))
                return "Manhã";
            else if (hora >= TimeSpan.FromHours(12) && hora < TimeSpan.FromHours(18))
                return "Tarde";
            else if (hora >= TimeSpan.FromHours(18) || hora < TimeSpan.FromHours(5))
                return "Noite";
            else
                return "Integral";
        }

        private TimeSpan? ObterHorarioPrevisto(int userId, DateTime data)
        {
            try
            {
                var diaSemana = data.DayOfWeek;
                var horario = _context.HorarioUsuarios
                    .FirstOrDefault(h => h.UserId == userId && h.Ativo);

                if (horario == null)
                    return null;

                bool trabalhaHoje = diaSemana switch
                {
                    DayOfWeek.Monday => horario.Segunda,
                    DayOfWeek.Tuesday => horario.Terca,
                    DayOfWeek.Wednesday => horario.Quarta,
                    DayOfWeek.Thursday => horario.Quinta,
                    DayOfWeek.Friday => horario.Sexta,
                    DayOfWeek.Saturday => horario.Sabado,
                    DayOfWeek.Sunday => horario.Domingo,
                    _ => false
                };

                if (!trabalhaHoje)
                    return null;

                return diaSemana switch
                {
                    DayOfWeek.Monday => horario.SegundaEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Tuesday => horario.TercaEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Wednesday => horario.QuartaEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Thursday => horario.QuintaEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Friday => horario.SextaEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Saturday => horario.SabadoEntrada ?? horario.HorarioPadraoEntrada,
                    DayOfWeek.Sunday => horario.DomingoEntrada ?? horario.HorarioPadraoEntrada,
                    _ => horario.HorarioPadraoEntrada
                };
            }
            catch
            {
                return null;
            }
        }

        private TimeSpan? CalcularAtraso(int userId, DateTime data, TimeSpan horaReal)
        {
            var horaPrevista = ObterHorarioPrevisto(userId, data);
            if (!horaPrevista.HasValue)
                return null;

            return CalcularAtraso(horaPrevista.Value, horaReal);
        }

        private TimeSpan? CalcularAtraso(TimeSpan horaPrevista, TimeSpan horaReal)
        {
            var tolerancia = TimeSpan.FromMinutes(5);

            if (horaReal > horaPrevista + tolerancia)
                return horaReal - horaPrevista;

            return TimeSpan.Zero;
        }

        private TimeSpan? CalcularHorasExtras(int userId, DateTime data, TimeSpan entrada, TimeSpan saida)
        {
            try
            {
                var diaSemana = data.DayOfWeek;
                var horario = _context.HorarioUsuarios
                    .FirstOrDefault(h => h.UserId == userId && h.Ativo);

                if (horario == null)
                    return null;

                TimeSpan? saidaPrevista = diaSemana switch
                {
                    DayOfWeek.Monday => horario.SegundaSaida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Tuesday => horario.TercaSaida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Wednesday => horario.QuartaSaida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Thursday => horario.QuintaSaida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Friday => horario.SextaSida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Saturday => horario.SabadoSaida ?? horario.HorarioPadraoSaida,
                    DayOfWeek.Sunday => horario.DomingoSaida ?? horario.HorarioPadraoSaida,
                    _ => horario.HorarioPadraoSaida
                };

                if (!saidaPrevista.HasValue)
                    return null;

                if (saida > saidaPrevista.Value)
                {
                    var horasDia = saida - entrada;
                    var horasPrevistas = horario.HorasPorDia;

                    if (horasDia.TotalHours > horasPrevistas)
                    {
                        return horasDia - TimeSpan.FromHours(horasPrevistas);
                    }
                }

                return TimeSpan.Zero;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Relatório e Resumo

        private RelatorioResumo CalcularResumoRelatorio(List<FuncionarioTurno> registros)
        {
            var resumo = new RelatorioResumo();

            if (registros == null || !registros.Any())
                return resumo;

            resumo.TotalRegistros = registros.Count;
            resumo.TotalFuncionarios = registros.Select(r => r.UserId).Distinct().Count();
            resumo.TotalAusencias = registros.Count(r => r.Ausente);

            resumo.TotalHorasTrabalhadas = TimeSpan.FromTicks(registros
                .Where(r => r.HorasTrabalhadas.HasValue)
                .Sum(r => r.HorasTrabalhadas.Value.Ticks));

            resumo.TotalAtrasos = TimeSpan.FromTicks(registros
                .Where(r => r.Atraso.HasValue)
                .Sum(r => r.Atraso.Value.Ticks));

            resumo.TotalHorasEfetivas = resumo.TotalHorasTrabalhadas - resumo.TotalAtrasos;
            if (resumo.TotalHorasEfetivas < TimeSpan.Zero)
                resumo.TotalHorasEfetivas = TimeSpan.Zero;

            resumo.TotalHorasExtras = TimeSpan.FromTicks(registros
                .Where(r => r.HorasExtras.HasValue)
                .Sum(r => r.HorasExtras.Value.Ticks));

            int totalPresencas = resumo.TotalRegistros - resumo.TotalAusencias;
            resumo.PercentualPresenca = resumo.TotalRegistros > 0
                ? (double)totalPresencas / resumo.TotalRegistros * 100
                : 0;

            return resumo;
        }

        private void ExibirResumoRelatorio(RelatorioResumo resumo, DateTime dataInicio, DateTime dataFim, int? funcionarioId)
        {
            string filtroNome = funcionarioId.HasValue ?
                $"para '{_context.Users.Find(funcionarioId.Value)?.FullName ?? "Funcionário"}'" :
                "para todos os funcionários";

            string mensagem = $@"
📊 RESUMO DO RELATÓRIO
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📅 Período: {dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}
👤 {filtroNome}

📈 ESTATÍSTICAS GERAIS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Total de Funcionários: {resumo.TotalFuncionarios}
• Total de Registros: {resumo.TotalRegistros}
• Ausências: {resumo.TotalAusencias}
• Percentual de Presença: {resumo.PercentualPresenca:0.0}%

⏰ TOTAIS DE HORAS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Total Horas Trabalhadas: {resumo.TotalHorasTrabalhadas:hh\\:mm}
• Total Atrasos: {resumo.TotalAtrasos:hh\\:mm}
• ⭐ Total Horas Efetivas (Trabalhadas - Atrasos): {resumo.TotalHorasEfetivas:hh\\:mm}
• Total Horas Extras: {resumo.TotalHorasExtras:hh\\:mm}
";

            MessageBox.Show(mensagem, "Resumo do Relatório", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Event Handlers

        private void cmbFuncionarios_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFuncionarios.SelectedItem is User user)
            {
                _funcionarioSelecionadoId = user.Id;
            }
        }

        private void cmbFuncionariosHorario_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbFuncionariosHorario.SelectedItem is User user)
            {
                CarregarHorarioFuncionario(user.Id);
            }
        }

        private void chkDia_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == chkSegunda)
            {
                txtSegundaEntrada.IsEnabled = chkSegunda.IsChecked == true;
                txtSegundaSaida.IsEnabled = chkSegunda.IsChecked == true;
            }
            else if (checkBox == chkTerca)
            {
                txtTercaEntrada.IsEnabled = chkTerca.IsChecked == true;
                txtTercaSaida.IsEnabled = chkTerca.IsChecked == true;
            }
            else if (checkBox == chkQuarta)
            {
                txtQuartaEntrada.IsEnabled = chkQuarta.IsChecked == true;
                txtQuartaSaida.IsEnabled = chkQuarta.IsChecked == true;
            }
            else if (checkBox == chkQuinta)
            {
                txtQuintaEntrada.IsEnabled = chkQuinta.IsChecked == true;
                txtQuintaSaida.IsEnabled = chkQuinta.IsChecked == true;
            }
            else if (checkBox == chkSexta)
            {
                txtSextaEntrada.IsEnabled = chkSexta.IsChecked == true;
                txtSextaSaida.IsEnabled = chkSexta.IsChecked == true;
            }
            else if (checkBox == chkSabado)
            {
                txtSabadoEntrada.IsEnabled = chkSabado.IsChecked == true;
                txtSabadoSaida.IsEnabled = chkSabado.IsChecked == true;
            }
            else if (checkBox == chkDomingo)
            {
                txtDomingoEntrada.IsEnabled = chkDomingo.IsChecked == true;
                txtDomingoSaida.IsEnabled = chkDomingo.IsChecked == true;
            }
        }

        private void btnRegistrarEntrada_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_funcionarioSelecionadoId == 0)
                {
                    MessageBox.Show("Selecione um funcionário", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var hoje = DateTime.Today;
                var agora = DateTime.Now.TimeOfDay;

                var registroExistente = _context.FuncionarioTurnos
                    .FirstOrDefault(f => f.UserId == _funcionarioSelecionadoId &&
                                        f.Data.Date == hoje);

                if (registroExistente != null)
                {
                    if (registroExistente.HoraEntrada != null)
                    {
                        MessageBox.Show("Este funcionário já registrou entrada hoje", "Aviso",
                                       MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    registroExistente.HoraEntrada = agora;
                    registroExistente.UpdatedAt = DateTime.Now;
                    registroExistente.Turno = registroExistente.Turno ?? DeterminarTurno(agora);
                    registroExistente.Atraso = CalcularAtraso(_funcionarioSelecionadoId, hoje, agora);
                }
                else
                {
                    var novoRegistro = new FuncionarioTurno
                    {
                        UserId = _funcionarioSelecionadoId,
                        Data = hoje,
                        HoraEntrada = agora,
                        Turno = DeterminarTurno(agora),
                        Observacoes = "",
                        TipoAusencia = "",
                        JustificativaAusencia = "",
                        JustificativaHorasExtras = "",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        HoraExtraAutorizada = false,
                        Ausente = false
                    };

                    var horarioPrevisto = ObterHorarioPrevisto(_funcionarioSelecionadoId, hoje);
                    if (horarioPrevisto.HasValue)
                    {
                        novoRegistro.HorarioPrevistoEntrada = horarioPrevisto.Value;
                        novoRegistro.Atraso = CalcularAtraso(horarioPrevisto.Value, agora);
                    }

                    _context.FuncionarioTurnos.Add(novoRegistro);
                }

                _context.SaveChanges();

                MessageBox.Show("Entrada registrada com sucesso!", "Sucesso",
                               MessageBoxButton.OK, MessageBoxImage.Information);

                CarregarRegistrosHoje();
                AtualizarEstatisticas();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao registrar entrada: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnRegistrarSaida_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_funcionarioSelecionadoId == 0)
                {
                    MessageBox.Show("Selecione um funcionário", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var hoje = DateTime.Today;
                var agora = DateTime.Now.TimeOfDay;

                var registro = _context.FuncionarioTurnos
                    .FirstOrDefault(f => f.UserId == _funcionarioSelecionadoId &&
                                        f.Data.Date == hoje);

                if (registro == null || registro.HoraEntrada == null)
                {
                    MessageBox.Show("Registro de entrada não encontrado para hoje", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (registro.HoraSaida != null)
                {
                    MessageBox.Show("Saída já registrada hoje", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                registro.HoraSaida = agora;
                registro.UpdatedAt = DateTime.Now;

                if (registro.HoraEntrada.HasValue)
                {
                    var horasTrabalhadas = agora - registro.HoraEntrada.Value;
                    if (horasTrabalhadas > TimeSpan.Zero)
                    {
                        registro.HorasTrabalhadas = horasTrabalhadas;
                    }
                }

                registro.HorasExtras = CalcularHorasExtras(_funcionarioSelecionadoId, hoje,
                                                          registro.HoraEntrada.Value, agora);

                _context.SaveChanges();

                MessageBox.Show("Saída registrada com sucesso!", "Sucesso",
                               MessageBoxButton.OK, MessageBoxImage.Information);

                CarregarRegistrosHoje();
                AtualizarEstatisticas();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao registrar saída: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnMarcarAusencia_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_funcionarioSelecionadoId == 0)
                {
                    MessageBox.Show("Selecione um funcionário", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var hoje = DateTime.Today;
                var registro = _context.FuncionarioTurnos
                    .FirstOrDefault(f => f.UserId == _funcionarioSelecionadoId &&
                                        f.Data.Date == hoje);

                var dialog = new Window
                {
                    Title = "Marcar Ausência",
                    Width = 400,
                    Height = 380,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.NoResize
                };

                var stackPanel = new StackPanel { Margin = new Thickness(10) };

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Tipo de Ausência:",
                    Margin = new Thickness(0, 5, 0, 5),
                    FontWeight = FontWeights.SemiBold
                });

                var cmbTipoAusencia = new ComboBox
                {
                    Margin = new Thickness(0, 5, 0, 5),
                    ItemsSource = new List<string> { "Falta", "Férias", "Licença Médica", "Dispensa", "Outro" }
                };
                cmbTipoAusencia.SelectedIndex = 0;
                stackPanel.Children.Add(cmbTipoAusencia);

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Turno:",
                    Margin = new Thickness(0, 5, 0, 5),
                    FontWeight = FontWeights.SemiBold
                });

                var cmbTurno = new ComboBox
                {
                    Margin = new Thickness(0, 5, 0, 5),
                    ItemsSource = new List<string> { "Manhã", "Tarde", "Noite", "Integral" }
                };
                cmbTurno.SelectedIndex = 3;
                stackPanel.Children.Add(cmbTurno);

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Justificativa:",
                    Margin = new Thickness(0, 5, 0, 5),
                    FontWeight = FontWeights.SemiBold
                });

                var txtJustificativa = new TextBox
                {
                    Margin = new Thickness(0, 5, 0, 5),
                    Height = 80,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                stackPanel.Children.Add(txtJustificativa);

                stackPanel.Children.Add(new TextBlock
                {
                    Text = "Observações Adicionais:",
                    Margin = new Thickness(0, 5, 0, 5),
                    FontWeight = FontWeights.SemiBold
                });

                var txtObservacoes = new TextBox
                {
                    Margin = new Thickness(0, 5, 0, 5),
                    Height = 60,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                };
                stackPanel.Children.Add(txtObservacoes);

                var btnSalvar = new Button
                {
                    Content = "✅ Salvar Ausência",
                    Margin = new Thickness(0, 10, 0, 10),
                    Padding = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                    Foreground = Brushes.White,
                    Width = 150,
                    Height = 40,
                    FontWeight = FontWeights.Bold
                };

                btnSalvar.Click += (s, ev) =>
                {
                    try
                    {
                        if (registro == null)
                        {
                            registro = new FuncionarioTurno
                            {
                                UserId = _funcionarioSelecionadoId,
                                Data = hoje,
                                Ausente = true,
                                Turno = cmbTurno.SelectedItem?.ToString() ?? "Integral",
                                TipoAusencia = cmbTipoAusencia.SelectedItem?.ToString() ?? "",
                                JustificativaAusencia = txtJustificativa.Text ?? "",
                                Observacoes = txtObservacoes.Text ?? "",
                                JustificativaHorasExtras = "",
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now
                            };
                            _context.FuncionarioTurnos.Add(registro);
                        }
                        else
                        {
                            registro.Ausente = true;
                            registro.Turno = cmbTurno.SelectedItem?.ToString() ?? registro.Turno ?? "Integral";
                            registro.TipoAusencia = cmbTipoAusencia.SelectedItem?.ToString() ?? registro.TipoAusencia ?? "";
                            registro.JustificativaAusencia = txtJustificativa.Text ?? registro.JustificativaAusencia ?? "";
                            registro.Observacoes = txtObservacoes.Text ?? registro.Observacoes ?? "";
                            registro.UpdatedAt = DateTime.Now;
                            registro.JustificativaHorasExtras = registro.JustificativaHorasExtras ?? "";
                        }

                        _context.SaveChanges();
                        dialog.Close();

                        MessageBox.Show("Ausência registrada com sucesso!", "Sucesso",
                                       MessageBoxButton.OK, MessageBoxImage.Information);

                        CarregarRegistrosHoje();
                        AtualizarEstatisticas();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao salvar ausência: {ex.Message}", "Erro",
                                       MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                stackPanel.Children.Add(btnSalvar);

                dialog.Content = stackPanel;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao marcar ausência: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnCriarHorario_Click(object sender, RoutedEventArgs e)
        {
            if (cmbFuncionariosHorario.SelectedItem == null)
            {
                MessageBox.Show("Selecione um funcionário", "Aviso",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            txtHorarioPadraoEntrada.Text = "09:00";
            txtHorarioPadraoSaida.Text = "18:00";
            txtHorasPorDia.Text = "8";

            chkSegunda.IsChecked = true;
            chkTerca.IsChecked = true;
            chkQuarta.IsChecked = true;
            chkQuinta.IsChecked = true;
            chkSexta.IsChecked = true;
            chkSabado.IsChecked = false;
            chkDomingo.IsChecked = false;

            txtSegundaEntrada.Text = "09:00";
            txtSegundaSaida.Text = "18:00";
            txtTercaEntrada.Text = "09:00";
            txtTercaSaida.Text = "18:00";
            txtQuartaEntrada.Text = "09:00";
            txtQuartaSaida.Text = "18:00";
            txtQuintaEntrada.Text = "09:00";
            txtQuintaSaida.Text = "18:00";
            txtSextaEntrada.Text = "09:00";
            txtSextaSaida.Text = "18:00";
            txtSabadoEntrada.Text = "";
            txtSabadoSaida.Text = "";
            txtDomingoEntrada.Text = "";
            txtDomingoSaida.Text = "";

            txtSegundaEntrada.IsEnabled = true;
            txtSegundaSaida.IsEnabled = true;
            txtTercaEntrada.IsEnabled = true;
            txtTercaSaida.IsEnabled = true;
            txtQuartaEntrada.IsEnabled = true;
            txtQuartaSaida.IsEnabled = true;
            txtQuintaEntrada.IsEnabled = true;
            txtQuintaSaida.IsEnabled = true;
            txtSextaEntrada.IsEnabled = true;
            txtSextaSaida.IsEnabled = true;
            txtSabadoEntrada.IsEnabled = false;
            txtSabadoSaida.IsEnabled = false;
            txtDomingoEntrada.IsEnabled = false;
            txtDomingoSaida.IsEnabled = false;
        }

        private void btnSalvarHorario_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cmbFuncionariosHorario.SelectedItem is not User user)
                {
                    MessageBox.Show("Selecione um funcionário", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(txtHorarioPadraoEntrada.Text, out var entradaPadrao))
                {
                    MessageBox.Show("Horário de entrada padrão inválido. Use formato HH:MM (ex: 09:00)", "Erro",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!TimeSpan.TryParse(txtHorarioPadraoSaida.Text, out var saidaPadrao))
                {
                    MessageBox.Show("Horário de saída padrão inválido. Use formato HH:MM (ex: 18:00)", "Erro",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!int.TryParse(txtHorasPorDia.Text, out var horasPorDia) || horasPorDia <= 0 || horasPorDia > 24)
                {
                    MessageBox.Show("Horas por dia inválidas. Digite um número entre 1 e 24", "Erro",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var horario = _context.HorarioUsuarios
                    .FirstOrDefault(h => h.UserId == user.Id && h.Ativo);

                bool isNew = false;
                if (horario == null)
                {
                    horario = new HorarioUsuario
                    {
                        UserId = user.Id,
                        CreatedAt = DateTime.Now
                    };
                    _context.HorarioUsuarios.Add(horario);
                    isNew = true;
                }

                horario.HorarioPadraoEntrada = entradaPadrao;
                horario.HorarioPadraoSaida = saidaPadrao;
                horario.HorasPorDia = horasPorDia;
                horario.VigenciaInicio = DateTime.Today;
                horario.Ativo = true;
                horario.UpdatedAt = DateTime.Now;

                horario.Segunda = chkSegunda.IsChecked == true;
                horario.Terca = chkTerca.IsChecked == true;
                horario.Quarta = chkQuarta.IsChecked == true;
                horario.Quinta = chkQuinta.IsChecked == true;
                horario.Sexta = chkSexta.IsChecked == true;
                horario.Sabado = chkSabado.IsChecked == true;
                horario.Domingo = chkDomingo.IsChecked == true;

                int diasTrabalho = 0;
                if (horario.Segunda) diasTrabalho++;
                if (horario.Terca) diasTrabalho++;
                if (horario.Quarta) diasTrabalho++;
                if (horario.Quinta) diasTrabalho++;
                if (horario.Sexta) diasTrabalho++;
                if (horario.Sabado) diasTrabalho++;
                if (horario.Domingo) diasTrabalho++;
                horario.DiasTrabalhoSemana = diasTrabalho;

                // Horários específicos - Segunda
                if (horario.Segunda)
                {
                    if (!string.IsNullOrWhiteSpace(txtSegundaEntrada.Text) &&
                        TimeSpan.TryParse(txtSegundaEntrada.Text, out var segEnt))
                        horario.SegundaEntrada = segEnt;
                    else
                        horario.SegundaEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtSegundaSaida.Text) &&
                        TimeSpan.TryParse(txtSegundaSaida.Text, out var segSai))
                        horario.SegundaSaida = segSai;
                    else
                        horario.SegundaSaida = saidaPadrao;
                }

                if (horario.Terca)
                {
                    if (!string.IsNullOrWhiteSpace(txtTercaEntrada.Text) &&
                        TimeSpan.TryParse(txtTercaEntrada.Text, out var terEnt))
                        horario.TercaEntrada = terEnt;
                    else
                        horario.TercaEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtTercaSaida.Text) &&
                        TimeSpan.TryParse(txtTercaSaida.Text, out var terSai))
                        horario.TercaSaida = terSai;
                    else
                        horario.TercaSaida = saidaPadrao;
                }

                if (horario.Quarta)
                {
                    if (!string.IsNullOrWhiteSpace(txtQuartaEntrada.Text) &&
                        TimeSpan.TryParse(txtQuartaEntrada.Text, out var quaEnt))
                        horario.QuartaEntrada = quaEnt;
                    else
                        horario.QuartaEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtQuartaSaida.Text) &&
                        TimeSpan.TryParse(txtQuartaSaida.Text, out var quaSai))
                        horario.QuartaSaida = quaSai;
                    else
                        horario.QuartaSaida = saidaPadrao;
                }

                if (horario.Quinta)
                {
                    if (!string.IsNullOrWhiteSpace(txtQuintaEntrada.Text) &&
                        TimeSpan.TryParse(txtQuintaEntrada.Text, out var quiEnt))
                        horario.QuintaEntrada = quiEnt;
                    else
                        horario.QuintaEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtQuintaSaida.Text) &&
                        TimeSpan.TryParse(txtQuintaSaida.Text, out var quiSai))
                        horario.QuintaSaida = quiSai;
                    else
                        horario.QuintaSaida = saidaPadrao;
                }

                if (horario.Sexta)
                {
                    if (!string.IsNullOrWhiteSpace(txtSextaEntrada.Text) &&
                        TimeSpan.TryParse(txtSextaEntrada.Text, out var sexEnt))
                        horario.SextaEntrada = sexEnt;
                    else
                        horario.SextaEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtSextaSaida.Text) &&
                        TimeSpan.TryParse(txtSextaSaida.Text, out var sexSai))
                        horario.SextaSida = sexSai;
                    else
                        horario.SextaSida = saidaPadrao;
                }

                if (horario.Sabado)
                {
                    if (!string.IsNullOrWhiteSpace(txtSabadoEntrada.Text) &&
                        TimeSpan.TryParse(txtSabadoEntrada.Text, out var sabEnt))
                        horario.SabadoEntrada = sabEnt;
                    else
                        horario.SabadoEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtSabadoSaida.Text) &&
                        TimeSpan.TryParse(txtSabadoSaida.Text, out var sabSai))
                        horario.SabadoSaida = sabSai;
                    else
                        horario.SabadoSaida = saidaPadrao;
                }

                if (horario.Domingo)
                {
                    if (!string.IsNullOrWhiteSpace(txtDomingoEntrada.Text) &&
                        TimeSpan.TryParse(txtDomingoEntrada.Text, out var domEnt))
                        horario.DomingoEntrada = domEnt;
                    else
                        horario.DomingoEntrada = entradaPadrao;

                    if (!string.IsNullOrWhiteSpace(txtDomingoSaida.Text) &&
                        TimeSpan.TryParse(txtDomingoSaida.Text, out var domSai))
                        horario.DomingoSaida = domSai;
                    else
                        horario.DomingoSaida = saidaPadrao;
                }

                try
                {
                    _context.SaveChanges();

                    string mensagem = isNew ? "Horário criado com sucesso!" : "Horário atualizado com sucesso!";
                    MessageBox.Show(mensagem, "Sucesso",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (DbUpdateException dbEx)
                {
                    var innerMessage = dbEx.InnerException?.Message ?? "Erro desconhecido";
                    MessageBox.Show($"Erro ao salvar no banco de dados: {innerMessage}\n\nDetalhes: {dbEx.Message}",
                                   "Erro de Banco de Dados",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    Debug.WriteLine($"DbUpdateException: {dbEx}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro inesperado: {ex.Message}", "Erro",
                                   MessageBoxButton.OK, MessageBoxImage.Error);
                    Debug.WriteLine($"Erro: {ex}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao processar horário: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Erro geral: {ex}");
            }
        }

        private void btnAtualizar_Click(object sender, RoutedEventArgs e)
        {
            CarregarRegistrosHoje();
            AtualizarEstatisticas();
        }

        private void btnFiltrarRelatorio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataInicio = dpDataInicio.SelectedDate ?? DateTime.Today.AddDays(-30);
                var dataFim = dpDataFim.SelectedDate ?? DateTime.Today;

                int? funcionarioId = null;
                if (cmbFiltroFuncionario.SelectedItem is ComboBoxItem item && item.Tag is int id && id > 0)
                {
                    funcionarioId = id;
                }

                var query = _context.FuncionarioTurnos
                    .Where(f => f.Data.Date >= dataInicio.Date && f.Data.Date <= dataFim.Date)
                    .Include(f => f.User)
                    .AsQueryable();

                if (funcionarioId.HasValue)
                {
                    query = query.Where(f => f.UserId == funcionarioId.Value);
                }

                var registros = query
                    .OrderBy(f => f.Data)
                    .ThenBy(f => f.User.FullName)
                    .ToList();

                var registrosView = registros.Select(r => new RegistroPontoView
                {
                    Id = r.Id,
                    UserId = r.UserId,
                    NomeFuncionario = r.User?.FullName ?? r.User?.Username ?? "Desconhecido",
                    Data = r.Data,
                    HoraEntrada = r.HoraEntrada,
                    HoraSaida = r.HoraSaida,
                    HorasTrabalhadas = r.HorasTrabalhadas,
                    HorarioPrevistoEntrada = r.HorarioPrevistoEntrada,
                    HorarioPrevistoSaida = r.HorarioPrevistoSaida,
                    Atraso = r.Atraso,
                    HorasExtras = r.HorasExtras,
                    HoraExtraAutorizada = r.HoraExtraAutorizada,
                    Turno = r.Turno ?? "Integral",
                    Ausente = r.Ausente
                }).ToList();

                dgRelatorio.ItemsSource = registrosView;
                _registrosRelatorio = registros;

                var resumo = CalcularResumoRelatorio(registros);
                ExibirResumoRelatorio(resumo, dataInicio, dataFim, funcionarioId);

                // Atualizar resumo na UI
                string filtroNome = funcionarioId.HasValue ?
                    $"para '{registros.FirstOrDefault()?.User?.FullName ?? "Funcionário"}'" :
                    "para todos os funcionários";

                txtResumoRelatorio.Text =
                    $"📊 {resumo.TotalRegistros} registros | " +
                    $"✅ Presenças: {resumo.TotalRegistros - resumo.TotalAusencias} | " +
                    $"❌ Ausências: {resumo.TotalAusencias} | " +
                    $"⏰ Horas Trab.: {resumo.TotalHorasTrabalhadas:hh\\:mm} | " +
                    $"⚠️ Atrasos: {resumo.TotalAtrasos:hh\\:mm} | " +
                    $"⭐ Horas Efetivas: {resumo.TotalHorasEfetivas:hh\\:mm}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar relatório: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Gráficos

        private void CarregarGraficosIniciais()
        {
            try
            {
                dpGraficoInicio.SelectedDate = DateTime.Today.AddDays(-30);
                dpGraficoFim.SelectedDate = DateTime.Today;

                var funcionarios = _context.Users
                    .Where(u => u.IsActive)
                    .OrderBy(u => u.FullName ?? u.Username)
                    .ToList();

                cmbGraficoFuncionario.Items.Clear();
                cmbGraficoFuncionario.Items.Add(new ComboBoxItem { Content = "👤 Todos os Funcionários", Tag = 0 });

                foreach (var func in funcionarios)
                {
                    cmbGraficoFuncionario.Items.Add(new ComboBoxItem
                    {
                        Content = func.FullName ?? func.Username,
                        Tag = func.Id
                    });
                }

                cmbGraficoFuncionario.SelectedIndex = 0;

                // Gerar gráficos iniciais
                btnGerarGraficos_Click(null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar gráficos iniciais: {ex.Message}");
            }
        }

        private async void btnGerarGraficos_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dataInicio = dpGraficoInicio.SelectedDate ?? DateTime.Today.AddDays(-30);
                var dataFim = dpGraficoFim.SelectedDate ?? DateTime.Today;

                int? funcionarioId = null;
                if (cmbGraficoFuncionario.SelectedItem is ComboBoxItem item && item.Tag is int id && id > 0)
                {
                    funcionarioId = id;
                }

                var query = _context.FuncionarioTurnos
                    .Where(f => f.Data.Date >= dataInicio.Date && f.Data.Date <= dataFim.Date)
                    .Include(f => f.User)
                    .AsQueryable();

                if (funcionarioId.HasValue)
                {
                    query = query.Where(f => f.UserId == funcionarioId.Value);
                }

                var registros = await query.ToListAsync();

                if (!registros.Any())
                {
                    txtResumoGraficos.Text = "📊 Nenhum dado encontrado para o período selecionado.";
                    return;
                }

                var resumo = CalcularResumoRelatorio(registros);

                // Atualizar resumo
                txtResumoGraficos.Text =
                    $"📊 Período: {dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy} | " +
                    $"Total: {resumo.TotalRegistros} registros | " +
                    $"✅ Presenças: {resumo.TotalRegistros - resumo.TotalAusencias} | " +
                    $"❌ Ausências: {resumo.TotalAusencias} | " +
                    $"📈 Presença: {resumo.PercentualPresenca:0.0}%";

                // Desenhar gráficos
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        DesenharGraficoPizza(registros);
                        DesenharGraficoBarras(registros);
                        DesenharGraficoAtrasos(registros);
                        DesenharGraficoResumoGeral(registros);
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar gráficos: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DesenharGraficoPizza(List<FuncionarioTurno> registros)
        {
            canvasPizza.Children.Clear();
            pnlLegendaPizza.Children.Clear();

            if (registros == null || !registros.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasPizza, "Sem dados para pizza");
                return;
            }

            int presencas = registros.Count(r => !r.Ausente);
            int ausencias = registros.Count(r => r.Ausente);
            int total = registros.Count;

            if (total == 0)
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasPizza, "Sem dados para pizza");
                return;
            }

            // Garantir que o Canvas tenha tamanho
            if (canvasPizza.ActualWidth <= 0 || canvasPizza.ActualHeight <= 0)
            {
                canvasPizza.Width = 400;
                canvasPizza.Height = 300;
            }

            var dados = new List<ChartData>
    {
        new ChartData { Label = $"Presenças ({presencas})", Value = presencas, Color = new SolidColorBrush(Colors.Green) },
        new ChartData { Label = $"Ausências ({ausencias})", Value = ausencias, Color = new SolidColorBrush(Colors.Red) }
    };

            double centerX = canvasPizza.ActualWidth / 2;
            double centerY = canvasPizza.ActualHeight / 2;
            double radius = Math.Min(canvasPizza.ActualWidth, canvasPizza.ActualHeight) / 2 - 40;

            if (radius <= 0) radius = 100;
            if (centerX <= 0) centerX = 200;
            if (centerY <= 0) centerY = 150;

            double startAngle = 0;

            foreach (var dado in dados)
            {
                if (dado.Value <= 0) continue;

                double angle = 360 * (dado.Value / total);
                double endAngle = startAngle + angle;

                var path = new System.Windows.Shapes.Path();
                var figure = new PathFigure();
                figure.StartPoint = new Point(centerX, centerY);

                double startRad = startAngle * Math.PI / 180;
                double endRad = endAngle * Math.PI / 180;

                double x1 = centerX + radius * Math.Cos(startRad);
                double y1 = centerY + radius * Math.Sin(startRad);
                double x2 = centerX + radius * Math.Cos(endRad);
                double y2 = centerY + radius * Math.Sin(endRad);

                figure.Segments.Add(new LineSegment(new Point(x1, y1), true));
                figure.Segments.Add(new ArcSegment(
                    new Point(x2, y2),
                    new Size(radius, radius),
                    0,
                    angle > 180,
                    SweepDirection.Clockwise,
                    true));
                figure.Segments.Add(new LineSegment(new Point(centerX, centerY), true));

                path.Data = new PathGeometry(new[] { figure });
                path.Fill = dado.Color;
                path.Stroke = new SolidColorBrush(Colors.White);
                path.StrokeThickness = 2;

                Canvas.SetLeft(path, 0);
                Canvas.SetTop(path, 0);
                canvasPizza.Children.Add(path);

                // Adicionar porcentagem no centro do setor
                double midAngle = startAngle + angle / 2;
                double midRad = midAngle * Math.PI / 180;
                double textRadius = radius * 0.6;
                double tx = centerX + textRadius * Math.Cos(midRad);
                double ty = centerY + textRadius * Math.Sin(midRad);

                if (dado.Value / total > 0.05 && !double.IsNaN(tx) && !double.IsNaN(ty))
                {
                    var percentage = new TextBlock
                    {
                        Text = $"{dado.Value / total * 100:0.0}%",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White)
                    };
                    Canvas.SetLeft(percentage, tx - 15);
                    Canvas.SetTop(percentage, ty - 8);
                    canvasPizza.Children.Add(percentage);
                }

                startAngle = endAngle;
            }

            // Adicionar legenda
            foreach (var dado in dados)
            {
                if (dado.Value <= 0) continue;

                var legendaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 2, 10, 2) };
                legendaStack.Children.Add(new Border
                {
                    Width = 16,
                    Height = 16,
                    Background = dado.Color,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                legendaStack.Children.Add(new TextBlock
                {
                    Text = $"{dado.Label} ({dado.Value / total * 100:0.0}%)",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.Black)
                });

                pnlLegendaPizza.Children.Add(legendaStack);
            }
        }

        private void DesenharGraficoBarras(List<FuncionarioTurno> registros)
        {
            canvasBarras.Children.Clear();
            pnlLegendaBarras.Children.Clear();

            if (registros == null || !registros.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasBarras, "Sem dados para barras");
                return;
            }

            // Garantir que o Canvas tenha tamanho
            if (canvasBarras.ActualWidth <= 0 || canvasBarras.ActualHeight <= 0)
            {
                canvasBarras.Width = 600;
                canvasBarras.Height = 300;
            }

            var dadosPorFuncionario = registros
                .Where(r => !r.Ausente && r.HorasTrabalhadas.HasValue)
                .GroupBy(r => r.User?.FullName ?? r.User?.Username ?? "Desconhecido")
                .Select(g => new ChartData
                {
                    Label = g.Key,
                    Value = g.Sum(x => x.HorasTrabalhadas.Value.TotalHours)
                })
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .Take(10)
                .ToList();

            if (!dadosPorFuncionario.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasBarras, "Sem dados de horas trabalhadas");
                return;
            }

            double maxValue = dadosPorFuncionario.Max(x => x.Value);
            if (maxValue <= 0)
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasBarras, "Valores inválidos");
                return;
            }

            double canvasWidth = canvasBarras.ActualWidth - 80;
            double canvasHeight = canvasBarras.ActualHeight - 60;

            if (canvasWidth <= 0) canvasWidth = 520;
            if (canvasHeight <= 0) canvasHeight = 240;

            double barWidth = Math.Min(canvasWidth / dadosPorFuncionario.Count * 0.7, 50);
            double barSpacing = Math.Min(canvasWidth / dadosPorFuncionario.Count * 0.3, 20);

            if (barWidth <= 0) barWidth = 30;
            if (barSpacing <= 0) barSpacing = 10;

            var cores = new List<Color>
    {
        Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple,
        Colors.Teal, Colors.Pink, Colors.Indigo, Colors.Cyan,
        Colors.Brown, Colors.Red
    };

            for (int i = 0; i < dadosPorFuncionario.Count; i++)
            {
                var dado = dadosPorFuncionario[i];
                double barHeight = (dado.Value / maxValue) * canvasHeight;
                double x = 40 + i * (barWidth + barSpacing);
                double y = canvasHeight + 20 - barHeight;

                // Garantir que os valores são válidos
                if (double.IsNaN(barHeight)) barHeight = 10;
                if (double.IsNaN(x)) x = 40 + i * 50;
                if (double.IsNaN(y)) y = canvasHeight + 20 - 10;
                if (barHeight < 0) barHeight = 0;

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(cores[i % cores.Count]),
                    RadiusX = 4,
                    RadiusY = 4,
                    ToolTip = $"{dado.Label}: {dado.Value:0.0}h"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvasBarras.Children.Add(rect);

                // Valor em cima da barra
                if (barHeight > 20)
                {
                    var valorText = new TextBlock
                    {
                        Text = $"{dado.Value:0.0}h",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Black),
                        FontWeight = FontWeights.SemiBold
                    };
                    Canvas.SetLeft(valorText, x + barWidth / 2 - 15);
                    Canvas.SetTop(valorText, y - 18);
                    canvasBarras.Children.Add(valorText);
                }

                // Label da barra (nome do funcionário)
                var labelText = new TextBlock
                {
                    Text = dado.Label.Length > 12 ? dado.Label.Substring(0, 10) + "..." : dado.Label,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Colors.Black),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(labelText, x + barWidth / 2 - 20);
                Canvas.SetTop(labelText, canvasHeight + 25);
                canvasBarras.Children.Add(labelText);
            }

            // Eixo Y
            var axisY = new Line
            {
                X1 = 30,
                Y1 = 10,
                X2 = 30,
                Y2 = canvasHeight + 20,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasBarras.Children.Add(axisY);

            // Eixo X
            var axisX = new Line
            {
                X1 = 30,
                Y1 = canvasHeight + 20,
                X2 = canvasWidth + 50,
                Y2 = canvasHeight + 20,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasBarras.Children.Add(axisX);
        }
        private void DesenharGraficoAtrasos(List<FuncionarioTurno> registros)
        {
            canvasAtrasos.Children.Clear();
            pnlLegendaAtrasos.Children.Clear();

            if (registros == null || !registros.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasAtrasos, "Sem dados para atrasos");
                return;
            }

            // Garantir que o Canvas tenha tamanho
            if (canvasAtrasos.ActualWidth <= 0 || canvasAtrasos.ActualHeight <= 0)
            {
                canvasAtrasos.Width = 600;
                canvasAtrasos.Height = 300;
            }

            var dadosPorFuncionario = registros
                .Where(r => r.Atraso.HasValue && r.Atraso.Value.TotalMinutes > 0)
                .GroupBy(r => r.User?.FullName ?? r.User?.Username ?? "Desconhecido")
                .Select(g => new ChartData
                {
                    Label = g.Key,
                    Value = g.Sum(x => x.Atraso.Value.TotalMinutes)
                })
                .Where(x => x.Value > 0)
                .OrderByDescending(x => x.Value)
                .Take(10)
                .ToList();

            if (!dadosPorFuncionario.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasAtrasos, "Sem dados de atrasos");
                return;
            }

            double maxValue = dadosPorFuncionario.Max(x => x.Value);
            if (maxValue <= 0)
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasAtrasos, "Valores inválidos");
                return;
            }

            double canvasWidth = canvasAtrasos.ActualWidth - 80;
            double canvasHeight = canvasAtrasos.ActualHeight - 60;

            if (canvasWidth <= 0) canvasWidth = 520;
            if (canvasHeight <= 0) canvasHeight = 240;

            double barWidth = Math.Min(canvasWidth / dadosPorFuncionario.Count * 0.7, 50);
            double barSpacing = Math.Min(canvasWidth / dadosPorFuncionario.Count * 0.3, 20);

            if (barWidth <= 0) barWidth = 30;
            if (barSpacing <= 0) barSpacing = 10;

            for (int i = 0; i < dadosPorFuncionario.Count; i++)
            {
                var dado = dadosPorFuncionario[i];
                double barHeight = (dado.Value / maxValue) * canvasHeight;
                double x = 40 + i * (barWidth + barSpacing);
                double y = canvasHeight + 20 - barHeight;

                // Garantir que os valores são válidos
                if (double.IsNaN(barHeight)) barHeight = 10;
                if (double.IsNaN(x)) x = 40 + i * 50;
                if (double.IsNaN(y)) y = canvasHeight + 20 - 10;
                if (barHeight < 0) barHeight = 0;

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(Colors.Orange),
                    RadiusX = 4,
                    RadiusY = 4,
                    ToolTip = $"{dado.Label}: {dado.Value / 60:0.0}h"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvasAtrasos.Children.Add(rect);

                if (barHeight > 20)
                {
                    var valorText = new TextBlock
                    {
                        Text = $"{dado.Value / 60:0.0}h",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Colors.Black),
                        FontWeight = FontWeights.SemiBold
                    };
                    Canvas.SetLeft(valorText, x + barWidth / 2 - 15);
                    Canvas.SetTop(valorText, y - 18);
                    canvasAtrasos.Children.Add(valorText);
                }

                var labelText = new TextBlock
                {
                    Text = dado.Label.Length > 12 ? dado.Label.Substring(0, 10) + "..." : dado.Label,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Colors.Black),
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(labelText, x + barWidth / 2 - 20);
                Canvas.SetTop(labelText, canvasHeight + 25);
                canvasAtrasos.Children.Add(labelText);
            }

            // Eixos
            var axisY = new Line
            {
                X1 = 30,
                Y1 = 10,
                X2 = 30,
                Y2 = canvasHeight + 20,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasAtrasos.Children.Add(axisY);

            var axisX = new Line
            {
                X1 = 30,
                Y1 = canvasHeight + 20,
                X2 = canvasWidth + 50,
                Y2 = canvasHeight + 20,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasAtrasos.Children.Add(axisX);
        }

        private void DesenharGraficoResumoGeral(List<FuncionarioTurno> registros)
        {
            canvasResumoGeral.Children.Clear();
            pnlLegendaResumo.Children.Clear();

            if (registros == null || !registros.Any())
            {
                LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(canvasResumoGeral, "Sem dados para resumo");
                return;
            }

            // Garantir que o Canvas tenha tamanho
            if (canvasResumoGeral.ActualWidth <= 0 || canvasResumoGeral.ActualHeight <= 0)
            {
                canvasResumoGeral.Width = 600;
                canvasResumoGeral.Height = 250;
            }

            var resumo = CalcularResumoRelatorio(registros);

            var dados = new List<ChartData>
    {
        new ChartData { Label = "Horas Trabalhadas", Value = resumo.TotalHorasTrabalhadas.TotalHours, Color = new SolidColorBrush(Colors.Blue) },
        new ChartData { Label = "Horas Efetivas", Value = resumo.TotalHorasEfetivas.TotalHours, Color = new SolidColorBrush(Colors.Green) },
        new ChartData { Label = "Atrasos", Value = resumo.TotalAtrasos.TotalHours, Color = new SolidColorBrush(Colors.Orange) },
        new ChartData { Label = "Horas Extras", Value = resumo.TotalHorasExtras.TotalHours, Color = new SolidColorBrush(Colors.Purple) }
    };

            double maxValue = dados.Max(x => x.Value);
            if (maxValue <= 0) maxValue = 1;

            double canvasWidth = canvasResumoGeral.ActualWidth - 80;
            double canvasHeight = canvasResumoGeral.ActualHeight - 50;

            if (canvasWidth <= 0) canvasWidth = 520;
            if (canvasHeight <= 0) canvasHeight = 200;

            double barWidth = Math.Min(canvasWidth / dados.Count * 0.7, 80);
            double barSpacing = Math.Min(canvasWidth / dados.Count * 0.3, 30);

            if (barWidth <= 0) barWidth = 60;
            if (barSpacing <= 0) barSpacing = 20;

            for (int i = 0; i < dados.Count; i++)
            {
                var dado = dados[i];
                double barHeight = (dado.Value / maxValue) * canvasHeight;
                double x = 40 + i * (barWidth + barSpacing);
                double y = canvasHeight + 10 - barHeight;

                // Garantir que os valores são válidos
                if (double.IsNaN(barHeight)) barHeight = 10;
                if (double.IsNaN(x)) x = 40 + i * 80;
                if (double.IsNaN(y)) y = canvasHeight + 10 - 10;
                if (barHeight < 0) barHeight = 0;

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = dado.Color,
                    RadiusX = 4,
                    RadiusY = 4,
                    ToolTip = $"{dado.Label}: {dado.Value:0.0}h"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvasResumoGeral.Children.Add(rect);

                if (barHeight > 20)
                {
                    var valorText = new TextBlock
                    {
                        Text = $"{dado.Value:0.0}h",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.Black),
                        FontWeight = FontWeights.Bold
                    };
                    Canvas.SetLeft(valorText, x + barWidth / 2 - 15);
                    Canvas.SetTop(valorText, y - 20);
                    canvasResumoGeral.Children.Add(valorText);
                }

                var labelText = new TextBlock
                {
                    Text = dado.Label,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Colors.Black),
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(labelText, x + barWidth / 2 - 25);
                Canvas.SetTop(labelText, canvasHeight + 15);
                canvasResumoGeral.Children.Add(labelText);
            }

            // Legenda
            foreach (var dado in dados)
            {
                var legendaStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 2, 10, 2) };
                legendaStack.Children.Add(new Border
                {
                    Width = 14,
                    Height = 14,
                    Background = dado.Color,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                legendaStack.Children.Add(new TextBlock
                {
                    Text = $"{dado.Label}: {dado.Value:0.0}h",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.Black)
                });

                pnlLegendaResumo.Children.Add(legendaStack);
            }

            // Eixos
            var axisY = new Line
            {
                X1 = 30,
                Y1 = 10,
                X2 = 30,
                Y2 = canvasHeight + 10,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasResumoGeral.Children.Add(axisY);

            var axisX = new Line
            {
                X1 = 30,
                Y1 = canvasHeight + 10,
                X2 = canvasWidth + 50,
                Y2 = canvasHeight + 10,
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 1
            };
            canvasResumoGeral.Children.Add(axisX);
        }

        private void LRJx62kAYSNxChe9ftum37i5zmgM8WG9qd(Canvas canvas, string mensagem)
        {
            canvas.Children.Clear();

            var textBlock = new TextBlock
            {
                Text = $"📊 {mensagem}",
                FontSize = 16,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas.SetLeft(textBlock, canvas.ActualWidth / 2 - 100);
            Canvas.SetTop(textBlock, canvas.ActualHeight / 2 - 15);
            canvas.Children.Add(textBlock);
        }
        #endregion

        #region Exportação

        private void btnExportarRelatorio_Click(object sender, RoutedEventArgs e)
        {
            if (_registrosRelatorio == null || !_registrosRelatorio.Any())
            {
                MessageBox.Show("Nenhum dado para exportar. Gere um relatório primeiro.", "Aviso",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new Window
            {
                Title = "Exportar Relatório",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var stackPanel = new StackPanel { Margin = new Thickness(20) };

            stackPanel.Children.Add(new TextBlock
            {
                Text = "Escolha o formato de exportação:",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15)
            });

            var btnPDF = new Button
            {
                Content = "📄 GERAR PDF E ENVIAR EMAIL",
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                Height = 40,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5722")),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            btnPDF.Click += (s, ev) =>
            {
                dialog.Close();
                GerarPDFeEnviarEmail();
            };

            var btnCSV = new Button
            {
                Content = "📊 EXPORTAR CSV",
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                Height = 40,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                Foreground = Brushes.White
            };

            btnCSV.Click += (s, ev) =>
            {
                dialog.Close();
                ExportToCsv();
            };

            var btnCancelar = new Button
            {
                Content = "❌ CANCELAR",
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(10),
                Height = 40
            };

            btnCancelar.Click += (s, ev) => dialog.Close();

            stackPanel.Children.Add(btnPDF);
            stackPanel.Children.Add(btnCSV);
            stackPanel.Children.Add(btnCancelar);

            dialog.Content = stackPanel;
            dialog.ShowDialog();
        }

        private void ExportToCsv()
        {
            try
            {
                if (_registrosRelatorio == null || !_registrosRelatorio.Any()) return;

                var dataInicio = dpDataInicio.SelectedDate ?? DateTime.Today.AddDays(-30);
                var dataFim = dpDataFim.SelectedDate ?? DateTime.Today;
                var resumo = CalcularResumoRelatorio(_registrosRelatorio);

                string fileName = $"Relatorio_Ponto_{dataInicio:yyyyMMdd}_a_{dataFim:yyyyMMdd}_{DateTime.Now:HHmmss}.csv";
                string filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

                var csv = new StringBuilder();

                csv.AppendLine($"Restaurante: {_config?.RestaurantName ?? "Restaurante"}");
                csv.AppendLine($"Período: {dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}");
                csv.AppendLine($"Data de Exportação: {DateTime.Now:dd/MM/yyyy HH:mm}");
                csv.AppendLine();

                csv.AppendLine("RESUMO DO PERÍODO");
                csv.AppendLine($"Total de Funcionários;{resumo.TotalFuncionarios}");
                csv.AppendLine($"Total de Registros;{resumo.TotalRegistros}");
                csv.AppendLine($"Total de Ausências;{resumo.TotalAusencias}");
                csv.AppendLine($"Percentual de Presença;{resumo.PercentualPresenca:0.0}%");
                csv.AppendLine($"Total Horas Trabalhadas;{resumo.TotalHorasTrabalhadas:hh\\:mm}");
                csv.AppendLine($"Total Atrasos;{resumo.TotalAtrasos:hh\\:mm}");
                csv.AppendLine($"Total Horas Efetivas (Trabalhadas - Atrasos);{resumo.TotalHorasEfetivas:hh\\:mm}");
                csv.AppendLine($"Total Horas Extras;{resumo.TotalHorasExtras:hh\\:mm}");
                csv.AppendLine();

                csv.AppendLine("REGISTROS DETALHADOS");
                csv.AppendLine("Funcionário;Data;Entrada;Saída;Horas Trabalhadas;Atraso;Horas Extras;Turno;Status;Observações");

                foreach (var r in _registrosRelatorio.OrderBy(r => r.Data).ThenBy(r => r.User?.FullName))
                {
                    var nome = r.User?.FullName ?? r.User?.Username ?? "Desconhecido";
                    var data = r.Data.ToString("dd/MM/yyyy");
                    var entrada = r.HoraEntrada?.ToString(@"hh\:mm") ?? "";
                    var saida = r.HoraSaida?.ToString(@"hh\:mm") ?? "";
                    var horasTrab = r.HorasTrabalhadas?.ToString(@"hh\:mm") ?? "";
                    var atraso = r.Atraso?.ToString(@"hh\:mm") ?? "";
                    var horasExtras = r.HorasExtras?.ToString(@"hh\:mm") ?? "";
                    var turno = r.Turno ?? "Integral";
                    var status = r.Ausente ? "Ausente" :
                                (r.HoraEntrada == null ? "Sem Registro" :
                                 r.HoraSaida == null ? "Em Trabalho" : "Finalizado");
                    var obs = r.Observacoes?.Replace(";", ",") ?? "";

                    csv.AppendLine($"{nome};{data};{entrada};{saida};{horasTrab};{atraso};{horasExtras};{turno};{status};{obs}");
                }

                csv.AppendLine();
                csv.AppendLine("TOTAIS FINAIS");
                csv.AppendLine($"Total Horas Trabalhadas;{resumo.TotalHorasTrabalhadas:hh\\:mm}");
                csv.AppendLine($"Total Atrasos;{resumo.TotalAtrasos:hh\\:mm}");
                csv.AppendLine($"Total Horas Efetivas;{resumo.TotalHorasEfetivas:hh\\:mm}");
                csv.AppendLine($"Total Horas Extras;{resumo.TotalHorasExtras:hh\\:mm}");

                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);

                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");

                MessageBox.Show($"✅ Relatório exportado com sucesso!\n\n📄 Arquivo: {fileName}\n📁 Local: {System.IO.Path.GetDirectoryName(filePath)}",
                               "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar CSV: {ex.Message}", "Erro",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region PDF e Email

        private async void GerarPDFeEnviarEmail()
        {
            try
            {
                if (_registrosRelatorio == null || !_registrosRelatorio.Any())
                {
                    MessageBox.Show("Nenhum dado para exportar. Gere um relatório primeiro.", "Aviso",
                                   MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dataInicio = dpDataInicio.SelectedDate ?? DateTime.Today.AddDays(-30);
                var dataFim = dpDataFim.SelectedDate ?? DateTime.Today;
                var resumo = CalcularResumoRelatorio(_registrosRelatorio);

                string reportsFolder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AyGestRest",
                    "Relatórios Ponto");

                Directory.CreateDirectory(reportsFolder);

                string fileName = $"Relatorio_Ponto_{dataInicio:yyyyMMdd}_a_{dataFim:yyyyMMdd}_{DateTime.Now:HHmmss}.pdf";
                string filePath = System.IO.Path.Combine(reportsFolder, fileName);

                byte[] logoBytes = null;
                string logoPath = _config?.LogoPath;

                if (!string.IsNullOrEmpty(logoPath))
                {
                    string fullLogoPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos",
                        logoPath);

                    if (File.Exists(fullLogoPath))
                    {
                        logoBytes = File.ReadAllBytes(fullLogoPath);
                    }
                }

                bool pdfCriado = await Task.Run(() => CriarPDFRelatorioPonto(
                    filePath,
                    _config?.RestaurantName ?? "Restaurante",
                    dataInicio,
                    dataFim,
                    _registrosRelatorio,
                    logoBytes,
                    _currencySymbol,
                    resumo));

                if (!pdfCriado || !File.Exists(filePath))
                {
                    MessageBox.Show("Erro ao gerar PDF.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_config != null && !string.IsNullOrEmpty(_config.EmailAddress) &&
                    !string.IsNullOrEmpty(_config.RecipientEmail) && _config.SendReports)
                {
                    bool emailEnviado = await EnviarEmailRelatorioPonto(filePath, dataInicio, dataFim);

                    if (emailEnviado)
                    {
                        MessageBox.Show($"✅ PDF gerado e enviado por email com sucesso!\n\n📄 Arquivo: {fileName}\n📧 Destinatário: {_config.RecipientEmail}",
                                       "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"⚠️ PDF gerado mas falha ao enviar email.\n\n📄 Arquivo salvo em:\n{filePath}",
                                       "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");

                    MessageBox.Show($"✅ PDF gerado com sucesso!\n\n📄 Arquivo salvo em:\n{filePath}\n\n" +
                                   "Configure o email nas Configurações do Sistema para enviar automaticamente.",
                                   "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CriarPDFRelatorioPonto(string filePath, string restaurantName,
            DateTime dataInicio, DateTime dataFim, List<FuncionarioTurno> registros,
            byte[] logoBytes, string currencySymbol, RelatorioResumo resumo)
        {
            try
            {
                var pdf = new PdfDocument();
                pdf.Info.Title = $"Relatório de Ponto - {dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}";
                pdf.Info.Author = restaurantName;
                pdf.Info.Subject = "Registro de Frequência";

                var primaryColor = XColor.FromArgb(63, 81, 181);
                var lightGray = XColor.FromArgb(250, 250, 250);
                var borderGray = XColor.FromArgb(224, 224, 224);
                var textGray = XColor.FromArgb(117, 117, 117);
                var successColor = XColor.FromArgb(76, 175, 80);
                var warningColor = XColor.FromArgb(255, 152, 0);
                var dangerColor = XColor.FromArgb(244, 67, 54);

                string fontName = "Arial";
                var titleFont = new XFont(fontName, 22, XFontStyle.Bold);
                var companyNameFont = new XFont(fontName, 18, XFontStyle.Bold);
                var companyInfoFont = new XFont(fontName, 9, XFontStyle.Regular);
                var reportTitleFont = new XFont(fontName, 16, XFontStyle.Bold);
                var dateFont = new XFont(fontName, 10, XFontStyle.Regular);
                var headerFont = new XFont(fontName, 10, XFontStyle.Bold);
                var normalFont = new XFont(fontName, 9, XFontStyle.Regular);
                var footerFont = new XFont(fontName, 8, XFontStyle.Italic);

                XGraphics gfx = null;
                PdfPage page = null;
                double y = 0;
                double margin = 40;

                XSize a4Size = PageSizeConverter.ToSize(PdfSharp.PageSize.A4);
                double a4Width = a4Size.Width;
                double a4Height = a4Size.Height;

                void StartNewPage(bool isFirstPage = false)
                {
                    page = pdf.AddPage();
                    page.Width = a4Width;
                    page.Height = a4Height;
                    gfx = XGraphics.FromPdfPage(page);
                    y = margin;

                    if (!isFirstPage)
                    {
                        var miniHeaderFont = new XFont(fontName, 9, XFontStyle.Bold);
                        gfx.DrawString(restaurantName, miniHeaderFont, XBrushes.DarkBlue,
                            new XPoint(margin, margin - 10));
                        gfx.DrawString($"Página {pdf.PageCount}", miniHeaderFont, XBrushes.Gray,
                            new XPoint(page.Width.Point - margin - 50, margin - 10));
                        gfx.DrawLine(new XPen(borderGray, 0.5), margin, margin,
                            page.Width.Point - margin, margin);
                        y = margin + 15;
                    }
                }

                StartNewPage(true);

                // Logo
                if (logoBytes != null && logoBytes.Length > 0)
                {
                    try
                    {
                        using (var ms = new MemoryStream(logoBytes))
                        {
                            var image = XImage.FromStream(ms);
                            double logoWidth = Math.Min(120, image.PixelWidth);
                            double logoHeight = logoWidth * image.PixelHeight / image.PixelWidth;
                            double x = (page.Width.Point - logoWidth) / 2;
                            gfx.DrawImage(image, x, y, logoWidth, logoHeight);
                            y += logoHeight + 15;
                        }
                    }
                    catch
                    {
                        gfx.DrawString("👥", new XFont(fontName, 36, XFontStyle.Bold),
                            XBrushes.Gray, new XPoint(page.Width.Point / 2 - 18, y));
                        y += 45;
                    }
                }
                else
                {
                    gfx.DrawString("👥", new XFont(fontName, 36, XFontStyle.Bold),
                        XBrushes.Gray, new XPoint(page.Width.Point / 2 - 18, y));
                    y += 45;
                }

                // Nome do restaurante
                string companyName = restaurantName.ToUpper();
                var companySize = gfx.MeasureString(companyName, companyNameFont);
                gfx.DrawString(companyName, companyNameFont, XBrushes.Black,
                    new XPoint((page.Width.Point - companySize.Width) / 2, y));
                y += 25;

                // Informações da empresa
                var companyDetails = new StringBuilder();
                if (!string.IsNullOrEmpty(_config?.NIF))
                    companyDetails.Append($"NIF: {_config.NIF} • ");
                if (!string.IsNullOrEmpty(_config?.Address))
                    companyDetails.Append($"Morada: {_config.Address} • ");
                if (!string.IsNullOrEmpty(_config?.Phone))
                    companyDetails.Append($"Tel: {_config.Phone} • ");
                if (!string.IsNullOrEmpty(_config?.Email))
                    companyDetails.Append($"Email: {_config.Email}");

                if (companyDetails.Length == 0)
                    companyDetails.Append("Informações da empresa não configuradas");

                string detailsText = companyDetails.ToString().TrimEnd(' ', '•');
                var detailsSize = gfx.MeasureString(detailsText, companyInfoFont);
                gfx.DrawString(detailsText, companyInfoFont, new XSolidBrush(textGray),
                    new XPoint((page.Width.Point - detailsSize.Width) / 2, y));
                y += 30;

                // Linha decorativa
                gfx.DrawLine(new XPen(primaryColor, 2), margin + 50, y,
                    page.Width.Point - margin - 50, y);
                y += 20;

                // Título do relatório
                string reportTitle = $"RELATÓRIO DE PONTO";
                var titleSize = gfx.MeasureString(reportTitle, reportTitleFont);
                gfx.DrawString(reportTitle, reportTitleFont, new XSolidBrush(primaryColor),
                    new XPoint((page.Width.Point - titleSize.Width) / 2, y));
                y += 25;

                // Período
                string periodText = $"Período: {dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}";
                var periodSize = gfx.MeasureString(periodText, dateFont);
                gfx.DrawString(periodText, dateFont, XBrushes.Black,
                    new XPoint((page.Width.Point - periodSize.Width) / 2, y));
                y += 20;

                // Data de geração
                string generatedText = $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}";
                var generatedSize = gfx.MeasureString(generatedText, dateFont);
                gfx.DrawString(generatedText, dateFont, new XSolidBrush(textGray),
                    new XPoint((page.Width.Point - generatedSize.Width) / 2, y));
                y += 30;

                // RESUMO ESTATÍSTICO
                int totalDias = (int)(dataFim - dataInicio).TotalDays + 1;
                int totalFuncionarios = registros.Select(r => r.UserId).Distinct().Count();
                int totalRegistros = registros.Count;
                int totalAusencias = registros.Count(r => r.Ausente);
                int totalPresencas = totalRegistros - totalAusencias;
                double percentualPresenca = totalRegistros > 0 ? (double)totalPresencas / totalRegistros * 100 : 0;

                // Resumo
                double summaryWidth = page.Width.Point - 2 * margin;
                double summaryX = margin;
                double summaryY = y;

                gfx.DrawRectangle(new XSolidBrush(lightGray), summaryX, summaryY, summaryWidth, 140);
                gfx.DrawRectangle(new XPen(borderGray, 0.5), summaryX, summaryY, summaryWidth, 140);

                gfx.DrawString("📊 RESUMO DO PERÍODO", new XFont(fontName, 12, XFontStyle.Bold),
                    new XSolidBrush(primaryColor), summaryX + 10, summaryY + 20);

                double colWidth = summaryWidth / 3;

                // Coluna 1
                double col1X = summaryX + 10;
                gfx.DrawString("Funcionários:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col1X, summaryY + 45);
                gfx.DrawString(totalFuncionarios.ToString("N0"), new XFont(fontName, 10, XFontStyle.Regular),
                    XBrushes.Black, col1X + 100, summaryY + 45);

                gfx.DrawString("Dias:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col1X, summaryY + 65);
                gfx.DrawString(totalDias.ToString("N0"), new XFont(fontName, 10, XFontStyle.Regular),
                    XBrushes.Black, col1X + 100, summaryY + 65);

                gfx.DrawString("Registros:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col1X, summaryY + 85);
                gfx.DrawString(totalRegistros.ToString("N0"), new XFont(fontName, 10, XFontStyle.Regular),
                    XBrushes.Black, col1X + 100, summaryY + 85);

                // Coluna 2
                double col2X = summaryX + colWidth + 10;
                gfx.DrawString("Presenças:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col2X, summaryY + 45);
                gfx.DrawString(totalPresencas.ToString("N0"), new XFont(fontName, 10, XFontStyle.Regular),
                    new XSolidBrush(successColor), col2X + 80, summaryY + 45);

                gfx.DrawString("Ausências:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col2X, summaryY + 65);
                gfx.DrawString(totalAusencias.ToString("N0"), new XFont(fontName, 10, XFontStyle.Regular),
                    new XSolidBrush(dangerColor), col2X + 80, summaryY + 65);

                gfx.DrawString("Percentual:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col2X, summaryY + 85);
                gfx.DrawString($"{percentualPresenca:0.0}%", new XFont(fontName, 10, XFontStyle.Regular),
                    XBrushes.Black, col2X + 80, summaryY + 85);

                // Coluna 3
                double col3X = summaryX + 2 * colWidth + 10;
                gfx.DrawString("Horas Trab.:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col3X, summaryY + 45);
                gfx.DrawString($"{resumo.TotalHorasTrabalhadas:hh\\:mm}", new XFont(fontName, 10, XFontStyle.Regular),
                    XBrushes.Black, col3X + 90, summaryY + 45);

                gfx.DrawString("Atrasos:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col3X, summaryY + 65);
                gfx.DrawString($"{resumo.TotalAtrasos:hh\\:mm}", new XFont(fontName, 10, XFontStyle.Regular),
                    new XSolidBrush(warningColor), col3X + 90, summaryY + 65);

                gfx.DrawString("⭐ Horas Efetivas:", new XFont(fontName, 10, XFontStyle.Bold),
                    new XSolidBrush(successColor), col3X, summaryY + 85);
                gfx.DrawString($"{resumo.TotalHorasEfetivas:hh\\:mm}", new XFont(fontName, 10, XFontStyle.Regular),
                    new XSolidBrush(successColor), col3X + 90, summaryY + 85);

                gfx.DrawString("Horas Extras:", new XFont(fontName, 10, XFontStyle.Bold),
                    XBrushes.Black, col3X, summaryY + 105);
                gfx.DrawString($"{resumo.TotalHorasExtras:hh\\:mm}", new XFont(fontName, 10, XFontStyle.Regular),
                    new XSolidBrush(primaryColor), col3X + 90, summaryY + 105);

                y += 160;

                // TABELA DETALHADA
                gfx.DrawString("📋 REGISTROS DETALHADOS", new XFont(fontName, 12, XFontStyle.Bold),
                    new XSolidBrush(primaryColor), margin, y);
                y += 20;

                double[] colWidths = new double[]
                {
                    25, 100, 80, 60, 60, 70, 60, 60, 80, 50
                };

                double tableWidth = colWidths.Sum();

                double xPos = margin;
                for (int i = 0; i < colWidths.Length; i++)
                {
                    string headerText = "";
                    switch (i)
                    {
                        case 0: headerText = "#"; break;
                        case 1: headerText = "Funcionário"; break;
                        case 2: headerText = "Data"; break;
                        case 3: headerText = "Entrada"; break;
                        case 4: headerText = "Saída"; break;
                        case 5: headerText = "H.Trab"; break;
                        case 6: headerText = "Atraso"; break;
                        case 7: headerText = "H.Extras"; break;
                        case 8: headerText = "Turno"; break;
                        case 9: headerText = "Status"; break;
                    }

                    gfx.DrawRectangle(new XSolidBrush(primaryColor), xPos, y, colWidths[i], 25);
                    gfx.DrawString(headerText, headerFont, XBrushes.White,
                        new XPoint(xPos + 5, y + 7));

                    xPos += colWidths[i];
                }
                y += 25;

                int linhaNum = 1;
                bool alternate = false;

                foreach (var registro in registros.OrderBy(r => r.Data).ThenBy(r => r.User?.FullName))
                {
                    if (y > page.Height.Point - margin - 30)
                    {
                        string pageNum = $"Página {pdf.PageCount}";
                        var pageNumSize = gfx.MeasureString(pageNum, footerFont);
                        gfx.DrawString(pageNum, footerFont, XBrushes.Gray,
                            new XPoint((page.Width.Point - pageNumSize.Width) / 2, page.Height.Point - 20));

                        StartNewPage();

                        xPos = margin;
                        for (int i = 0; i < colWidths.Length; i++)
                        {
                            string headerText = "";
                            switch (i)
                            {
                                case 0: headerText = "#"; break;
                                case 1: headerText = "Funcionário"; break;
                                case 2: headerText = "Data"; break;
                                case 3: headerText = "Entrada"; break;
                                case 4: headerText = "Saída"; break;
                                case 5: headerText = "H.Trab"; break;
                                case 6: headerText = "Atraso"; break;
                                case 7: headerText = "H.Extras"; break;
                                case 8: headerText = "Turno"; break;
                                case 9: headerText = "Status"; break;
                            }

                            gfx.DrawRectangle(new XSolidBrush(primaryColor), xPos, y, colWidths[i], 25);
                            gfx.DrawString(headerText, headerFont, XBrushes.White,
                                new XPoint(xPos + 5, y + 7));

                            xPos += colWidths[i];
                        }
                        y += 25;
                    }

                    var fillBrush = alternate ? new XSolidBrush(lightGray) : XBrushes.White;
                    gfx.DrawRectangle(fillBrush, margin, y, tableWidth, 20);
                    gfx.DrawLine(new XPen(borderGray, 0.2), margin, y, margin + tableWidth, y);

                    xPos = margin;
                    for (int i = 0; i < colWidths.Length; i++)
                    {
                        string cellText = "";
                        XBrush textBrush = XBrushes.Black;

                        switch (i)
                        {
                            case 0: cellText = linhaNum.ToString(); break;
                            case 1:
                                cellText = registro.User?.FullName ?? registro.User?.Username ?? "-";
                                if (cellText.Length > 15) cellText = cellText.Substring(0, 12) + "..."; break;
                            case 2: cellText = registro.Data.ToString("dd/MM/yy"); break;
                            case 3: cellText = registro.HoraEntrada?.ToString(@"hh\:mm") ?? "--:--"; break;
                            case 4: cellText = registro.HoraSaida?.ToString(@"hh\:mm") ?? "--:--"; break;
                            case 5: cellText = registro.HorasTrabalhadas?.ToString(@"hh\:mm") ?? "--:--"; break;
                            case 6:
                                cellText = registro.Atraso?.ToString(@"hh\:mm") ?? "--:--";
                                if (registro.Atraso > TimeSpan.Zero) textBrush = new XSolidBrush(warningColor); break;
                            case 7:
                                cellText = registro.HorasExtras?.ToString(@"hh\:mm") ?? "--:--";
                                if (registro.HorasExtras > TimeSpan.Zero) textBrush = new XSolidBrush(successColor); break;
                            case 8: cellText = registro.Turno ?? "-"; break;
                            case 9:
                                if (registro.Ausente) { cellText = "Ausente"; textBrush = new XSolidBrush(dangerColor); }
                                else if (registro.HoraEntrada == null) { cellText = "Aguardando"; textBrush = new XSolidBrush(warningColor); }
                                else if (registro.HoraSaida == null) { cellText = "Em Trabalho"; textBrush = new XSolidBrush(successColor); }
                                else { cellText = "Finalizado"; textBrush = new XSolidBrush(primaryColor); }
                                break;
                        }

                        gfx.DrawString(cellText, normalFont, textBrush, new XPoint(xPos + 5, y + 5));

                        if (i < colWidths.Length - 1)
                        {
                            gfx.DrawLine(new XPen(borderGray, 0.2), xPos + colWidths[i], y, xPos + colWidths[i], y + 20);
                        }

                        xPos += colWidths[i];
                    }

                    y += 20;
                    linhaNum++;
                    alternate = !alternate;
                }

                y += 10;
                gfx.DrawLine(new XPen(borderGray, 1), margin, y, page.Width.Point - margin, y);
                y += 15;

                string footerText = $"{restaurantName} • Relatório de Ponto • {DateTime.Now:dd/MM/yyyy HH:mm} • Página {pdf.PageCount}";
                var footerSize = gfx.MeasureString(footerText, footerFont);
                gfx.DrawString(footerText, footerFont, XBrushes.Gray,
                    new XPoint((page.Width.Point - footerSize.Width) / 2, page.Height.Point - 20));

                pdf.Save(filePath);
                pdf.Close();

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao criar PDF: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> EnviarEmailRelatorioPonto(string pdfPath, DateTime dataInicio, DateTime dataFim)
        {
            try
            {
                if (_config == null || !_config.SendReports ||
                    string.IsNullOrEmpty(_config.EmailAddress) ||
                    string.IsNullOrEmpty(_config.RecipientEmail))
                {
                    return false;
                }

                if (!File.Exists(pdfPath))
                    return false;

                var resumo = CalcularResumoRelatorio(_registrosRelatorio);

                string subject = $"📋 Relatório de Ponto - {_config.RestaurantName} - {dataInicio:dd/MM} a {dataFim:dd/MM}";

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .header {{ background: linear-gradient(135deg, #3f51b5 0%, #2196f3 100%); color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }}
        .success {{ color: #4CAF50; }}
        .warning {{ color: #FF9800; }}
        .danger {{ color: #F44336; }}
        .info {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #3f51b5; margin-bottom: 20px; }}
        table {{ border-collapse: collapse; width: 100%; margin-bottom: 20px; }}
        th {{ background: #3f51b5; color: white; padding: 10px; text-align: left; }}
        td {{ padding: 8px; border-bottom: 1px solid #ddd; }}
    </style>
</head>
<body>
    <div class='header'>
        <h2>📋 RELATÓRIO DE PONTO</h2>
        <p>{_config.RestaurantName} - {DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>

    <div class='info'>
        <h3>📊 RESUMO DO PERÍODO</h3>
        <table>
            <tr><td><strong>Período:</strong></td><td>{dataInicio:dd/MM/yyyy} a {dataFim:dd/MM/yyyy}</td></tr>
            <tr><td><strong>Total de Funcionários:</strong></td><td>{resumo.TotalFuncionarios}</td></tr>
            <tr><td><strong>Total de Registros:</strong></td><td>{resumo.TotalRegistros}</td></tr>
            <tr><td><strong>Ausências:</strong></td><td><span class='danger'>{resumo.TotalAusencias}</span></td></tr>
            <tr><td><strong>Percentual de Presença:</strong></td><td>{resumo.PercentualPresenca:0.0}%</td></tr>
            <tr><td><strong>Total Horas Trabalhadas:</strong></td><td><span class='success'>{resumo.TotalHorasTrabalhadas:hh\\:mm}</span></td></tr>
            <tr><td><strong>Total Atrasos:</strong></td><td><span class='warning'>{resumo.TotalAtrasos:hh\\:mm}</span></td></tr>
            <tr><td><strong>⭐ Total Horas Efetivas:</strong></td><td><span class='success'>{resumo.TotalHorasEfetivas:hh\\:mm}</span></td></tr>
            <tr><td><strong>Total Horas Extras:</strong></td><td>{resumo.TotalHorasExtras:hh\\:mm}</td></tr>
        </table>
    </div>

    <p><strong>Anexo:</strong> Relatório completo em PDF com todos os registros detalhados.</p>

    <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666;'>
        <p><strong>{_config.RestaurantName}</strong></p>
        <p>Sistema de Gestão AyGestRest</p>
        <p>{DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>
</body>
</html>";

                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(_config.EmailAddress, _config.RestaurantName ?? "AyGestRest");
                    message.To.Add(_config.RecipientEmail);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;

                    var attachment = new Attachment(pdfPath);
                    message.Attachments.Add(attachment);

                    using (var smtpClient = new SmtpClient())
                    {
                        smtpClient.Host = string.IsNullOrEmpty(_config.SMTPServer) ? "smtp.gmail.com" : _config.SMTPServer;
                        smtpClient.Port = _config.SMTPPort > 0 ? _config.SMTPPort : 587;
                        smtpClient.EnableSsl = _config.UseSSL;
                        smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                        smtpClient.UseDefaultCredentials = false;
                        smtpClient.Credentials = new NetworkCredential(_config.EmailAddress, _config.EmailPassword);
                        smtpClient.Timeout = 30000;

                        ServicePointManager.SecurityProtocol =
                            SecurityProtocolType.Tls12 |
                            SecurityProtocolType.Tls11 |
                            SecurityProtocolType.Tls;

                        await Task.Run(() => smtpClient.Send(message));
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao enviar email: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}