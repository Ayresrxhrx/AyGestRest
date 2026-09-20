using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AyGestRest.Views
{
    public partial class ReportsControl : UserControl
    {
        private AyGestRestContext _context;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _currentReportType;
        private Dictionary<string, List<object>> _cachedReports = new Dictionary<string, List<object>>();
        private bool _isSidebarCollapsed = false;
        private RestaurantConfig _restaurantConfig;
        private bool _isGenerating = false;
        private readonly SemaphoreSlim _reportGenerationLock = new SemaphoreSlim(1, 1);
        private int _reportGenerationVersion = 0;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _isReportGenerated = false;
        private object _currentReportData = null;

        public class ReportItem
        {
            public string Icon { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string ReportType { get; set; }
            public System.Windows.Media.Color Color { get; set; }
        }

        public class StatCardData
        {
            public string Icon { get; set; }
            public string Title { get; set; }
            public string Value { get; set; }
            public string Subtitle { get; set; }
            public System.Windows.Media.Color Color { get; set; }
            public double Change { get; set; }
        }

        public ReportsControl()
        {
            try
            {
                InitializeComponent();
                InitializeAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Erro na inicialização: {ex.Message}");
            }
        }

        private async void InitializeAsync()
        {
            try
            {
                _context = new AyGestRestContext();
                _context.Database.EnsureCreated();

                _restaurantConfig = await _context.RestaurantConfigs.FirstOrDefaultAsync();
                if (_restaurantConfig == null)
                {
                    _restaurantConfig = new RestaurantConfig();
                }

                LoadReportList();
                InitializeControls();

                this.SizeChanged += ReportsControl_SizeChanged;
                SetupKeyboardShortcuts();

                await UpdateGeneralStatsAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Erro na inicialização assíncrona: {ex.Message}");
            }
        }

        private void LoadReportList()
        {
            var reports = new List<ReportItem>
            {
                new ReportItem { Icon = "💰", Title = "Vendas Diárias", Description = "Análise detalhada de vendas por período", ReportType = "DailySales", Color = Colors.Green },
                new ReportItem { Icon = "📦", Title = "Produtos Mais Vendidos", Description = "Ranking dos produtos mais populares", ReportType = "TopProducts", Color = Colors.Blue },
                new ReportItem { Icon = "👥", Title = "Clientes Ativos", Description = "Clientes com maior frequência e ticket", ReportType = "ActiveCustomers", Color = Colors.Purple },
                new ReportItem { Icon = "🛒", Title = "Pedidos por Mesa", Description = "Desempenho das mesas do restaurante", ReportType = "TableOrders", Color = Colors.Orange },
                new ReportItem { Icon = "📊", Title = "Faturamento por Período", Description = "Evolução do faturamento ao longo do tempo", ReportType = "RevenuePeriod", Color = Colors.Red },
                new ReportItem { Icon = "💳", Title = "Pagamentos", Description = "Distribuição dos métodos de pagamento", ReportType = "Payments", Color = Colors.Teal },
                new ReportItem { Icon = "👨‍🍳", Title = "Itens de Cozinha", Description = "Desempenho dos itens preparados", ReportType = "KitchenItems", Color = Colors.Brown },
                new ReportItem { Icon = "📋", Title = "Reservas", Description = "Análise das reservas e ocupação", ReportType = "Reservations", Color = Colors.Pink },
                new ReportItem { Icon = "🚚", Title = "Entregas", Description = "Eficiência do serviço de delivery", ReportType = "Deliveries", Color = Colors.Cyan },
                new ReportItem { Icon = "👤", Title = "Vendas por Utilizador", Description = "Desempenho da equipe de vendas", ReportType = "UserSales", Color = Colors.Indigo }
            };

            lstReports.ItemsSource = reports;
        }

        private void InitializeControls()
        {
            try
            {
                dpStart.SelectedDate = DateTime.Today;
                dpEnd.SelectedDate = DateTime.Today;
                _startDate = DateTime.Today;
                _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);

                if (cmbPeriod.Items.Count > 0)
                {
                    cmbPeriod.SelectedIndex = 0;
                }

                UpdateDateRangeText();
                ShowEmptyState();

                dgReportData.EnableRowVirtualization = true;
                dgReportData.EnableColumnVirtualization = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro em InitializeControls: {ex.Message}");
            }
        }

        private void UpdateDateRangeText()
        {
            try
            {
                if (txtDateRange == null) return;

                if (_startDate.Date == _endDate.Date)
                    txtDateRange.Text = $"Hoje ({_startDate:dd/MM/yyyy})";
                else if (_startDate.Month == _endDate.Month && _startDate.Year == _endDate.Year)
                    txtDateRange.Text = $"{_startDate:dd} a {_endDate:dd/MM/yyyy}";
                else
                    txtDateRange.Text = $"{_startDate:dd/MM} a {_endDate:dd/MM/yyyy}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro em UpdateDateRangeText: {ex.Message}");
            }
        }

        private void ShowEmptyState()
        {
            dgReportData.Visibility = Visibility.Collapsed;
            pnlEmpty.Visibility = Visibility.Visible;
            pnlSummary.Visibility = Visibility.Collapsed;
            txtReportTitle.Text = "Selecione um relatório";
            txtTotalRecords.Text = "0 registros";
            pnlStats.Children.Clear();
            _isReportGenerated = false;
            _currentReportData = null;
        }

        // ============================================
        // MÉTODOS DE ESTATÍSTICAS
        // ============================================

        private async Task UpdateGeneralStatsAsync()
        {
            try
            {
                var stats = await GetGeneralStatsAsync();
                await Dispatcher.InvokeAsync(() => UpdateStatCards(stats));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar estatísticas: {ex.Message}");
            }
        }

        private async Task<List<StatCardData>> GetGeneralStatsAsync()
        {
            var stats = new List<StatCardData>();

            try
            {
                var paymentsInPeriod = await _context.Payments
                    .Where(p => p.PaymentDate >= _startDate && p.PaymentDate <= _endDate)
                    .ToListAsync();

                var currentPeriodSales = paymentsInPeriod.Sum(p => p.Amount);

                var previousPeriodStart = _startDate.AddDays(-(int)(_endDate - _startDate).TotalDays - 1);
                var previousPeriodEnd = _startDate.AddSeconds(-1);

                var paymentsPreviousPeriod = await _context.Payments
                    .Where(p => p.PaymentDate >= previousPeriodStart && p.PaymentDate <= previousPeriodEnd)
                    .ToListAsync();

                var previousPeriodSales = paymentsPreviousPeriod.Sum(p => p.Amount);

                var ordersInPeriod = await _context.Orders
                    .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate)
                    .ToListAsync();

                int currentOrders = ordersInPeriod.Count;

                var ordersPreviousPeriod = await _context.Orders
                    .Where(o => o.OpenDate >= previousPeriodStart && o.OpenDate <= previousPeriodEnd)
                    .ToListAsync();

                int previousOrders = ordersPreviousPeriod.Count;

                var activeCustomers = ordersInPeriod
                    .Select(o => o.ClienteNome)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .Count();

                var ticketMedio = currentOrders > 0 ? currentPeriodSales / currentOrders : 0;

                var salesChange = previousPeriodSales > 0 ? (double)((currentPeriodSales - previousPeriodSales) / previousPeriodSales * 100) : 0;
                var ordersChange = previousOrders > 0 ? ((double)(currentOrders - previousOrders) / previousOrders * 100) : 0;

                string currencySymbol = _restaurantConfig?.CurrencySymbol ?? "MT";

                // Calcular totais por método de pagamento
                var totalDinheiro = paymentsInPeriod.Where(p => p.Type == PaymentType.Dinheiro).Sum(p => p.Amount);
                var totalCartao = paymentsInPeriod.Where(p => p.Type == PaymentType.CartaoCredito || p.Type == PaymentType.CartaoDebito || p.Type == PaymentType.Cartao || p.Type == PaymentType.Multibanco).Sum(p => p.Amount);
                var totalMBWay = paymentsInPeriod.Where(p => p.Type == PaymentType.MBWay).Sum(p => p.Amount);
                var totalEmola = paymentsInPeriod.Where(p => p.Type == PaymentType.Emola).Sum(p => p.Amount);
                var totalMPesa = paymentsInPeriod.Where(p => p.Type == PaymentType.MPesa).Sum(p => p.Amount);
                var totalPos = paymentsInPeriod.Where(p => p.Type == PaymentType.PosNedBank || p.Type == PaymentType.PosMoza || p.Type == PaymentType.Ponto24).Sum(p => p.Amount);
                var totalOutros = paymentsInPeriod.Where(p => p.Type == PaymentType.Outro).Sum(p => p.Amount);

                stats.Add(new StatCardData { Icon = "💰", Title = "Vendas Totais", Value = $"{currencySymbol} {currentPeriodSales:N2}", Subtitle = "Período atual", Color = Colors.Green, Change = salesChange });
                stats.Add(new StatCardData { Icon = "📦", Title = "Pedidos", Value = currentOrders.ToString(), Subtitle = "Total de pedidos", Color = Colors.Blue, Change = ordersChange });
                stats.Add(new StatCardData { Icon = "👥", Title = "Clientes Ativos", Value = activeCustomers.ToString(), Subtitle = "Clientes únicos", Color = Colors.Purple, Change = 0 });
                stats.Add(new StatCardData { Icon = "📈", Title = "Ticket Médio", Value = $"{currencySymbol} {ticketMedio:N2}", Subtitle = "Por pedido", Color = Colors.Orange, Change = 0 });

                // Cards de métodos de pagamento
                if (totalDinheiro > 0)
                    stats.Add(new StatCardData { Icon = "💰", Title = "Dinheiro", Value = $"{currencySymbol} {totalDinheiro:N2}", Subtitle = "Total", Color = Colors.Green, Change = 0 });

                if (totalCartao > 0)
                    stats.Add(new StatCardData { Icon = "💳", Title = "Cartão", Value = $"{currencySymbol} {totalCartao:N2}", Subtitle = "Total", Color = Colors.Blue, Change = 0 });

                if (totalMBWay > 0)
                    stats.Add(new StatCardData { Icon = "📱", Title = "MBWay", Value = $"{currencySymbol} {totalMBWay:N2}", Subtitle = "Total", Color = Colors.Purple, Change = 0 });

                if (totalEmola > 0)
                    stats.Add(new StatCardData { Icon = "📲", Title = "Emola", Value = $"{currencySymbol} {totalEmola:N2}", Subtitle = "Total", Color = Colors.HotPink, Change = 0 });

                if (totalMPesa > 0)
                    stats.Add(new StatCardData { Icon = "📲", Title = "M-Pesa", Value = $"{currencySymbol} {totalMPesa:N2}", Subtitle = "Total", Color = Colors.Teal, Change = 0 });

                if (totalPos > 0)
                    stats.Add(new StatCardData { Icon = "🏦", Title = "POS", Value = $"{currencySymbol} {totalPos:N2}", Subtitle = "Total", Color = Colors.Indigo, Change = 0 });

                if (totalOutros > 0)
                    stats.Add(new StatCardData { Icon = "🔄", Title = "Outros", Value = $"{currencySymbol} {totalOutros:N2}", Subtitle = "Total", Color = Colors.Gray, Change = 0 });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao obter estatísticas: {ex.Message}");
                return new List<StatCardData>();
            }

            return stats;
        }

        private void UpdateStatCards(List<StatCardData> stats)
        {
            pnlStats.Children.Clear();
            foreach (var stat in stats)
            {
                var card = CreateStatCard(stat);
                pnlStats.Children.Add(card);
            }
        }

        private Border CreateStatCard(StatCardData stat)
        {
            var card = new Border
            {
                Style = (Style)FindResource("StatCard"),
                Background = Brushes.White,
                MinWidth = 140,
                MaxWidth = 200
            };

            var stackPanel = new StackPanel { Margin = new Thickness(4) };

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            headerStack.Children.Add(new TextBlock { Text = stat.Icon, FontSize = 20, Margin = new Thickness(0, 0, 8, 0) });
            headerStack.Children.Add(new TextBlock
            {
                Text = stat.Title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Gray),
                VerticalAlignment = System.Windows.VerticalAlignment.Center  // CORRIGIDO: usar o namespace completo
            });
            stackPanel.Children.Add(headerStack);

            stackPanel.Children.Add(new TextBlock
            {
                Text = stat.Value,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(stat.Color),
                Margin = new Thickness(0, 4, 0, 0)
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = stat.Subtitle,
                FontSize = 10,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(0, 2, 0, 0)
            });

            if (stat.Change != 0)
            {
                var changeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                var changeIcon = stat.Change > 0 ? "📈" : "📉";
                var changeColor = stat.Change > 0 ? Colors.Green : Colors.Red;
                changeStack.Children.Add(new TextBlock { Text = changeIcon, FontSize = 10, Margin = new Thickness(0, 0, 4, 0) });
                changeStack.Children.Add(new TextBlock { Text = $"{Math.Abs(stat.Change):0.0}%", FontSize = 10, Foreground = new SolidColorBrush(changeColor) });
                stackPanel.Children.Add(changeStack);
            }

            card.Child = stackPanel;
            return card;
        }

        // ============================================
        // MÉTODO DE GERAÇÃO DE RELATÓRIO - CORRIGIDO
        // ============================================
        private async Task UpdateFooterSummaryAsync()
        {
            try
            {
                var paymentsInPeriod = await _context.Payments
                    .Where(p => p.PaymentDate >= _startDate && p.PaymentDate <= _endDate)
                    .ToListAsync();

                string currencySymbol = _restaurantConfig?.CurrencySymbol ?? "MT";

                var totalDinheiro = paymentsInPeriod.Where(p => p.Type == PaymentType.Dinheiro).Sum(p => p.Amount);
                var totalCartao = paymentsInPeriod.Where(p => p.Type == PaymentType.CartaoCredito || p.Type == PaymentType.CartaoDebito || p.Type == PaymentType.Cartao || p.Type == PaymentType.Multibanco).Sum(p => p.Amount);
                var totalMBWay = paymentsInPeriod.Where(p => p.Type == PaymentType.MBWay).Sum(p => p.Amount);
                var totalEmola = paymentsInPeriod.Where(p => p.Type == PaymentType.Emola).Sum(p => p.Amount);
                var totalMPesa = paymentsInPeriod.Where(p => p.Type == PaymentType.MPesa).Sum(p => p.Amount);
                var totalPOS = paymentsInPeriod.Where(p => p.Type == PaymentType.PosNedBank || p.Type == PaymentType.PosMoza || p.Type == PaymentType.Ponto24).Sum(p => p.Amount);
                var totalGeral = paymentsInPeriod.Sum(p => p.Amount);

                await Dispatcher.InvokeAsync(() =>
                {
                    txtTotalCash.Text = $"{currencySymbol} {totalDinheiro:N2}";
                    txtTotalCard.Text = $"{currencySymbol} {totalCartao:N2}";
                    txtTotalMBWay.Text = $"{currencySymbol} {totalMBWay:N2}";
                    txtTotalEmola.Text = $"{currencySymbol} {totalEmola:N2}";
                    txtTotalMPesa.Text = $"{currencySymbol} {totalMPesa:N2}";
                    txtTotalPOS.Text = $"{currencySymbol} {totalPOS:N2}";
                    txtTotalGeral.Text = $"{currencySymbol} {totalGeral:N2}";
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar rodapé: {ex.Message}");
            }
        }
        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating)
            {
                ShowMessage("Aguarde, o relatório está sendo gerado...", MessageType.Info);
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentReportType))
            {
                ShowMessage("Selecione um tipo de relatório primeiro.", MessageType.Warning);
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            await GenerateSelectedReportAsync(_cts.Token);
        }

        private async Task GenerateSelectedReportAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_currentReportType))
                return;

            if (!await _reportGenerationLock.WaitAsync(0, cancellationToken))
            {
                ShowMessage("Aguarde, o relatório está sendo gerado...", MessageType.Info);
                return;
            }

            int generationVersion = Interlocked.Increment(ref _reportGenerationVersion);

            try
            {
                _isGenerating = true;
                SetLoadingState(true);

                // Limpar UI apenas uma vez, no início
                await Dispatcher.InvokeAsync(() =>
                {
                    dgReportData.ItemsSource = null;
                    dgReportData.Columns.Clear();
                    canvasChart.Children.Clear();
                    pnlSummary.Visibility = Visibility.Collapsed;

                    // Mostrar loading state no DataGrid
                    dgReportData.Visibility = Visibility.Visible;
                    pnlEmpty.Visibility = Visibility.Collapsed;
                    txtReportTitle.Text = "Carregando...";
                    txtTotalRecords.Text = "0 registros";
                });

                cancellationToken.ThrowIfCancellationRequested();

                // Gerar dados do relatório
                var reportData = await GenerateReportDataAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (generationVersion != _reportGenerationVersion)
                    return;

                // Armazenar dados localmente
                _currentReportData = reportData;

                // Atualizar UI com os dados
                await Dispatcher.InvokeAsync(() =>
                {
                    if (reportData != null && reportData.Count > 0)
                    {
                        ConfigureDataGridForReportType();
                        dgReportData.ItemsSource = reportData;
                        dgReportData.Visibility = Visibility.Visible;
                        pnlEmpty.Visibility = Visibility.Collapsed;
                        txtTotalRecords.Text = $"{reportData.Count} registros";
                        _isReportGenerated = true;

                        // Atualizar título do relatório
                        txtReportTitle.Text = GetReportTitle(_currentReportType);
                        txtBreadcrumb.Text = $"Dashboard / Relatórios / {GetReportTitle(_currentReportType)}";
                    }
                    else
                    {
                        dgReportData.Visibility = Visibility.Collapsed;
                        pnlEmpty.Visibility = Visibility.Visible;
                        txtTotalRecords.Text = "0 registros";
                        _isReportGenerated = false;
                    }
                });

                cancellationToken.ThrowIfCancellationRequested();

                // Atualizar estatísticas e gráfico em segundo plano
                await Task.WhenAll(
                    UpdateGeneralStatsAsync(),
                    DrawModernChartAsync()
                );

                if (generationVersion == _reportGenerationVersion && _isReportGenerated)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShowMessage($"✅ Relatório '{_currentReportType}' gerado com sucesso!", MessageType.Success);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Geração de relatório cancelada.");
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowMessage("Geração de relatório cancelada.", MessageType.Info);
                    // Restaurar estado anterior se houver dados em cache
                    RestoreReportData();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao gerar relatório: {ex}");
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowError($"Erro ao gerar relatório: {ex.Message}");
                    ShowEmptyState();
                    canvasChart.Children.Clear();
                });
            }
            finally
            {
                _isGenerating = false;
                SetLoadingState(false);
                _reportGenerationLock.Release();
            }
        }

        private void RestoreReportData()
        {
            if (_currentReportData != null && _isReportGenerated)
            {
                var data = _currentReportData as System.Collections.IEnumerable;
                if (data != null)
                {
                    ConfigureDataGridForReportType();
                    dgReportData.ItemsSource = data;
                    dgReportData.Visibility = Visibility.Visible;
                    pnlEmpty.Visibility = Visibility.Collapsed;
                }
            }
        }

        private string GetReportTitle(string reportType)
        {
            return reportType switch
            {
                "DailySales" => "Vendas Diárias",
                "TopProducts" => "Produtos Mais Vendidos",
                "ActiveCustomers" => "Clientes Ativos",
                "TableOrders" => "Pedidos por Mesa",
                "RevenuePeriod" => "Faturamento por Período",
                "Payments" => "Pagamentos",
                "KitchenItems" => "Itens de Cozinha",
                "Reservations" => "Reservas",
                "Deliveries" => "Entregas",
                "UserSales" => "Vendas por Utilizador",
                _ => "Relatório"
            };
        }

        // ============================================
        // GERAÇÃO DE DADOS DO RELATÓRIO
        // ============================================

        private async Task<List<object>> GenerateReportDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                List<object> result = new List<object>();

                switch (_currentReportType)
                {
                    case "DailySales":
                        result = await GenerateDailySalesDataAsync(cancellationToken);
                        break;
                    case "TopProducts":
                        result = await GenerateTopProductsDataAsync(cancellationToken);
                        break;
                    case "ActiveCustomers":
                        result = await GenerateActiveCustomersDataAsync(cancellationToken);
                        break;
                    case "TableOrders":
                        result = await GenerateTableOrdersDataAsync(cancellationToken);
                        break;
                    case "RevenuePeriod":
                        result = await GenerateRevenuePeriodDataAsync(cancellationToken);
                        break;
                    case "Payments":
                        result = await GeneratePaymentsDataAsync(cancellationToken);
                        break;
                    case "KitchenItems":
                        result = await GenerateKitchenItemsDataAsync(cancellationToken);
                        break;
                    case "Reservations":
                        result = await GenerateReservationsDataAsync(cancellationToken);
                        break;
                    case "Deliveries":
                        result = await GenerateDeliveriesDataAsync(cancellationToken);
                        break;
                    case "UserSales":
                        result = await GenerateUserSalesDataAsync(cancellationToken);
                        break;
                    default:
                        result = new List<object>();
                        break;
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro em GenerateReportDataAsync: {ex.Message}");
                throw;
            }
        }

        private void ConfigureDataGridForReportType()
        {
            dgReportData.Columns.Clear();

            switch (_currentReportType)
            {
                case "DailySales":
                    ConfigureDataGridForDailySales();
                    break;
                case "TopProducts":
                    ConfigureDataGridForTopProducts();
                    break;
                case "ActiveCustomers":
                    ConfigureDataGridForActiveCustomers();
                    break;
                case "TableOrders":
                    ConfigureDataGridForTableOrders();
                    break;
                case "RevenuePeriod":
                    ConfigureDataGridForRevenuePeriod();
                    break;
                case "Payments":
                    ConfigureDataGridForPayments();
                    break;
                case "KitchenItems":
                    ConfigureDataGridForKitchenItems();
                    break;
                case "Reservations":
                    ConfigureDataGridForReservations();
                    break;
                case "Deliveries":
                    ConfigureDataGridForDeliveries();
                    break;
                case "UserSales":
                    ConfigureDataGridForUserSales();
                    break;
            }
        }

        // ============================================
        // RELATÓRIO DE VENDAS DIÁRIAS
        // ============================================

        private async Task<List<object>> GenerateDailySalesDataAsync(CancellationToken cancellationToken)
        {
            var orders = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .Include(o => o.User)
                .Include(o => o.Table)
                .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate)
                .OrderByDescending(o => o.OpenDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!orders.Any())
            {
                await Dispatcher.InvokeAsync(() =>
                    ShowMessage("Nenhum pedido encontrado para o período selecionado.", MessageType.Info));
                return new List<object>();
            }

            var reportData = orders.Select(o => new
            {
                Pedido = o.Id,
                Data = o.OpenDate,
                Hora = o.OpenDate.ToString("HH:mm"),
                Mesa = o.Table?.Number ?? "Balcão",
                Cliente = o.ClienteNome ?? "Cliente não identificado",
                Utilizador = o.User?.FullName ?? o.User?.Username ?? "N/A",
                Itens = o.Items?.Sum(i => i.Quantity) ?? 0,
                Subtotal = o.Items?.Sum(i => i.Quantity * i.PriceAtMoment) ?? 0m,
                Total = o.Payments?.Sum(p => p.Amount) ?? 0m,
                Status = GetStatusText(o.Status),
                FormaPagamento = GetPaymentMethods(o.Payments)
            }).ToList<object>();

            return reportData;
        }

        private void ConfigureDataGridForDailySales()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Pedido", "Pedido", 80);
            AddTextColumn("Data", "Data", 100, "dd/MM/yyyy");
            AddTextColumn("Hora", "Hora", 70);
            AddTextColumn("Mesa", "Mesa", 80);
            AddTextColumn("Cliente", "Cliente", 150);
            AddTextColumn("Utilizador", "Utilizador", 120);
            AddTextColumn("Itens", "Itens", 70);
            AddTextColumn("Subtotal", "Subtotal", 100, "N2");
            AddTextColumn("Total", "Total", 100, "N2");
            AddTextColumn("Status", "Status", 100);
            AddTextColumn("Pagamento", "FormaPagamento", 120);
        }

        private void AddTextColumn(string header, string bindingPath, double width, string format = null)
        {
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = string.IsNullOrEmpty(format)
                    ? new Binding(bindingPath)
                    : new Binding(bindingPath) { StringFormat = format },
                Width = width
            };
            dgReportData.Columns.Add(column);
        }

        // ============================================
        // RELATÓRIO DE PRODUTOS MAIS VENDIDOS
        // ============================================

        private async Task<List<object>> GenerateTopProductsDataAsync(CancellationToken cancellationToken)
        {
            var orderItems = await _context.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => oi.Order.OpenDate >= _startDate && oi.Order.OpenDate <= _endDate)
                .Take(1000)
                .ToListAsync(cancellationToken);

            if (!orderItems.Any()) return new List<object>();

            var grouped = orderItems
                .GroupBy(oi => new { oi.ProductId, oi.Name })
                .Select(g => new
                {
                    Produto = string.IsNullOrEmpty(g.Key.Name) ? "Produto Desconhecido" : g.Key.Name,
                    Quantidade = g.Sum(x => x.Quantity),
                    TotalVendido = g.Sum(x => x.Quantity * x.PriceAtMoment),
                    MediaPreco = g.Average(x => x.PriceAtMoment)
                })
                .Where(x => x.Quantidade > 0)
                .OrderByDescending(x => x.Quantidade)
                .ToList<object>();

            return grouped;
        }

        private void ConfigureDataGridForTopProducts()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Produto", "Produto", 200);
            AddTextColumn("Quantidade", "Quantidade", 100);
            AddTextColumn("Total Vendido", "TotalVendido", 120, "N2");
            AddTextColumn("Preço Médio", "MediaPreco", 100, "N2");
        }

        // ============================================
        // RELATÓRIO DE CLIENTES ATIVOS
        // ============================================

        private async Task<List<object>> GenerateActiveCustomersDataAsync(CancellationToken cancellationToken)
        {
            var orders = await _context.Orders
                .Include(o => o.Payments)
                .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            var clientesDb = await _context.Clientes.Take(200).ToListAsync(cancellationToken);

            var clientesAtivos = clientesDb
                .Select(c => new
                {
                    c.Nome,
                    c.Telefone,
                    c.Email,
                    c.Pontos,
                    PedidosCliente = orders.Where(o => !string.IsNullOrEmpty(o.ClienteNome) && o.ClienteNome.Equals(c.Nome, StringComparison.OrdinalIgnoreCase)).ToList(),
                    Visitas = orders.Count(o => !string.IsNullOrEmpty(o.ClienteNome) && o.ClienteNome.Equals(c.Nome, StringComparison.OrdinalIgnoreCase))
                })
                .Where(c => c.PedidosCliente.Any())
                .Select(c => new
                {
                    c.Nome,
                    c.Telefone,
                    c.Email,
                    c.Pontos,
                    TotalCompras = c.PedidosCliente.Sum(o => o.Payments?.Sum(p => p.Amount) ?? 0m),
                    Visitas = c.Visitas,
                    UltimoPedido = c.PedidosCliente.OrderByDescending(o => o.OpenDate).FirstOrDefault()?.OpenDate
                })
                .Where(c => c.Visitas > 0)
                .OrderByDescending(c => c.Visitas)
                .ToList<object>();

            return clientesAtivos;
        }

        private void ConfigureDataGridForActiveCustomers()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Cliente", "Nome", 150);
            AddTextColumn("Telefone", "Telefone", 100);
            AddTextColumn("Pontos", "Pontos", 80);
            AddTextColumn("Total Compras", "TotalCompras", 120, "N2");
            AddTextColumn("Visitas", "Visitas", 80);
            AddTextColumn("Última Visita", "UltimoPedido", 120, "dd/MM/yyyy HH:mm");
        }

        // ============================================
        // RELATÓRIO DE PEDIDOS POR MESA
        // ============================================

        private async Task<List<object>> GenerateTableOrdersDataAsync(CancellationToken cancellationToken)
        {
            var orders = await _context.Orders
                .Include(o => o.Table)
                .Include(o => o.Payments)
                .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate && o.TableId != null)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!orders.Any()) return new List<object>();

            var tableOrders = orders
                .GroupBy(o => new { TableId = o.TableId ?? 0, Mesa = o.Table != null ? o.Table.Number : "N/A" })
                .Select(g => new
                {
                    Mesa = g.Key.Mesa,
                    NumPedidos = g.Count(),
                    TotalVendas = g.Sum(o => o.Payments?.Sum(p => p.Amount) ?? 0m),
                    MediaTempoMinutos = g.Any(o => o.CloseDate.HasValue) ? g.Where(o => o.CloseDate.HasValue).Average(o => (o.CloseDate.Value - o.OpenDate).TotalMinutes) : 0,
                    Status = g.Any(o => o.Status == OrderStatus.Aberto) ? "Aberta" : "Fechada",
                    Clientes = g.Select(o => o.ClienteNome).Where(n => !string.IsNullOrEmpty(n)).Distinct().Count(),
                    UltimaAtividade = g.Max(o => o.CloseDate ?? o.OpenDate)
                })
                .Where(t => t.NumPedidos > 0)
                .OrderByDescending(x => x.NumPedidos)
                .ToList<object>();

            return tableOrders;
        }

        private void ConfigureDataGridForTableOrders()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Mesa", "Mesa", 100);
            AddTextColumn("Pedidos", "NumPedidos", 80);
            AddTextColumn("Total Vendas", "TotalVendas", 120, "N2");
            AddTextColumn("Tempo Médio (min)", "MediaTempoMinutos", 120, "N1");
            AddTextColumn("Clientes", "Clientes", 80);
            AddTextColumn("Status", "Status", 100);
        }

        // ============================================
        // RELATÓRIO DE FATURAMENTO POR PERÍODO
        // ============================================

        private async Task<List<object>> GenerateRevenuePeriodDataAsync(CancellationToken cancellationToken)
        {
            var payments = await _context.Payments
                .Where(p => p.PaymentDate >= _startDate && p.PaymentDate <= _endDate)
                .OrderBy(p => p.PaymentDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!payments.Any()) return new List<object>();

            var reportData = payments
                .GroupBy(p => p.PaymentDate.Date)
                .Select(g => new
                {
                    Data = g.Key,
                    TotalVendas = g.Sum(p => p.Amount),
                    NumPagamentos = g.Count(),
                    TicketMedio = g.Count() > 0 ? g.Sum(p => p.Amount) / g.Count() : 0m
                })
                .OrderBy(x => x.Data)
                .ToList<object>();

            return reportData;
        }

        private void ConfigureDataGridForRevenuePeriod()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Data", "Data", 120, "dd/MM/yyyy");
            AddTextColumn("Total Vendas", "TotalVendas", 140, "N2");
            AddTextColumn("Nº Pagamentos", "NumPagamentos", 120);
            AddTextColumn("Ticket Médio", "TicketMedio", 120, "N2");
        }

        // ============================================
        // RELATÓRIO DE PAGAMENTOS
        // ============================================

        private async Task<List<object>> GeneratePaymentsDataAsync(CancellationToken cancellationToken)
        {
            var payments = await _context.Payments
                .Include(p => p.Order)
                .Where(p => p.PaymentDate >= _startDate && p.PaymentDate <= _endDate)
                .OrderByDescending(p => p.PaymentDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!payments.Any()) return new List<object>();

            var reportData = payments.Select(p => new
            {
                Data = p.PaymentDate,
                Tipo = GetPaymentTypeText(p.Type),
                TipoIcon = GetPaymentTypeIcon(p.Type),
                Valor = p.Amount,
                Referencia = p.Reference ?? "",
                Observacoes = p.Notes ?? "",
                Pedido = p.OrderId,
                FormaPagamento = GetPaymentTypeText(p.Type)
            }).ToList<object>();

            return reportData;
        }

        private void ConfigureDataGridForPayments()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Data", "Data", 120, "dd/MM/yyyy HH:mm");
            AddTextColumn("Tipo", "Tipo", 120);
            AddTextColumn("Valor", "Valor", 100, "N2");
            AddTextColumn("Referência", "Referencia", 150);
            AddTextColumn("Pedido", "Pedido", 80);
        }

        // ============================================
        // RELATÓRIO DE ITENS DE COZINHA
        // ============================================

        private async Task<List<object>> GenerateKitchenItemsDataAsync(CancellationToken cancellationToken)
        {
            var orderItems = await _context.OrderItems
                .Where(oi => oi.Order.OpenDate >= _startDate && oi.Order.OpenDate <= _endDate)
                .Take(1000)
                .ToListAsync(cancellationToken);

            if (!orderItems.Any()) return new List<object>();

            var reportData = orderItems
                .GroupBy(oi => string.IsNullOrWhiteSpace(oi.Name) ? "Produto Desconhecido" : oi.Name)
                .Select(g => new
                {
                    Item = g.Key,
                    Quantidade = g.Sum(x => x.Quantity),
                    TotalVendido = g.Sum(x => x.Quantity * x.PriceAtMoment),
                    PrecoMedio = g.Any() ? g.Average(x => x.PriceAtMoment) : 0m,
                    Pedidos = g.Select(x => x.OrderId).Distinct().Count()
                })
                .Where(x => x.Quantidade > 0)
                .OrderByDescending(x => x.Quantidade)
                .ToList<object>();

            return reportData;
        }

        private void ConfigureDataGridForKitchenItems()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Item", "Item", 220);
            AddTextColumn("Quantidade", "Quantidade", 100);
            AddTextColumn("Total Vendido", "TotalVendido", 130, "N2");
            AddTextColumn("Preço Médio", "PrecoMedio", 110, "N2");
            AddTextColumn("Pedidos", "Pedidos", 90);
        }

        // ============================================
        // RELATÓRIO DE RESERVAS
        // ============================================

        private async Task<List<object>> GenerateReservationsDataAsync(CancellationToken cancellationToken)
        {
            var reservations = await _context.Reservations
                .Include(r => r.RestaurantTable)
                .Include(r => r.Cliente)
                .Where(r => r.Inicio >= _startDate && r.Inicio <= _endDate)
                .OrderByDescending(r => r.Inicio)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!reservations.Any()) return new List<object>();

            var reportData = reservations.Select(r => new
            {
                Cliente = r.NomeCliente ?? "Não especificado",
                Mesa = r.RestaurantTable?.Number ?? "N/A",
                Inicio = r.Inicio,
                Fim = r.Fim,
                Pessoas = r.NumPessoas,
                Estado = r.Estado.ToString(),
                Codigo = r.CodigoVerificacao ?? "N/A",
                ReservaAtiva = r.MesaAtiva ? "Sim" : "Não",
                TemObservacoes = !string.IsNullOrEmpty(r.Observacoes) ? "Sim" : "Não"
            }).ToList<object>();

            return reportData;
        }

        private void ConfigureDataGridForReservations()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Cliente", "Cliente", 150);
            AddTextColumn("Mesa", "Mesa", 80);
            AddTextColumn("Início", "Inicio", 100, "dd/MM HH:mm");
            AddTextColumn("Fim", "Fim", 100, "dd/MM HH:mm");
            AddTextColumn("Pessoas", "Pessoas", 70);
            AddTextColumn("Estado", "Estado", 100);
            AddTextColumn("Ativa", "ReservaAtiva", 70);
        }

        // ============================================
        // RELATÓRIO DE ENTREGAS
        // ============================================

        private async Task<List<object>> GenerateDeliveriesDataAsync(CancellationToken cancellationToken)
        {
            var orders = await _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.User)
                .Include(o => o.Table)
                .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate)
                .OrderByDescending(o => o.OpenDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!orders.Any()) return new List<object>();

            var reportData = orders
                .Select(o =>
                {
                    object deliveryValue = GetPropertyValue(o, "DeliveryStatus", "DeliveryState", "EstadoEntrega", "StatusEntrega", "DeliveryType", "TipoEntrega", "IsDelivery");
                    string tipo = ConvertToDeliveryText(deliveryValue, o.TableId);
                    decimal total = o.Payments?.Sum(p => p.Amount) ?? 0m;

                    return new
                    {
                        Pedido = o.Id,
                        Data = o.OpenDate,
                        Cliente = string.IsNullOrWhiteSpace(o.ClienteNome) ? "Não identificado" : o.ClienteNome,
                        Tipo = tipo,
                        Total = total,
                        Estado = GetStatusText(o.Status),
                        Utilizador = o.User?.FullName ?? o.User?.Username ?? "N/A"
                    };
                })
                .Where(x => x.Tipo.Contains("Entrega", StringComparison.OrdinalIgnoreCase) ||
                            x.Tipo.Contains("Delivery", StringComparison.OrdinalIgnoreCase))
                .ToList<object>();

            if (!reportData.Any())
            {
                reportData = orders
                    .Where(o => o.TableId == null)
                    .Select(o => new
                    {
                        Pedido = o.Id,
                        Data = o.OpenDate,
                        Cliente = string.IsNullOrWhiteSpace(o.ClienteNome) ? "Não identificado" : o.ClienteNome,
                        Tipo = "Sem mesa (delivery/balcão)",
                        Total = o.Payments?.Sum(p => p.Amount) ?? 0m,
                        Estado = GetStatusText(o.Status),
                        Utilizador = o.User?.FullName ?? o.User?.Username ?? "N/A"
                    })
                    .ToList<object>();
            }

            return reportData;
        }

        private void ConfigureDataGridForDeliveries()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Pedido", "Pedido", 80);
            AddTextColumn("Data", "Data", 130, "dd/MM/yyyy HH:mm");
            AddTextColumn("Cliente", "Cliente", 180);
            AddTextColumn("Tipo", "Tipo", 180);
            AddTextColumn("Total", "Total", 110, "N2");
            AddTextColumn("Estado", "Estado", 110);
            AddTextColumn("Utilizador", "Utilizador", 150);
        }

        // ============================================
        // RELATÓRIO DE VENDAS POR UTILIZADOR
        // ============================================

        private async Task<List<object>> GenerateUserSalesDataAsync(CancellationToken cancellationToken)
        {
            var orders = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.Payments)
                .Where(o => o.OpenDate >= _startDate && o.OpenDate <= _endDate)
                .Take(500)
                .ToListAsync(cancellationToken);

            if (!orders.Any()) return new List<object>();

            var userSales = orders
                .Where(o => o.User != null && o.Payments != null)
                .GroupBy(o => new { o.User.Id, Username = o.User.Username ?? "N/A", FullName = o.User.FullName ?? o.User.Username ?? "N/A" })
                .Select(g => new
                {
                    Utilizador = !string.IsNullOrEmpty(g.Key.FullName) ? g.Key.FullName : g.Key.Username,
                    Username = g.Key.Username,
                    TotalVendas = g.Sum(o => o.Payments?.Sum(p => p.Amount) ?? 0m),
                    NumPedidos = g.Count(),
                    TicketMedio = g.Any() ? g.Sum(o => o.Payments?.Sum(p => p.Amount) ?? 0m) / g.Count() : 0m,
                    PrimeiroPedido = g.Min(o => o.OpenDate),
                    UltimoPedido = g.Max(o => o.OpenDate),
                    MesasAtendidas = g.Where(o => o.Table != null).Select(o => o.Table.Number).Distinct().Count(),
                    PedidosAbertos = g.Count(o => o.Status == OrderStatus.Aberto),
                    PedidosFechados = g.Count(o => o.Status == OrderStatus.Fechado)
                })
                .Where(u => u.NumPedidos > 0)
                .OrderByDescending(x => x.NumPedidos)
                .ToList<object>();

            return userSales;
        }

        private void ConfigureDataGridForUserSales()
        {
            dgReportData.Columns.Clear();
            AddTextColumn("Utilizador", "Utilizador", 150);
            AddTextColumn("Total Vendas", "TotalVendas", 120, "N2");
            AddTextColumn("Nº Pedidos", "NumPedidos", 100);
            AddTextColumn("Ticket Médio", "TicketMedio", 100, "N2");
            AddTextColumn("Mesas Atendidas", "MesasAtendidas", 100);
            AddTextColumn("Atividade", "UltimoPedido", 100, "dd/MM HH:mm");
        }

        // ============================================
        // MÉTODOS AUXILIARES
        // ============================================

        private string GetStatusText(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Aberto => "🟡 Aberto",
                OrderStatus.Fechado => "🟢 Fechado",
                OrderStatus.Cancelado => "🔴 Cancelado",
                _ => status.ToString()
            };
        }

        private string GetPaymentMethods(ICollection<Payment> payments)
        {
            if (payments == null || !payments.Any()) return "Não pago";
            var methods = payments.GroupBy(p => p.Type).Select(g => $"{GetPaymentTypeIcon(g.Key)} {GetPaymentTypeText(g.Key)} ({g.Count()}x)").ToList();
            return string.Join(" | ", methods);
        }

        private string GetPaymentTypeIcon(PaymentType type)
        {
            return type switch
            {
                PaymentType.Dinheiro => "💰",
                PaymentType.Multibanco => "🏦",
                PaymentType.MBWay => "📱",
                PaymentType.CartaoCredito => "💳",
                PaymentType.CartaoDebito => "💳",
                PaymentType.Cartao => "💳",
                PaymentType.MPesa => "📲",
                PaymentType.Emola => "📲",
                PaymentType.Ponto24 => "🏧",
                PaymentType.PosNedBank => "🏦",
                PaymentType.PosMoza => "🏦",
                PaymentType.Outro => "🔄",
                _ => "💳"
            };
        }

        private string GetPaymentTypeText(PaymentType type)
        {
            return type switch
            {
                PaymentType.Dinheiro => "Dinheiro",
                PaymentType.Multibanco => "Multibanco",
                PaymentType.MBWay => "MBWay",
                PaymentType.CartaoCredito => "Cartão Crédito",
                PaymentType.CartaoDebito => "Cartão Débito",
                PaymentType.Cartao => "Cartão",
                PaymentType.MPesa => "M-Pesa",
                PaymentType.Emola => "Emola",
                PaymentType.Ponto24 => "Ponto24",
                PaymentType.PosNedBank => "POS NedBank",
                PaymentType.PosMoza => "POS Moza",
                PaymentType.Outro => "Outro",
                _ => type.ToString()
            };
        }

        private object GetPropertyValue(object instance, params string[] propertyNames)
        {
            if (instance == null || propertyNames == null) return null;
            foreach (string propertyName in propertyNames)
            {
                if (string.IsNullOrWhiteSpace(propertyName)) continue;
                var property = instance.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
                if (property == null) continue;
                try { return property.GetValue(instance); } catch { }
            }
            return null;
        }

        private string ConvertToDeliveryText(object value, int? tableId)
        {
            if (value == null) return tableId == null ? "Sem mesa (delivery/balcão)" : "Mesa";
            if (value is bool boolValue) return boolValue ? "Entrega" : "Mesa";
            string text = value.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return tableId == null ? "Sem mesa (delivery/balcão)" : "Mesa";
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return "Entrega";
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase)) return "Mesa";
            return text;
        }

        private void SetLoadingState(bool isLoading)
        {
            Dispatcher.Invoke(() =>
            {
                if (isLoading)
                {
                    Cursor = Cursors.Wait;
                    btnGenerate.IsEnabled = false;
                    btnExport.IsEnabled = false;
                    btnRefresh.IsEnabled = false;
                }
                else
                {
                    Cursor = Cursors.Arrow;
                    btnGenerate.IsEnabled = true;
                    btnExport.IsEnabled = true;
                    btnRefresh.IsEnabled = true;
                }
            });
        }

        // ============================================
        // GRÁFICO
        // ============================================

        private async Task DrawModernChartAsync()
        {
            bool showChart = chkShowChart?.IsChecked == true;

            if (!showChart)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    pnlChartContainer.Visibility = Visibility.Collapsed;
                    canvasChart.Children.Clear();
                });
                return;
            }

            try
            {
                var payments = await _context.Payments
                    .Where(p => p.PaymentDate >= _startDate && p.PaymentDate <= _endDate)
                    .Take(1000)
                    .ToListAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    pnlChartContainer.Visibility = Visibility.Visible;
                    DrawChartFromPayments(payments);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao desenhar gráfico: {ex.Message}");
            }
        }

        private void DrawChartFromPayments(List<Payment> payments)
        {
            canvasChart.Children.Clear();
            if (payments == null || payments.Count == 0) return;

            var hourlySales = payments
                .GroupBy(p => p.PaymentDate.Hour)
                .Select(g => new { Hora = g.Key, Total = g.Sum(p => p.Amount), NumVendas = g.Count() })
                .OrderBy(x => x.Hora)
                .ToList();

            if (!hourlySales.Any()) return;

            double canvasWidth = Math.Max(canvasChart.ActualWidth, 500);
            double canvasHeight = Math.Max(canvasChart.ActualHeight, 200);
            double maxSale = (double)hourlySales.Max(x => x.Total);

            if (maxSale <= 0) return;

            double barWidth = Math.Min(Math.Max((canvasWidth - 60) / hourlySales.Count, 10), 60);
            double scale = (canvasHeight - 40) / maxSale;

            var axis = new Line { X1 = 40, Y1 = canvasHeight - 30, X2 = canvasWidth, Y2 = canvasHeight - 30, Stroke = Brushes.Gray, StrokeThickness = 1 };
            canvasChart.Children.Add(axis);

            var axisY = new Line { X1 = 40, Y1 = 10, X2 = 40, Y2 = canvasHeight - 30, Stroke = Brushes.Gray, StrokeThickness = 1 };
            canvasChart.Children.Add(axisY);

            for (int i = 0; i < hourlySales.Count; i++)
            {
                var sale = hourlySales[i];
                double barHeight = Math.Max(0, (double)sale.Total * scale);
                double x = 50 + (i * (barWidth + 5));
                double y = canvasHeight - 30 - barHeight;

                double saleValue = (double)sale.Total;
                double highValueThreshold = maxSale * 0.7;
                var barColor = saleValue > highValueThreshold ? Colors.Green : Colors.Blue;

                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(barColor),
                    ToolTip = $"Hora: {sale.Hora:00}h\nValor: {sale.Total:N2}\nVendas: {sale.NumVendas}"
                };

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                canvasChart.Children.Add(bar);

                if (barHeight > 20 && barWidth > 30)
                {
                    var valueText = new TextBlock
                    {
                        Text = sale.Total.ToString("N2"),
                        FontSize = 9,
                        Foreground = Brushes.DarkSlateGray
                    };
                    Canvas.SetLeft(valueText, x + barWidth / 2 - 15);
                    Canvas.SetTop(valueText, y - 18);
                    canvasChart.Children.Add(valueText);
                }

                var hourText = new TextBlock
                {
                    Text = $"{sale.Hora:00}h\n({sale.NumVendas})",
                    FontSize = 9,
                    Foreground = Brushes.Gray,
                    TextAlignment = TextAlignment.Center
                };

                Canvas.SetLeft(hourText, x + barWidth / 2 - 12);
                Canvas.SetTop(hourText, canvasHeight - 25);
                canvasChart.Children.Add(hourText);
            }

            var titleText = new TextBlock
            {
                Text = "Vendas por Hora",
                FontSize = 12,
                Foreground = Brushes.DarkSlateGray
            };
            Canvas.SetLeft(titleText, 40);
            Canvas.SetTop(titleText, 5);
            canvasChart.Children.Add(titleText);

            var legendText = new TextBlock
            {
                Text = $"Período: {_startDate:dd/MM/yyyy} a {_endDate:dd/MM/yyyy}",
                FontSize = 10,
                Foreground = Brushes.Gray
            };
            Canvas.SetLeft(legendText, 40);
            Canvas.SetTop(legendText, 25);
            canvasChart.Children.Add(legendText);
        }

        private void ChkShowChart_Changed(object sender, RoutedEventArgs e)
        {
            _ = DrawModernChartAsync();
        }

        // ============================================
        // MÉTODOS DE UI
        // ============================================

        private void ShowMessage(string message, MessageType type)
        {
            var brush = type switch
            {
                MessageType.Success => Brushes.Green,
                MessageType.Warning => Brushes.Orange,
                MessageType.Error => Brushes.Red,
                MessageType.Info => Brushes.Blue,
                _ => Brushes.Black
            };

            var snackbar = new Border
            {
                Background = brush,
                Padding = new Thickness(16, 12, 16, 12),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,  // CORRIGIDO: usando o tipo completo
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,      // CORRIGIDO: usando o tipo completo
                Margin = new Thickness(0, 0, 24, 24),
                Opacity = 0
            };

            var textBlock = new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };

            snackbar.Child = textBlock;

            var parent = VisualTreeHelper.GetParent(this) as Grid;
            parent?.Children.Add(snackbar);

            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.3) };
            snackbar.BeginAnimation(OpacityProperty, fadeIn);

            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromSeconds(0.3) };
                fadeOut.Completed += (sender2, args) => parent?.Children.Remove(snackbar);
                snackbar.BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();
        }
        private void ShowError(string message) => ShowMessage(message, MessageType.Error);
        private enum MessageType { Success, Warning, Error, Info }

        private void SetupKeyboardShortcuts()
        {
            this.PreviewKeyDown += (sender, e) =>
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    switch (e.Key)
                    {
                        case Key.G: BtnGenerate_Click(null, null); e.Handled = true; break;
                        case Key.R: BtnRefresh_Click(null, null); e.Handled = true; break;
                        case Key.F: cmbPeriod.Focus(); e.Handled = true; break;
                    }
                }
            };
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating)
            {
                ShowMessage("Aguarde, o relatório está sendo atualizado...", MessageType.Info);
                return;
            }

            await UpdateGeneralStatsAsync();

            if (!string.IsNullOrWhiteSpace(_currentReportType))
            {
                await GenerateSelectedReportAsync(_cts.Token);
            }
        }

        private void BtnToggleSidebar_Checked(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = true;
            sidebarBorder.Visibility = Visibility.Collapsed;
            sidebarColumn.Width = new GridLength(0);
        }

        private void BtnToggleSidebar_Unchecked(object sender, RoutedEventArgs e)
        {
            _isSidebarCollapsed = false;
            sidebarBorder.Visibility = Visibility.Visible;
            sidebarColumn.Width = new GridLength(320);
        }

        private void ReportsControl_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustLayoutForResponsiveness(e.NewSize.Width);

        private void AdjustLayoutForResponsiveness(double width)
        {
            if (width < 768)
            {
                sidebarColumn.Width = new GridLength(280);
                foreach (var card in FindVisualChildren<Border>(pnlStats)) card.MinWidth = 160;
            }
            else if (width < 1024)
            {
                sidebarColumn.Width = new GridLength(300);
            }
        }

        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T) yield return (T)child;
                    foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
                }
            }
        }

        private void CmbPeriod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbPeriod.SelectedItem == null) return;

                string selectedText = cmbPeriod.SelectedItem is ComboBoxItem comboBoxItem
                    ? comboBoxItem.Content?.ToString()
                    : cmbPeriod.SelectedItem.ToString();

                if (string.IsNullOrEmpty(selectedText)) return;

                ApplyPeriod(selectedText);
                UpdateDateRangeText();
            }
            catch (Exception ex) { Debug.WriteLine($"Erro em CmbPeriod_SelectionChanged: {ex.Message}"); }
        }

        private void ApplyPeriod(string selectedText)
        {
            try
            {
                if (string.IsNullOrEmpty(selectedText)) return;
                if (pnlCustomDates != null) pnlCustomDates.Visibility = Visibility.Collapsed;

                switch (selectedText)
                {
                    case "Hoje":
                        _startDate = DateTime.Today;
                        _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                        break;
                    case "Ontem":
                        _startDate = DateTime.Today.AddDays(-1);
                        _endDate = DateTime.Today.AddSeconds(-1);
                        break;
                    case "Esta Semana":
                        var today = DateTime.Today;
                        int daysToSubtract = (int)today.DayOfWeek;
                        if (daysToSubtract == 0) daysToSubtract = 7;
                        _startDate = today.AddDays(-daysToSubtract + 1);
                        _endDate = _startDate.AddDays(7).AddSeconds(-1);
                        break;
                    case "Última Semana":
                        var lastWeekToday = DateTime.Today.AddDays(-7);
                        int lastWeekDaysToSubtract = (int)lastWeekToday.DayOfWeek;
                        if (lastWeekDaysToSubtract == 0) lastWeekDaysToSubtract = 7;
                        _startDate = lastWeekToday.AddDays(-lastWeekDaysToSubtract + 1);
                        _endDate = _startDate.AddDays(7).AddSeconds(-1);
                        break;
                    case "Este Mês":
                        _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                        _endDate = _startDate.AddMonths(1).AddSeconds(-1);
                        break;
                    case "Mês Passado":
                        var lastMonth = DateTime.Today.AddMonths(-1);
                        _startDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                        _endDate = _startDate.AddMonths(1).AddSeconds(-1);
                        break;
                    case "Personalizado":
                        if (pnlCustomDates != null) pnlCustomDates.Visibility = Visibility.Visible;
                        if (dpStart != null && dpEnd != null && dpStart.SelectedDate.HasValue && dpEnd.SelectedDate.HasValue)
                        {
                            _startDate = dpStart.SelectedDate.Value;
                            _endDate = dpEnd.SelectedDate.Value.AddDays(1).AddSeconds(-1);
                        }
                        else if (dpStart != null && dpEnd != null)
                        {
                            dpStart.SelectedDate = DateTime.Today;
                            dpEnd.SelectedDate = DateTime.Today;
                            _startDate = DateTime.Today;
                            _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                        }
                        break;
                    default:
                        _startDate = DateTime.Today;
                        _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro em ApplyPeriod: {ex.Message}");
                _startDate = DateTime.Today;
                _endDate = DateTime.Today.AddDays(1).AddSeconds(-1);
            }
        }

        private void LstReports_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstReports.SelectedItem is ReportItem selectedReport)
            {
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                _currentReportType = selectedReport.ReportType;

                // Mostrar loading state
                dgReportData.Visibility = Visibility.Visible;
                pnlEmpty.Visibility = Visibility.Collapsed;
                txtReportTitle.Text = "Carregando...";
                txtTotalRecords.Text = "0 registros";
                dgReportData.ItemsSource = null;
                dgReportData.Columns.Clear();

                // Gerar automaticamente
                if (!_isGenerating)
                {
                    BtnGenerate_Click(null, null);
                }
            }
        }

        private void DpStart_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dpStart.SelectedDate.HasValue && dpEnd.SelectedDate.HasValue)
            {
                _startDate = dpStart.SelectedDate.Value;
                _endDate = dpEnd.SelectedDate.Value.AddDays(1).AddSeconds(-1);
                UpdateDateRangeText();
            }
        }

        private void DpEnd_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dpStart.SelectedDate.HasValue && dpEnd.SelectedDate.HasValue)
            {
                _startDate = dpStart.SelectedDate.Value;
                _endDate = dpEnd.SelectedDate.Value.AddDays(1).AddSeconds(-1);
                UpdateDateRangeText();
            }
        }

        private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            cmbPeriod.SelectedIndex = 0;
            lstReports.SelectedIndex = -1;
            _currentReportType = null;
            _currentReportData = null;
            _isReportGenerated = false;
            ShowEmptyState();
            canvasChart.Children.Clear();
            _cachedReports.Clear();
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (dgReportData.Items.Count == 0)
            {
                ShowMessage("Não há dados para imprimir.", MessageType.Warning);
                return;
            }

            try
            {
                PrintDialog printDialog = new PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    printDialog.PrintVisual(dgReportData, $"Relatório {DateTime.Now:yyyyMMdd}");
                    ShowMessage("Relatório enviado para impressão com sucesso!", MessageType.Success);
                }
            }
            catch (Exception ex) { ShowError($"Erro ao imprimir: {ex.Message}"); }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeControls();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            if (canvasChart.ActualWidth > 0 && canvasChart.ActualHeight > 0) await DrawModernChartAsync();
        }

        private object GetCellValue(object item, DataGridColumn column)
        {
            if (item == null || column == null) return "N/A";

            if (column is DataGridTextColumn textColumn)
            {
                var binding = textColumn.Binding as Binding;
                if (binding != null && !string.IsNullOrEmpty(binding.Path?.Path))
                {
                    var property = item.GetType().GetProperty(binding.Path.Path);
                    var value = property?.GetValue(item);
                    return value ?? "N/A";
                }
            }

            var cell = column.GetCellContent(item);
            if (cell is TextBlock textBlock) return string.IsNullOrEmpty(textBlock.Text) ? "N/A" : textBlock.Text;
            return "N/A";
        }

        // ============================================
        // EXPORTAÇÃO PARA EXCEL
        // ============================================

        private async void MenuItem_ExportExcel_Click(object sender, RoutedEventArgs e)
        {
            if (dgReportData.Items.Count == 0)
            {
                ShowMessage("Não há dados para exportar.", MessageType.Warning);
                return;
            }

            try
            {
                SetLoadingState(true);

                string reportTitle = txtReportTitle.Text ?? "Relatório";
                string dateRange = txtDateRange.Text ?? "Período não definido";
                string restaurantName = _restaurantConfig?.RestaurantName ?? "Restaurante";

                var headers = new List<string>();
                foreach (var column in dgReportData.Columns) headers.Add(column.Header?.ToString() ?? "");

                var dataRows = new List<List<object>>();
                foreach (var item in dgReportData.Items)
                {
                    var row = new List<object>();
                    foreach (var column in dgReportData.Columns)
                    {
                        var value = GetCellValue(item, column);
                        row.Add(value ?? "N/A");
                    }
                    dataRows.Add(row);
                }

                var saveDialog = new SaveFileDialog
                {
                    FileName = $"{reportTitle.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}",
                    DefaultExt = ".xlsx",
                    Filter = "Ficheiros Excel (*.xlsx)|*.xlsx"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await Task.Run(() => ExportToExcelSimple(saveDialog.FileName, restaurantName, reportTitle, dateRange, headers, dataRows));
                    ShowMessage($"✅ Relatório exportado para Excel com sucesso!", MessageType.Success);
                }
            }
            catch (Exception ex) { ShowError($"Erro ao exportar para Excel: {ex.Message}"); }
            finally { SetLoadingState(false); }
        }

        private void ExportToExcelSimple(string filePath, string restaurantName, string reportTitle, string dateRange,
            List<string> headers, List<List<object>> dataRows)
        {
            try
            {
                using (var workbook = new XSSFWorkbook())
                {
                    var sheet = workbook.CreateSheet("Relatório");

                    int rowIndex = 0;
                    var titleRow = sheet.CreateRow(rowIndex++);
                    titleRow.HeightInPoints = 30;
                    var titleCell = titleRow.CreateCell(0);
                    titleCell.SetCellValue($"{restaurantName} - {reportTitle}");
                    var titleStyle = workbook.CreateCellStyle();
                    var titleFont = workbook.CreateFont();
                    titleFont.FontHeightInPoints = 14;
                    titleFont.IsBold = true;
                    titleStyle.SetFont(titleFont);
                    titleStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
                    titleCell.CellStyle = titleStyle;
                    sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, headers.Count - 1));

                    var periodRow = sheet.CreateRow(rowIndex++);
                    var periodCell = periodRow.CreateCell(0);
                    periodCell.SetCellValue($"Período: {dateRange} | Exportado: {DateTime.Now:dd/MM/yyyy HH:mm}");
                    sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, headers.Count - 1));

                    rowIndex++;

                    var headerRow = sheet.CreateRow(rowIndex++);
                    headerRow.HeightInPoints = 25;
                    var headerStyle = workbook.CreateCellStyle();
                    var headerFont = workbook.CreateFont();
                    headerFont.FontHeightInPoints = 10;
                    headerFont.IsBold = true;
                    headerFont.Color = IndexedColors.White.Index;
                    headerStyle.SetFont(headerFont);
                    headerStyle.FillForegroundColor = IndexedColors.DarkBlue.Index;
                    headerStyle.FillPattern = FillPattern.SolidForeground;
                    headerStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
                    headerStyle.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;

                    for (int i = 0; i < headers.Count; i++)
                    {
                        var cell = headerRow.CreateCell(i);
                        cell.SetCellValue(headers[i]);
                        cell.CellStyle = headerStyle;
                        sheet.SetColumnWidth(i, Math.Min(Math.Max(headers[i].Length * 256, 3500), 15000));
                    }

                    var dataStyle = workbook.CreateCellStyle();
                    var dataFont = workbook.CreateFont();
                    dataFont.FontHeightInPoints = 10;
                    dataStyle.SetFont(dataFont);
                    dataStyle.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;

                    foreach (var rowData in dataRows)
                    {
                        var dataRow = sheet.CreateRow(rowIndex++);
                        dataRow.HeightInPoints = 20;

                        for (int i = 0; i < rowData.Count && i < headers.Count; i++)
                        {
                            var cell = dataRow.CreateCell(i);
                            var value = rowData[i];

                            if (value is decimal || value is double || value is float)
                            {
                                if (double.TryParse(value.ToString(), out double num))
                                {
                                    cell.SetCellValue(num);
                                    var numStyle = workbook.CreateCellStyle();
                                    var numFormat = workbook.CreateDataFormat();
                                    numStyle.DataFormat = numFormat.GetFormat("#,##0.00");
                                    numStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Right;
                                    cell.CellStyle = numStyle;
                                }
                            }
                            else if (value is int || value is long)
                            {
                                if (long.TryParse(value.ToString(), out long num))
                                {
                                    cell.SetCellValue(num);
                                    var numStyle = workbook.CreateCellStyle();
                                    var numFormat = workbook.CreateDataFormat();
                                    numStyle.DataFormat = numFormat.GetFormat("#,##0");
                                    numStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Right;
                                    cell.CellStyle = numStyle;
                                }
                            }
                            else if (value is DateTime dt)
                            {
                                cell.SetCellValue(dt);
                                var dateStyle = workbook.CreateCellStyle();
                                var dateFormat = workbook.CreateDataFormat();
                                dateStyle.DataFormat = dateFormat.GetFormat("dd/mm/yyyy hh:mm");
                                dateStyle.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
                                cell.CellStyle = dateStyle;
                            }
                            else
                            {
                                cell.SetCellValue(value?.ToString() ?? "-");
                                cell.CellStyle = dataStyle;
                            }
                        }
                    }

                    var footerRow = sheet.CreateRow(rowIndex++);
                    footerRow.HeightInPoints = 18;
                    var footerCell = footerRow.CreateCell(0);
                    footerCell.SetCellValue($"Total: {dataRows.Count} registros");
                    sheet.AddMergedRegion(new CellRangeAddress(rowIndex - 1, rowIndex - 1, 0, headers.Count - 1));

                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write)) workbook.Write(fs);
                }
            }
            catch (Exception ex) { throw new Exception($"Erro ao exportar Excel: {ex.Message}", ex); }
        }

        // ============================================
        // EXPORTAÇÃO PARA PDF
        // ============================================

        private async void MenuItem_ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (dgReportData.Items.Count == 0)
            {
                ShowMessage("Não há dados para exportar.", MessageType.Warning);
                return;
            }

            try
            {
                SetLoadingState(true);

                string reportTitle = txtReportTitle.Text ?? "Relatório";
                string dateRange = txtDateRange.Text ?? "Período não definido";
                string restaurantName = _restaurantConfig?.RestaurantName ?? "Restaurante";

                var headers = new List<string>();
                foreach (var column in dgReportData.Columns) headers.Add(column.Header?.ToString() ?? "");

                var dataRows = new List<List<string>>();
                foreach (var item in dgReportData.Items)
                {
                    var row = new List<string>();
                    foreach (var column in dgReportData.Columns)
                    {
                        var value = GetCellValue(item, column)?.ToString() ?? "N/A";
                        row.Add(value);
                    }
                    dataRows.Add(row);
                }

                var saveDialog = new SaveFileDialog
                {
                    FileName = $"{reportTitle.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}",
                    DefaultExt = ".pdf",
                    Filter = "Documentos PDF (*.pdf)|*.pdf"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    await Task.Run(() => ExportToPdfSimple(saveDialog.FileName, restaurantName, reportTitle, dateRange, headers, dataRows));
                    ShowMessage($"✅ Relatório exportado para PDF com sucesso!", MessageType.Success);
                }
            }
            catch (Exception ex) { ShowError($"Erro ao exportar para PDF: {ex.Message}"); }
            finally { SetLoadingState(false); }
        }

        private void ExportToPdfSimple(string filePath, string restaurantName, string reportTitle, string dateRange,
            List<string> headers, List<List<string>> dataRows)
        {
            try
            {
                using (var document = new PdfDocument())
                {
                    var page = document.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    page.Orientation = PageOrientation.Landscape;

                    double pageWidth = page.Width.Point;
                    double pageHeight = page.Height.Point;

                    XGraphics gfx = XGraphics.FromPdfPage(page);

                    var fontTitle = new XFont("Arial", 16, XFontStyle.Bold);
                    var fontSubTitle = new XFont("Arial", 10, XFontStyle.Regular);
                    var fontHeader = new XFont("Arial", 9, XFontStyle.Bold);
                    var fontNormal = new XFont("Arial", 8, XFontStyle.Regular);

                    double y = 40;
                    double margin = 40;
                    double tableWidth = pageWidth - 2 * margin;
                    double colWidth = tableWidth / Math.Max(headers.Count, 1);

                    string title = $"{restaurantName} - {reportTitle}";
                    gfx.DrawString(title, fontTitle, XBrushes.DarkBlue, new XPoint((pageWidth - gfx.MeasureString(title, fontTitle).Width) / 2, y));
                    y += 30;

                    string info = $"Período: {dateRange} | Exportado: {DateTime.Now:dd/MM/yyyy HH:mm}";
                    gfx.DrawString(info, fontSubTitle, XBrushes.DarkGray, new XPoint((pageWidth - gfx.MeasureString(info, fontSubTitle).Width) / 2, y));
                    y += 25;

                    gfx.DrawLine(new XPen(XColors.LightGray, 1), margin, y, pageWidth - margin, y);
                    y += 10;

                    double headerY = y;
                    for (int i = 0; i < headers.Count; i++)
                    {
                        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(25, 118, 210)), margin + (i * colWidth), headerY, colWidth, 20);
                        gfx.DrawString(headers[i], fontHeader, XBrushes.White, new XPoint(margin + (i * colWidth) + 4, headerY + 14));
                    }
                    y += 20;

                    int rowsPerPage = 35;
                    int currentRow = 0;

                    while (currentRow < dataRows.Count)
                    {
                        int rowsToShow = Math.Min(rowsPerPage, dataRows.Count - currentRow);

                        for (int r = 0; r < rowsToShow; r++)
                        {
                            int dataIndex = currentRow + r;
                            for (int c = 0; c < dataRows[dataIndex].Count && c < headers.Count; c++)
                            {
                                if (r % 2 == 0) gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(245, 245, 245)), margin + (c * colWidth), y, colWidth, 16);
                                gfx.DrawRectangle(new XPen(XColors.LightGray, 0.3), margin + (c * colWidth), y, colWidth, 16);
                                string text = dataRows[dataIndex][c] ?? "-";

                                XPoint textPoint;
                                if (c > 0 && decimal.TryParse(text.Replace("MT", "").Replace("$", "").Trim(), out _))
                                    textPoint = new XPoint(margin + (c * colWidth) + colWidth - 10 - gfx.MeasureString(text, fontNormal).Width, y + 12);
                                else
                                    textPoint = new XPoint(margin + (c * colWidth) + 4, y + 12);

                                gfx.DrawString(text, fontNormal, XBrushes.Black, textPoint);
                            }
                            y += 16;

                            if (y > pageHeight - 60 && r < rowsToShow - 1)
                            {
                                page = document.AddPage();
                                page.Size = PdfSharp.PageSize.A4;
                                page.Orientation = PageOrientation.Landscape;
                                gfx = XGraphics.FromPdfPage(page);
                                y = 40;

                                headerY = y;
                                for (int i = 0; i < headers.Count; i++)
                                {
                                    gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(25, 118, 210)), margin + (i * colWidth), headerY, colWidth, 20);
                                    gfx.DrawString(headers[i], fontHeader, XBrushes.White, new XPoint(margin + (i * colWidth) + 4, headerY + 14));
                                }
                                y += 20;
                            }
                        }

                        currentRow += rowsToShow;

                        if (currentRow < dataRows.Count)
                        {
                            page = document.AddPage();
                            page.Size = PdfSharp.PageSize.A4;
                            page.Orientation = PageOrientation.Landscape;
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;

                            headerY = y;
                            for (int i = 0; i < headers.Count; i++)
                            {
                                gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(25, 118, 210)), margin + (i * colWidth), headerY, colWidth, 20);
                                gfx.DrawString(headers[i], fontHeader, XBrushes.White, new XPoint(margin + (i * colWidth) + 4, headerY + 14));
                            }
                            y += 20;
                        }
                    }

                    y = pageHeight - 30;
                    string footer = $"Total: {dataRows.Count} registros • {restaurantName} • {DateTime.Now:yyyy}";
                    gfx.DrawString(footer, fontSubTitle, XBrushes.Gray, new XPoint(margin, y));

                    document.Save(filePath);
                }
            }
            catch (Exception ex) { throw new Exception($"Erro ao exportar PDF: {ex.Message}", ex); }
        }
    }
}