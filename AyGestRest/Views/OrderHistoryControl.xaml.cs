using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Printing;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AyGestRest.Views
{
    public partial class OrderHistoryControl : UserControl
    {
        private readonly AyGestRestContext _db = new();
        private readonly ObservableCollection<OrderDisplay> _pedidosFiltrados = new();
        private RestaurantConfig _restaurantConfig = new();
        private decimal _totalNumerario = 0;
        private decimal _totalEletronico = 0;

        // Classe wrapper para exibição
        public class OrderDisplay
        {
            public Order Order { get; set; }
            public string TalaoFormatado => $"T{Order.TalaoNumber:D6}";
            public string DataFormatada => Order.CloseDate?.ToString("dd/MM") ?? "";
            public string HoraFormatada => Order.CloseDate?.ToString("HH:mm") ?? "";
            public string MesaFormatada => Order.Table?.Number ?? "BALCÃO";
            public string PagamentoFormatado => Order.Payments?.FirstOrDefault()?.Type.ToString() ?? "N/D";
            public string UtilizadorFormatado => Order.User?.Username ?? "N/A";
            public string TotalFormatado { get; set; } = "0,00 MT";
            public decimal TotalDecimal { get; set; }
        }

        public OrderHistoryControl()
        {
            InitializeComponent();
            lstPedidos.ItemsSource = _pedidosFiltrados;
            Loaded += OrderHistoryControl_Loaded;
        }

        private async void OrderHistoryControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await CarregarConfiguracaoAsync();
                await CarregarFiltrosAsync();
                await AplicarFiltrosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar histórico: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task CarregarConfiguracaoAsync()
        {
            try
            {
                _restaurantConfig = await _db.RestaurantConfigs.FirstOrDefaultAsync();
                if (_restaurantConfig == null)
                {
                    _restaurantConfig = new RestaurantConfig
                    {
                        RestaurantName = "RESTAURANTE",
                        CurrencySymbol = "MT",
                        Address = "",
                        Phone = "",
                        NIF = ""
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar configuração: {ex.Message}");
                _restaurantConfig = new RestaurantConfig
                {
                    RestaurantName = "RESTAURANTE",
                    CurrencySymbol = "MT",
                    Address = "",
                    Phone = "",
                    NIF = ""
                };
            }
        }

        private void IndicatorBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.DataContext is OrderDisplay orderDisplay)
            {
                var paymentType = orderDisplay.Order.Payments?.FirstOrDefault()?.Type ?? PaymentType.Dinheiro;
                border.Background = new SolidColorBrush(ObterCorPagamento(paymentType));
            }
        }

        private async Task CarregarFiltrosAsync()
        {
            try
            {
                // Mesas
                cmbMesa.Items.Clear();
                cmbMesa.Items.Add(new ComboBoxItem { Content = "Todas as mesas", Tag = "" });

                var mesas = await _db.RestaurantTables
                    .Where(t => t.Status != TableStatus.Inativa)
                    .Select(t => t.Number)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToListAsync();

                foreach (var mesa in mesas)
                {
                    cmbMesa.Items.Add(new ComboBoxItem
                    {
                        Content = $"Mesa {mesa}",
                        Tag = mesa
                    });
                }

                cmbMesa.SelectedIndex = 0;

                // Utilizadores
                cmbUtilizador.Items.Clear();
                cmbUtilizador.Items.Add(new ComboBoxItem { Content = "Todos os utilizadores", Tag = "" });

                var users = await _db.Users
                    .Where(u => u.IsActive)
                    .Select(u => u.Username)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToListAsync();

                foreach (var user in users)
                {
                    cmbUtilizador.Items.Add(new ComboBoxItem
                    {
                        Content = user,
                        Tag = user
                    });
                }

                cmbUtilizador.SelectedIndex = 0;

                // Formas de pagamento
                cmbPagamento.Items.Clear();
                cmbPagamento.Items.Add(new ComboBoxItem { Content = "Todas as formas", Tag = "" });

                var pagamentos = Enum.GetValues(typeof(PaymentType))
                    .Cast<PaymentType>()
                    .OrderBy(p => p.ToString())
                    .ToList();

                foreach (var pagamento in pagamentos)
                {
                    var descricao = ObterDescricaoPagamento(pagamento);
                    cmbPagamento.Items.Add(new ComboBoxItem
                    {
                        Content = descricao,
                        Tag = pagamento.ToString()
                    });
                }

                cmbPagamento.SelectedIndex = 0;

                // Configurar data para hoje
                dpData.SelectedDate = DateTime.Today;
                txtValorMin.Text = "0";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar filtros: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ObterDescricaoPagamento(PaymentType tipo)
        {
            return tipo switch
            {
                PaymentType.Dinheiro => "💵 Dinheiro",
                PaymentType.Cartao => "💳 Cartão",
                PaymentType.CartaoDebito => "💳 Cartão Débito",
                PaymentType.CartaoCredito => "💳 Cartão Crédito",
                PaymentType.MBWay => "📱 MBWay",
                PaymentType.Multibanco => "🏦 Multibanco",
                PaymentType.MPesa => "📱 M-Pesa",
                PaymentType.Emola => "📱 Emola",
                PaymentType.BCI => "🏦 BCI",
                PaymentType.BIM => "🏦 BIM",
                PaymentType.Ponto24 => "🏦 Ponto24",
                PaymentType.Outro => "📄 Outro",
                _ => tipo.ToString()
            };
        }
        private void BtnImprimir80mm_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is OrderDisplay orderDisplay)
            {
                // Renomeie o método antigo GerarReciboProfissional para GerarRecibo80mm
                GerarReciboProfissional(orderDisplay.Order); // Use o método existente
            }
        }

        private Color ObterCorPagamento(PaymentType tipo)
        {
            return tipo switch
            {
                PaymentType.Dinheiro => Color.FromRgb(76, 175, 80),     // Verde
                PaymentType.Cartao => Color.FromRgb(33, 150, 243),      // Azul
                PaymentType.CartaoDebito => Color.FromRgb(33, 150, 243), // Azul
                PaymentType.CartaoCredito => Color.FromRgb(156, 39, 176), // Roxo
                PaymentType.MBWay => Color.FromRgb(255, 152, 0),        // Laranja
                PaymentType.Multibanco => Color.FromRgb(0, 150, 136),   // Teal
                PaymentType.MPesa => Color.FromRgb(255, 87, 34),        // Laranja escuro
                PaymentType.Emola => Color.FromRgb(103, 58, 183),       // Roxo escuro
                PaymentType.BCI => Color.FromRgb(0, 77, 64),            // Verde escuro
                PaymentType.BIM => Color.FromRgb(183, 28, 28),          // Vermelho
                PaymentType.Ponto24 => Color.FromRgb(245, 127, 23),     // Laranja
                _ => Colors.Gray
            };
        }

        private async void BtnFiltrar_Click(object sender, RoutedEventArgs e)
        {
            await AplicarFiltrosAsync();
        }

        private async void BtnLimparFiltros_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                dpData.SelectedDate = DateTime.Today;
                cmbMesa.SelectedIndex = 0;
                cmbUtilizador.SelectedIndex = 0;
                cmbPagamento.SelectedIndex = 0;
                txtValorMin.Text = "0";
                txtBuscaTalao.Text = "";

                await AplicarFiltrosAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao limpar filtros: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImprimirA4_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is OrderDisplay orderDisplay)
            {
                var result = MessageBox.Show($"Imprimir recibo A4 para Talão {orderDisplay.Order.TalaoNumber:D6}?",
                    "Impressão A4",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    GerarReciboA4(orderDisplay.Order);
                }
            }
        }



        // Método para gerar recibo em formato A4
        // Método completo corrigido para gerar recibo em formato A4
        private void GerarReciboA4(Order order)
        {
            try
            {
                var printDialog = new PrintDialog();

                // Configurar para impressão A4
                printDialog.PrintQueue = LocalPrintServer.GetDefaultPrintQueue();
                printDialog.PrintTicket.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);

                if (printDialog.ShowDialog() == true)
                {
                    // Criar documento A4
                    var doc = new FlowDocument();
                    doc.PageWidth = 793.7; // 21cm em pontos (96 DPI * 21/2.54)
                    doc.PageHeight = 1122.52; // 29.7cm em pontos
                    doc.PagePadding = new Thickness(48); // 1.27cm de margem
                    doc.ColumnWidth = 697.7; // Largura da coluna (largura página - 2*margens)

                    // Título principal
                    var titlePara = new Paragraph();
                    titlePara.Inlines.Add(new Run(_restaurantConfig.RestaurantName.ToUpper()));
                    titlePara.FontSize = 16;
                    titlePara.FontWeight = FontWeights.Bold;
                    titlePara.TextAlignment = TextAlignment.Center;
                    titlePara.Margin = new Thickness(0, 10, 0, 15);
                    doc.Blocks.Add(titlePara);

                    // Subtítulo
                    var subtitlePara = new Paragraph();
                    subtitlePara.Inlines.Add(new Run("SEGUNDA VIA DO RECIBO"));
                    subtitlePara.FontSize = 12;
                    subtitlePara.Foreground = Brushes.DarkGray;
                    subtitlePara.TextAlignment = TextAlignment.Center;
                    subtitlePara.Margin = new Thickness(0, 0, 0, 20);
                    doc.Blocks.Add(subtitlePara);

                    // Tabela de informações
                    var infoTable = new Table();
                    var column1 = new TableColumn { Width = new GridLength(150) };
                    var column2 = new TableColumn { Width = new GridLength(200) };
                    var column3 = new TableColumn { Width = new GridLength(150) };
                    var column4 = new TableColumn { Width = new GridLength(200) };

                    infoTable.Columns.Add(column1);
                    infoTable.Columns.Add(column2);
                    infoTable.Columns.Add(column3);
                    infoTable.Columns.Add(column4);

                    var rowGroup = new TableRowGroup();

                    // Linha 1
                    var row1 = new TableRow();

                    var cell11 = new TableCell(new Paragraph(new Run("Talão:")));
                    cell11.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell11.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell12 = new TableCell(new Paragraph(new Run($"T{order.TalaoNumber:D6}")));

                    var cell13 = new TableCell(new Paragraph(new Run("Data:")));
                    cell13.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell13.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell14 = new TableCell(new Paragraph(new Run(order.CloseDate?.ToString("dd/MM/yyyy HH:mm") ?? "N/A")));

                    row1.Cells.Add(cell11);
                    row1.Cells.Add(cell12);
                    row1.Cells.Add(cell13);
                    row1.Cells.Add(cell14);
                    rowGroup.Rows.Add(row1);

                    // Linha 2
                    var row2 = new TableRow();

                    var cell21 = new TableCell(new Paragraph(new Run("Mesa:")));
                    cell21.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell21.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell22 = new TableCell(new Paragraph(new Run(order.Table?.Number ?? "Balcão")));

                    var cell23 = new TableCell(new Paragraph(new Run("Atendente:")));
                    cell23.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell23.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell24 = new TableCell(new Paragraph(new Run(order.User?.Username ?? "N/A")));

                    row2.Cells.Add(cell21);
                    row2.Cells.Add(cell22);
                    row2.Cells.Add(cell23);
                    row2.Cells.Add(cell24);
                    rowGroup.Rows.Add(row2);

                    // Linha 3
                    var row3 = new TableRow();

                    var cell31 = new TableCell(new Paragraph(new Run("Cliente:")));
                    cell31.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell31.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell32 = new TableCell(new Paragraph(new Run("Consumidor Final")));

                    var cell33 = new TableCell(new Paragraph(new Run("NIF:")));
                    cell33.Blocks.FirstBlock.FontWeight = FontWeights.SemiBold;
                    cell33.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                    var cell34 = new TableCell(new Paragraph(new Run(_restaurantConfig.NIF ?? "N/A")));

                    row3.Cells.Add(cell31);
                    row3.Cells.Add(cell32);
                    row3.Cells.Add(cell33);
                    row3.Cells.Add(cell34);
                    rowGroup.Rows.Add(row3);

                    infoTable.RowGroups.Add(rowGroup);
                    infoTable.Margin = new Thickness(0, 0, 0, 20);
                    doc.Blocks.Add(infoTable);

                    // Divisória
                    var divider1 = new Paragraph();
                    divider1.Inlines.Add(new Run(new string('_', 100)));
                    divider1.FontSize = 10;
                    divider1.Foreground = Brushes.LightGray;
                    divider1.Margin = new Thickness(0, 0, 0, 20);
                    doc.Blocks.Add(divider1);

                    // Título dos itens
                    var itemsTitle = new Paragraph();
                    itemsTitle.Inlines.Add(new Run("ITENS DO PEDIDO"));
                    itemsTitle.FontSize = 14;
                    itemsTitle.FontWeight = FontWeights.Bold;
                    itemsTitle.TextAlignment = TextAlignment.Center;
                    itemsTitle.Margin = new Thickness(0, 0, 0, 15);
                    doc.Blocks.Add(itemsTitle);

                    // Tabela de itens
                    var itemsTable = new Table();
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(350) }); // Descrição
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(100) }); // Qtd
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Preço Unit.
                    itemsTable.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Total

                    var itemsRowGroup = new TableRowGroup();

                    // Cabeçalho da tabela
                    var headerRow = new TableRow();
                    headerRow.Background = Brushes.AliceBlue;

                    var headerCell1 = new TableCell(new Paragraph(new Run("Descrição")));
                    headerCell1.Blocks.FirstBlock.FontWeight = FontWeights.Bold;

                    var headerCell2 = new TableCell(new Paragraph(new Run("Qtd")));
                    headerCell2.Blocks.FirstBlock.FontWeight = FontWeights.Bold;
                    headerCell2.Blocks.FirstBlock.TextAlignment = TextAlignment.Center;

                    var headerCell3 = new TableCell(new Paragraph(new Run("Preço Unit.")));
                    headerCell3.Blocks.FirstBlock.FontWeight = FontWeights.Bold;
                    headerCell3.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                    var headerCell4 = new TableCell(new Paragraph(new Run("Total")));
                    headerCell4.Blocks.FirstBlock.FontWeight = FontWeights.Bold;
                    headerCell4.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                    headerRow.Cells.Add(headerCell1);
                    headerRow.Cells.Add(headerCell2);
                    headerRow.Cells.Add(headerCell3);
                    headerRow.Cells.Add(headerCell4);
                    itemsRowGroup.Rows.Add(headerRow);

                    // Itens
                    decimal totalPedido = 0;
                    foreach (var item in order.Items)
                    {
                        var itemTotal = item.UnitPrice * item.Quantity;
                        totalPedido += itemTotal;

                        var itemRow = new TableRow();

                        var descCell = new TableCell();
                        var descPara = new Paragraph(new Run(item.Name));
                        if (!string.IsNullOrEmpty(item.Notes))
                        {
                            descPara.Inlines.Add(new LineBreak());
                            descPara.Inlines.Add(new Run($"Obs: {item.Notes}")
                            {
                                FontStyle = FontStyles.Italic,
                                FontSize = 10,
                                Foreground = Brushes.DarkGray
                            });
                        }
                        descCell.Blocks.Add(descPara);

                        var qtdCell = new TableCell(new Paragraph(new Run($"x{item.Quantity}")));
                        qtdCell.Blocks.FirstBlock.TextAlignment = TextAlignment.Center;

                        var priceCell = new TableCell(new Paragraph(new Run($"{item.UnitPrice:F2} {_restaurantConfig.CurrencySymbol}")));
                        priceCell.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                        var totalCell = new TableCell(new Paragraph(new Run($"{itemTotal:F2} {_restaurantConfig.CurrencySymbol}")));
                        totalCell.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                        itemRow.Cells.Add(descCell);
                        itemRow.Cells.Add(qtdCell);
                        itemRow.Cells.Add(priceCell);
                        itemRow.Cells.Add(totalCell);

                        itemsRowGroup.Rows.Add(itemRow);
                    }

                    itemsTable.RowGroups.Add(itemsRowGroup);
                    itemsTable.Margin = new Thickness(0, 0, 0, 20);
                    doc.Blocks.Add(itemsTable);

                    // Divisória
                    var divider2 = new Paragraph();
                    divider2.Inlines.Add(new Run(new string('_', 100)));
                    divider2.FontSize = 10;
                    divider2.Foreground = Brushes.LightGray;
                    divider2.Margin = new Thickness(0, 0, 0, 20);
                    doc.Blocks.Add(divider2);

                    // Totais
                    var totalTable = new Table();
                    totalTable.Columns.Add(new TableColumn { Width = new GridLength(400) });
                    totalTable.Columns.Add(new TableColumn { Width = new GridLength(300) });

                    var totalRowGroup = new TableRowGroup();

                    var totalRow = new TableRow();

                    var totalLabelCell = new TableCell(new Paragraph(new Run("TOTAL DO PEDIDO:")));
                    totalLabelCell.Blocks.FirstBlock.FontWeight = FontWeights.Bold;
                    totalLabelCell.Blocks.FirstBlock.FontSize = 14;

                    var totalValueCell = new TableCell(new Paragraph(new Run($"{totalPedido:F2} {_restaurantConfig.CurrencySymbol}")));
                    totalValueCell.Blocks.FirstBlock.FontWeight = FontWeights.Bold;
                    totalValueCell.Blocks.FirstBlock.FontSize = 14;
                    totalValueCell.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                    totalRow.Cells.Add(totalLabelCell);
                    totalRow.Cells.Add(totalValueCell);
                    totalRowGroup.Rows.Add(totalRow);

                    // Formas de pagamento
                    if (order.Payments != null && order.Payments.Any())
                    {
                        foreach (var payment in order.Payments)
                        {
                            var paymentRow = new TableRow();

                            var paymentLabelCell = new TableCell(new Paragraph(new Run(ObterDescricaoPagamento(payment.Type))));
                            paymentLabelCell.Blocks.FirstBlock.FontSize = 12;
                            paymentLabelCell.Blocks.FirstBlock.Foreground = Brushes.DarkSlateGray;

                            var paymentValueCell = new TableCell(new Paragraph(new Run($"{payment.Amount:F2} {_restaurantConfig.CurrencySymbol}")));
                            paymentValueCell.Blocks.FirstBlock.FontSize = 12;
                            paymentValueCell.Blocks.FirstBlock.TextAlignment = TextAlignment.Right;

                            paymentRow.Cells.Add(paymentLabelCell);
                            paymentRow.Cells.Add(paymentValueCell);
                            totalRowGroup.Rows.Add(paymentRow);
                        }
                    }

                    totalTable.RowGroups.Add(totalRowGroup);
                    totalTable.Margin = new Thickness(0, 0, 0, 40);
                    doc.Blocks.Add(totalTable);

                    // Rodapé
                    var footerPara = new Paragraph();
                    footerPara.TextAlignment = TextAlignment.Center;
                    footerPara.Margin = new Thickness(0, 20, 0, 0);

                    footerPara.Inlines.Add(new Run(new string('=', 100))
                    {
                        FontSize = 10,
                        Foreground = Brushes.LightGray
                    });
                    footerPara.Inlines.Add(new LineBreak());
                    footerPara.Inlines.Add(new Run("*** SEGUNDA VIA - PARA ARQUIVO ***")
                    {
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.DarkRed
                    });
                    footerPara.Inlines.Add(new LineBreak());

                    if (!string.IsNullOrEmpty(_restaurantConfig.Address))
                    {
                        footerPara.Inlines.Add(new Run(_restaurantConfig.Address));
                        footerPara.Inlines.Add(new LineBreak());
                    }

                    if (!string.IsNullOrEmpty(_restaurantConfig.Phone))
                    {
                        footerPara.Inlines.Add(new Run($"Tel: {_restaurantConfig.Phone}")
                        {
                            FontSize = 10
                        });
                        footerPara.Inlines.Add(new LineBreak());
                    }

                    footerPara.Inlines.Add(new Run($"Emitido em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}")
                    {
                        FontSize = 10,
                        FontStyle = FontStyles.Italic,
                        Foreground = Brushes.DarkGray
                    });
                    footerPara.Inlines.Add(new LineBreak());
                    footerPara.Inlines.Add(new LineBreak());
                    footerPara.Inlines.Add(new Run("Obrigado pela preferência!")
                    {
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold
                    });

                    doc.Blocks.Add(footerPara);

                    // Imprimir
                    printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,
                        $"Recibo A4 - Talão T{order.TalaoNumber:D6}");

                    MessageBox.Show("Recibo A4 enviado para impressão!", "Sucesso",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar recibo A4: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async Task AplicarFiltrosAsync()
        {
            try
            {
                Cursor = Cursors.Wait;

                var data = dpData.SelectedDate ?? DateTime.Today;

                string filtroMesa = "";
                if (cmbMesa.SelectedItem is ComboBoxItem mesaItem)
                {
                    filtroMesa = mesaItem.Tag?.ToString() ?? "";
                }

                string filtroUtilizador = "";
                if (cmbUtilizador.SelectedItem is ComboBoxItem userItem)
                {
                    filtroUtilizador = userItem.Tag?.ToString() ?? "";
                }

                string filtroPagamento = "";
                if (cmbPagamento.SelectedItem is ComboBoxItem pagamentoItem)
                {
                    filtroPagamento = pagamentoItem.Tag?.ToString() ?? "";
                }

                decimal.TryParse(txtValorMin.Text, out decimal minValor);

                // Se há busca por talão específico, processa primeiro
                if (!string.IsNullOrEmpty(txtBuscaTalao.Text) &&
                    int.TryParse(txtBuscaTalao.Text, out int numeroBusca))
                {
                    await BuscarPorTalaoAsync(numeroBusca);
                    return;
                }

                var query = _db.Orders
                    .Include(o => o.Table)
                    .Include(o => o.User)
                    .Include(o => o.Items)
                    .Include(o => o.Payments)
                    .Where(o => o.Status == OrderStatus.Fechado &&
                               o.CloseDate.HasValue &&
                               o.CloseDate.Value.Date == data.Date)
                    .AsQueryable();

                // Aplicar filtro de mesa
                if (!string.IsNullOrEmpty(filtroMesa))
                {
                    if (filtroMesa == "BALCÃO" || string.IsNullOrEmpty(filtroMesa))
                    {
                        query = query.Where(o => o.Table == null || o.Table.Number == null);
                    }
                    else
                    {
                        query = query.Where(o => o.Table != null && o.Table.Number == filtroMesa);
                    }
                }

                // Aplicar filtro de utilizador
                if (!string.IsNullOrEmpty(filtroUtilizador))
                {
                    query = query.Where(o => o.User != null && o.User.Username == filtroUtilizador);
                }

                // Aplicar filtro de pagamento
                if (!string.IsNullOrEmpty(filtroPagamento))
                {
                    if (Enum.TryParse<PaymentType>(filtroPagamento, out var tipoPagamento))
                    {
                        query = query.Where(o => o.Payments.Any(p => p.Type == tipoPagamento));
                    }
                }

                var pedidos = await query
                    .OrderByDescending(o => o.CloseDate)
                    .Take(1000) // Limitar para performance
                    .ToListAsync();

                // Calcular total para cada pedido e filtrar por valor mínimo
                var pedidosComTotal = new List<OrderDisplay>();
                foreach (var pedido in pedidos)
                {
                    var totalPedido = CalcularTotalPedido(pedido);
                    if (totalPedido >= minValor)
                    {
                        pedidosComTotal.Add(new OrderDisplay
                        {
                            Order = pedido,
                            TotalFormatado = $"{totalPedido:F2} {_restaurantConfig.CurrencySymbol}",
                            TotalDecimal = totalPedido
                        });
                    }
                }

                // Atualizar a coleção observável
                _pedidosFiltrados.Clear();
                foreach (var pedidoDisplay in pedidosComTotal)
                {
                    _pedidosFiltrados.Add(pedidoDisplay);
                }

                // Calcular totais
                await CalcularTotaisAsync(pedidosComTotal.Select(p => p.Order).ToList());

                // Atualizar UI
                txtContadorVendas.Text = pedidosComTotal.Count.ToString();
                string dataFormatada = data.ToString("dd/MM/yyyy");
                decimal totalGeral = _totalNumerario + _totalEletronico;
                txtResumo.Text = $"Total do dia {dataFormatada}: {totalGeral:F2} {_restaurantConfig.CurrencySymbol} • {pedidosComTotal.Count} vendas";

                // Atualizar totais de pagamento
                txtTotalNumerario.Text = $"{_totalNumerario:F2} {_restaurantConfig.CurrencySymbol}";
                txtTotalEletronico.Text = $"{_totalEletronico:F2} {_restaurantConfig.CurrencySymbol}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao aplicar filtros: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private decimal CalcularTotalPedido(Order pedido)
        {
            if (pedido.Items == null || !pedido.Items.Any())
                return 0;

            // Calcular total manualmente
            decimal total = 0;
            foreach (var item in pedido.Items)
            {
                total += item.UnitPrice * item.Quantity;
            }
            return total;
        }

        private async Task CalcularTotaisAsync(List<Order> pedidos)
        {
            _totalNumerario = 0;
            _totalEletronico = 0;

            foreach (var pedido in pedidos)
            {
                var totalPedido = CalcularTotalPedido(pedido);
                var pagamento = pedido.Payments?.FirstOrDefault();

                if (pagamento != null)
                {
                    if (pagamento.Type == PaymentType.Dinheiro)
                    {
                        _totalNumerario += totalPedido;
                    }
                    else
                    {
                        _totalEletronico += totalPedido;
                    }
                }
            }
        }

        private async void TxtBuscaTalao_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (!string.IsNullOrEmpty(txtBuscaTalao.Text) &&
                    int.TryParse(txtBuscaTalao.Text, out int numeroTalao))
                {
                    await BuscarPorTalaoAsync(numeroTalao);
                }
                else
                {
                    await AplicarFiltrosAsync();
                }
                e.Handled = true;
            }
        }

        private async void BtnBuscarTalao_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(txtBuscaTalao.Text) &&
                int.TryParse(txtBuscaTalao.Text, out int numeroTalao))
            {
                await BuscarPorTalaoAsync(numeroTalao);
            }
        }

        private async Task BuscarPorTalaoAsync(int numero)
        {
            try
            {
                Cursor = Cursors.Wait;

                var ordem = await _db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Table)
                    .Include(o => o.Payments)
                    .Include(o => o.User)
                    .FirstOrDefaultAsync(o => o.Status == OrderStatus.Fechado &&
                        (o.TalaoNumber == numero || o.Id == numero));

                if (ordem == null)
                {
                    MessageBox.Show($"Venda ou talão nº {numero} não encontrado.",
                        "Não encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Criar OrderDisplay para exibição
                var totalPedido = CalcularTotalPedido(ordem);
                var orderDisplay = new OrderDisplay
                {
                    Order = ordem,
                    TotalFormatado = $"{totalPedido:F2} {_restaurantConfig.CurrencySymbol}",
                    TotalDecimal = totalPedido
                };

                // Limpar filtros e mostrar apenas esta venda
                _pedidosFiltrados.Clear();
                _pedidosFiltrados.Add(orderDisplay);

                // Calcular totais para esta venda
                await CalcularTotaisAsync(new List<Order> { ordem });

                // Atualizar UI
                txtContadorVendas.Text = "1";
                string dataFormatada = ordem.CloseDate?.ToString("dd/MM/yyyy") ?? "N/A";
                decimal totalGeral = _totalNumerario + _totalEletronico;
                txtResumo.Text = $"Talão {ordem.TalaoNumber:D6} • {dataFormatada} • Total: {totalGeral:F2} {_restaurantConfig.CurrencySymbol}";

                txtTotalNumerario.Text = $"{_totalNumerario:F2} {_restaurantConfig.CurrencySymbol}";
                txtTotalEletronico.Text = $"{_totalEletronico:F2} {_restaurantConfig.CurrencySymbol}";

                MessageBox.Show($"Talão {ordem.TalaoNumber:D6} encontrado! Mesa: {ordem.Table?.Number ?? "Balcão"}, Total: {totalGeral:F2} {_restaurantConfig.CurrencySymbol}",
                    "Talão Encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao buscar talão: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void BtnReimprimir_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is OrderDisplay orderDisplay)
            {
                GerarReciboProfissional(orderDisplay.Order);
            }
        }
        private void GerarReciboProfissional(Order order)
        {
            try
            {
                var printDialog = new PrintDialog();

                if (printDialog.ShowDialog() == true)
                {
                    // Criar documento
                    var doc = new FixedDocument();
                    doc.DocumentPaginator.PageSize = new Size(288, 1000); // 4 polegadas

                    // Página única
                    var page = new FixedPage();
                    page.Width = 288;
                    page.Height = 1000;

                    // Container principal
                    var container = new Border
                    {
                        Width = 280,
                        Background = Brushes.White,
                        Padding = new Thickness(12),
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1)
                    };

                    var stackPanel = new StackPanel();

                    // Cabeçalho com informações do restaurante
                    var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

                    // Nome do restaurante
                    var restaurantName = new TextBlock
                    {
                        Text = _restaurantConfig.RestaurantName.ToUpper(),
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    headerPanel.Children.Add(restaurantName);

                    // Endereço
                    if (!string.IsNullOrEmpty(_restaurantConfig.Address))
                    {
                        var address = new TextBlock
                        {
                            Text = _restaurantConfig.Address,
                            FontSize = 9,
                            Foreground = Brushes.DarkGray,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 2)
                        };
                        headerPanel.Children.Add(address);
                    }

                    // Telefone
                    if (!string.IsNullOrEmpty(_restaurantConfig.Phone))
                    {
                        var phone = new TextBlock
                        {
                            Text = $"Tel: {_restaurantConfig.Phone}",
                            FontSize = 9,
                            Foreground = Brushes.DarkGray,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 2)
                        };
                        headerPanel.Children.Add(phone);
                    }

                    // NIF
                    if (!string.IsNullOrEmpty(_restaurantConfig.NIF))
                    {
                        var nif = new TextBlock
                        {
                            Text = $"NIF: {_restaurantConfig.NIF}",
                            FontSize = 9,
                            Foreground = Brushes.DarkGray,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 0, 0, 4)
                        };
                        headerPanel.Children.Add(nif);
                    }

                    stackPanel.Children.Add(headerPanel);

                    // Linha divisória
                    stackPanel.Children.Add(CreateDivider());

                    // Informações da venda
                    var saleInfoGrid = new Grid();
                    saleInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    saleInfoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    // Coluna esquerda
                    var leftCol = new StackPanel();
                    leftCol.Children.Add(CreateInfoRow("TALÃO:", $"T{order.TalaoNumber:D6}"));
                    leftCol.Children.Add(CreateInfoRow("MESA:", order.Table?.Number ?? "Balcão"));
                    leftCol.Children.Add(CreateInfoRow("CLIENTE:", "Anônimo"));

                    // Coluna direita
                    var rightCol = new StackPanel();
                    rightCol.Children.Add(CreateInfoRow("ID:", order.Id.ToString()));
                    rightCol.Children.Add(CreateInfoRow("DATA:", order.CloseDate?.ToString("dd/MM/yyyy HH:mm") ?? "N/A"));
                    rightCol.Children.Add(CreateInfoRow("ATENDENTE:", order.User?.Username ?? "N/A"));

                    Grid.SetColumn(leftCol, 0);
                    Grid.SetColumn(rightCol, 1);

                    saleInfoGrid.Children.Add(leftCol);
                    saleInfoGrid.Children.Add(rightCol);
                    stackPanel.Children.Add(saleInfoGrid);

                    // Linha divisória
                    stackPanel.Children.Add(CreateDivider());

                    // Itens
                    var itemsHeader = new TextBlock
                    {
                        Text = "ITENS CONSUMIDOS",
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black,
                        TextAlignment = TextAlignment.Center,
                        Margin = new Thickness(0, 5, 0, 8)
                    };
                    stackPanel.Children.Add(itemsHeader);

                    // Tabela de itens
                    decimal totalPedido = 0;
                    foreach (var item in order.Items)
                    {
                        var itemTotal = item.UnitPrice * item.Quantity;
                        totalPedido += itemTotal;

                        var itemGrid = new Grid();
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        // Descrição
                        var desc = new TextBlock
                        {
                            Text = item.Name,
                            FontSize = 10,
                            FontWeight = FontWeights.Normal,
                            Foreground = Brushes.Black,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 2, 0, 2)
                        };
                        Grid.SetColumn(desc, 0);
                        itemGrid.Children.Add(desc);

                        // Quantidade
                        var qtd = new TextBlock
                        {
                            Text = $"x{item.Quantity}",
                            FontSize = 10,
                            FontWeight = FontWeights.Normal,
                            Foreground = Brushes.DarkGray,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 2, 0, 2)
                        };
                        Grid.SetColumn(qtd, 1);
                        itemGrid.Children.Add(qtd);

                        // Total
                        var total = new TextBlock
                        {
                            Text = $"{itemTotal:F2} {_restaurantConfig.CurrencySymbol}",
                            FontSize = 10,
                            FontWeight = FontWeights.Normal,
                            Foreground = Brushes.Black,
                            TextAlignment = TextAlignment.Right,
                            Margin = new Thickness(0, 2, 0, 2)
                        };
                        Grid.SetColumn(total, 2);
                        itemGrid.Children.Add(total);

                        stackPanel.Children.Add(itemGrid);

                        // Notas do item
                        if (!string.IsNullOrEmpty(item.Notes))
                        {
                            var notes = new TextBlock
                            {
                                Text = $"   Obs: {item.Notes}",
                                FontSize = 8,
                                FontStyle = FontStyles.Italic,
                                Foreground = Brushes.DarkGray,
                                Margin = new Thickness(0, 0, 0, 4)
                            };
                            stackPanel.Children.Add(notes);
                        }
                    }

                    // Linha divisória
                    stackPanel.Children.Add(CreateDivider());

                    // Totais
                    var totalRow = CreateTotalRow("TOTAL", $"{totalPedido:F2} {_restaurantConfig.CurrencySymbol}",
                        Brushes.Black, FontWeights.Bold, 14);
                    stackPanel.Children.Add(totalRow);

                    // Formas de pagamento
                    if (order.Payments != null && order.Payments.Any())
                    {
                        foreach (var payment in order.Payments)
                        {
                            var paymentRow = CreateTotalRow(ObterDescricaoPagamento(payment.Type),
                                $"{payment.Amount:F2} {_restaurantConfig.CurrencySymbol}",
                                Brushes.DarkGray, FontWeights.Normal, 11);
                            stackPanel.Children.Add(paymentRow);
                        }
                    }

                    // Linha divisória
                    stackPanel.Children.Add(CreateDivider());

                    // Rodapé
                    var footer = new TextBlock
                    {
                        Text = $"*** SEGUNDA VIA - T{order.TalaoNumber:D6} ***\n" +
                               "Obrigado pela preferência!\n" +
                               $"Emitido em: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                        FontSize = 9,
                        Foreground = Brushes.DarkGray,
                        TextAlignment = TextAlignment.Center,
                        LineHeight = 12,
                        Margin = new Thickness(0, 10, 0, 0)
                    };
                    stackPanel.Children.Add(footer);

                    container.Child = stackPanel;
                    page.Children.Add(container);

                    var pageContent = new PageContent();
                    ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
                    doc.Pages.Add(pageContent);

                    // Imprimir
                    printDialog.PrintDocument(doc.DocumentPaginator,
                        $"Segunda Via - Talão T{order.TalaoNumber:D6}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao gerar recibo: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Métodos auxiliares
        private Border CreateDivider()
        {
            return new Border
            {
                Height = 1,
                Background = Brushes.LightGray,
                Margin = new Thickness(0, 8, 0, 8)
            };
        }

        private StackPanel CreateInfoRow(string label, string value)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.DarkGray,
                Width = 70
            });

            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 10,
                FontWeight = FontWeights.Normal,
                Foreground = Brushes.Black
            });

            return panel;
        }

        private StackPanel CreateTotalRow(string label, string value, Brush color, FontWeight fontWeight, double fontSize)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 3, 0, 3)
            };

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Left
            });

            panel.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = fontSize,
                FontWeight = fontWeight,
                Foreground = color,
                HorizontalAlignment = HorizontalAlignment.Right
            });

            return panel;
        }
    }
}