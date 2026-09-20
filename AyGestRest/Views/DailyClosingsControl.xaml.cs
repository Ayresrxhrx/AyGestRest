using AyGestRest.Data;
using AyGestRest.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.EntityFrameworkCore;
using OxyPlot;
using OxyPlot.Legends;
using OxyPlot.Series;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace AyGestRest.Views
{
    public partial class DailyClosingsControl : UserControl
    {
        private AyGestRestContext _db;
        private RestaurantConfig _config;
        private decimal _totalVendido;
        private decimal _totalDinheiro;
        private decimal _totalOutros;
        private decimal _lucroEstimado;
        private decimal _totalDespesasHoje;
        private decimal _saldoLiquido;
        private List<PurchaseOrder> _comprasHoje = new List<PurchaseOrder>();
        private decimal _valorContadoAtual;
        private int _numeroVendas;
        private decimal _ticketMedio;
        private DateTime _dataInicioContagem = DateTime.Today;

        public ObservableCollection<PaymentMethodSalesViewModel> SalesByPaymentMethod { get; }
            = new ObservableCollection<PaymentMethodSalesViewModel>();

        public PlotModel PieModel { get; private set; }

        public string CurrencySymbol { get; private set; } = "MTn";

        public DailyClosingsControl()
        {
            InitializeComponent();
            DataContext = this;

            InitializeDatabase();
            InitializeChart();
            LoadTodayData();
        }

        private void InitializeDatabase()
        {
            _db = new AyGestRestContext();
            _config = _db.RestaurantConfigs.FirstOrDefault() ?? new RestaurantConfig();
            CurrencySymbol = string.IsNullOrWhiteSpace(_config.CurrencySymbol) ? "MTn" : _config.CurrencySymbol;
        }

        private void InitializeChart()
        {
            PieModel = new PlotModel
            {
                Title = "Distribuição por Método de Pagamento",
                TitleColor = OxyColors.Black,
                TitleFontSize = 14,
                Background = OxyColors.White
            };

            PieModel.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.RightTop,
                LegendPlacement = LegendPlacement.Outside
            });
        }

        // 🔥 MÉTODO: Carregar despesas do dia
        private async Task LoadTodayExpenses()
        {
            try
            {
                var today = DateTime.Today;

                _comprasHoje = await _db.PurchaseOrders
                    .Include(p => p.Supplier)
                    .Include(p => p.Items)
                    .Where(p => p.CreatedAt.Date == today && p.Status == "Recebida")
                    .OrderBy(p => p.CreatedAt)
                    .ToListAsync();

                _totalDespesasHoje = _comprasHoje.Sum(p => p.TotalAmount);

                // Calcular saldo líquido (Vendas - Despesas)
                _saldoLiquido = _totalVendido - _totalDespesasHoje;

                Debug.WriteLine($"📊 Despesas do dia: {_totalDespesasHoje:N2} MTn");
                Debug.WriteLine($"💰 Saldo líquido: {_saldoLiquido:N2} MTn");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao carregar despesas: {ex.Message}");
                _totalDespesasHoje = 0;
                _saldoLiquido = _totalVendido;
            }
        }

        private void LoadTodayData()
        {
            var ultimoFecho = _db.DailyClosings
                .Where(f => f.Data.Date == DateTime.Today)
                .OrderByDescending(f => f.Data)
                .FirstOrDefault();

            _dataInicioContagem = ultimoFecho?.Data ?? DateTime.Today.Date;

            UpdatePeriodText();
            RecalculateValuesFrom(_dataInicioContagem);
            UpdateDifference();
            UpdateUIState();
            _ = CheckPendingPurchasesAsync();
        }

        private void UpdatePeriodText()
        {
            runStartDate.Text = _dataInicioContagem.ToString("dd/MM/yyyy HH:mm");
            runEndDate.Text = DateTime.Now.ToString("HH:mm");
            txtPeriodo.Text = $"Período: {_dataInicioContagem:dd/MM/yyyy HH:mm} até {DateTime.Now:HH:mm}";
        }

        private async void RecalculateValuesFrom(DateTime startDate)
        {
            try
            {
                var payments = _db.Payments
                    .Include(p => p.Order)
                        .ThenInclude(o => o.Items)
                    .Where(p => p.PaymentDate >= startDate)
                    .ToList();

                _totalVendido = payments.Sum(p => p.Amount);
                _totalDinheiro = payments.Where(p => p.Type == PaymentType.Dinheiro).Sum(p => p.Amount);
                _totalOutros = payments.Where(p => p.Type != PaymentType.Dinheiro).Sum(p => p.Amount);

                decimal outrosCalculado = _totalVendido - _totalDinheiro;
                if (_totalOutros != outrosCalculado)
                {
                    _totalOutros = outrosCalculado;
                }

                _numeroVendas = payments.Select(p => p.OrderId).Distinct().Count();
                _ticketMedio = _numeroVendas > 0 ? _totalVendido / _numeroVendas : 0;

                _lucroEstimado = payments
                    .SelectMany(p => p.Order?.Items ?? Enumerable.Empty<OrderItem>())
                    .Sum(i => i.Quantity * i.UnitPrice * 0.6m);

                // 🔥 CARREGAR DESPESAS DO DIA
                await LoadTodayExpenses();

                UpdateDisplayTexts();
                LoadPaymentMethodSales(payments);
                UpdateChart();
                UpdateExpensesDisplay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro em RecalculateValuesFrom: {ex.Message}");
            }
        }

        private void UpdateExpensesDisplay()
        {
            try
            {
                // Atualizar valores
                var txtTotalDespesas = FindName("txtTotalDespesas") as TextBlock;
                if (txtTotalDespesas != null)
                    txtTotalDespesas.Text = $"{_totalDespesasHoje:N2} {CurrencySymbol}";

                var txtSaldoLiquido = FindName("txtSaldoLiquido") as TextBlock;
                if (txtSaldoLiquido != null)
                {
                    txtSaldoLiquido.Text = $"{_saldoLiquido:N2} {CurrencySymbol}";

                    // Colorir baseado no saldo
                    if (_saldoLiquido >= 0)
                    {
                        txtSaldoLiquido.Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 83));
                    }
                    else
                    {
                        txtSaldoLiquido.Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    }
                }

                // Atualizar contador de compras
                var txtNumeroCompras = FindName("txtNumeroCompras") as TextBlock;
                if (txtNumeroCompras != null)
                {
                    txtNumeroCompras.Text = _comprasHoje.Count.ToString("N0");
                }

                // Atualizar lista de compras
                var dgComprasHoje = FindName("dgComprasHoje") as DataGrid;
                if (dgComprasHoje != null)
                {
                    dgComprasHoje.ItemsSource = _comprasHoje.Select(c => new
                    {
                        c.PurchaseNumber,
                        Fornecedor = c.Supplier?.Name ?? "N/A",
                        c.CreatedAt,
                        Valor = c.TotalAmount,
                        Itens = c.Items?.Count ?? 0
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao atualizar despesas: {ex.Message}");
            }
        }

        // 🔥 MÉTODO PÚBLICO: Recarregar dados (chamado pelo ProductsManagementControl)
        public void ReloadData()
        {
            LoadTodayData();
            ShowSuccessMessage("🔄 Dados atualizados com novas compras!");
        }

        // 🔥 MÉTODO: Mostrar mensagem de sucesso
        private void ShowSuccessMessage(string message)
        {
            try
            {
                var snackbarQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(4));
                snackbarQueue.Enqueue(message, "OK", () => { });

                var snackbar = FindName("MainSnackbar") as Snackbar;
                if (snackbar != null)
                {
                    snackbar.MessageQueue = snackbarQueue;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao mostrar mensagem: {ex.Message}");
            }
        }

        // 🔥 MÉTODO: Ver compras do dia
        private void BtnVerComprasHoje_Click(object sender, RoutedEventArgs e)
        {
            if (_comprasHoje == null || _comprasHoje.Count == 0)
            {
                MessageBox.Show("Nenhuma compra registrada hoje.", "Informação",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Window
            {
                Title = "📦 Compras do Dia",
                Width = 800,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                WindowStyle = WindowStyle.ToolWindow,
                Background = Brushes.White
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var headerPanel = new StackPanel { Margin = new Thickness(20, 15, 20, 10) };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "📋 DETALHES DAS COMPRAS DO DIA",
                FontSize = 18,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                Margin = new Thickness(0, 0, 0, 5)
            });

            headerPanel.Children.Add(new TextBlock
            {
                Text = $"{DateTime.Now:dddd, dd/MM/yyyy}",
                FontSize = 12,
                Foreground = Brushes.Gray
            });

            Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                Margin = new Thickness(20),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                FontSize = 12,
                RowHeight = 36,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(250, 250, 250))
            };

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Nº Compra",
                Binding = new Binding("PurchaseNumber"),
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Fornecedor",
                Binding = new Binding("Supplier.Name"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Hora",
                Binding = new Binding("CreatedAt") { StringFormat = "HH:mm" },
                Width = 80
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Tipo",
                Binding = new Binding("PurchaseType"),
                Width = 100
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Itens",
                Binding = new Binding("ItemCount"),
                Width = 60,
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Right),
                        new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0))
                    }
                }
            });

            dataGrid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = "Valor Total",
                Binding = new Binding("TotalAmount") { StringFormat = "N2" },
                Width = 120,
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = {
                        new Setter(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Right),
                        new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0)),
                        new Setter(TextBlock.FontWeightProperty, System.Windows.FontWeights.Bold),
                        new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(255, 107, 107)))
                    }
                }
            });

            dataGrid.ItemsSource = _comprasHoje;

            Grid.SetRow(dataGrid, 1);
            grid.Children.Add(dataGrid);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        // 🔥 MÉTODO: Confirmar Fecho do Dia
        private async void ConfirmarFecho_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateCount())
                return;

            // 🔥 VALIDAÇÃO: Verificar se há compras não recebidas
            var pendingPurchases = await _db.PurchaseOrders
                .Where(p => p.CreatedAt.Date == DateTime.Today && p.Status == "Pendente")
                .CountAsync();

            string pendingWarning = "";
            if (pendingPurchases > 0)
            {
                pendingWarning = $"\n⚠️ ATENÇÃO: Existem {pendingPurchases} compra(s) pendente(s) hoje!\n" +
                                $"Estas compras não foram incluídas no cálculo de despesas.\n";
            }

            var result = MessageBox.Show(
                $"📊 CONFIRMAR FECHO DO DIA\n" +
                $"========================\n" +
                $"💰 VENDAS TOTAIS: {_totalVendido:N2} {CurrencySymbol}\n" +
                $"💵 Dinheiro Registrado: {_totalDinheiro:N2} {CurrencySymbol}\n" +
                $"💳 Outros Pagamentos: {_totalOutros:N2} {CurrencySymbol}\n" +
                $"📦 COMPRAS DO DIA: {_totalDespesasHoje:N2} {CurrencySymbol}\n" +
                $"💷 Saldo Líquido: {_saldoLiquido:N2} {CurrencySymbol}\n" +
                $"========================\n" +
                $"💶 Dinheiro Contado: {_valorContadoAtual:N2} {CurrencySymbol}\n" +
                $"🔍 Diferença no Caixa: {(_valorContadoAtual - _totalDinheiro):N2} {CurrencySymbol}\n" +
                $"{pendingWarning}" +
                $"========================\n\n" +
                $"Esta ação não pode ser desfeita automaticamente.\n\n" +
                $"Confirmar fecho do dia?",
                "Confirmar Fecho",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                var closing = new DailyClosing
                {
                    Data = DateTime.Now,
                    TotalVendido = _totalVendido,
                    TotalDinheiro = _totalDinheiro,
                    TotalOutrosPagamentos = _totalOutros,
                    LucroBrutoEstimado = _lucroEstimado,
                    ValorContado = _valorContadoAtual,
                    Diferenca = _valorContadoAtual - _totalDinheiro
                };

                _db.DailyClosings.Add(closing);
                await _db.SaveChangesAsync();

                // 🔥 GERAR E ENVIAR 2 PDFs (FECHO + INVENTÁRIO)
                bool pdfsEnviados = await CreateAndSendReportsAsync(closing);

                // Imprimir recibo
                await PrintProfessionalReceiptAsync(closing);

                string emailMessage = pdfsEnviados ?
                    "📧 Relatórios PDF enviados por email!" :
                    "⚠️ PDFs criados mas não enviados por email (verifique configurações)";

                MessageBox.Show(
                    $"✅ FECHO DO DIA CONFIRMADO COM SUCESSO!\n\n" +
                    $"📊 RESUMO FINAL:\n" +
                    $"💰 Vendas Totais: {_totalVendido:N2} {CurrencySymbol}\n" +
                    $"📦 Compras do Dia: {_totalDespesasHoje:N2} {CurrencySymbol}\n" +
                    $"💷 Saldo Líquido: {_saldoLiquido:N2} {CurrencySymbol}\n" +
                    $"💵 Dinheiro: {_totalDinheiro:N2} {CurrencySymbol}\n" +
                    $"💳 Outros: {_totalOutros:N2} {CurrencySymbol}\n" +
                    $"🔍 Diferença: {(_valorContadoAtual - _totalDinheiro):N2} {CurrencySymbol}\n\n" +
                    $"{emailMessage}\n" +
                    $"📄 Recibo impresso e dados salvos.",
                    "Sucesso",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Recarregar dados para reiniciar contagem
                LoadTodayData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Erro ao salvar fecho: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateDisplayTexts()
        {
            txtTotalVendido.Text = $"{_totalVendido:N2} {CurrencySymbol}";
            txtDinheiro.Text = $"{_totalDinheiro:N2} {CurrencySymbol}";
            txtOutros.Text = $"{_totalOutros:N2} {CurrencySymbol}";
            txtLucro.Text = $"{_lucroEstimado:N2} {CurrencySymbol}";
            txtNumeroVendas.Text = _numeroVendas.ToString("N0");
            txtTicketMedio.Text = $"{_ticketMedio:N2} {CurrencySymbol}";
            txtTotalDinheiroRegistrado.Text = $"{_totalDinheiro:N2} {CurrencySymbol}";

            txtTotalVendidoHeader.Text = $"{_totalVendido:N2}";
            txtTotalDinheiroHeader.Text = $"{_totalDinheiro:N2}";
        }

        private void LoadPaymentMethodSales(List<Payment> payments)
        {
            var salesByMethod = payments
                .GroupBy(p => p.Type)
                .Select(g => new PaymentMethodSalesViewModel
                {
                    MethodName = GetPaymentMethodName(g.Key),
                    Amount = g.Sum(p => p.Amount),
                    Count = g.Count()
                })
                .OrderByDescending(m => m.Amount)
                .ToList();

            UpdateSalesListAndChart(salesByMethod);
        }

        private void UpdateSalesListAndChart(List<PaymentMethodSalesViewModel> sales)
        {
            decimal total = sales.Sum(m => m.Amount);

            foreach (var method in sales)
            {
                method.Percentage = total > 0 ? (double)(method.Amount / total * 100) : 0;
            }

            SalesByPaymentMethod.Clear();
            foreach (var item in sales)
            {
                SalesByPaymentMethod.Add(item);
            }
        }

        private void UpdateChart()
        {
            PieModel.Series.Clear();

            var pieSeries = new PieSeries
            {
                StrokeThickness = 2,
                Stroke = OxyColors.White,
                InsideLabelPosition = 0.7,
                InsideLabelFormat = "{2:0}%",
                OutsideLabelFormat = "{0}: {1:N2}",
                StartAngle = 0,
                AngleSpan = 360
            };

            var colors = new[]
            {
                OxyColors.Green,
                OxyColors.Blue,
                OxyColors.Purple,
                OxyColors.Orange,
                OxyColors.Teal,
                OxyColors.Gray,
                OxyColors.Red,
                OxyColors.Brown,
                OxyColors.Pink
            };

            int colorIndex = 0;
            foreach (var method in SalesByPaymentMethod)
            {
                pieSeries.Slices.Add(new PieSlice(method.MethodName, (double)method.Amount)
                {
                    Fill = colors[colorIndex % colors.Length]
                });
                colorIndex++;
            }

            PieModel.Series.Add(pieSeries);
            PieModel.InvalidatePlot(true);
        }

        private string GetPaymentMethodName(PaymentType type)
        {
            return type switch
            {
                PaymentType.Dinheiro => "Dinheiro",
                PaymentType.Multibanco => "Multibanco",
                PaymentType.MBWay => "MB Way",
                PaymentType.CartaoCredito => "Cartão Crédito",
                PaymentType.CartaoDebito => "Cartão Débito",
                PaymentType.MPesa => "M-Pesa",
                PaymentType.Emola => "Emola",
                PaymentType.BCI => "BCI",
                PaymentType.BIM => "BIM",
                PaymentType.Ponto24 => "Ponto24",
                PaymentType.Cartao => "Cartão",
                _ => type.ToString()
            };
        }

        private void UpdateDifference()
        {
            decimal difference = _valorContadoAtual - _totalDinheiro;

            txtDiferenca.Text = $"{difference:N2} {CurrencySymbol}";

            if (difference > 0)
            {
                borderDiferenca.Background = new SolidColorBrush(Color.FromRgb(229, 245, 233));
                txtDiferenca.Foreground = Brushes.Green;
                txtDiferencaStatus.Text = "(Sobrou)";
                txtDiferencaStatus.Foreground = Brushes.Green;
            }
            else if (difference < 0)
            {
                borderDiferenca.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238));
                txtDiferenca.Foreground = Brushes.Red;
                txtDiferencaStatus.Text = "(Falta)";
                txtDiferencaStatus.Foreground = Brushes.Red;
            }
            else
            {
                borderDiferenca.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
                txtDiferenca.Foreground = Brushes.Black;
                txtDiferencaStatus.Text = "(OK)";
                txtDiferencaStatus.Foreground = Brushes.Gray;
            }
        }

        private void TxtValorContado_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != ',' && c != '.')
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        private void TxtValorContado_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (decimal.TryParse(txtValorContado.Text.Replace(".", ","), out decimal contado))
            {
                _valorContadoAtual = contado;
                UpdateDifference();
            }
            else
            {
                _valorContadoAtual = 0;
                UpdateDifference();
            }
        }

        // 🔥 MÉTODO PRINCIPAL: Criar e enviar ambos os PDFs
        private async Task<bool> CreateAndSendReportsAsync(DailyClosing closing)
        {
            try
            {
                string restaurantName = _config.RestaurantName ?? "Restaurante";
                string reportTitle = $"FECHO DO DIA - {DateTime.Now:dd/MM/yyyy}";
                string dateRange = $"{_dataInicioContagem:dd/MM/yyyy HH:mm} - {DateTime.Now:dd/MM/yyyy HH:mm}";

                // Preparar cabeçalhos da tabela para o Fecho
                List<string> headers = new List<string>
                {
                    "Item", "Descrição", "Valor", "Observações"
                };

                // Preparar dados para o Fecho
                List<List<string>> dataRows = new List<List<string>>
                {
                    new List<string> { "1", "Total Vendido", _totalVendido.ToString("N2"), "Todos os pagamentos" },
                    new List<string> { "2", "Dinheiro (Registrado)", _totalDinheiro.ToString("N2"), "Pagamentos em dinheiro" },
                    new List<string> { "3", "Outros Pagamentos", _totalOutros.ToString("N2"), "Cartões, MB Way, etc." },
                    new List<string> { "4", "COMPRAS DO DIA", _totalDespesasHoje.ToString("N2"), "Despesas com fornecedores" },
                    new List<string> { "5", "SALDO LÍQUIDO", _saldoLiquido.ToString("N2"),
                        _saldoLiquido >= 0 ? "Lucro do dia" : "Prejuízo do dia" },
                    new List<string> { "6", "Dinheiro (Contado)", _valorContadoAtual.ToString("N2"), "Valor físico contado" },
                    new List<string> { "7", "Diferença no Caixa", (closing.Diferenca).ToString("N2"),
                        closing.Diferenca >= 0 ? "Sobrou" : "Falta" },
                    new List<string> { "8", "Lucro Estimado", _lucroEstimado.ToString("N2"), "Estimativa de 60% de lucro" },
                    new List<string> { "9", "Número de Vendas", _numeroVendas.ToString(), "Quantidade de transações" },
                    new List<string> { "10", "Ticket Médio", _ticketMedio.ToString("N2"), $"Média por venda ({CurrencySymbol})" }
                };

                // Adicionar métodos de pagamento detalhados
                dataRows.Add(new List<string> { "", "", "", "" });
                dataRows.Add(new List<string> { "MÉTODOS DE PAGAMENTO", "", "", "" });

                int methodNum = 1;
                foreach (var method in SalesByPaymentMethod)
                {
                    dataRows.Add(new List<string>
                    {
                        (methodNum++).ToString(),
                        method.MethodName,
                        method.Amount.ToString("N2"),
                        $"{method.Count} transações ({method.Percentage:0.0}%)"
                    });
                }

                // Adicionar detalhes das compras do dia
                if (_comprasHoje.Count > 0)
                {
                    dataRows.Add(new List<string> { "", "", "", "" });
                    dataRows.Add(new List<string> { "COMPRAS DO DIA", "", "", "" });

                    int compraNum = 1;
                    foreach (var compra in _comprasHoje)
                    {
                        dataRows.Add(new List<string>
                        {
                            (compraNum++).ToString(),
                            $"Compra: {compra.PurchaseNumber} - {compra.Supplier?.Name ?? "N/A"}",
                            compra.TotalAmount.ToString("N2"),
                            $"{compra.Items?.Count ?? 0} itens"
                        });
                    }
                }

                // Carregar logo
                byte[] logoBytes = null;
                string logoPath = _config?.LogoPath;

                if (!string.IsNullOrEmpty(logoPath))
                {
                    string fullLogoPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos",
                        logoPath);

                    if (File.Exists(fullLogoPath))
                    {
                        try
                        {
                            logoBytes = File.ReadAllBytes(fullLogoPath);
                        }
                        catch
                        {
                            logoBytes = null;
                        }
                    }
                }

                // Criar pasta para relatórios
                string reportsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "AyGestRest",
                    "Relatórios Fecho");

                Directory.CreateDirectory(reportsFolder);

                // 📊 PDF 1: Fecho do Dia
                string closingFileName = $"Fecho_Dia_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                string closingPdfPath = Path.Combine(reportsFolder, closingFileName);
                ExportToPdf(closingPdfPath, restaurantName, reportTitle, dateRange,
                    headers, dataRows, logoBytes, CurrencySymbol);

                // 📦 PDF 2: Inventário (Produtos + Ingredientes)
                string inventoryFileName = $"Inventario_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
                string inventoryPdfPath = Path.Combine(reportsFolder, inventoryFileName);
                bool inventoryCreated = await CreateInventoryPDF(inventoryPdfPath, restaurantName);

                // Lista de anexos
                List<string> attachments = new List<string> { closingPdfPath };
                if (inventoryCreated && File.Exists(inventoryPdfPath))
                {
                    attachments.Add(inventoryPdfPath);
                }

                // 🔥 ENVIAR EMAIL
                bool emailSent = false;
                if (_config != null && !string.IsNullOrEmpty(_config.EmailAddress) &&
                    !string.IsNullOrEmpty(_config.RecipientEmail) && _config.SendReports)
                {
                    emailSent = await SendEmailWithAttachments(attachments, restaurantName);
                }

                // Mostrar confirmação local
                if (!emailSent || !_config.SendReports)
                {
                    string fileInfo = inventoryCreated ?
                        $"\n\n📋 Inventário:\n{inventoryPdfPath}" : "";

                    MessageBox.Show($"📄 PDFs gerados com sucesso!\n\n" +
                        $"📊 Fecho do Dia:\n{closingPdfPath}" +
                        fileInfo,
                        "PDFs Criados", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return emailSent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao criar relatórios: {ex.Message}");
                MessageBox.Show($"Erro ao criar PDFs: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        // 🔥 MÉTODO: Criar PDF de Inventário
        private async Task<bool> CreateInventoryPDF(string filePath, string restaurantName)
        {
            try
            {
                string reportTitle = "INVENTÁRIO - PRODUTOS E INGREDIENTES";
                string reportDate = $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}";

                // Obter produtos ativos
                var products = await _db.Products
                    .Where(p => p.Active)
                    .OrderBy(p => p.Name)
                    .ToListAsync();

                // Obter ingredientes
                var ingredients = await _db.Ingredients
                    .OrderBy(i => i.Name)
                    .ToListAsync();

                // Preparar cabeçalhos
                List<string> headers = new List<string>
                {
                    "Nº", "Item", "Quantidade", "Tipo"
                };

                // Preparar dados
                List<List<string>> allItems = new List<List<string>>();
                int itemCounter = 1;

                // Adicionar produtos
                foreach (var product in products)
                {
                    allItems.Add(new List<string>
                    {
                        itemCounter.ToString(),
                        product.Name,
                        product.Stock.ToString("N2"),
                        "Produto"
                    });
                    itemCounter++;
                }

                // Adicionar ingredientes
                foreach (var ingredient in ingredients)
                {
                    allItems.Add(new List<string>
                    {
                        itemCounter.ToString(),
                        ingredient.Name,
                        ingredient.Stock.ToString("N2") + " " + (ingredient.Unit ?? "un"),
                        "Ingrediente"
                    });
                    itemCounter++;
                }

                // Adicionar resumo
                allItems.Add(new List<string> { "", "", "", "" });
                allItems.Add(new List<string>
                {
                    "",
                    "📦 TOTAL DE ITENS:",
                    $"{products.Count + ingredients.Count} itens",
                    ""
                });

                // Carregar logo
                byte[] logoBytes = null;
                string logoPath = _config?.LogoPath;

                if (!string.IsNullOrEmpty(logoPath))
                {
                    string fullLogoPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AyGestRest",
                        "Logos",
                        logoPath);

                    if (File.Exists(fullLogoPath))
                    {
                        logoBytes = File.ReadAllBytes(fullLogoPath);
                    }
                }

                // Criar PDF
                CreateSimpleListPDF(filePath, restaurantName, reportTitle, reportDate,
                    headers, allItems, logoBytes, CurrencySymbol,
                    products.Count, ingredients.Count);

                return File.Exists(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao criar inventário: {ex.Message}");
                return false;
            }
        }

        // 🔥 MÉTODO: Exportar para PDF (Fecho do Dia)
        private void ExportToPdf(string filePath, string restaurantName, string reportTitle, string dateRange,
            List<string> headers, List<List<string>> dataRows, byte[] logoBytes, string currencySymbol)
        {
            var pdf = new PdfDocument();
            pdf.Info.Title = reportTitle;
            pdf.Info.Author = restaurantName;
            pdf.Info.Subject = "Relatório Gerencial";

            var primaryColor = XColor.FromArgb(25, 118, 210);
            var lightGray = XColor.FromArgb(250, 250, 250);
            var mediumGray = XColor.FromArgb(245, 245, 245);
            var borderGray = XColor.FromArgb(224, 224, 224);
            var textGray = XColor.FromArgb(117, 117, 117);
            var successColor = XColor.FromArgb(0, 200, 83);
            var dangerColor = XColor.FromArgb(255, 82, 82);

            string fontName = "Arial";
            var titleFont = new XFont(fontName, 22, XFontStyle.Bold);
            var companyNameFont = new XFont(fontName, 18, XFontStyle.Bold);
            var companyInfoFont = new XFont(fontName, 9, XFontStyle.Regular);
            var reportTitleFont = new XFont(fontName, 16, XFontStyle.Bold);
            var dateFont = new XFont(fontName, 10, XFontStyle.Italic);
            var headerFont = new XFont(fontName, 11, XFontStyle.Bold);
            var normalFont = new XFont(fontName, 10, XFontStyle.Regular);
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
                        double logoWidth = Math.Min(150, image.PixelWidth);
                        double logoHeight = logoWidth * image.PixelHeight / image.PixelWidth;
                        double x = (page.Width.Point - logoWidth) / 2;
                        gfx.DrawImage(image, x, y, logoWidth, logoHeight);
                        y += logoHeight + 20;
                    }
                }
                catch
                {
                    gfx.DrawString("🍽️", new XFont(fontName, 36, XFontStyle.Bold),
                        XBrushes.Gray, new XPoint(page.Width.Point / 2 - 15, y));
                    y += 50;
                }
            }
            else
            {
                gfx.DrawString("🍽️", new XFont(fontName, 36, XFontStyle.Bold),
                    XBrushes.Gray, new XPoint(page.Width.Point / 2 - 15, y));
                y += 50;
            }

            // Nome do restaurante
            string companyName = restaurantName.ToUpper();
            var companySize = gfx.MeasureString(companyName, companyNameFont);
            gfx.DrawString(companyName, companyNameFont, XBrushes.Black,
                new XPoint((page.Width.Point - companySize.Width) / 2, y));
            y += 30;

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
            y += 35;

            // Linha decorativa
            gfx.DrawLine(new XPen(primaryColor, 2), margin + 50, y,
                page.Width.Point - margin - 50, y);
            y += 25;

            // Título do relatório
            var titleSize = gfx.MeasureString(reportTitle, reportTitleFont);
            gfx.DrawString(reportTitle, reportTitleFont, new XSolidBrush(primaryColor),
                new XPoint((page.Width.Point - titleSize.Width) / 2, y));
            y += 30;

            // Informações do período
            string periodText = $"📅 Período: {dateRange} • ⏰ Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm} • 📊 {dataRows.Count} registos";
            var periodSize = gfx.MeasureString(periodText, dateFont);
            gfx.DrawString(periodText, dateFont, XBrushes.Gray,
                new XPoint((page.Width.Point - periodSize.Width) / 2, y));
            y += 40;

            // ===== TABELA =====
            int colCount = headers.Count;
            double availableWidth = page.Width.Point - 2 * margin;
            double colWidth = Math.Max(availableWidth / colCount, 80);

            void DrawTableHeader()
            {
                double x = margin;
                var headerRect = new XRect(margin, y, colCount * colWidth, 30);
                gfx.DrawRectangle(new XSolidBrush(primaryColor), headerRect);
                gfx.DrawRectangle(new XPen(XColors.LightGray, 0.5), headerRect);

                foreach (var header in headers)
                {
                    string text = header.Length > 25 ? header.Substring(0, 22) + "..." : header;
                    var textSize = gfx.MeasureString(text, headerFont);
                    gfx.DrawString(text, headerFont, XBrushes.White,
                        new XPoint(x + (colWidth - textSize.Width) / 2, y + 9));

                    if (x > margin)
                    {
                        gfx.DrawLine(new XPen(XColors.White, 0.5), x, y, x, y + 30);
                    }
                    x += colWidth;
                }
                y += 30;
            }

            void DrawTableRow(List<string> row, bool alternate, int rowNumber)
            {
                double x = margin;
                var fill = alternate ? new XSolidBrush(mediumGray) : XBrushes.White;

                gfx.DrawRectangle(fill, margin, y, colCount * colWidth, 25);
                gfx.DrawLine(new XPen(borderGray, 0.5), margin, y,
                    margin + colCount * colWidth, y);

                for (int i = 0; i < row.Count && i < headers.Count; i++)
                {
                    string value = row[i] ?? "-";
                    XBrush textBrush = XBrushes.Black;

                    // Formatação especial para valores monetários
                    if (decimal.TryParse(value.Replace(currencySymbol, "").Trim(),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out decimal numericValue))
                    {
                        bool isCurrency = headers[i].ToLower().Contains("valor") ||
                                         headers[i].ToLower().Contains("total") ||
                                         headers[i].ToLower().Contains("preço") ||
                                         headers[i].ToLower().Contains("montante");

                        if (isCurrency)
                        {
                            value = $"{currencySymbol} {numericValue:N2}";

                            // Colorir saldo líquido
                            if (row.Count > 1 && row[1].Contains("SALDO LÍQUIDO"))
                            {
                                textBrush = numericValue >= 0 ? new XSolidBrush(successColor) : new XSolidBrush(dangerColor);
                            }
                        }
                        else
                        {
                            value = numericValue.ToString("N2");
                        }
                    }

                    string displayText = value;
                    var textSize = gfx.MeasureString(value, normalFont);

                    if (textSize.Width > colWidth - 16)
                    {
                        for (int len = value.Length; len > 0; len--)
                        {
                            displayText = value.Substring(0, len) + "...";
                            if (gfx.MeasureString(displayText, normalFont).Width <= colWidth - 16)
                                break;
                        }
                    }

                    // Alinhamento
                    double textX = x + 8;
                    if (decimal.TryParse(value.Replace(currencySymbol, "").Trim(), out _))
                    {
                        textX = x + colWidth - gfx.MeasureString(displayText, normalFont).Width - 8;
                    }

                    gfx.DrawString(displayText, normalFont, textBrush,
                        new XPoint(textX, y + 7));

                    if (i < headers.Count - 1)
                    {
                        gfx.DrawLine(new XPen(borderGray, 0.5),
                            x + colWidth, y, x + colWidth, y + 25);
                    }
                    x += colWidth;
                }

                y += 25;
            }

            DrawTableHeader();
            bool alternate = false;
            int currentRow = 1;

            foreach (var row in dataRows)
            {
                if (y > page.Height.Point - margin - 60)
                {
                    string pageNum = $"Página {pdf.PageCount}";
                    var pageNumSize = gfx.MeasureString(pageNum, footerFont);
                    gfx.DrawString(pageNum, footerFont, XBrushes.Gray,
                        new XPoint((page.Width.Point - pageNumSize.Width) / 2, page.Height.Point - 20));

                    StartNewPage();
                    DrawTableHeader();
                }

                DrawTableRow(row, alternate, currentRow);
                alternate = !alternate;
                currentRow++;
            }

            // Rodapé
            y += 10;
            gfx.DrawLine(new XPen(borderGray, 1), margin, y, page.Width.Point - margin, y);
            y += 15;

            string footerText = $"{restaurantName} • Sistema de Gestão • {DateTime.Now:dd/MM/yyyy HH:mm} • Página {pdf.PageCount}";
            var footerSize = gfx.MeasureString(footerText, footerFont);
            gfx.DrawString(footerText, footerFont, XBrushes.Gray,
                new XPoint((page.Width.Point - footerSize.Width) / 2, page.Height.Point - 20));

            pdf.Save(filePath);
            pdf.Close();
        }

        // 🔥 MÉTODO: Criar PDF simples com lista (para inventário)
        private void CreateSimpleListPDF(string filePath, string restaurantName, string reportTitle,
            string reportDate, List<string> headers, List<List<string>> allItems,
            byte[] logoBytes, string currencySymbol, int productCount, int ingredientCount)
        {
            var pdf = new PdfDocument();
            pdf.Info.Title = reportTitle;
            pdf.Info.Author = restaurantName;
            pdf.Info.Subject = "Lista de Produtos e Ingredientes";

            var primaryColor = XColor.FromArgb(25, 118, 210);
            var borderGray = XColor.FromArgb(224, 224, 224);
            var textGray = XColor.FromArgb(117, 117, 117);

            string fontName = "Arial";
            var titleFont = new XFont(fontName, 18, XFontStyle.Bold);
            var companyNameFont = new XFont(fontName, 16, XFontStyle.Bold);
            var dateFont = new XFont(fontName, 10, XFontStyle.Regular);
            var headerFont = new XFont(fontName, 10, XFontStyle.Bold);
            var normalFont = new XFont(fontName, 9, XFontStyle.Regular);
            var footerFont = new XFont(fontName, 8, XFontStyle.Italic);

            XGraphics gfx = null;
            PdfPage page = null;
            double y = 0;
            double margin = 30;

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
                    var miniHeaderFont = new XFont(fontName, 9, XFontStyle.Regular);
                    gfx.DrawString(restaurantName, miniHeaderFont, XBrushes.DarkBlue,
                        new XPoint(margin, margin - 10));
                    gfx.DrawString($"Página {pdf.PageCount}", miniHeaderFont, XBrushes.Gray,
                        new XPoint(page.Width.Point - margin - 50, margin - 10));
                    y = margin + 10;
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
                        double logoWidth = Math.Min(80, image.PixelWidth);
                        double logoHeight = logoWidth * image.PixelHeight / image.PixelWidth;
                        double x = (page.Width.Point - logoWidth) / 2;
                        gfx.DrawImage(image, x, y, logoWidth, logoHeight);
                        y += logoHeight + 10;
                    }
                }
                catch
                {
                    gfx.DrawString("📋", new XFont(fontName, 24, XFontStyle.Bold),
                        XBrushes.Gray, new XPoint(page.Width.Point / 2 - 12, y));
                    y += 30;
                }
            }
            else
            {
                gfx.DrawString("📋", new XFont(fontName, 24, XFontStyle.Bold),
                    XBrushes.Gray, new XPoint(page.Width.Point / 2 - 12, y));
                y += 30;
            }

            // Nome do restaurante
            string companyName = restaurantName.ToUpper();
            var companySize = gfx.MeasureString(companyName, companyNameFont);
            gfx.DrawString(companyName, companyNameFont, XBrushes.Black,
                new XPoint((page.Width.Point - companySize.Width) / 2, y));
            y += 25;

            // Título
            var titleSize = gfx.MeasureString(reportTitle, titleFont);
            gfx.DrawString(reportTitle, titleFont, new XSolidBrush(primaryColor),
                new XPoint((page.Width.Point - titleSize.Width) / 2, y));
            y += 25;

            // Informações
            string infoText = $"📅 {reportDate} • 📦 {productCount} Produtos • 🧪 {ingredientCount} Ingredientes";
            var infoSize = gfx.MeasureString(infoText, dateFont);
            gfx.DrawString(infoText, dateFont, XBrushes.Gray,
                new XPoint((page.Width.Point - infoSize.Width) / 2, y));
            y += 30;

            gfx.DrawLine(new XPen(borderGray, 0.5), margin, y, page.Width.Point - margin, y);
            y += 20;

            // ===== LISTA =====
            int colCount = headers.Count;
            double availableWidth = page.Width.Point - 2 * margin;

            double[] columnWidths = new double[]
            {
                40,
                availableWidth - 180,
                80,
                60
            };

            // Cabeçalho
            double currentX = margin;
            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i];
                gfx.DrawString(header, headerFont, XBrushes.Black,
                    new XPoint(currentX + 5, y + 5));
                gfx.DrawLine(new XPen(borderGray, 0.5), currentX, y + 20,
                    currentX + columnWidths[i], y + 20);
                currentX += columnWidths[i];
            }
            y += 25;

            // Itens
            foreach (var item in allItems)
            {
                if (y > page.Height.Point - margin - 30)
                {
                    string pageNum = $"Página {pdf.PageCount}";
                    var pageNumSize = gfx.MeasureString(pageNum, footerFont);
                    gfx.DrawString(pageNum, footerFont, XBrushes.Gray,
                        new XPoint((page.Width.Point - pageNumSize.Width) / 2, page.Height.Point - 20));

                    StartNewPage();

                    currentX = margin;
                    for (int i = 0; i < headers.Count; i++)
                    {
                        string header = headers[i];
                        gfx.DrawString(header, headerFont, XBrushes.Black,
                            new XPoint(currentX + 5, y + 5));
                        gfx.DrawLine(new XPen(borderGray, 0.5), currentX, y + 20,
                            currentX + columnWidths[i], y + 20);
                        currentX += columnWidths[i];
                    }
                    y += 25;
                }

                currentX = margin;
                for (int i = 0; i < item.Count && i < headers.Count; i++)
                {
                    string value = item[i] ?? "";
                    XBrush textBrush = XBrushes.Black;

                    if (i == 3)
                    {
                        if (value == "Produto")
                            textBrush = new XSolidBrush(XColor.FromArgb(0, 100, 0));
                        else if (value == "Ingrediente")
                            textBrush = new XSolidBrush(XColor.FromArgb(0, 0, 139));
                    }

                    gfx.DrawString(value, normalFont, textBrush,
                        new XPoint(currentX + 5, y + 5));

                    currentX += columnWidths[i];
                }

                gfx.DrawLine(new XPen(borderGray, 0.2), margin, y + 15,
                    page.Width.Point - margin, y + 15);

                y += 18;
            }

            // Rodapé
            y += 10;
            gfx.DrawLine(new XPen(borderGray, 0.5), margin, y, page.Width.Point - margin, y);
            y += 15;

            string footerText = $"{restaurantName} • Lista de Itens • Página {pdf.PageCount} • {DateTime.Now:dd/MM/yyyy HH:mm}";
            var footerSize = gfx.MeasureString(footerText, footerFont);
            gfx.DrawString(footerText, footerFont, XBrushes.Gray,
                new XPoint((page.Width.Point - footerSize.Width) / 2, page.Height.Point - 20));

            pdf.Save(filePath);
            pdf.Close();
        }

        // 🔥 MÉTODO: Enviar email com múltiplos anexos
        private async Task<bool> SendEmailWithAttachments(List<string> attachmentPaths, string restaurantName)
        {
            try
            {
                if (_config == null || !_config.SendReports)
                    return false;

                if (string.IsNullOrEmpty(_config.EmailAddress) ||
                    string.IsNullOrEmpty(_config.RecipientEmail))
                    return false;

                List<string> validAttachments = new List<string>();
                foreach (var attachment in attachmentPaths)
                {
                    if (File.Exists(attachment))
                        validAttachments.Add(attachment);
                }

                if (validAttachments.Count == 0)
                    return false;

                // Construir corpo do email com despesas
                string subject = $"📊 Fecho do Dia - {restaurantName} - {DateTime.Now:dd/MM/yyyy}";

                string body = $@"
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; color: #333; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }}
        .success {{ color: #00C853; }}
        .warning {{ color: #FF9100; }}
        .danger {{ color: #FF5252; }}
        .info {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #667eea; margin-bottom: 20px; }}
        table {{ border-collapse: collapse; width: 100%; margin-bottom: 20px; }}
        th {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 12px; text-align: left; }}
        td {{ padding: 10px; border-bottom: 1px solid #ddd; }}
        .total {{ font-size: 18px; font-weight: bold; color: #667eea; text-align: right; margin-top: 20px; }}
        .footer {{ margin-top: 30px; padding-top: 20px; border-top: 1px solid #ddd; font-size: 12px; color: #666; }}
    </style>
</head>
<body>
    <div class='header'>
        <h2>📊 FECHO DO DIA CONCLUÍDO</h2>
        <p>{restaurantName} - {DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>

    <div class='info'>
        <h3>📋 RESUMO FINANCEIRO</h3>
        <table>
            <tr>
                <td><strong>Período:</strong></td>
                <td>{_dataInicioContagem:dd/MM/yyyy HH:mm} - {DateTime.Now:HH:mm}</td>
            </tr>
            <tr>
                <td><strong>Total Vendido:</strong></td>
                <td><span style='font-size: 16px; font-weight: bold; color: #667eea;'>{CurrencySymbol} {_totalVendido:N2}</span></td>
            </tr>
            <tr>
                <td><strong>Dinheiro Registrado:</strong></td>
                <td>{CurrencySymbol} {_totalDinheiro:N2}</td>
            </tr>
            <tr>
                <td><strong>Outros Pagamentos:</strong></td>
                <td>{CurrencySymbol} {_totalOutros:N2}</td>
            </tr>
            <tr>
                <td><strong>📦 COMPRAS DO DIA:</strong></td>
                <td><span style='font-weight: bold; color: #FF6B6B;'>{CurrencySymbol} {_totalDespesasHoje:N2}</span></td>
            </tr>
            <tr>
                <td><strong>💷 SALDO LÍQUIDO:</strong></td>
                <td><span style='font-size: 18px; font-weight: bold; color: {(_saldoLiquido >= 0 ? "#00C853" : "#FF5252")};'>{CurrencySymbol} {_saldoLiquido:N2}</span></td>
            </tr>
            <tr>
                <td><strong>Dinheiro Contado:</strong></td>
                <td>{CurrencySymbol} {_valorContadoAtual:N2}</td>
            </tr>
            <tr>
                <td><strong>Diferença no Caixa:</strong></td>
                <td><span class='{(_valorContadoAtual - _totalDinheiro >= 0 ? "success" : "danger")}'>{CurrencySymbol} {(_valorContadoAtual - _totalDinheiro):N2}</span></td>
            </tr>
            <tr>
                <td><strong>Número de Vendas:</strong></td>
                <td>{_numeroVendas}</td>
            </tr>
            <tr>
                <td><strong>Ticket Médio:</strong></td>
                <td>{CurrencySymbol} {_ticketMedio:N2}</td>
            </tr>
        </table>
    </div>";

                // Adicionar detalhes das compras
                if (_comprasHoje.Count > 0)
                {
                    body += $@"
    <div class='info'>
        <h3>📦 COMPRAS DO DIA ({_comprasHoje.Count})</h3>
        <table>
            <thead>
                <tr>
                    <th>Nº Compra</th>
                    <th>Fornecedor</th>
                    <th>Hora</th>
                    <th>Itens</th>
                    <th>Valor</th>
                </tr>
            </thead>
            <tbody>";

                    foreach (var compra in _comprasHoje)
                    {
                        body += $@"
                <tr>
                    <td>{compra.PurchaseNumber}</td>
                    <td>{compra.Supplier?.Name ?? "N/A"}</td>
                    <td>{compra.CreatedAt:HH:mm}</td>
                    <td>{compra.Items?.Count ?? 0}</td>
                    <td><strong>{CurrencySymbol} {compra.TotalAmount:N2}</strong></td>
                </tr>";
                    }

                    body += $@"
            </tbody>
        </table>
    </div>";
                }

                // Métodos de pagamento
                body += $@"
    <div class='info'>
        <h3>💳 MÉTODOS DE PAGAMENTO</h3>
        <table>
            <thead>
                <tr>
                    <th>Método</th>
                    <th>Transações</th>
                    <th>Valor</th>
                    <th>%</th>
                </tr>
            </thead>
            <tbody>";

                foreach (var method in SalesByPaymentMethod)
                {
                    body += $@"
                <tr>
                    <td>{method.MethodName}</td>
                    <td>{method.Count}</td>
                    <td>{CurrencySymbol} {method.Amount:N2}</td>
                    <td>{method.Percentage:0.0}%</td>
                </tr>";
                }

                body += $@"
            </tbody>
        </table>
    </div>

    <p><strong>Anexos:</strong></p>
    <ul>";

                foreach (var attachment in validAttachments)
                {
                    string fileName = Path.GetFileName(attachment);
                    if (fileName.Contains("Fecho_Dia"))
                        body += $"<li>📊 {fileName} - Relatório do Fecho do Dia</li>";
                    else if (fileName.Contains("Inventario"))
                        body += $"<li>📦 {fileName} - Inventário de Produtos e Ingredientes</li>";
                    else
                        body += $"<li>📄 {fileName}</li>";
                }

                body += $@"
    </ul>

    <div class='footer'>
        <p><strong>{restaurantName}</strong></p>
        <p>Sistema de Gestão AyGestRest</p>
        <p>{DateTime.Now:dd/MM/yyyy HH:mm}</p>
    </div>
</body>
</html>";

                // Enviar email
                return await SendAllAttachmentsInOneEmail(subject, body, validAttachments);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao enviar email: {ex.Message}");
                return false;
            }
        }

        // 🔥 MÉTODO: Enviar todos os anexos em um único email
        private async Task<bool> SendAllAttachmentsInOneEmail(string subject, string body, List<string> attachmentPaths)
        {
            try
            {
                using (var message = new System.Net.Mail.MailMessage())
                {
                    message.From = new System.Net.Mail.MailAddress(_config.EmailAddress,
                        _config.RestaurantName ?? "AyGestRest");
                    message.To.Add(_config.RecipientEmail);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;

                    foreach (var attachmentPath in attachmentPaths)
                    {
                        if (File.Exists(attachmentPath))
                        {
                            var attachment = new System.Net.Mail.Attachment(attachmentPath);
                            message.Attachments.Add(attachment);
                        }
                    }

                    using (var smtpClient = new System.Net.Mail.SmtpClient())
                    {
                        smtpClient.Host = string.IsNullOrEmpty(_config.SMTPServer) ?
                            "smtp.gmail.com" : _config.SMTPServer;
                        smtpClient.Port = _config.SMTPPort > 0 ? _config.SMTPPort : 587;
                        smtpClient.EnableSsl = _config.UseSSL;
                        smtpClient.DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network;
                        smtpClient.UseDefaultCredentials = false;
                        smtpClient.Credentials = new System.Net.NetworkCredential(
                            _config.EmailAddress,
                            _config.EmailPassword);
                        smtpClient.Timeout = 30000;

                        System.Net.ServicePointManager.SecurityProtocol =
                            System.Net.SecurityProtocolType.Tls12 |
                            System.Net.SecurityProtocolType.Tls11 |
                            System.Net.SecurityProtocolType.Tls;

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

        // 🔥 MÉTODO: Verificar compras pendentes
        private async Task CheckPendingPurchasesAsync()
        {
            try
            {
                var today = DateTime.Today;
                var pendingCount = await _db.PurchaseOrders
                    .Where(p => p.CreatedAt.Date == today && p.Status == "Pendente")
                    .CountAsync();

                if (pendingCount > 0)
                {
                    var result = MessageBox.Show(
                        $"⚠️ ATENÇÃO: Existem {pendingCount} compra(s) PENDENTE(S) hoje!\n\n" +
                        $"Estas compras NÃO estão incluídas no cálculo de despesas.\n" +
                        $"Deseja visualizar as compras pendentes agora?",
                        "Compras Pendentes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        BtnVerComprasHoje_Click(null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro ao verificar compras pendentes: {ex.Message}");
            }
        }

        // 🔥 MÉTODO: Imprimir recibo profissional
        private async Task PrintProfessionalReceiptAsync(DailyClosing closing)
        {
            if (string.IsNullOrEmpty(_config?.PrinterName))
            {
                MessageBox.Show("Impressora não configurada.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var sb = new StringBuilder();

                sb.Append("\x1B\x40");
                sb.Append("\x1B\x61\x01");
                sb.Append("\x1B\x21\x30");
                sb.AppendLine(_config.RestaurantName.ToUpper());
                sb.Append("\x1B\x21\x00");

                sb.AppendLine(new string('=', 42));
                sb.Append("\x1B\x61\x01");
                sb.AppendLine("FECHO DO DIA - RESUMO FINANCEIRO");
                sb.AppendLine(new string('=', 42));

                sb.Append("\x1B\x61\x00");
                sb.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy}");
                sb.AppendLine($"Hora: {DateTime.Now:HH:mm}");
                sb.AppendLine($"Período: {_dataInicioContagem:HH:mm} - {DateTime.Now:HH:mm}");
                sb.AppendLine(new string('-', 42));

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("RESUMO FINANCEIRO");
                sb.Append("\x1B\x61\x00");

                AppendReceiptLine(sb, "TOTAL VENDIDO:", $"{_totalVendido:N2} {CurrencySymbol}");
                AppendReceiptLine(sb, "(-) COMPRAS:", $"{_totalDespesasHoje:N2} {CurrencySymbol}");
                sb.AppendLine(new string('-', 42));

                sb.Append("\x1B\x45\x01");
                if (_saldoLiquido >= 0)
                {
                    AppendReceiptLine(sb, "SALDO LÍQUIDO:", $"{_saldoLiquido:N2} {CurrencySymbol}");
                }
                else
                {
                    AppendReceiptLine(sb, "SALDO LÍQUIDO:", $"-{Math.Abs(_saldoLiquido):N2} {CurrencySymbol}");
                }
                sb.Append("\x1B\x45\x00");

                sb.AppendLine(new string('-', 42));

                AppendReceiptLine(sb, "Dinheiro:", $"{_totalDinheiro:N2} {CurrencySymbol}");
                AppendReceiptLine(sb, "Outros Pagam.:", $"{_totalOutros:N2} {CurrencySymbol}");
                AppendReceiptLine(sb, "Lucro Bruto:", $"{_lucroEstimado:N2} {CurrencySymbol}");

                sb.AppendLine(new string('-', 42));

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("CONTAGEM DE CAIXA");
                sb.Append("\x1B\x61\x00");

                AppendReceiptLine(sb, "Dinheiro Reg.:", $"{_totalDinheiro:N2} {CurrencySymbol}");
                AppendReceiptLine(sb, "Dinheiro Cont.:", $"{_valorContadoAtual:N2} {CurrencySymbol}");

                sb.Append("\x1B\x45\x01");
                string diferencaText = $"DIFERENÇA: {(_valorContadoAtual - _totalDinheiro):N2} {CurrencySymbol}";
                if ((_valorContadoAtual - _totalDinheiro) >= 0)
                {
                    sb.AppendLine("✅ " + diferencaText);
                }
                else
                {
                    sb.AppendLine("❌ " + diferencaText);
                }
                sb.Append("\x1B\x45\x00");

                sb.AppendLine(new string('-', 42));

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("ESTATÍSTICAS");
                sb.Append("\x1B\x61\x00");

                AppendReceiptLine(sb, "Nº de Vendas:", _numeroVendas.ToString("N0"));
                AppendReceiptLine(sb, "Nº de Compras:", _comprasHoje.Count.ToString("N0"));
                AppendReceiptLine(sb, "Ticket Médio:", $"{_ticketMedio:N2} {CurrencySymbol}");

                sb.AppendLine(new string('-', 42));

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("MÉTODOS DE PAGAMENTO");
                sb.Append("\x1B\x61\x00");

                foreach (var method in SalesByPaymentMethod)
                {
                    sb.AppendLine($"{method.MethodName,-20} {method.Amount,10:N2} {CurrencySymbol}");
                }

                sb.AppendLine(new string('-', 42));
                sb.AppendLine($"TOTAL:{" ",25}{_totalVendido,10:N2} {CurrencySymbol}");

                sb.AppendLine(new string('=', 42));

                sb.Append("\x1B\x61\x01");
                sb.AppendLine("FECHO CONCLUÍDO COM SUCESSO");
                sb.Append("\x1B\x61\x00");

                sb.AppendLine("\nObrigado pelo trabalho de hoje!");
                sb.AppendLine(_config.Address ?? "");
                sb.AppendLine($"Tel: {_config.Phone ?? ""}");
                sb.AppendLine($"NIF: {_config.NIF ?? ""}");

                sb.AppendLine("\n\n\n");
                sb.Append("\x1B\x69");
                sb.Append("\x1B\x64\x03");

                bool success = RawPrinterHelper.SendStringToPrinter(_config.PrinterName, sb.ToString());

                if (!success)
                {
                    SaveReceiptToFile(sb.ToString());
                    MessageBox.Show("Recibo salvo em arquivo (impressora não disponível).",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na impressão: {ex.Message}");
                MessageBox.Show("Erro ao imprimir recibo. Verifique a impressora.",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveReceiptToFile(string receiptText)
        {
            try
            {
                string fileName = $"Fecho_Dia_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
                File.WriteAllText(filePath, receiptText);
                Process.Start("notepad.exe", filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao salvar arquivo: {ex.Message}");
            }
        }

        private void AppendReceiptLine(StringBuilder sb, string label, string value)
        {
            sb.AppendLine($"{label,-20} {value,20}");
        }

        private bool ValidateCount()
        {
            if (string.IsNullOrWhiteSpace(txtValorContado.Text))
            {
                MessageBox.Show("Por favor, insira o valor contado.", "Validação",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                txtValorContado.Focus();
                return false;
            }

            if (_valorContadoAtual < 0)
            {
                MessageBox.Show("O valor contado não pode ser negativo.", "Validação",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void UpdateUIState()
        {
            btnConfirmar.Visibility = Visibility.Visible;
            btnCancelar.Visibility = Visibility.Visible;
        }

        private void BtnImprimirResumo_Click(object sender, RoutedEventArgs e)
        {
            var closing = new DailyClosing
            {
                Data = DateTime.Now,
                TotalVendido = _totalVendido,
                TotalDinheiro = _totalDinheiro,
                TotalOutrosPagamentos = _totalOutros,
                LucroBrutoEstimado = _lucroEstimado,
                ValorContado = _valorContadoAtual,
                Diferenca = _valorContadoAtual - _totalDinheiro
            };

            PrintProfessionalReceiptAsync(closing).ConfigureAwait(false);
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            ExportToCsv();
        }

        private void ExportToCsv()
        {
            try
            {
                string fileName = $"Fecho_Dia_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

                var csv = new StringBuilder();

                csv.AppendLine($"Restaurante: {_config.RestaurantName}");
                csv.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm}");
                csv.AppendLine($"Período: {_dataInicioContagem:HH:mm} - {DateTime.Now:HH:mm}");
                csv.AppendLine();
                csv.AppendLine("RESUMO FINANCEIRO");
                csv.AppendLine($"Total Vendido;{_totalVendido:N2};{CurrencySymbol}");
                csv.AppendLine($"Dinheiro;{_totalDinheiro:N2};{CurrencySymbol}");
                csv.AppendLine($"Outros Pagamentos;{_totalOutros:N2};{CurrencySymbol}");
                csv.AppendLine($"Compras do Dia;{_totalDespesasHoje:N2};{CurrencySymbol}");
                csv.AppendLine($"Saldo Líquido;{_saldoLiquido:N2};{CurrencySymbol}");
                csv.AppendLine($"Lucro Bruto Estimado;{_lucroEstimado:N2};{CurrencySymbol}");
                csv.AppendLine($"Valor Contado;{_valorContadoAtual:N2};{CurrencySymbol}");
                csv.AppendLine($"Diferença;{_valorContadoAtual - _totalDinheiro:N2};{CurrencySymbol}");
                csv.AppendLine($"Número de Vendas;{_numeroVendas}");
                csv.AppendLine($"Ticket Médio;{_ticketMedio:N2};{CurrencySymbol}");
                csv.AppendLine();
                csv.AppendLine("MÉTODOS DE PAGAMENTO");
                csv.AppendLine("Método;Quantidade;Valor;%");

                foreach (var method in SalesByPaymentMethod)
                {
                    csv.AppendLine($"{method.MethodName};{method.Count};{method.Amount:N2};{method.Percentage:0.0}%");
                }

                File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
                Process.Start("notepad.exe", filePath);

                MessageBox.Show($"Relatório exportado para:\n{filePath}", "Exportado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            var parentWindow = Window.GetWindow(this);
            if (parentWindow != null)
            {
                parentWindow.Close();
            }
        }
    }

    public class PaymentMethodSalesViewModel
    {
        public string MethodName { get; set; } = "";
        public decimal Amount { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}