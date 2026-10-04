using AyGestRest.Models;
using AyGestRest.Services;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AyGestRest.Views
{
    public partial class POSControl
    {
        private bool _productionPaymentHandling;
        private bool _productionHandlersRegistered;

        private void RegisterProductionPaymentHandlers()
        {
            if (_productionHandlersRegistered) return;
            _productionHandlersRegistered = true;

            Loaded += ProductionIntegration_Loaded;
        }

        private void ProductionIntegration_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (btnFinalizarMultiplo != null)
                    btnFinalizarMultiplo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionPaymentMouseDown), true);

                if (btnFinalizarPagamentoMultiplo != null)
                    btnFinalizarPagamentoMultiplo.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionPaymentMouseDown), true);

                if (btnFinalizarMultiplo != null)
                    btnFinalizarMultiplo.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionPaymentKeyDown), true);

                if (btnFinalizarPagamentoMultiplo != null)
                    btnFinalizarPagamentoMultiplo.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionPaymentKeyDown), true);

                if (pnlBotoesMetodosSimplificado != null)
                    pnlBotoesMetodosSimplificado.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(ProductionSimplePaymentMouseDown), true);

                if (pnlBotoesMetodosSimplificado != null)
                    pnlBotoesMetodosSimplificado.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(ProductionSimplePaymentKeyDown), true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao registar integração de produção do POS: {ex}");
            }
        }

        private async void ProductionPaymentMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _productionPaymentHandling) return;
            e.Handled = true;
            await ExecuteProductionPaymentAsync(_metodosPagamento.ToList());
        }

        private async void ProductionPaymentKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || _productionPaymentHandling) return;
            e.Handled = true;
            await ExecuteProductionPaymentAsync(_metodosPagamento.ToList());
        }

        private async void ProductionSimplePaymentMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || _productionPaymentHandling) return;
            if (e.OriginalSource is not DependencyObject source) return;

            var button = FindParent<Button>(source);
            if (button == null || button.Tag is not PaymentType paymentType) return;

            e.Handled = true;
            await ExecuteProductionPaymentAsync(new[]
            {
                new MetodoPagamentoItem
                {
                    Metodo = paymentType.ToString(),
                    Icon = GetPaymentTypeIcon(paymentType),
                    Valor = CalcularTotalAtualDaVenda(),
                    Tipo = paymentType,
                    Background = new System.Windows.Media.SolidColorBrush(GetMetodoColor(paymentType))
                }
            });
        }

        private async void ProductionSimplePaymentKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || _productionPaymentHandling) return;
            if (e.OriginalSource is not DependencyObject source) return;

            var button = FindParent<Button>(source);
            if (button == null || button.Tag is not PaymentType paymentType) return;

            e.Handled = true;
            await ExecuteProductionPaymentAsync(new[]
            {
                new MetodoPagamentoItem
                {
                    Metodo = paymentType.ToString(),
                    Icon = GetPaymentTypeIcon(paymentType),
                    Valor = CalcularTotalAtualDaVenda(),
                    Tipo = paymentType,
                    Background = new System.Windows.Media.SolidColorBrush(GetMetodoColor(paymentType))
                }
            });
        }

        private async Task ExecuteProductionPaymentAsync(IReadOnlyCollection<MetodoPagamentoItem> methods)
        {
            if (_productionPaymentHandling) return;
            _productionPaymentHandling = true;

            try
            {
                if (_ordemAtual == null || !Carrinho.Any())
                    throw new InvalidOperationException("Não existe uma venda válida para finalizar.");

                if (methods.Count == 0 || methods.Any(m => m.Valor <= 0))
                    throw new InvalidOperationException("Indique um pagamento válido.");

                var total = CalcularTotalAtualDaVenda();
                var requested = methods.Sum(m => m.Valor);
                if (requested + 0.01m < total)
                    throw new InvalidOperationException($"Pagamento insuficiente. Faltam {(total - requested):N2} MT.");

                await PersistirCarrinhoNoPedidoAsync(total);

                var terminalId = ResolveProductionTerminalId();
                var userId = AppSession.CurrentUser?.Id;
                var cashRegisterId = await ResolveActiveCashRegisterIdAsync();

                if (!cashRegisterId.HasValue)
                    throw new InvalidOperationException("Não existe um caixa aberto para este terminal. Abra o caixa antes de finalizar a venda.");

                using var db = new AyGestRestContext();
                var inventory = new InventoryTransactionService(db);
                var sale = new SaleCompletionService(db, inventory);

                var payments = methods.Select(m => new PaymentPart(
                    m.Valor,
                    m.Tipo.ToString(),
                    null,
                    $"POS;Terminal={terminalId}"))
                    .ToArray();

                var result = await sale.CompleteAsync(
                    _ordemAtual.Id,
                    payments,
                    terminalId,
                    userId,
                    cashRegisterId.Value);

                using var invoiceDb = new AyGestRestContext();
                var faturacao = new FaturacaoService(invoiceDb);
                await faturacao.EmitirFaturaAsync(
                    result.OrderId,
                    _clienteAtual?.Id,
                    _clienteAtual?.Nome,
                    _clienteAtual?.Nuit);

                await Dispatcher.InvokeAsync(() =>
                {
                    popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                    popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                    overlayGrid.Visibility = Visibility.Collapsed;
                    _metodosPagamento.Clear();
                    Carrinho.Clear();
                    _ordemAtual = null;
                    _mesaAtual = null;
                    _clienteAtual = null;
                    _horaAberturaMesa = null;
                    txtMesaAtual.Text = "NENHUMA";
                    txtTempoMesa.Text = string.Empty;
                    txtValorRecebido.Text = string.Empty;
                    txtDesconto.Text = "0";
                    AtualizarTotal();
                    MostrarMensagemStatus($"Venda concluída • Factura emitida • Troco {result.Change:N2} MT", System.Windows.Media.Brushes.Green, 5);
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CarregarComandasAbertasAsync();
                        await CarregarMesasAsync();
                        AppEvents.RaiseSaleFinalized();
                        AppEvents.RaiseOrderInfoUpdated("000000", "Nenhuma");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Erro ao actualizar POS depois da venda: {ex}");
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Venda não concluída", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                _productionPaymentHandling = false;
            }
        }

        private async Task PersistirCarrinhoNoPedidoAsync(decimal total)
        {
            using var db = new AyGestRestContext();
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == _ordemAtual!.Id)
                ?? throw new InvalidOperationException("O pedido actual não existe mais na base de dados.");

            var existing = await db.OrderItems.Where(i => i.OrderId == order.Id).ToListAsync();
            db.OrderItems.RemoveRange(existing);

            foreach (var item in Carrinho)
            {
                if (item.Quantity <= 0) continue;
                db.OrderItems.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    Name = item.ProductName,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity
                });
            }

            order.Total = total;
            order.TotalAmount = total;
            order.UserId = AppSession.CurrentUser?.Id ?? order.UserId;
            order.Data = DateTime.Now;
            await db.SaveChangesAsync();
        }

        private decimal CalcularTotalAtualDaVenda()
        {
            var subtotal = Carrinho.Sum(i => i.SubTotal);
            var desconto = decimal.TryParse(txtDesconto?.Text ?? "0", NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? value : 0m;
            if ((cmbDescontoTipo?.SelectedIndex ?? 0) == 0)
                desconto = subtotal * Math.Clamp(desconto, 0m, 100m) / 100m;
            else
                desconto = Math.Clamp(desconto, 0m, subtotal);
            return Math.Max(0m, Math.Round(subtotal - desconto, 2, MidpointRounding.AwayFromZero));
        }

        private async Task<int?> ResolveActiveCashRegisterIdAsync()
        {
            using var db = new AyGestRestContext();
            var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM AyGestCashRegisters WHERE Status = 1 ORDER BY Id DESC LIMIT 1;";
            var value = await command.ExecuteScalarAsync();
            return value == null || value == DBNull.Value ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static string ResolveProductionTerminalId()
        {
            var config = AppConfig.Load();
            if (!string.IsNullOrWhiteSpace(config.TerminalId)) return config.TerminalId.Trim();
            return $"{Environment.MachineName}-{Environment.UserName}".ToUpperInvariant();
        }

        private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T typed) return typed;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
