using AyGestRest.Data;
using AyGestRest.Helpers;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Printing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SD = System.Drawing;
using SDP = System.Drawing.Printing;

namespace AyGestRest.Views
{
    public partial class POSControl : UserControl
    {
        private AyGestRestContext? _db;
        private readonly ObservableCollection<CartItem> Carrinho = new();
        private Order? _ordemAtual;
        public string ResumoCozinha => GerarResumoCozinha();
        private RestaurantTable? _mesaAtual;
        private BarcodeScannerManager? _barcodeScanner;
        private readonly SemaphoreSlim _printLock = new(1, 1);
        private readonly SemaphoreSlim _cozinhaLock = new(1, 1);
        private Cliente? _clienteAtual = null;
        private DateTime? _horaAberturaMesa;
        private VFDConfig? _vfdConfig;
        private SerialPort? _vfdPort;
        private RestaurantConfig? _config;
        private int _numeroTalaoAtual = 0;

        private DateTime? _ultimaAberturaGaveta = null;

        // Variáveis para ativação de mesa reservada
        private RestaurantTable? _mesaParaAtivar;
        private Reservation? _reservaParaAtivar;
        private bool _popupAtivarAberto = false;
        private DispatcherTimer? _timerAtualizacao;
        private CustomerDisplayWindow? _customerDisplay;

        // Sistema de mesas
        private List<RestaurantTable> _todasMesas = new();
        private bool _modoSelecaoMesa = false;
        private List<Order> _comandasAbertas = new();

        // Clientes
        private readonly ObservableCollection<Cliente> _listaClientes = new();
        private Cliente? _clienteSelecionado;
        private PaymentType _selectedPaymentType = PaymentType.Dinheiro;
        private bool _impressaoEmAndamento = false;
        private DispatcherTimer _timerVerificacaoImpressao;

        // Variáveis para controle do popup
        private bool _isDraggingPopup = false;
        private Point _popupDragStartPoint;
        private Point _popupOriginalPosition;
        private bool _popupAberto = false;
        private bool _isInitialized = false;
        private bool _isInitializing = false;

        // Variáveis para pagamento
        private ObservableCollection<MetodoPagamentoItem> _metodosPagamento = new ObservableCollection<MetodoPagamentoItem>();
        private decimal _totalVendaMultiplo = 0;
        private bool _pagamentoMultiploAtivo = false;
        private Dictionary<PaymentType, decimal> _valoresMetodosMultiplos = new Dictionary<PaymentType, decimal>();

        // Classe para método de pagamento
        public class MetodoPagamentoItem
        {
            public string Metodo { get; set; }
            public string Icon { get; set; }
            public decimal Valor { get; set; }
            public PaymentType Tipo { get; set; }
            public SolidColorBrush Background { get; set; }
        }

        // Propriedades públicas para binding
        public string RestaurantName => _config?.RestaurantName ?? "RESTAURANTE";
        public string ItemCount => Carrinho.Any() ? $"{Carrinho.Sum(i => i.Quantity)} itens" : "Vazio";
        public string OperatorName => AppSession.CurrentUser?.Username ?? "Operador";
        public string CurrencySymbol => _config?.CurrencySymbol ?? "Mtn";

        public POSControl()
        {
            InitializeComponent();

            // Verificar se estamos no designer do Visual Studio
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                InitializeDesignMode();
                return;
            }

            try
            {
                _db = new AyGestRestContext();
                Carrinho.CollectionChanged += Carrinho_CollectionChanged;
                Loaded += POSControl_Loaded;
                Unloaded += POSControl_Unloaded;

                // Registrar para ouvir eventos de mudança de status de mesa
                AppEvents.TableStatusChanged += OnTableStatusChanged;
                AppEvents.SpecificTableStatusChanged += OnSpecificTableStatusChanged;

                // Inicializar sistema de eventos
                InicializarEventos();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no construtor do POSControl: {ex.Message}");
                MessageBox.Show($"Erro ao inicializar o controle: {ex.Message}",
                    "Erro de Inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeDesignMode()
        {
            txtMesaAtual.Text = "NENHUMA";
            txtUsuario.Text = "OPERADOR";
            txtTotalHeader.Text = "0,00 Mtn";
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                AtualizarLayoutResponsivo(e.NewSize.Width);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no SizeChanged: {ex.Message}");
            }
        }

        private void AtualizarLayoutResponsivo(double larguraDisponivel)
        {
            try
            {
                if (txtMesaAtual == null || txtTotalHeader == null || txtUsuario == null)
                    return;

                if (larguraDisponivel < 800)
                {
                    txtMesaAtual.FontSize = 16;
                    txtTotalHeader.FontSize = 20;
                    txtUsuario.FontSize = 9;
                    ColMesas.Width = new GridLength(160, GridUnitType.Pixel);
                    ColCarrinho.Width = new GridLength(200, GridUnitType.Pixel);

                    var botoes = new[] { btnNovaComanda, btnCancelarVenda };
                    foreach (var btn in botoes)
                    {
                        if (btn != null)
                        {
                            var content = btn.Content?.ToString() ?? string.Empty;
                            if (content.Length > 6)
                            {
                                btn.Content = content.Substring(0, 4) + "...";
                                btn.ToolTip = content;
                            }
                        }
                    }
                }
                else if (larguraDisponivel < 1024)
                {
                    txtMesaAtual.FontSize = 18;
                    txtTotalHeader.FontSize = 24;
                    txtUsuario.FontSize = 10;
                    ColMesas.Width = new GridLength(200, GridUnitType.Pixel);
                    ColCarrinho.Width = new GridLength(250, GridUnitType.Pixel);
                }
                else if (larguraDisponivel < 1280)
                {
                    txtMesaAtual.FontSize = 20;
                    txtTotalHeader.FontSize = 28;
                    txtUsuario.FontSize = 11;
                    ColMesas.Width = new GridLength(240, GridUnitType.Pixel);
                    ColCarrinho.Width = new GridLength(300, GridUnitType.Pixel);
                }
                else
                {
                    txtMesaAtual.FontSize = 24;
                    txtTotalHeader.FontSize = 32;
                    txtUsuario.FontSize = 12;
                    ColMesas.Width = new GridLength(280, GridUnitType.Pixel);
                    ColCarrinho.Width = new GridLength(350, GridUnitType.Pixel);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarLayoutResponsivo: {ex.Message}");
            }
        }

        #region Métodos de Inicialização e Configuração

        private void InicializarEventos()
        {
            try
            {
                if (txtDesconto != null)
                {
                    txtDesconto.TextChanged += (s, e) =>
                    {
                        if (decimal.TryParse(txtDesconto.Text, out decimal discount))
                        {
                            AppEvents.RaiseDiscountUpdated(discount);
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na InicializarEventos: {ex.Message}");
            }
        }

        private void InicializarEventosVenda()
        {
            try
            {
                Carrinho.CollectionChanged += (s, e) =>
                {
                    var cartCopy = new ObservableCollection<CartItem>(Carrinho);
                    AppEvents.RaiseCartUpdated(cartCopy);
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na InicializarEventosVenda: {ex.Message}");
            }
        }

        private void TxtValorRecebido_GotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TxtValorRecebido_GotFocus: {ex.Message}");
            }
        }

        private async void POSControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized || _isInitializing || _db == null)
                return;

            _isInitializing = true;

            try
            {
                Debug.WriteLine("Iniciando carregamento do POSControl...");

                await InitializeDatabaseAsync();
                await LoadConfigurationAsync();
                ConfigureBasicUI();
                await LoadInitialDataAsync();
                InitializePeripherals();
                SetupTimersAndEvents();
                SafeFocusControl(txtPesquisa);

                _isInitialized = true;
                Debug.WriteLine("POSControl carregado com sucesso!");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERRO CRÍTICO no POSControl_Loaded: {ex.Message}\n{ex.StackTrace}");
                await HandleCriticalErrorAsync(ex);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private string GerarResumoCozinha()
        {
            try
            {
                if (!Carrinho.Any()) return "Carrinho vazio";

                var itensCozinha = new List<string>();
                using (var dbContext = new AyGestRestContext())
                {
                    foreach (var item in Carrinho)
                    {
                        var produto = dbContext.Products
                            .AsNoTracking()
                            .FirstOrDefault(p => p.Id == item.ProductId);

                        if (produto != null && produto.PrintToKitchen == true)
                        {
                            itensCozinha.Add($"{item.ProductName} x{item.Quantity}");
                        }
                    }
                }

                if (!itensCozinha.Any()) return "Nenhum item para cozinha";

                return $"Itens para cozinha:\n{string.Join("\n", itensCozinha)}";
            }
            catch
            {
                return "Erro ao gerar resumo";
            }
        }

        private async Task InitializeDatabaseAsync()
        {
            try
            {
                if (_db == null)
                {
                    _db = new AyGestRestContext();
                }

                await _db.Database.CanConnectAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na inicialização do banco: {ex.Message}");
                throw new Exception($"Falha na conexão com o banco de dados: {ex.Message}", ex);
            }
        }

        private async Task LoadConfigurationAsync()
        {
            try
            {
                if (_db == null) return;

                _config = await _db.RestaurantConfigs.FirstOrDefaultAsync() ?? new RestaurantConfig { Id = 1 };
                _vfdConfig = await _db.VFDConfigs.FirstOrDefaultAsync() ?? new VFDConfig
                {
                    IsEnabled = false,
                    ShowRealTimeTotal = false,
                    COMPort = "COM1",
                    BaudRate = 9600,
                    DisplayColumns = 20,
                    DisplayLines = 2
                };

                CarregarConfiguracoesGlobais();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar configurações: {ex.Message}");
                _config = new RestaurantConfig { Id = 1 };
                _vfdConfig = new VFDConfig { IsEnabled = false };
            }
        }

        private void ConfigureBasicUI()
        {
            try
            {
                if (cmbClientes != null)
                {
                    cmbClientes.ItemsSource = _listaClientes;
                    cmbClientes.DisplayMemberPath = "Nome";
                    cmbClientes.SelectedValuePath = "Id";
                }

                InicializarEventosVenda();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na ConfigureBasicUI: {ex.Message}");
            }
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                if (_db == null) return;

                await Task.WhenAll(
                    CarregarCategoriasAsync(),
                    CarregarMesasAsync(),
                    CarregarComandasAbertasAsync(),
                    CarregarClientesAsync()
                );

                CarregarUltimoTalaoDoBanco();
                await CarregarProdutosAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no LoadInitialDataAsync: {ex.Message}");
                throw;
            }
        }

        private void InitializePeripherals()
        {
            try
            {
                if (txtPesquisa != null && _db != null)
                {
                    _barcodeScanner = new BarcodeScannerManager(txtPesquisa, _db, async (productId) =>
                    {
                        await HandleBarcodeScanned(productId);
                    });
                }

                if (_vfdConfig != null && _vfdConfig.IsEnabled)
                {
                    InicializarVFD();
                }

                if (_config?.SecondScreenEnabled == true)
                {
                    AbrirTelaSecundaria();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na InitializePeripherals: {ex.Message}");
            }
        }

        private void SetupTimersAndEvents()
        {
            try
            {
                _timerAtualizacao = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _timerAtualizacao.Tick += TimerAtualizacao_Tick;
                _timerAtualizacao.Start();

                this.KeyDown += POSControl_KeyDown;

                AtualizarTotal();
                AtualizarPopupRecibo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no SetupTimersAndEvents: {ex.Message}");
            }
        }

        private async Task HandleBarcodeScanned(string productId)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_ordemAtual == null)
                    {
                        MessageBox.Show("Selecione uma mesa primeiro para iniciar uma venda.",
                            "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (int.TryParse(productId, out int id) && _db != null)
                    {
                        var produto = await _db.Products.FindAsync(id);
                        if (produto != null)
                        {
                            AdicionarProduto(produto);
                        }
                        else
                        {
                            produto = await _db.Products
                                .FirstOrDefaultAsync(p => p.Barcode == productId);

                            if (produto != null)
                            {
                                AdicionarProduto(produto);
                            }
                            else
                            {
                                MessageBox.Show($"Produto com código {productId} não encontrado.",
                                    "Produto não encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no HandleBarcodeScanned: {ex.Message}");
            }
        }

        private void SafeFocusControl(Control control)
        {
            try
            {
                if (control == null || !control.IsVisible || !control.IsEnabled)
                    return;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        control.Focus();
                        if (control is TextBox textBox)
                        {
                            textBox.SelectAll();
                        }
                    }
                    catch { }
                }), DispatcherPriority.ContextIdle);
            }
            catch { }
        }

        private async Task HandleCriticalErrorAsync(Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show($"Erro crítico ao inicializar PDV:\n{ex.Message}\n\nA aplicação pode não funcionar corretamente.",
                    "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void POSControl_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                AppEvents.TableStatusChanged -= OnTableStatusChanged;
                AppEvents.SpecificTableStatusChanged -= OnSpecificTableStatusChanged;

                _timerAtualizacao?.Stop();
                _timerAtualizacao = null;

                FecharTelaSecundaria();

                if (_vfdPort != null && _vfdPort.IsOpen)
                {
                    _vfdPort.Close();
                    _vfdPort = null;
                }

                if (_barcodeScanner != null)
                {
                    _barcodeScanner.Dispose();
                    _barcodeScanner = null;
                }

                if (_db != null)
                {
                    _db.Dispose();
                    _db = null;
                }

                // Garantir que o overlay seja removido
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                popupAtivarMesa.Visibility = Visibility.Collapsed;

                _isInitialized = false;
                _isInitializing = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no Unloaded: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Clientes

        private async Task CarregarClientesAsync()
        {
            try
            {
                if (_db == null) return;

                var clientesDb = await _db.Clientes.OrderBy(c => c.Nome).ToListAsync();
                await Dispatcher.InvokeAsync(() =>
                {
                    _listaClientes.Clear();
                    foreach (var c in clientesDb)
                    {
                        _listaClientes.Add(c);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar clientes: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Erro ao carregar clientes: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void CmbClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbClientes.SelectedItem is Cliente clienteSelecionado)
                {
                    _clienteSelecionado = clienteSelecionado;

                    if (!string.IsNullOrEmpty(clienteSelecionado.Telefone))
                    {
                        MostrarMensagemStatus($"Cliente selecionado: {clienteSelecionado.Nome} ({clienteSelecionado.Telefone})",
                            Brushes.Green);
                    }
                    else
                    {
                        MostrarMensagemStatus($"Cliente selecionado: {clienteSelecionado.Nome}",
                            Brushes.Green);
                    }
                }
                else
                {
                    _clienteSelecionado = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CmbClientes_SelectionChanged: {ex.Message}");
            }
        }

        private void BtnClienteAleatorio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_listaClientes.Any())
                {
                    Random rnd = new Random();
                    int index = rnd.Next(_listaClientes.Count);
                    cmbClientes.SelectedItem = _listaClientes[index];

                    MostrarMensagemStatus("Cliente aleatório selecionado!", Brushes.Green);
                }
                else
                {
                    MostrarMensagemStatus("Não há clientes cadastrados.", Brushes.Orange);
                }
            }
            catch (Exception ex)
            {
                MostrarMensagemStatus($"Erro ao selecionar cliente aleatório: {ex.Message}", Brushes.Red);
            }
        }

        private void MostrarMensagemStatus(string mensagem, SolidColorBrush cor, int segundos = 3)
        {
            try
            {
                if (txtMensagemStatus == null || borderMensagemStatus == null)
                    return;

                txtMensagemStatus.Text = mensagem;
                txtMensagemStatus.Foreground = cor;
                borderMensagemStatus.Visibility = Visibility.Visible;

                DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(segundos) };
                timer.Tick += (s, ev) =>
                {
                    if (borderMensagemStatus != null)
                    {
                        borderMensagemStatus.Visibility = Visibility.Collapsed;
                    }
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no MostrarMensagemStatus: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Atualização em Tempo Real

        private void AtualizarTodasAsTelas()
        {
            try
            {
                AtualizarTotal();
                AtualizarPopupRecibo();
                AtualizarContadorItens();
                AtualizarContadorComandas();
                AtualizarTelaCliente();
                UpdateVFDTotal();

                var cartCopy = new ObservableCollection<CartItem>(Carrinho);
                AppEvents.RaiseCartUpdated(cartCopy);

                if (_ordemAtual != null)
                {
                    AppEvents.RaiseOrderInfoUpdated(
                        _ordemAtual.Id.ToString("D6"),
                        _mesaAtual?.Number ?? "Balcão"
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarTodasAsTelas: {ex.Message}");
            }
        }

        private void AtualizarTelaCliente()
        {
            try
            {
                if (_customerDisplay != null && _customerDisplay.IsLoaded)
                {
                    bool descontoPorcentagem = cmbDescontoTipo?.SelectedIndex == 0;
                    decimal descontoValor = decimal.TryParse(txtDesconto?.Text ?? "0", out var d) ? d : 0m;
                    decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                    decimal descontoAplicado = descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor;

                    string formaPagamento = _selectedPaymentType.ToString();

                    string numeroComanda = _ordemAtual?.Id.ToString("D6") ?? "000000";
                    string mesa = _mesaAtual?.Number ?? "Balcão";

                    _customerDisplay.AtualizarInformacoesVenda(
                        numeroComanda,
                        mesa,
                        descontoAplicado,
                        formaPagamento
                    );

                    _customerDisplay.AtualizarDisplay();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar tela do cliente: {ex.Message}");
                _customerDisplay = null;
            }
        }

        #endregion

        #region Sistema de Mesas

        private async Task CarregarMesasAsync()
        {
            try
            {
                if (_db == null) return;

                _todasMesas = await _db.Tables
                    .OrderBy(t => t.Number)
                    .ToListAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    AtualizarListaMesasUI();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar mesas: {ex.Message}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Erro ao carregar mesas: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private async Task CarregarComandasAbertasAsync()
        {
            try
            {
                if (_db == null) return;

                _comandasAbertas = await _db.Orders
                    .Include(o => o.Table)
                    .Where(o => o.Status == OrderStatus.Aberto)
                    .ToListAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    AtualizarContadorComandas();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar comandas abertas: {ex.Message}");
            }
        }

        private async void OnTableStatusChanged()
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await CarregarMesasAsync();
                    await CarregarComandasAbertasAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao atualizar mesas: {ex.Message}");
                }
            });
        }

        private async void OnSpecificTableStatusChanged(int tableId)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (_db == null) return;

                    var mesaAtualizada = await _db.Tables.FindAsync(tableId);
                    if (mesaAtualizada != null)
                    {
                        foreach (var child in pnlMesas.Children)
                        {
                            if (child is Button btn && btn.Tag is RestaurantTable mesa && mesa.Id == tableId)
                            {
                                var novoBtn = CriarBotaoMesa(mesaAtualizada);
                                int index = pnlMesas.Children.IndexOf(btn);
                                pnlMesas.Children.RemoveAt(index);
                                pnlMesas.Children.Insert(index, novoBtn);
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro ao atualizar mesa específica: {ex.Message}");
                }
            });
        }

        private void InicializarVFD()
        {
            try
            {
                if (_vfdConfig != null && _vfdConfig.IsEnabled)
                {
                    TryOpenVFD();

                    if (_vfdPort != null && _vfdPort.IsOpen)
                    {
                        UpdateVFDDisplay("BEM-VINDO", _config?.RestaurantName ?? "RESTAURANTE");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro na InicializarVFD: {ex.Message}");
            }
        }

        private void AtualizarListaMesasUI()
        {
            try
            {
                if (pnlMesas == null) return;

                pnlMesas.Children.Clear();

                var btnBalcao = CriarBotaoMesa(new RestaurantTable
                {
                    Id = 0,
                    Number = "BALCÃO",
                    Status = TableStatus.Livre
                });
                pnlMesas.Children.Add(btnBalcao);

                foreach (var mesa in _todasMesas)
                {
                    var btnMesa = CriarBotaoMesa(mesa);
                    pnlMesas.Children.Add(btnMesa);
                }

                AtualizarContadorComandas();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarListaMesasUI: {ex.Message}");
            }
        }

        private async Task<Reservation?> VerificarReservaAtivaParaMesa(int mesaId)
        {
            try
            {
                using var db = new AyGestRestContext();
                var agora = DateTime.Now;

                var reserva = await db.Reservations
                    .Include(r => r.Cliente)
                    .Where(r => r.RestaurantTableId == mesaId &&
                               r.Estado != ReservationStatus.Cancelada &&
                               r.Estado != ReservationStatus.Concluida &&
                               r.Inicio <= agora.AddMinutes(30) &&
                               r.Fim >= agora &&
                               !r.MesaAtiva)
                    .OrderByDescending(r => r.Inicio)
                    .FirstOrDefaultAsync();

                return reserva;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar reserva: {ex.Message}");
                return null;
            }
        }

        private async Task AbrirPopupAtivacaoMesa(RestaurantTable mesa, Reservation reserva)
        {
            try
            {
                _mesaParaAtivar = mesa;
                _reservaParaAtivar = reserva;

                txtReservaCliente.Text = $"Cliente: {reserva.NomeCliente}";
                txtReservaHorario.Text = $"Horário: {reserva.Inicio:HH:mm} - {reserva.Fim:HH:mm}";
                txtReservaPessoas.Text = $"Pessoas: {reserva.NumPessoas}";

                txtCodigoAtivacao.Text = "";
                borderErroAtivacao.Visibility = Visibility.Collapsed;

                popupAtivarMesa.Visibility = Visibility.Visible;
                _popupAtivarAberto = true;
                overlayGrid.Visibility = Visibility.Visible;

                await Task.Delay(100);
                SafeFocusControl(txtCodigoAtivacao);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir popup de ativação: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnConfirmarAtivacao_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mesaParaAtivar == null)
                {
                    MostrarErroAtivacao("Erro: Nenhuma mesa selecionada para ativação.");
                    return;
                }

                string codigoInserido = txtCodigoAtivacao?.Text?.Trim()?.ToUpper() ?? "";

                if (string.IsNullOrEmpty(codigoInserido))
                {
                    MostrarErroAtivacao("Digite o código da reserva.");
                    SafeFocusControl(txtCodigoAtivacao);
                    return;
                }

                using var db = new AyGestRestContext();
                var agora = DateTime.Now;

                var reserva = await db.Reservations
                    .Include(r => r.Cliente)
                    .Where(r => r.RestaurantTableId == _mesaParaAtivar.Id &&
                               r.Estado != ReservationStatus.Cancelada &&
                               r.Estado != ReservationStatus.Concluida &&
                               r.Inicio <= agora.AddMinutes(30) &&
                               r.Fim >= agora &&
                               !r.MesaAtiva)
                    .OrderByDescending(r => r.Inicio)
                    .FirstOrDefaultAsync();

                if (reserva == null)
                {
                    MostrarErroAtivacao("Reserva não encontrada ou já foi ativada.");
                    return;
                }

                if (codigoInserido != reserva.CodigoVerificacao)
                {
                    MostrarErroAtivacao("Código incorreto. Verifique e tente novamente.");
                    if (txtCodigoAtivacao != null)
                    {
                        txtCodigoAtivacao.SelectAll();
                        SafeFocusControl(txtCodigoAtivacao);
                    }
                    return;
                }

                reserva.Estado = ReservationStatus.Ativa;
                reserva.MesaAtiva = true;
                reserva.DataAtivacao = DateTime.Now;

                var mesa = await db.Tables.FindAsync(_mesaParaAtivar.Id);
                if (mesa != null)
                {
                    mesa.Status = TableStatus.Ocupada;
                }

                await db.SaveChangesAsync();

                _ordemAtual = new Order
                {
                    UserId = AppSession.CurrentUser?.Id ?? 1,
                    OpenDate = DateTime.Now,
                    Status = OrderStatus.Aberto,
                    TableId = _mesaParaAtivar.Id,
                    ReservationId = reserva.Id
                };

                db.Orders.Add(_ordemAtual);
                await db.SaveChangesAsync();

                _mesaAtual = _mesaParaAtivar;
                _horaAberturaMesa = DateTime.Now;
                txtMesaAtual.Text = _mesaAtual.Number ?? "DESCONHECIDA";

                AtualizarTempoMesa();

                _modoSelecaoMesa = false;
                borderMensagemStatus.Visibility = Visibility.Collapsed;

                Carrinho.Clear();
                AtualizarTotal();

                AppEvents.RaiseOrderInfoUpdated(_ordemAtual.Id.ToString("D6"), _mesaAtual.Number);
                AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>());
                AtualizarTelaCliente();

                FecharPopupAtivacao();

                await CarregarMesasAsync();
                await CarregarComandasAbertasAsync();

                MessageBox.Show($"✅ Mesa {_mesaAtual.Number} ativada com sucesso!\n" +
                               $"Comanda #{_ordemAtual.Id:D6} criada para {reserva.NomeCliente}",
                               "Mesa Ativada", MessageBoxButton.OK, MessageBoxImage.Information);

                AbrirPopupRecibo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao ativar mesa: {ex.Message}");
                MostrarErroAtivacao($"Erro ao ativar mesa: {ex.Message}");

                MessageBox.Show($"Erro detalhado:\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MostrarErroAtivacao(string mensagem)
        {
            try
            {
                if (txtErroAtivacao != null && borderErroAtivacao != null)
                {
                    txtErroAtivacao.Text = mensagem;
                    borderErroAtivacao.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no MostrarErroAtivacao: {ex.Message}");
            }
        }

        private void FecharPopupAtivacao()
        {
            try
            {
                popupAtivarMesa.Visibility = Visibility.Collapsed;
                _popupAtivarAberto = false;
                _mesaParaAtivar = null;
                _reservaParaAtivar = null;

                if (!_popupAberto &&
                    popupPagamentoMultiplo.Visibility != Visibility.Visible &&
                    popupPagamentoSimplificado.Visibility != Visibility.Visible)
                {
                    overlayGrid.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no FecharPopupAtivacao: {ex.Message}");
            }
        }

        private void BtnFecharAtivar_Click(object sender, RoutedEventArgs e)
        {
            FecharPopupAtivacao();
        }

        private Button CriarBotaoMesa(RestaurantTable mesa)
        {
            var btn = new Button
            {
                Tag = mesa,
                Style = (Style)FindResource("MesaButtonStyle")
            };

            var reservaAtiva = VerificarReservaAtivaParaMesa(mesa.Id).Result;

            if (mesa.Id == 0)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219));
                btn.Content = $"🛒 {mesa.Number}";
            }
            else
            {
                var ordemMesa = _comandasAbertas.FirstOrDefault(o => o.TableId == mesa.Id);
                bool temComandaAberta = ordemMesa != null;

                if (reservaAtiva != null && mesa.Status == TableStatus.Reservada)
                {
                    btn.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    btn.Content = $"📅 {mesa.Number}";

                    var tooltip = new ToolTip();
                    var stackTooltip = new StackPanel();
                    stackTooltip.Children.Add(new TextBlock
                    {
                        Text = $"RESERVADA",
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Background = Brushes.Orange,
                        Padding = new Thickness(4),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    stackTooltip.Children.Add(new TextBlock
                    {
                        Text = $"Cliente: {reservaAtiva.NomeCliente}",
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    stackTooltip.Children.Add(new TextBlock
                    {
                        Text = $"Horário: {reservaAtiva.Inicio:HH:mm} - {reservaAtiva.Fim:HH:mm}"
                    });
                    stackTooltip.Children.Add(new TextBlock
                    {
                        Text = $"Pessoas: {reservaAtiva.NumPessoas}"
                    });
                    stackTooltip.Children.Add(new TextBlock
                    {
                        Text = $"Clique para ativar com código",
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Orange,
                        Margin = new Thickness(0, 8, 0, 0)
                    });
                    tooltip.Content = stackTooltip;
                    btn.ToolTip = tooltip;
                }
                else
                {
                    switch (mesa.Status)
                    {
                        case TableStatus.Livre:
                            if (temComandaAberta)
                            {
                                btn.Background = new SolidColorBrush(Color.FromRgb(155, 89, 182));
                                btn.Content = $"📝 {mesa.Number}";
                            }
                            else
                            {
                                btn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                                btn.Content = $"✅ {mesa.Number}";
                            }
                            break;

                        case TableStatus.Ocupada:
                            btn.Background = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                            btn.Content = $"⏱️ {mesa.Number}";

                            if (ordemMesa != null)
                            {
                                var tempo = DateTime.Now - ordemMesa.OpenDate;
                                btn.ToolTip = $"Ocupada há: {tempo:hh\\:mm\\:ss}\nComanda: #{ordemMesa.Id}";
                            }
                            break;

                        default:
                            btn.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166));
                            btn.Content = $"❓ {mesa.Number}";
                            break;
                    }
                }
            }

            btn.Click += async (s, e) =>
            {
                try
                {
                    if (_modoSelecaoMesa)
                    {
                        var reserva = await VerificarReservaAtivaParaMesa(mesa.Id);
                        if (reserva != null && mesa.Status == TableStatus.Reservada)
                        {
                            await AbrirPopupAtivacaoMesa(mesa, reserva);
                        }
                        else
                        {
                            await SelecionarMesaParaNovaVenda(mesa);
                        }
                    }
                    else
                    {
                        await ContinuarVendaMesa(mesa);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro no clique da mesa: {ex.Message}");
                    MessageBox.Show($"Erro: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            return btn;
        }

        private void AtualizarContadorComandas()
        {
            try
            {
                if (txtContadorComandas == null || txtInfoMesa == null)
                    return;

                int count = _comandasAbertas.Count;
                txtContadorComandas.Text = $"{count} comanda{(count != 1 ? "s" : "")} aberta{(count != 1 ? "s" : "")}";

                if (_modoSelecaoMesa)
                {
                    txtInfoMesa.Text = "MODO SELEÇÃO: Clique em uma mesa para nova comanda";
                }
                else if (_ordemAtual != null)
                {
                    txtInfoMesa.Text = $"Comanda ativa na {(_mesaAtual?.Number ?? "Balcão")} - {Carrinho.Count} item{(Carrinho.Count != 1 ? "s" : "")}";
                }
                else
                {
                    txtInfoMesa.Text = "Selecione uma mesa para começar";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarContadorComandas: {ex.Message}");
            }
        }

        private void AtivarModoSelecaoMesa()
        {
            try
            {
                if (_ordemAtual != null && Carrinho.Any())
                {
                    var result = MessageBox.Show(
                        "Há uma venda em andamento. O que deseja fazer?\n\n" +
                        "Salvar e Continuar: Salva os itens atuais e abre nova comanda\n" +
                        "Descartar: Abre nova comanda sem salvar (itens serão perdidos)\n" +
                        "Cancelar: Volta para a venda atual",
                        "Nova Comanda",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question,
                        MessageBoxResult.Yes);

                    if (result == MessageBoxResult.Cancel)
                        return;

                    if (result == MessageBoxResult.Yes)
                    {
                        SalvarOrdemAtual();
                    }
                    else
                    {
                        Carrinho.Clear();
                        _ordemAtual = null;
                    }
                }

                _modoSelecaoMesa = true;
                txtMensagemStatus.Text = "MODO SELEÇÃO DE MESA ATIVADO - Clique em uma mesa para iniciar nova comanda";
                borderMensagemStatus.Visibility = Visibility.Visible;
                AtualizarContadorComandas();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtivarModoSelecaoMesa: {ex.Message}");
            }
        }

        private async Task SelecionarMesaParaNovaVenda(RestaurantTable mesa)
        {
            try
            {
                var reserva = await VerificarReservaAtivaParaMesa(mesa.Id);
                if (reserva != null && mesa.Status == TableStatus.Reservada)
                {
                    MessageBox.Show($"A mesa {mesa.Number} está reservada para {reserva.NomeCliente}.\n" +
                                   $"Horário: {reserva.Inicio:HH:mm} - {reserva.Fim:HH:mm}\n\n" +
                                   $"Clique na mesa para ativar com o código da reserva.",
                                   "Mesa Reservada", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var ordemExistente = _comandasAbertas.FirstOrDefault(o =>
                    (mesa.Id == 0 && o.TableId == null) || o.TableId == mesa.Id);

                if (ordemExistente != null)
                {
                    var result = MessageBox.Show(
                        $"A {mesa.Number} já tem uma comanda aberta.\n\nDeseja continuar a venda existente?",
                        "Comanda Existente",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await ContinuarVendaMesa(mesa);
                        return;
                    }
                    else
                    {
                        MessageBox.Show("Escolha outra mesa ou finalize a comanda atual primeiro.",
                            "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (mesa.Id > 0 && mesa.Status == TableStatus.Ocupada)
                {
                    MessageBox.Show($"A mesa {mesa.Number} está ocupada. Escolha outra mesa.",
                        "Mesa Ocupada", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _ordemAtual = new Order
                {
                    UserId = AppSession.CurrentUser?.Id ?? 1,
                    OpenDate = DateTime.Now,
                    Status = OrderStatus.Aberto,
                    TableId = mesa.Id > 0 ? mesa.Id : (int?)null
                };

                if (mesa.Id > 0 && _db != null)
                {
                    mesa.Status = TableStatus.Ocupada;
                    await _db.SaveChangesAsync();
                    AppEvents.RaiseSpecificTableStatusChanged(mesa.Id);
                }

                if (_db != null)
                {
                    _db.Orders.Add(_ordemAtual);
                    await _db.SaveChangesAsync();
                }

                AppEvents.RaiseOrderInfoUpdated(_ordemAtual.Id.ToString("D6"), mesa.Number);

                _mesaAtual = mesa.Id > 0 ? mesa : null;
                _horaAberturaMesa = DateTime.Now;
                txtMesaAtual.Text = mesa.Number;
                AtualizarTempoMesa();

                AtualizarTelaCliente();

                _modoSelecaoMesa = false;
                borderMensagemStatus.Visibility = Visibility.Collapsed;

                Carrinho.Clear();
                AtualizarTotal();

                await CarregarComandasAbertasAsync();
                await CarregarMesasAsync();

                AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>(Carrinho));

                AbrirPopupRecibo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir comanda: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ContinuarVendaMesa(RestaurantTable mesa)
        {
            try
            {
                if (_ordemAtual != null && _mesaAtual?.Id == mesa.Id)
                {
                    MessageBox.Show($"Você já está na comanda da {mesa.Number}",
                        "Informação", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_db == null) return;

                var ordemExistente = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Status == OrderStatus.Aberto &&
                        ((mesa.Id == 0 && o.TableId == null) || o.TableId == mesa.Id));

                if (ordemExistente == null)
                {
                    if (mesa.Id == 0)
                    {
                        var result = MessageBox.Show(
                            "Não há comanda aberta no balcão.\nDeseja criar uma nova comanda?",
                            "Nova Comanda",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            await SelecionarMesaParaNovaVenda(mesa);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Não há comanda aberta na mesa {mesa.Number}.\nSelecione 'Nova Comanda' para iniciar.",
                            "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    return;
                }

                if (_ordemAtual != null && Carrinho.Any())
                {
                    SalvarOrdemAtual();
                }

                _ordemAtual = ordemExistente;
                _mesaAtual = mesa;
                _horaAberturaMesa = ordemExistente.OpenDate;

                Carrinho.Clear();
                foreach (var item in ordemExistente.Items)
                {
                    Carrinho.Add(new CartItem
                    {
                        ProductId = item.ProductId,
                        ProductName = item.Name,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity
                    });
                }

                AppEvents.RaiseOrderInfoUpdated(_ordemAtual.Id.ToString("D6"), mesa.Number);
                AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>(Carrinho));

                txtMesaAtual.Text = mesa.Number;
                AtualizarTempoMesa();
                AtualizarTotal();

                AtualizarTelaCliente();

                _modoSelecaoMesa = false;
                borderMensagemStatus.Visibility = Visibility.Collapsed;

                await CarregarMesasAsync();

                AbrirPopupRecibo();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar comanda: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SalvarOrdemAtual()
        {
            if (_ordemAtual == null || _db == null) return;

            try
            {
                var itensAntigos = _db.OrderItems.Where(oi => oi.OrderId == _ordemAtual.Id);
                _db.OrderItems.RemoveRange(itensAntigos);

                foreach (var item in Carrinho)
                {
                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = _ordemAtual.Id,
                        ProductId = item.ProductId,
                        Name = item.ProductName,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity
                    });
                }

                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao salvar ordem: {ex.Message}");
            }
        }

        #endregion

        #region Métodos de Ação do Usuário

        private async void BtnImprimirCozinha_Click(object sender, RoutedEventArgs e)
        {
            if (_ordemAtual == null || !Carrinho.Any())
            {
                MessageBox.Show("Não há comanda ou itens para imprimir na cozinha.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var itensCozinha = new List<CartItem>();
            using (var db = new AyGestRestContext())
            {
                foreach (var item in Carrinho)
                {
                    var prod = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == item.ProductId);
                    if (prod?.PrintToKitchen == true)
                        itensCozinha.Add(item);
                }
            }

            if (!itensCozinha.Any())
            {
                MessageBox.Show("Nenhum item marcado para cozinha.", "Informação", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string textoCozinha = GerarTextoReciboCozinhaCompleto(itensCozinha);

            string impressora = _config?.KitchenPrinterName;
            if (string.IsNullOrWhiteSpace(impressora) || impressora == "(Nenhuma)")
                impressora = _config?.PrinterName;

            if (string.IsNullOrWhiteSpace(impressora))
            {
                MessageBox.Show("Nenhuma impressora configurada para cozinha.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool sucesso = await ImprimirCozinhaAsync(textoCozinha, impressora);

            if (sucesso)
            {
                MostrarMensagemStatus("Pedido enviado para cozinha!", Brushes.Green, 4);
            }
            else
            {
                MessageBox.Show($"Falha ao enviar para cozinha.\n\nVerifique:\n• Impressora ligada?\n• Papel colocado?\n• Nome correto: {impressora}",
                    "Erro de Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> ImprimirCozinhaAsync(string textoEscPos, string nomeImpressora)
        {
            if (string.IsNullOrWhiteSpace(nomeImpressora)) return false;

            await _cozinhaLock.WaitAsync();
            try
            {
                bool rawOk = RawPrinterHelper.SendStringToPrinter(nomeImpressora, textoEscPos);
                if (rawOk)
                {
                    Debug.WriteLine("[COZINHA] Impresso via RAW com sucesso");
                    return true;
                }

                string textoPuro = RemoverComandosEscPos(textoEscPos);

                var tcs = new TaskCompletionSource<bool>();

                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        using var printDoc = new SDP.PrintDocument();
                        printDoc.PrinterSettings.PrinterName = nomeImpressora;
                        printDoc.PrinterSettings.Copies = 1;

                        printDoc.PrintPage += (s, ev) =>
                        {
                            using var font = new SD.Font("Consolas", 10);
                            float y = 10f;

                            foreach (var linha in textoPuro.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
                            {
                                if (!string.IsNullOrWhiteSpace(linha))
                                {
                                    ev.Graphics.DrawString(linha, font, SD.Brushes.Black, 5f, y);
                                    y += font.GetHeight(ev.Graphics) + 1f;
                                }
                            }
                            ev.HasMorePages = false;
                        };

                        printDoc.EndPrint += (s, ev) => tcs.TrySetResult(true);
                        printDoc.Print();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[COZINHA FALLBACK ERRO] " + ex.Message);
                        tcs.TrySetResult(false);
                    }
                });

                bool fallbackOk = await tcs.Task;
                if (fallbackOk)
                {
                    Debug.WriteLine("[COZINHA] Impresso via fallback (texto puro)");
                    return true;
                }

                return false;
            }
            finally
            {
                _cozinhaLock.Release();
            }
        }

        private string RemoverComandosEscPos(string textoComComandos)
        {
            var sb = new StringBuilder();
            bool escape = false;

            foreach (char c in textoComComandos)
            {
                if (c == '\x1B' || c == '\x1D')
                {
                    escape = true;
                    continue;
                }
                if (escape && char.IsLetterOrDigit(c) || c == '@' || c == '!' || c == 'V')
                {
                    escape = false;
                    continue;
                }
                if (!escape) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        private int ContarItensCozinha()
        {
            int count = 0;
            if (_db != null)
            {
                foreach (var item in Carrinho)
                {
                    var produto = _db.Products.Find(item.ProductId);
                    if (produto != null && produto.PrintToKitchen == true)
                    {
                        count += item.Quantity;
                    }
                }
            }
            return count;
        }

        private async Task ImprimirRecibosAsync(string textoReciboPrincipal)
        {
            string printerPrincipal = _config?.PrinterName;
            string printerCozinha = _config?.KitchenPrinterName ?? printerPrincipal;

            if (string.IsNullOrEmpty(printerPrincipal) || printerPrincipal == "(Nenhuma)")
            {
                MessageBox.Show("ERRO: Nenhuma impressora principal configurada!", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool impressaoPrincipalOk = false;
            bool impressaoCozinhaOk = false;

            await _printLock.WaitAsync();

            try
            {
                impressaoPrincipalOk = await ImprimirDocumentoAsync(
                    textoReciboPrincipal,
                    printerPrincipal,
                    "Recibo Principal",
                    _config?.CopiesCount ?? 1
                );

                if (!impressaoPrincipalOk)
                {
                    MessageBox.Show($"Falha na impressão principal.\nVerifique a impressora '{printerPrincipal}'.",
                        "Erro de Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                if (_config?.PrintOrderToKitchen == true)
                {
                    string textoCozinha = GerarTextoReciboCozinha();
                    if (!string.IsNullOrEmpty(textoCozinha))
                    {
                        string printerParaCozinha = string.IsNullOrEmpty(printerCozinha) || printerCozinha == "(Nenhuma)"
                            ? printerPrincipal
                            : printerCozinha;

                        impressaoCozinhaOk = await ImprimirDocumentoAsync(
                            textoCozinha,
                            printerParaCozinha,
                            "Recibo Cozinha",
                            1
                        );

                        if (!impressaoCozinhaOk)
                        {
                            impressaoCozinhaOk = RawPrinterHelper.SendStringToPrinter(printerParaCozinha, textoCozinha);
                            if (!impressaoCozinhaOk)
                            {
                                Debug.WriteLine("⚠️ Cozinha falhou mesmo com raw");
                            }
                        }
                    }
                }

                if (impressaoPrincipalOk)
                {
                    AbrirGavetaDinheiro(printerPrincipal);
                }
                else
                {
                    Debug.WriteLine("Gaveta NÃO aberta — falha na impressão principal");
                }

                string mensagem = "Venda finalizada!\n";
                if (impressaoPrincipalOk) mensagem += "• Recibo cliente impresso\n";
                if (impressaoCozinhaOk) mensagem += "• Pedido cozinha enviado\n";
                if (!impressaoPrincipalOk || !impressaoCozinhaOk)
                    mensagem += "\nAtenção: Alguma impressão falhou. Verifique impressoras.";

                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(mensagem, "Resultado da Impressão",
                        MessageBoxButton.OK,
                        impressaoPrincipalOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exceção crítica na impressão: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Erro grave durante impressão:\n{ex.Message}",
                        "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                _printLock.Release();
            }
        }

        private async Task<bool> ImprimirDocumentoAsync(string texto, string printerName, string tipo, int copias)
        {
            if (string.IsNullOrEmpty(printerName) || printerName == "(Nenhuma)")
                return false;

            try
            {
                bool sucesso = await Task.Run(() =>
                    RawPrinterHelper.SendStringToPrinter(printerName, texto));

                if (sucesso)
                {
                    Debug.WriteLine($"✅ {tipo} impresso via RawPrinterHelper");
                    return true;
                }

                var tcs = new TaskCompletionSource<bool>();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        using var printDoc = new System.Drawing.Printing.PrintDocument();
                        printDoc.PrinterSettings.PrinterName = printerName;
                        printDoc.PrinterSettings.Copies = (short)copias;

                        printDoc.PrintPage += (s, e) =>
                        {
                            using var fonte = new System.Drawing.Font("Consolas", 9);
                            float y = 10;

                            foreach (var linha in texto.Split('\n'))
                            {
                                if (!string.IsNullOrWhiteSpace(linha))
                                {
                                    e.Graphics.DrawString(linha, fonte,
                                        System.Drawing.Brushes.Black, 5, y);
                                    y += fonte.GetHeight(e.Graphics) + 2;
                                }
                            }
                            e.HasMorePages = false;
                        };

                        printDoc.EndPrint += (s, e) => tcs.TrySetResult(true);
                        printDoc.Print();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"❌ Erro no PrintDocument: {ex.Message}");
                        tcs.TrySetResult(false);
                    }
                });

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Falha ao imprimir {tipo}: {ex.Message}");
                return false;
            }
        }

        private async void BtnNovaComanda_Click(object sender, RoutedEventArgs e)
        {
            FecharPopupRecibo();
            AtivarModoSelecaoMesa();
        }

        private bool TentarImpressaoAlternativa(string texto, string printerName, string tipo)
        {
            try
            {
                Debug.WriteLine($"🔄 Tentando método alternativo para {tipo}...");

                bool sucesso = RawPrinterHelper.SendStringToPrinter(printerName, texto);

                if (sucesso)
                {
                    Debug.WriteLine($"✅ {tipo} impresso com RawPrinterHelper");
                    return true;
                }

                string tempFile = Path.Combine(Path.GetTempPath(), $"recibo_{Guid.NewGuid():N}.txt");
                File.WriteAllText(tempFile, texto);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C print /D:\"{printerName}\" \"{tempFile}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                Process.Start(psi);

                Task.Delay(5000).ContinueWith(_ =>
                {
                    try { File.Delete(tempFile); } catch { }
                });

                Debug.WriteLine($"✅ {tipo} impresso com método Process.Start");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Método alternativo também falhou: {ex.Message}");
                return false;
            }
        }

        private async void BtnCancelarVenda_Click(object sender, RoutedEventArgs e)
        {
            if (!Carrinho.Any() && _ordemAtual == null)
            {
                MessageBox.Show("Não há venda em andamento.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var resultado = MessageBox.Show(
                "Deseja cancelar a venda atual?\n\n" +
                "• Todos os itens serão removidos\n" +
                "• A mesa será liberada (se aplicável)\n" +
                "• A comanda será excluída",
                "Cancelar Venda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (resultado != MessageBoxResult.Yes) return;

            try
            {
                if (_ordemAtual != null && _db != null)
                {
                    _db.Orders.Remove(_ordemAtual);
                }

                if (_mesaAtual != null && _mesaAtual.Id > 0 && _db != null)
                {
                    _mesaAtual.Status = TableStatus.Livre;
                    await _db.SaveChangesAsync();
                    AppEvents.RaiseSpecificTableStatusChanged(_mesaAtual.Id);
                }

                AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>());
                AppEvents.RaiseOrderInfoUpdated("000000", "Nenhuma");

                Carrinho.Clear();
                _ordemAtual = null;
                _mesaAtual = null;
                _horaAberturaMesa = null;
                txtMesaAtual.Text = "NENHUMA";
                txtTempoMesa.Text = "";
                txtValorRecebido.Text = "";
                txtDesconto.Text = "0";
                AtualizarTotal();
                FecharPopupRecibo();

                AtualizarTelaCliente();

                if (_db != null)
                {
                    await _db.SaveChangesAsync();
                }
                await CarregarMesasAsync();
                await CarregarComandasAbertasAsync();

                MessageBox.Show("Venda cancelada e mesa liberada com sucesso.",
                    "Cancelado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao cancelar venda: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TxtPesquisaTalao_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await BuscarTalaoAsync();
                e.Handled = true;
            }
        }

        private async void BtnBuscarTalao_Click(object sender, RoutedEventArgs e)
        {
            await BuscarTalaoAsync();
        }

        private async Task BuscarTalaoAsync()
        {
            string texto = txtPesquisaTalao?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(texto) || texto == "Pesquisar talão...")
            {
                MessageBox.Show("Digite o número do talão ou ID da venda.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(texto, out int numero))
            {
                MessageBox.Show("Digite apenas números.", "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var db = new AyGestRestContext();
            Order? ordem = await db.Orders
                .Include(o => o.Items)
                .Include(o => o.Table)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Status == OrderStatus.Fechado && o.TalaoNumber == numero);

            if (ordem == null)
            {
                ordem = await db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Table)
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Status == OrderStatus.Fechado && o.Id == numero);
            }

            if (ordem == null)
            {
                MessageBox.Show($"Talão ou venda nº {texto} não encontrado.", "Não encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MostrarPreviewSegundaVia(ordem);
        }

        private void MostrarPreviewSegundaVia(Order ordem)
        {
            decimal total = ordem.Items.Sum(i => i.TotalItem);
            string metodoPagamento = ordem.Payments?.FirstOrDefault()?.Type.ToString() ?? "Não registrado";
            int talaoNumero = ordem.TalaoNumber;

            var sb = new StringBuilder();
            sb.AppendLine("================== SEGUNDA VIA ==================");
            sb.AppendLine($" {(_config?.RestaurantName?.ToUpper() ?? "RESTAURANTE")}");
            sb.AppendLine(ordem.CloseDate?.ToString("dd/MM/yyyy HH:mm:ss") ?? "Data não registrada");
            sb.AppendLine($"Mesa: {ordem.Table?.Number ?? "Balcão"}");
            sb.AppendLine($"Talão Nº: {talaoNumero:D6}");
            sb.AppendLine("-----------------------------------------------");

            foreach (var item in ordem.Items)
            {
                sb.AppendLine($"{item.Quantity,3}x {item.Name,-30} {item.TotalItem,10:F2}");
            }

            sb.AppendLine("-----------------------------------------------");
            sb.AppendLine($"TOTAL: {total,36:F2} {CurrencySymbol}");
            sb.AppendLine($"Pagamento: {metodoPagamento}");
            sb.AppendLine("");
            sb.AppendLine("*** SEGUNDA VIA - NÃO VÁLIDA COMO DOCUMENTO FISCAL ***");
            sb.AppendLine("Obrigado pela preferência!");
            sb.AppendLine("================================================");

            var janela = new Window
            {
                Title = $"Segunda Via - Talão {talaoNumero:D6}",
                Width = 620,
                Height = 820,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var stack = new StackPanel { Margin = new Thickness(20) };
            var preview = new TextBox
            {
                Text = sb.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.WhiteSmoke,
                Margin = new Thickness(0, 0, 0, 20),
                Padding = new Thickness(10)
            };
            stack.Children.Add(preview);

            var btnReimprimir = new Button
            {
                Content = "Reimprimir Segunda Via",
                Height = 60,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Background = Brushes.ForestGreen,
                Foreground = Brushes.White
            };

            btnReimprimir.Click += async (_, __) => await ReimprimirSegundaViaAsync(sb.ToString());
            stack.Children.Add(btnReimprimir);
            janela.Content = stack;
            janela.ShowDialog();
        }

        private async Task ReimprimirSegundaViaAsync(string textoRecibo)
        {
            var printDlg = new PrintDialog();
            if (!string.IsNullOrEmpty(_config?.PrinterName))
            {
                try
                {
                    printDlg.PrintQueue = new PrintQueue(new PrintServer(), _config.PrinterName);
                }
                catch { }
            }

            if (printDlg.ShowDialog() == true)
            {
                var doc = new FlowDocument(
                    new Paragraph(new Run(textoRecibo))
                    {
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12
                    })
                {
                    PagePadding = new Thickness(30)
                };
                printDlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Segunda Via - Talão");
            }
        }

        private void TimerAtualizacao_Tick(object? sender, EventArgs e)
        {
            try
            {
                AtualizarTempoMesa();
                if (txtDataHora != null)
                {
                    txtDataHora.Text = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                }
                AtualizarPopupRecibo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TimerAtualizacao_Tick: {ex.Message}");
            }
        }

        private void CarregarConfiguracoesGlobais()
        {
            try
            {
                if (txtUsuario != null)
                {
                    txtUsuario.Text = AppSession.CurrentUser?.Username ?? "OPERADOR NÃO IDENTIFICADO";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CarregarConfiguracoesGlobais: {ex.Message}");
            }
        }

        private void AtualizarTempoMesa()
        {
            try
            {
                if (txtTempoMesa == null) return;

                if (_horaAberturaMesa.HasValue)
                {
                    var duracao = DateTime.Now - _horaAberturaMesa.Value;
                    txtTempoMesa.Text = $"Aberta há {duracao:hh\\:mm\\:ss}";
                }
                else
                {
                    txtTempoMesa.Text = "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarTempoMesa: {ex.Message}");
            }
        }

        private void Carrinho_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            try
            {
                AtualizarContadorItens();
                AtualizarTotal();
                AtualizarContadorComandas();
                AtualizarPopupRecibo();

                var cartCopy = new ObservableCollection<CartItem>(Carrinho);
                AppEvents.RaiseCartUpdated(cartCopy);
                AtualizarTelaCliente();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no Carrinho_CollectionChanged: {ex.Message}");
            }
        }

        private void AtualizarContadorItens()
        {
            try
            {
                if (txtContadorItens != null)
                {
                    txtContadorItens.Text = $"({Carrinho.Sum(i => i.Quantity)} itens)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarContadorItens: {ex.Message}");
            }
        }

        private void AtualizarTotal()
        {
            try
            {
                if (cmbDescontoTipo == null || txtDesconto == null || txtTotalFinal == null ||
                    txtTotalHeader == null || txtValorRecebido == null || txtTroco == null)
                    return;

                decimal subtotal = Carrinho.Sum(i => i.SubTotal);

                bool descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
                decimal descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
                decimal descontoAplicado = descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor;
                decimal totalFinal = Math.Max(subtotal - descontoAplicado, 0);

                string simbolo = CurrencySymbol;
                txtTotalFinal.Text = $"{totalFinal:F2} {simbolo}";
                txtTotalHeader.Text = $"{totalFinal:F2} {simbolo}";

                if (_vfdConfig != null)
                {
                    UpdateVFDTotal();
                }

                if (decimal.TryParse(txtValorRecebido.Text ?? "0", out decimal recebido))
                {
                    decimal troco = recebido - totalFinal;
                    if (troco >= 0)
                    {
                        txtTroco.Text = $"Troco: {troco:F2} {simbolo}";
                        txtTroco.Foreground = Brushes.Green;
                    }
                    else
                    {
                        txtTroco.Text = $"Faltam {Math.Abs(troco):F2} {simbolo}";
                        txtTroco.Foreground = Brushes.Red;
                    }
                }
                else
                {
                    txtTroco.Text = $"Troco: 0,00 {simbolo}";
                    txtTroco.Foreground = Brushes.Gray;
                }

                AtualizarTelaCliente();
                UpdateVFDTotal();
                AtualizarPopupRecibo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarTotal: {ex.Message}");
            }
        }

        private void BtnValorExato_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                bool descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
                decimal descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
                decimal descontoAplicado = descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor;
                decimal totalFinal = Math.Max(subtotal - descontoAplicado, 0);
                txtValorRecebido.Text = totalFinal.ToString("F2");
                AtualizarTotal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnValorExato_Click: {ex.Message}");
            }
        }

        private void BtnMais50_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (decimal.TryParse(txtValorRecebido.Text, out decimal atual))
                {
                    txtValorRecebido.Text = (atual + 50m).ToString("F2");
                }
                else
                {
                    txtValorRecebido.Text = "50.00";
                }
                AtualizarTotal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnMais50_Click: {ex.Message}");
            }
        }

        private void BtnMais100_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (decimal.TryParse(txtValorRecebido.Text, out decimal atual))
                {
                    txtValorRecebido.Text = (atual + 100m).ToString("F2");
                }
                else
                {
                    txtValorRecebido.Text = "100.00";
                }
                AtualizarTotal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnMais100_Click: {ex.Message}");
            }
        }

        private void BtnMais500_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (decimal.TryParse(txtValorRecebido.Text, out decimal atual))
                {
                    txtValorRecebido.Text = (atual + 500m).ToString("F2");
                }
                else
                {
                    txtValorRecebido.Text = "500.00";
                }
                AtualizarTotal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnMais500_Click: {ex.Message}");
            }
        }

        private void TxtValorRecebido_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                AtualizarTotal();
                AtualizarPopupRecibo();
                AtualizarTelaCliente();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TxtValorRecebido_TextChanged: {ex.Message}");
            }
        }

        private void CarregarUltimoTalaoDoBanco()
        {
            try
            {
                if (_db == null) return;

                var ultimo = _db.Orders
                    .Where(o => o.Status == OrderStatus.Fechado && o.TalaoNumber > 0)
                    .OrderByDescending(o => o.TalaoNumber)
                    .Select(o => o.TalaoNumber)
                    .FirstOrDefault();
                _numeroTalaoAtual = ultimo;
                AtualizarNumeroTalao();
            }
            catch
            {
                _numeroTalaoAtual = 0;
                AtualizarNumeroTalao();
            }
        }

        private async Task CarregarCategoriasAsync()
        {
            try
            {
                if (pnlCategorias == null || _db == null) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    pnlCategorias.Children.Clear();
                });

                var categorias = await _db.ProductCategories.OrderBy(c => c.Nome).ToListAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    var btnTodas = new Button
                    {
                        Content = "TODAS",
                        Tag = 0,
                        Style = (Style)FindResource("CategoryButtonStyle"),
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                        Foreground = Brushes.White
                    };
                    btnTodas.Click += (s, e) => SelecionarCategoria(0);
                    pnlCategorias.Children.Add(btnTodas);

                    foreach (var cat in categorias)
                    {
                        var btnCategoria = new Button
                        {
                            Content = cat.Nome.ToUpper(),
                            Tag = cat.Id,
                            Style = (Style)FindResource("CategoryButtonStyle")
                        };
                        btnCategoria.Click += (s, e) => SelecionarCategoria(cat.Id);
                        pnlCategorias.Children.Add(btnCategoria);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CarregarCategoriasAsync: {ex.Message}");
            }
        }

        private string _lastSearchText = "";

        private async void TxtPesquisa_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _lastSearchText = txtPesquisa.Text;
                await Task.Delay(300);

                if (_lastSearchText == txtPesquisa.Text)
                {
                    await CarregarProdutosAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TxtPesquisa_TextChanged: {ex.Message}");
            }
        }

        private async Task CarregarProdutosAsync(int? categoriaId = null)
        {
            try
            {
                if (pnlProdutos == null || _db == null) return;

                await Dispatcher.InvokeAsync(() =>
                {
                    pnlProdutos.Children.Clear();
                });

                int catId = categoriaId ?? 0;
                string termo = txtPesquisa?.Text?.Trim() ?? "";

                var query = _db.Products.AsQueryable();

                if (catId != 0)
                {
                    query = query.Where(p => p.CategoryId == catId);
                }

                if (!string.IsNullOrWhiteSpace(termo))
                {
                    query = query.Where(p =>
                        EF.Functions.Like(p.Name, $"%{termo}%") ||
                        (p.Barcode != null && EF.Functions.Like(p.Barcode, $"%{termo}%"))
                    );
                }

                var produtos = await query
                    .OrderBy(p => p.Name)
                    .Take(120)
                    .ToListAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var p in produtos)
                    {
                        var card = CriarCardProduto(p);
                        pnlProdutos.Children.Add(card);
                    }

                    if (txtContadorItens != null)
                    {
                        int totalItens = produtos.Count;
                        string textoBusca = !string.IsNullOrEmpty(termo) ? $" para '{termo}'" : "";
                        txtContadorItens.Text = $"{totalItens} produtos{textoBusca}";

                        if (!string.IsNullOrEmpty(termo))
                        {
                            txtContadorItens.Foreground = Brushes.DarkBlue;
                            txtContadorItens.FontWeight = FontWeights.Bold;
                        }
                        else
                        {
                            txtContadorItens.Foreground = Brushes.Green;
                            txtContadorItens.FontWeight = FontWeights.Normal;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CarregarProdutosAsync: {ex.Message}");
            }
        }

        private Button CriarCardProduto(Product produto)
        {
            var btn = new Button
            {
                Tag = produto,
                Cursor = Cursors.Hand,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Style = (Style)FindResource("ProductCardStyle")
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70, GridUnitType.Pixel) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40, GridUnitType.Pixel) });

            var imageContainer = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                ClipToBounds = true,
                Height = 70,
                Background = new LinearGradientBrush(
                    Color.FromRgb(245, 247, 250),
                    Color.FromRgb(235, 239, 245),
                    90)
            };

            if (produto.Image != null && produto.Image.Length > 0)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = new MemoryStream(produto.Image);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var img = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.UniformToFill,
                        Height = 70,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                    var gradientOverlay = new Grid();
                    gradientOverlay.Children.Add(img);

                    var gradientBorder = new Border
                    {
                        Background = new LinearGradientBrush(
                            Colors.Transparent,
                            Color.FromArgb(15, 0, 0, 0),
                            90)
                    };
                    gradientOverlay.Children.Add(gradientBorder);

                    imageContainer.Child = gradientOverlay;
                }
                catch
                {
                    CriarPlaceholderImagemModerno(imageContainer);
                }
            }
            else
            {
                CriarPlaceholderImagemModerno(imageContainer);
            }

            Grid.SetRow(imageContainer, 0);
            grid.Children.Add(imageContainer);

            var textContainer = new Border
            {
                Margin = new Thickness(10, 6, 10, 4),
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent
            };

            var nomeStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Top
            };

            var txtNome = new TextBlock
            {
                Text = produto.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 43, 54)),
                MaxHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };

            if (!string.IsNullOrEmpty(produto.Description) && produto.Description.Length > 2)
            {
                var descricao = produto.Description.Length > 40 ?
                    produto.Description.Substring(0, 37) + "..." : produto.Description;

                var txtDescricao = new TextBlock
                {
                    Text = descricao,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(108, 115, 127)),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                nomeStack.Children.Add(txtNome);
                nomeStack.Children.Add(txtDescricao);
            }
            else
            {
                nomeStack.Children.Add(txtNome);
            }

            textContainer.Child = nomeStack;
            Grid.SetRow(textContainer, 1);
            grid.Children.Add(textContainer);

            if (produto.Name.Length > 25)
            {
                btn.ToolTip = produto.Name;
            }

            var priceContainer = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(249, 250, 251),
                    Color.FromRgb(243, 244, 246),
                    90),
                CornerRadius = new CornerRadius(0, 0, 10, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Height = 40,
                VerticalAlignment = VerticalAlignment.Bottom
            };

            var priceStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var symbolText = new TextBlock
            {
                Text = CurrencySymbol,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 0)
            };

            var priceText = new TextBlock
            {
                Text = produto.Price.ToString("F2"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                VerticalAlignment = VerticalAlignment.Center
            };

            priceStack.Children.Add(symbolText);
            priceStack.Children.Add(priceText);

            if (produto.Stock <= 5 && produto.Stock > 0)
            {
                var stockBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(254, 243, 199)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var stockText = new TextBlock
                {
                    Text = $"⏳ {produto.Stock}",
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9))
                };

                stockBadge.Child = stockText;
                priceStack.Children.Add(stockBadge);
            }
            else if (produto.Stock <= 0)
            {
                var outOfStockBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(254, 226, 226)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var outOfStockText = new TextBlock
                {
                    Text = "SEM STOCK",
                    FontSize = 7,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28))
                };

                outOfStockBadge.Child = outOfStockText;
                priceStack.Children.Add(outOfStockBadge);
            }

            priceContainer.Child = priceStack;
            Grid.SetRow(priceContainer, 2);
            grid.Children.Add(priceContainer);

            if (produto.IsComposite || produto.PrintToKitchen)
            {
                var badgeContainer = new Border
                {
                    Background = produto.IsComposite ?
                        new SolidColorBrush(Color.FromRgb(219, 234, 254)) :
                        new SolidColorBrush(Color.FromRgb(220, 252, 231)),
                    CornerRadius = new CornerRadius(10, 0, 10, 0),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(-1, -1, 0, 0)
                };

                var badgeText = new TextBlock
                {
                    Text = produto.IsComposite ? "🍳" : "🔥",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = produto.IsComposite ?
                        new SolidColorBrush(Color.FromRgb(30, 64, 175)) :
                        new SolidColorBrush(Color.FromRgb(21, 128, 61))
                };

                badgeContainer.Child = badgeText;

                var overlayGrid = new Grid();
                overlayGrid.Children.Add(grid);
                overlayGrid.Children.Add(badgeContainer);

                btn.Content = overlayGrid;
            }
            else
            {
                btn.Content = grid;
            }

            btn.Click += (_, __) =>
            {
                try
                {
                    if (_ordemAtual == null)
                    {
                        MessageBox.Show("Selecione uma mesa primeiro para iniciar uma venda.",
                            "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (produto.Stock <= 0)
                    {
                        MessageBox.Show($"Produto '{produto.Name}' sem stock disponível.",
                            "Stock Esgotado", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    AdicionarProduto(produto);

                    btn.BeginAnimation(Button.OpacityProperty,
                        new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(0.2)));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erro no clique do produto: {ex.Message}");
                }
            };

            return btn;
        }

        private void CriarPlaceholderImagemModerno(Border container)
        {
            var placeholderGrid = new Grid
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(240, 242, 245),
                    Color.FromRgb(233, 236, 239),
                    90)
            };

            var iconContainer = new Viewbox
            {
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var iconText = new TextBlock
            {
                Text = "🛍️",
                FontSize = 24,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184))
            };

            iconContainer.Child = iconText;
            placeholderGrid.Children.Add(iconContainer);

            container.Child = placeholderGrid;
        }

        private void AdicionarProduto(Product produto)
        {
            try
            {
                var itemExistente = Carrinho.FirstOrDefault(i => i.ProductId == produto.Id);
                if (itemExistente != null)
                {
                    itemExistente.Quantity++;
                }
                else
                {
                    Carrinho.Add(new CartItem
                    {
                        ProductId = produto.Id,
                        ProductName = produto.Name ?? "Produto sem nome",
                        UnitPrice = produto.Price,
                        Quantity = 1
                    });
                }

                AtualizarTotal();

                var cartCopy = new ObservableCollection<CartItem>(Carrinho);
                AppEvents.RaiseCartUpdated(cartCopy);

                if (Carrinho.Count == 1 && !_popupAberto)
                {
                    AbrirPopupRecibo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AdicionarProduto: {ex.Message}");
            }
        }

        private void BtnLimparCarrinho_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (Carrinho.Any() && MessageBox.Show("Deseja realmente limpar todos os itens da comanda atual?",
                        "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>());
                    Carrinho.Clear();
                    AtualizarTelaCliente();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnLimparCarrinho_Click: {ex.Message}");
            }
        }

        private void TxtDesconto_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                AtualizarTotal();
                AtualizarPopupRecibo();

                if (decimal.TryParse(txtDesconto.Text, out decimal discount))
                {
                    AppEvents.RaiseDiscountUpdated(discount);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TxtDesconto_TextChanged: {ex.Message}");
            }
        }

        #endregion

        #region Pagamento e Popups de Pagamento

        private void AbrirPopupPagamento()
        {
            try
            {
                if (!Carrinho.Any())
                {
                    MessageBox.Show("Não há itens no carrinho para finalizar a venda.",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_ordemAtual == null)
                {
                    MessageBox.Show("Selecione uma mesa primeiro para iniciar uma venda.",
                        "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Calcular total
                decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                bool descontoPorcentagem = cmbDescontoTipo?.SelectedIndex == 0;
                decimal descontoValor = decimal.TryParse(txtDesconto?.Text ?? "0", out var d) ? d : 0m;
                decimal descontoAplicado = descontoPorcentagem == true ? subtotal * (descontoValor / 100m) : descontoValor;
                _totalVendaMultiplo = Math.Max(subtotal - descontoAplicado, 0m);

                // Resetar estado do toggle - com verificações de null
                _pagamentoMultiploAtivo = false;

                // Verificar se o toggle existe antes de usar
                if (tglPagamentoMultiplo != null)
                {
                    tglPagamentoMultiplo.IsChecked = false;
                }

                if (panelMetodosSimples != null)
                    panelMetodosSimples.Visibility = Visibility.Visible;

                if (panelMetodosMultiplos != null)
                    panelMetodosMultiplos.Visibility = Visibility.Collapsed;

                if (txtToggleStatus != null)
                {
                    txtToggleStatus.Text = "OFF";
                    txtToggleStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                if (txtPagamentoSimplificadoSubtitulo != null)
                    txtPagamentoSimplificadoSubtitulo.Text = "Selecione o método de pagamento";

                if (txtModoPagamentoLabel != null)
                {
                    txtModoPagamentoLabel.Text = "Pagamento Único";
                    txtModoPagamentoLabel.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                    if (txtModoPagamentoLabel.Background is SolidColorBrush bg)
                        bg.Color = Color.FromRgb(220, 252, 231);
                }

                AbrirPopupPagamentoSimplificado();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AbrirPopupPagamento: {ex.Message}");
                MessageBox.Show($"Erro ao abrir pagamento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void AbrirPopupPagamentoSimplificado()
        {
            try
            {
                if (txtPagamentoSimplificadoTotal != null)
                    txtPagamentoSimplificadoTotal.Text = $"{_totalVendaMultiplo:F2} {CurrencySymbol}";

                // Limpar listas
                _metodosPagamento.Clear();
                _valoresMetodosMultiplos.Clear();

                if (itemsMetodosPagamentoMulti != null)
                {
                    itemsMetodosPagamentoMulti.ItemsSource = null;
                    itemsMetodosPagamentoMulti.ItemsSource = _metodosPagamento;
                }

                if (txtTotalDistribuidoMulti != null)
                    txtTotalDistribuidoMulti.Text = $"0,00 {CurrencySymbol}";

                // Criar botões do modo simples
                CriarBotoesMetodosPagamentoSimplificado();

                // Se o toggle estiver ativo, criar botões do modo múltiplo
                if (_pagamentoMultiploAtivo)
                {
                    CriarBotoesMetodosMultiplos();
                }

                if (popupPagamentoSimplificado != null)
                    popupPagamentoSimplificado.Visibility = Visibility.Visible;

                if (overlayGrid != null)
                    overlayGrid.Visibility = Visibility.Visible;

                // Animação de entrada
                if (popupPagamentoSimplificadoScale != null)
                {
                    popupPagamentoSimplificadoScale.ScaleX = 0.9;
                    popupPagamentoSimplificadoScale.ScaleY = 0.9;

                    var storyboard = new Storyboard();
                    var scaleXAnim = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    var scaleYAnim = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    Storyboard.SetTarget(scaleXAnim, popupPagamentoSimplificadoScale);
                    Storyboard.SetTarget(scaleYAnim, popupPagamentoSimplificadoScale);
                    Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("ScaleX"));
                    Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("ScaleY"));
                    storyboard.Children.Add(scaleXAnim);
                    storyboard.Children.Add(scaleYAnim);
                    storyboard.Begin();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AbrirPopupPagamentoSimplificado: {ex.Message}");
            }
        }

        private void CriarBotoesMetodosPagamentoSimplificado()
        {
            try
            {
                if (pnlBotoesMetodosSimplificado == null) return;

                pnlBotoesMetodosSimplificado.Children.Clear();

                var metodos = new List<(string Nome, string Icon, PaymentType Tipo, Color Cor, string CorHex)>
        {
            ("Dinheiro", "💰", PaymentType.Dinheiro, Color.FromRgb(16, 185, 129), "#10B981"),
            ("Multibanco", "🏦", PaymentType.Multibanco, Color.FromRgb(59, 130, 246), "#3B82F6"),
            ("MB Way", "📱", PaymentType.MBWay, Color.FromRgb(139, 92, 246), "#8B5CF6"),
            ("Cartão Crédito", "💳", PaymentType.CartaoCredito, Color.FromRgb(239, 68, 68), "#EF4444"),
            ("Cartão Débito", "💳", PaymentType.CartaoDebito, Color.FromRgb(245, 158, 11), "#F59E0B"),
            ("M-Pesa", "📲", PaymentType.MPesa, Color.FromRgb(124, 58, 237), "#7C3AED"),
            ("Emola", "📲", PaymentType.Emola, Color.FromRgb(236, 72, 153), "#EC4899"),
            ("Ponto24", "🏧", PaymentType.Ponto24, Color.FromRgb(14, 165, 233), "#0EA5E9"),
            ("POS NedBank", "🏦", PaymentType.PosNedBank, Color.FromRgb(30, 64, 175), "#1E40AF"),
            ("POS Moza", "🏦", PaymentType.PosMoza, Color.FromRgb(185, 28, 28), "#B91C1C")
        };

                foreach (var metodo in metodos)
                {
                    var border = new Border
                    {
                        Background = new SolidColorBrush(metodo.Cor),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(0),
                        Margin = new Thickness(6, 4, 6, 4)
                    };

                    var btn = new Button
                    {
                        Content = $"{metodo.Icon} {metodo.Nome}",
                        Tag = metodo.Tipo,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 14,
                        Height = 48,
                        Padding = new Thickness(16, 0, 16, 0),
                        Cursor = Cursors.Hand,
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };

                    btn.Click += (s, e) =>
                    {
                        var tipo = (PaymentType)((Button)s).Tag;
                        _selectedPaymentType = tipo;

                        var nomeMetodo = metodos.FirstOrDefault(m => m.Tipo == tipo).Nome;
                        var iconMetodo = metodos.FirstOrDefault(m => m.Tipo == tipo).Icon;

                        var metodoUnico = new List<MetodoPagamentoItem>
                {
                    new MetodoPagamentoItem
                    {
                        Metodo = nomeMetodo,
                        Icon = iconMetodo,
                        Valor = _totalVendaMultiplo,
                        Tipo = tipo,
                        Background = new SolidColorBrush(metodo.Cor)
                    }
                };

                        // Fechar popup e finalizar
                        if (popupPagamentoSimplificado != null)
                            popupPagamentoSimplificado.Visibility = Visibility.Collapsed;

                        if (popupPagamentoMultiplo != null && popupPagamentoMultiplo.Visibility != Visibility.Visible &&
                            !_popupAberto && !_popupAtivarAberto && overlayGrid != null)
                        {
                            overlayGrid.Visibility = Visibility.Collapsed;
                        }
                        _ = FinalizarVendaMultiplaAsync(metodoUnico);
                    };

                    border.Child = btn;
                    pnlBotoesMetodosSimplificado.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CriarBotoesMetodosPagamentoSimplificado: {ex.Message}");
            }
        }

        private void CriarBotoesMetodosMultiplos()
        {
            try
            {
                pnlBotoesMetodosMultiplos.Children.Clear();
                _valoresMetodosMultiplos.Clear();

                var metodos = new List<(string Nome, string Icon, PaymentType Tipo, Color Cor, string CorHex)>
                {
                    ("Dinheiro", "💰", PaymentType.Dinheiro, Color.FromRgb(16, 185, 129), "#10B981"),
                    ("Multibanco", "🏦", PaymentType.Multibanco, Color.FromRgb(59, 130, 246), "#3B82F6"),
                    ("MB Way", "📱", PaymentType.MBWay, Color.FromRgb(139, 92, 246), "#8B5CF6"),
                    ("Cartão Crédito", "💳", PaymentType.CartaoCredito, Color.FromRgb(239, 68, 68), "#EF4444"),
                    ("Cartão Débito", "💳", PaymentType.CartaoDebito, Color.FromRgb(245, 158, 11), "#F59E0B"),
                    ("M-Pesa", "📲", PaymentType.MPesa, Color.FromRgb(124, 58, 237), "#7C3AED"),
                    ("Emola", "📲", PaymentType.Emola, Color.FromRgb(236, 72, 153), "#EC4899"),
                    ("Ponto24", "🏧", PaymentType.Ponto24, Color.FromRgb(14, 165, 233), "#0EA5E9"),
                    ("POS NedBank", "🏦", PaymentType.PosNedBank, Color.FromRgb(30, 64, 175), "#1E40AF"),
                    ("POS Moza", "🏦", PaymentType.PosMoza, Color.FromRgb(185, 28, 28), "#B91C1C")
                };

                foreach (var metodo in metodos)
                {
                    // Container para o botão + campo de valor
                    var container = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(4),
                        Margin = new Thickness(4, 4, 4, 4),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(229, 231, 235)),
                        BorderThickness = new Thickness(1)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Botão do método
                    var btnMetodo = new Button
                    {
                        Content = $"{metodo.Icon} {metodo.Nome}",
                        Tag = metodo.Tipo,
                        Background = new SolidColorBrush(metodo.Cor),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 11,
                        Height = 32,
                        Padding = new Thickness(10, 0, 10, 0),
                        Cursor = Cursors.Hand,
                        BorderThickness = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    btnMetodo.Click += (s, e) =>
                    {
                        var tipo = (PaymentType)((Button)s).Tag;
                        AdicionarMetodoMultiplo(tipo);
                    };

                    Grid.SetColumn(btnMetodo, 0);
                    grid.Children.Add(btnMetodo);

                    // Campo de valor
                    var txtValor = new TextBox
                    {
                        Width = 80,
                        Height = 32,
                        FontSize = 12,
                        TextAlignment = TextAlignment.Right,
                        Text = "0,00",
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalContentAlignment = VerticalAlignment.Center,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
                        BorderThickness = new Thickness(1)
                    };
                    txtValor.GotFocus += (s, e) => txtValor.SelectAll();
                    txtValor.TextChanged += (s, e) =>
                    {
                        if (decimal.TryParse(txtValor.Text?.Replace(",", "."), out decimal valor))
                        {
                            var tipo = (PaymentType)btnMetodo.Tag;
                            _valoresMetodosMultiplos[tipo] = valor;
                        }
                    };

                    Grid.SetColumn(txtValor, 2);
                    grid.Children.Add(txtValor);

                    container.Child = grid;
                    pnlBotoesMetodosMultiplos.Children.Add(container);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no CriarBotoesMetodosMultiplos: {ex.Message}");
            }
        }

        private void AdicionarMetodoMultiplo(PaymentType tipo)
        {
            try
            {
                if (!_valoresMetodosMultiplos.TryGetValue(tipo, out decimal valor) || valor <= 0)
                {
                    MessageBox.Show("Digite um valor válido maior que zero para este método.",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
                decimal restante = _totalVendaMultiplo - totalDistribuido;

                if (valor > restante + 0.01m)
                {
                    MessageBox.Show($"O valor excede o restante a pagar ({restante:F2} {CurrencySymbol}).",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var metodos = new Dictionary<PaymentType, (string Nome, string Icon, Color Cor)>
                {
                    { PaymentType.Dinheiro, ("Dinheiro", "💰", Color.FromRgb(16, 185, 129)) },
                    { PaymentType.Multibanco, ("Multibanco", "🏦", Color.FromRgb(59, 130, 246)) },
                    { PaymentType.MBWay, ("MB Way", "📱", Color.FromRgb(139, 92, 246)) },
                    { PaymentType.CartaoCredito, ("Cartão Crédito", "💳", Color.FromRgb(239, 68, 68)) },
                    { PaymentType.CartaoDebito, ("Cartão Débito", "💳", Color.FromRgb(245, 158, 11)) },
                    { PaymentType.MPesa, ("M-Pesa", "📲", Color.FromRgb(124, 58, 237)) },
                    { PaymentType.Emola, ("Emola", "📲", Color.FromRgb(236, 72, 153)) },
                    { PaymentType.Ponto24, ("Ponto24", "🏧", Color.FromRgb(14, 165, 233)) },
                    { PaymentType.PosNedBank, ("POS NedBank", "🏦", Color.FromRgb(30, 64, 175)) },
                    { PaymentType.PosMoza, ("POS Moza", "🏦", Color.FromRgb(185, 28, 28)) }
                };

                var info = metodos[tipo];

                var existente = _metodosPagamento.FirstOrDefault(m => m.Tipo == tipo);
                if (existente != null)
                {
                    existente.Valor += valor;
                }
                else
                {
                    var item = new MetodoPagamentoItem
                    {
                        Metodo = info.Nome,
                        Icon = info.Icon,
                        Valor = valor,
                        Tipo = tipo,
                        Background = new SolidColorBrush(info.Cor)
                    };
                    _metodosPagamento.Add(item);
                }

                itemsMetodosPagamentoMulti.ItemsSource = null;
                itemsMetodosPagamentoMulti.ItemsSource = _metodosPagamento;

                decimal totalDistribuidoAtual = _metodosPagamento.Sum(m => m.Valor);
                txtTotalDistribuidoMulti.Text = $"{totalDistribuidoAtual:F2} {CurrencySymbol}";

                _valoresMetodosMultiplos[tipo] = 0;

                foreach (var child in pnlBotoesMetodosMultiplos.Children)
                {
                    if (child is Border border && border.Child is Grid grid)
                    {
                        foreach (var gridChild in grid.Children)
                        {
                            if (gridChild is Button btn && btn.Tag is PaymentType btnTipo && btnTipo == tipo)
                            {
                                foreach (var innerChild in grid.Children)
                                {
                                    if (innerChild is TextBox txt)
                                    {
                                        txt.Text = "0,00";
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                if (totalDistribuidoAtual >= _totalVendaMultiplo - 0.01m)
                {
                    MostrarMensagemStatus("✅ Total completamente distribuído!", Brushes.Green, 2);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao adicionar método: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoverMetodoMulti_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button?.Tag is MetodoPagamentoItem item)
                {
                    _metodosPagamento.Remove(item);
                    itemsMetodosPagamentoMulti.ItemsSource = null;
                    itemsMetodosPagamentoMulti.ItemsSource = _metodosPagamento;

                    decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
                    txtTotalDistribuidoMulti.Text = $"{totalDistribuido:F2} {CurrencySymbol}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnRemoverMetodoMulti_Click: {ex.Message}");
            }
        }

        private async void BtnFinalizarMultiplo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_metodosPagamento.Any())
                {
                    MessageBox.Show("Adicione pelo menos um método de pagamento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
                if (totalDistribuido < _totalVendaMultiplo - 0.01m)
                {
                    var result = MessageBox.Show(
                        $"O valor distribuído ({totalDistribuido:F2} {CurrencySymbol}) é menor que o total ({_totalVendaMultiplo:F2} {CurrencySymbol}).\n\n" +
                        "Deseja finalizar mesmo assim?",
                        "Confirmação",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                await FinalizarVendaMultiplaAsync(_metodosPagamento.ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao finalizar pagamento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
            }
        }

        private async void BtnFinalizarPagamentoSimplificado_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_pagamentoMultiploAtivo)
                {
                    BtnFinalizarMultiplo_Click(sender, e);
                }
                else
                {
                    MessageBox.Show("Selecione um método de pagamento clicando nos botões acima.",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao finalizar pagamento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnFecharPagamentoSimplificado_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                _metodosPagamento.Clear();
                _valoresMetodosMultiplos.Clear();

                if (popupPagamentoMultiplo.Visibility != Visibility.Visible &&
                    !_popupAberto &&
                    !_popupAtivarAberto)
                {
                    overlayGrid.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnFecharPagamentoSimplificado_Click: {ex.Message}");
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnFecharPagamentoMultiplo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                _metodosPagamento.Clear();

                if (popupPagamentoSimplificado.Visibility != Visibility.Visible &&
                    !_popupAberto &&
                    !_popupAtivarAberto)
                {
                    overlayGrid.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnFecharPagamentoMultiplo_Click: {ex.Message}");
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCancelarPagamentoMultiplo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                _metodosPagamento.Clear();

                if (popupPagamentoSimplificado.Visibility != Visibility.Visible &&
                    !_popupAberto &&
                    !_popupAtivarAberto)
                {
                    overlayGrid.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnCancelarPagamentoMultiplo_Click: {ex.Message}");
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
            }
        }

        private void TglPagamentoMultiplo_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                _pagamentoMultiploAtivo = true;

                if (panelMetodosSimples != null)
                    panelMetodosSimples.Visibility = Visibility.Collapsed;

                if (panelMetodosMultiplos != null)
                    panelMetodosMultiplos.Visibility = Visibility.Visible;

                if (txtToggleStatus != null)
                {
                    txtToggleStatus.Text = "ON";
                    txtToggleStatus.Foreground = new SolidColorBrush(Color.FromRgb(79, 70, 229));
                }

                if (txtPagamentoSimplificadoSubtitulo != null)
                    txtPagamentoSimplificadoSubtitulo.Text = "Distribua o valor entre diferentes métodos";

                if (txtModoPagamentoLabel != null)
                {
                    txtModoPagamentoLabel.Text = "Pagamento Múltiplo";
                    txtModoPagamentoLabel.Foreground = new SolidColorBrush(Color.FromRgb(124, 58, 237));
                    if (txtModoPagamentoLabel.Background is SolidColorBrush bg)
                        bg.Color = Color.FromRgb(237, 233, 254);
                }

                CriarBotoesMetodosMultiplos();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TglPagamentoMultiplo_Checked: {ex.Message}");
            }
        }

        private void TglPagamentoMultiplo_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                _pagamentoMultiploAtivo = false;

                if (panelMetodosSimples != null)
                    panelMetodosSimples.Visibility = Visibility.Visible;

                if (panelMetodosMultiplos != null)
                    panelMetodosMultiplos.Visibility = Visibility.Collapsed;

                if (txtToggleStatus != null)
                {
                    txtToggleStatus.Text = "OFF";
                    txtToggleStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }

                if (txtPagamentoSimplificadoSubtitulo != null)
                    txtPagamentoSimplificadoSubtitulo.Text = "Selecione o método de pagamento";

                if (txtModoPagamentoLabel != null)
                {
                    txtModoPagamentoLabel.Text = "Pagamento Único";
                    txtModoPagamentoLabel.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
                    if (txtModoPagamentoLabel.Background is SolidColorBrush bg)
                        bg.Color = Color.FromRgb(220, 252, 231);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TglPagamentoMultiplo_Unchecked: {ex.Message}");
            }
        }

       
        private void BtnAdicionarMetodo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (cmbMetodoPagamento.SelectedItem == null)
                {
                    MessageBox.Show("Selecione um método de pagamento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!decimal.TryParse(txtValorMetodo.Text?.Replace(",", "."), out decimal valor) || valor <= 0)
                {
                    MessageBox.Show("Digite um valor válido maior que zero.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
                decimal restante = _totalVendaMultiplo - totalDistribuido;

                if (valor > restante + 0.01m)
                {
                    MessageBox.Show($"O valor excede o restante a pagar ({restante:F2} {CurrencySymbol}).",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var comboItem = cmbMetodoPagamento.SelectedItem as ComboBoxItem;
                string metodo = comboItem.Content.ToString();
                string tag = comboItem.Tag.ToString();

                PaymentType tipo = tag switch
                {
                    "Dinheiro" => PaymentType.Dinheiro,
                    "Multibanco" => PaymentType.Multibanco,
                    "MBWay" => PaymentType.MBWay,
                    "CartaoCredito" => PaymentType.CartaoCredito,
                    "CartaoDebito" => PaymentType.CartaoDebito,
                    "MPesa" => PaymentType.MPesa,
                    "Emola" => PaymentType.Emola,
                    "Ponto24" => PaymentType.Ponto24,
                    "PosNedBank" => PaymentType.PosNedBank,
                    "PosMoza" => PaymentType.PosMoza,
                    _ => PaymentType.Outro
                };

                var existente = _metodosPagamento.FirstOrDefault(m => m.Tipo == tipo);
                if (existente != null)
                {
                    existente.Valor += valor;
                }
                else
                {
                    var icon = GetPaymentTypeIcon(tipo);
                    var item = new MetodoPagamentoItem
                    {
                        Metodo = metodo,
                        Icon = icon,
                        Valor = valor,
                        Tipo = tipo,
                        Background = new SolidColorBrush(GetMetodoColor(tipo))
                    };
                    _metodosPagamento.Add(item);
                }

                itemsMetodosPagamento.ItemsSource = null;
                itemsMetodosPagamento.ItemsSource = _metodosPagamento;

                txtValorMetodo.Text = "0,00";
                SafeFocusControl(txtValorMetodo);

                AtualizarRestanteMultiplo();

                decimal restanteAposAdicao = _totalVendaMultiplo - _metodosPagamento.Sum(m => m.Valor);
                if (restanteAposAdicao <= 0)
                {
                    MostrarMensagemStatus("✅ Total completamente distribuído! Clique em 'Finalizar Pagamento'.", Brushes.Green, 3);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao adicionar método: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoverMetodoPagamento_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button?.Tag is MetodoPagamentoItem item)
                {
                    _metodosPagamento.Remove(item);
                    itemsMetodosPagamento.ItemsSource = null;
                    itemsMetodosPagamento.ItemsSource = _metodosPagamento;
                    AtualizarRestanteMultiplo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnRemoverMetodoPagamento_Click: {ex.Message}");
            }
        }

        private void AtualizarRestanteMultiplo()
        {
            decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
            decimal restante = _totalVendaMultiplo - totalDistribuido;
            txtRestanteMultiplo.Text = $"Restante: {Math.Max(restante, 0):F2} {CurrencySymbol}";
            txtRestanteMultiplo.Foreground = restante > 0 ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.Green);
            txtTotalDistribuido.Text = $"{totalDistribuido:F2} {CurrencySymbol}";
        }

        private Color GetMetodoColor(PaymentType tipo)
        {
            return tipo switch
            {
                PaymentType.Dinheiro => Color.FromRgb(16, 185, 129),
                PaymentType.Multibanco => Color.FromRgb(59, 130, 246),
                PaymentType.MBWay => Color.FromRgb(139, 92, 246),
                PaymentType.CartaoCredito => Color.FromRgb(239, 68, 68),
                PaymentType.CartaoDebito => Color.FromRgb(245, 158, 11),
                PaymentType.MPesa => Color.FromRgb(124, 58, 237),
                PaymentType.Emola => Color.FromRgb(236, 72, 153),
                PaymentType.Ponto24 => Color.FromRgb(14, 165, 233),
                PaymentType.PosNedBank => Color.FromRgb(30, 64, 175),
                PaymentType.PosMoza => Color.FromRgb(185, 28, 28),
                _ => Color.FromRgb(107, 114, 128)
            };
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
                PaymentType.MPesa => "📲",
                PaymentType.Emola => "📲",
                PaymentType.Ponto24 => "🏧",
                PaymentType.PosNedBank => "🏦",
                PaymentType.PosMoza => "🏦",
                PaymentType.Outro => "🔄",
                _ => "💳"
            };
        }

        private void TxtValorMetodo_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Apenas para garantir que o evento não causa problemas
        }

        private async void BtnFinalizarPagamentoMultiplo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_metodosPagamento.Any())
                {
                    MessageBox.Show("Adicione pelo menos um método de pagamento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal totalDistribuido = _metodosPagamento.Sum(m => m.Valor);
                if (totalDistribuido < _totalVendaMultiplo - 0.01m)
                {
                    var result = MessageBox.Show(
                        $"O valor distribuído ({totalDistribuido:F2} {CurrencySymbol}) é menor que o total ({_totalVendaMultiplo:F2} {CurrencySymbol}).\n\n" +
                        "Deseja finalizar mesmo assim?",
                        "Confirmação",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                await FinalizarVendaMultiplaAsync(_metodosPagamento.ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao finalizar pagamento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Finalização de Venda

        private async void BtnFinalizarVenda_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Carrinho.Any())
                {
                    MessageBox.Show("Não há itens no carrinho para finalizar a venda.",
                        "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_ordemAtual == null)
                {
                    MessageBox.Show("Selecione uma mesa primeiro para iniciar uma venda.",
                        "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                AbrirPopupPagamento();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnFinalizarVenda_Click: {ex.Message}");
                MessageBox.Show($"Erro ao finalizar venda: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task FinalizarVendaMultiplaAsync(List<MetodoPagamentoItem> metodos)
        {
            try
            {
                if (_impressaoEmAndamento)
                {
                    MessageBox.Show("Uma impressão já está em andamento. Aguarde...",
                        "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _impressaoEmAndamento = true;

                if (_ordemAtual == null || !Carrinho.Any() || _db == null)
                {
                    MessageBox.Show("Não há venda em andamento.", "Aviso",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    _impressaoEmAndamento = false;
                    return;
                }

                decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                bool descontoPorcentagem = (cmbDescontoTipo?.SelectedIndex ?? 0) == 0;
                decimal descontoValor = decimal.TryParse(txtDesconto?.Text ?? "0", out var d) ? d : 0m;
                decimal descontoAplicado = descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor;
                decimal totalFinal = Math.Max(subtotal - descontoAplicado, 0m);
                decimal totalPago = metodos.Sum(m => m.Valor);

                string printerPrincipal = _config?.PrinterName;
                string printerCozinha = _config?.KitchenPrinterName;

                if (string.IsNullOrEmpty(printerPrincipal) || printerPrincipal == "(Nenhuma)")
                {
                    var result = MessageBox.Show(
                        "Nenhuma impressora principal configurada!\n\nDeseja continuar sem imprimir?",
                        "Aviso", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        _impressaoEmAndamento = false;
                        return;
                    }
                }

                if (string.IsNullOrEmpty(printerCozinha) || printerCozinha == "(Nenhuma)")
                {
                    printerCozinha = printerPrincipal;
                }

                var itensCozinha = new List<CartItem>();
                bool temItensCozinha = false;

                try
                {
                    using (var dbContext = new AyGestRestContext())
                    {
                        foreach (var item in Carrinho)
                        {
                            var produto = dbContext.Products
                                .AsNoTracking()
                                .FirstOrDefault(p => p.Id == item.ProductId);

                            if (produto != null && produto.PrintToKitchen == true)
                            {
                                itensCozinha.Add(item);
                                temItensCozinha = true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Erro ao verificar itens cozinha: {ex.Message}");
                }

                string textoReciboPrincipal = GerarTextoReciboMultiplo(totalFinal, metodos);
                string textoReciboCozinha = "";

                if (temItensCozinha && itensCozinha.Any())
                {
                    textoReciboCozinha = GerarTextoReciboCozinhaCompleto(itensCozinha);
                }

                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM OrderItems WHERE OrderId = {0}",
                        _ordemAtual.Id);

                    var orderItems = Carrinho.Select(item => new OrderItem
                    {
                        OrderId = _ordemAtual.Id,
                        ProductId = item.ProductId,
                        Name = item.ProductName,
                        UnitPrice = item.UnitPrice,
                        Quantity = item.Quantity
                    }).ToList();

                    _db.OrderItems.AddRange(orderItems);

                    foreach (var metodo in metodos)
                    {
                        var pagamento = new Payment
                        {
                            OrderId = _ordemAtual.Id,
                            Amount = metodo.Valor,
                            Type = metodo.Tipo,
                            PaymentDate = DateTime.Now,
                            Notes = $"Pagamento via {metodo.Metodo}"
                        };
                        _db.Payments.Add(pagamento);
                    }

                    foreach (var item in Carrinho)
                    {
                        var produto = await _db.Products.FindAsync(item.ProductId);
                        if (produto != null && !produto.IsComposite)
                        {
                            produto.Stock -= item.Quantity;
                        }
                    }

                    _ordemAtual.Status = OrderStatus.Fechado;
                    _ordemAtual.CloseDate = DateTime.Now;
                    _numeroTalaoAtual++;
                    _ordemAtual.TalaoNumber = _numeroTalaoAtual;

                    if (_mesaAtual != null && _mesaAtual.Id > 0)
                    {
                        _mesaAtual.Status = TableStatus.Livre;
                    }

                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _impressaoEmAndamento = false;
                    throw new Exception("Erro ao salvar no banco: " + ex.Message, ex);
                }

                bool impressaoPrincipalOk = false;
                bool impressaoCozinhaOk = false;

                try
                {
                    await _printLock.WaitAsync();

                    bool mesmaImpressora = (printerPrincipal == printerCozinha);

                    if (mesmaImpressora)
                    {
                        string textoCombinado = "";

                        if (temItensCozinha && !string.IsNullOrEmpty(textoReciboCozinha))
                        {
                            textoCombinado = textoReciboCozinha + "\n\n" + textoReciboPrincipal;
                        }
                        else
                        {
                            textoCombinado = textoReciboPrincipal;
                        }

                        impressaoPrincipalOk = await ImprimirDocumentoAsync(
                            textoCombinado,
                            printerPrincipal,
                            "Recibo Completo",
                            _config?.CopiesCount ?? 1
                        );

                        impressaoCozinhaOk = impressaoPrincipalOk && temItensCozinha;
                    }
                    else
                    {
                        if (temItensCozinha && !string.IsNullOrEmpty(textoReciboCozinha))
                        {
                            impressaoCozinhaOk = await ImprimirDocumentoAsync(
                                textoReciboCozinha,
                                printerCozinha,
                                "Recibo Cozinha",
                                1
                            );

                            await Task.Delay(500);
                        }

                        impressaoPrincipalOk = await ImprimirDocumentoAsync(
                            textoReciboPrincipal,
                            printerPrincipal,
                            "Recibo Principal",
                            _config?.CopiesCount ?? 1
                        );
                    }

                    if (impressaoPrincipalOk)
                    {
                        AbrirGavetaDinheiro(printerPrincipal);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Erro na impressão: {ex.Message}");
                    MessageBox.Show($"Erro na impressão: {ex.Message}", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _printLock.Release();
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    AppEvents.RaiseCartUpdated(new ObservableCollection<CartItem>());
                    Carrinho.Clear();
                    _ordemAtual = null;
                    _mesaAtual = null;
                    _horaAberturaMesa = null;

                    txtMesaAtual.Text = "NENHUMA";
                    txtTempoMesa.Text = "";
                    txtValorRecebido.Text = "";
                    txtDesconto.Text = "0";

                    AtualizarTotal();
                    FecharPopupRecibo();
                    AtualizarNumeroTalao();

                    popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                    popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
                    overlayGrid.Visibility = Visibility.Collapsed;
                    _metodosPagamento.Clear();

                    MostrarMensagemStatus($"✅ Venda finalizada com {metodos.Count} método(s) de pagamento!", Brushes.Green, 4);
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
                        Debug.WriteLine($"Erro em background: {ex.Message}");
                    }
                    finally
                    {
                        _impressaoEmAndamento = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no FinalizarVendaMultiplaAsync: {ex.Message}");
                MessageBox.Show($"Erro ao finalizar venda: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                _impressaoEmAndamento = false;
                overlayGrid.Visibility = Visibility.Collapsed;
                popupPagamentoSimplificado.Visibility = Visibility.Collapsed;
                popupPagamentoMultiplo.Visibility = Visibility.Collapsed;
            }
        }

        private string GerarTextoReciboMultiplo(decimal totalFinal, List<MetodoPagamentoItem> metodos)
        {
            try
            {
                var sb = new StringBuilder();
                int larguraMaxima = 40;
                string linhaFina = new string('-', larguraMaxima);
                string linhaGrossa = new string('=', larguraMaxima);

                string mesaNumero = _mesaAtual?.Number ?? "Balcão";
                string operadorNome = AppSession.CurrentUser?.Username ?? "N/A";
                string nomeRestaurante = _config?.RestaurantName?.ToUpper() ?? "RESTAURANTE";
                string nif = _config?.NIF ?? "";
                string telefone = _config?.Phone ?? "";
                string endereco = _config?.Address ?? "";

                decimal descontoValor = 0;
                bool descontoPorcentagem = false;

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    if (cmbDescontoTipo != null && txtDesconto != null)
                    {
                        descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
                        descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
                    }
                }

                decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                decimal desconto = descontoValor > 0 ?
                    (descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor) : 0;

                sb.AppendLine(CenterText(nomeRestaurante, larguraMaxima));
                sb.AppendLine(linhaGrossa);

                if (!string.IsNullOrEmpty(endereco))
                    sb.AppendLine(CenterText(TruncarTexto(endereco, larguraMaxima), larguraMaxima));
                if (!string.IsNullOrEmpty(telefone))
                    sb.AppendLine(CenterText($"Tel: {telefone}", larguraMaxima));
                if (!string.IsNullOrEmpty(nif))
                    sb.AppendLine(CenterText($"NUIT: {nif}", larguraMaxima));

                sb.AppendLine(linhaFina);
                sb.AppendLine($"MESA : {mesaNumero,-12} TALÃO: {_numeroTalaoAtual:D6}");
                sb.AppendLine($"DATA : {DateTime.Now:dd/MM/yyyy HH:mm}");
                sb.AppendLine($"OPER : {TruncarTexto(operadorNome, 15)}");
                sb.AppendLine($"COMANDA: {_ordemAtual?.Id.ToString("D6") ?? "000000"}");
                sb.AppendLine(linhaFina);

                sb.Append("\x1B\x21\x08");
                sb.AppendLine("ITEM                QTD    PREÇO     TOTAL");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(new string('-', larguraMaxima));

                foreach (var item in Carrinho)
                {
                    string nome = TruncarTexto(item.ProductName, 18);
                    sb.AppendLine($"{nome,-18} {item.Quantity,3}x{item.UnitPrice,7:F2} {item.SubTotal,8:F2}");
                }

                sb.AppendLine(linhaFina);
                sb.AppendLine($"SUBTOTAL:{subtotal,25:F2}{CurrencySymbol}");

                if (desconto > 0)
                {
                    string tipoDesc = descontoPorcentagem ? $"{descontoValor}%" : "Valor";
                    sb.AppendLine($"DESCONTO ({tipoDesc}):{desconto,18:F2}{CurrencySymbol}");
                    sb.AppendLine(linhaFina);
                }

                sb.Append("\x1B\x21\x10");
                sb.AppendLine($"TOTAL:{totalFinal,29:F2}{CurrencySymbol}");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(linhaGrossa);
                sb.AppendLine("PAGAMENTO:");

                foreach (var metodo in metodos)
                {
                    sb.AppendLine($"  {metodo.Icon} {metodo.Metodo,-15} {metodo.Valor,15:F2} {CurrencySymbol}");
                }

                sb.AppendLine(linhaGrossa);
                sb.AppendLine(CenterText("*** PAGAMENTO CONCLUÍDO ***", larguraMaxima));
                sb.AppendLine(CenterText("OBRIGADO PELA PREFERÊNCIA", larguraMaxima));
                sb.AppendLine(linhaFina);
                sb.AppendLine(CenterText("Software: AyGestRest", larguraMaxima));
                sb.AppendLine(CenterText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), larguraMaxima));

                sb.Append("\x1B\x64\x03");
                sb.Append("\x1D\x56\x41\x00");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"💥 ERRO CRÍTICO ao gerar recibo: {ex.Message}");
                return $"ERRO AO GERAR RECIBO: {ex.Message}";
            }
        }

        #endregion

        #region Métodos de Recibo e Impressão

        private string GerarTextoReciboCozinha()
        {
            try
            {
                if (!Carrinho.Any()) return string.Empty;

                var sb = new StringBuilder();
                int larguraMaxima = 40;
                string linha = new string('=', larguraMaxima);

                string mesaNumero = _mesaAtual?.Number ?? "Balcão";
                string operadorNome = AppSession.CurrentUser?.Username ?? "N/A";

                var itensCozinha = new List<CartItem>();
                using (var dbContext = new AyGestRestContext())
                {
                    foreach (var item in Carrinho)
                    {
                        var produto = dbContext.Products
                            .AsNoTracking()
                            .FirstOrDefault(p => p.Id == item.ProductId);

                        if (produto != null && produto.PrintToKitchen == true)
                        {
                            itensCozinha.Add(item);
                        }
                    }
                }

                if (!itensCozinha.Any())
                {
                    return string.Empty;
                }

                sb.Append("\x1B\x40");
                sb.Append("\x1B\x21\x30");
                sb.AppendLine(CenterText("PEDIDO COZINHA", larguraMaxima));
                sb.Append("\x1B\x21\x00");

                sb.AppendLine(CenterText(_config?.RestaurantName?.ToUpper() ?? "RESTAURANTE", larguraMaxima));
                sb.AppendLine(linha);

                sb.AppendLine($"Talão: {_numeroTalaoAtual:D6}");
                sb.AppendLine($"Mesa: {mesaNumero}");
                sb.AppendLine($"Comanda: {_ordemAtual?.Id.ToString("D6") ?? "000000"}");
                sb.AppendLine($"Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                sb.AppendLine($"Operador: {operadorNome}");
                sb.AppendLine(new string('-', larguraMaxima));

                sb.Append("\x1B\x21\x08");
                sb.AppendLine("ITEM                QTD");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(new string('-', larguraMaxima));

                foreach (var item in itensCozinha)
                {
                    string nome = TruncarTexto(item.ProductName, 20);
                    sb.AppendLine($"{nome,-20} {item.Quantity,3}x");

                    using (var db = new AyGestRestContext())
                    {
                        var produto = db.Products
                            .Include(p => p.ProductIngredients)
                                .ThenInclude(pi => pi.Ingredient)
                            .FirstOrDefault(p => p.Id == item.ProductId);

                        if (produto != null && produto.IsComposite && produto.ProductIngredients != null)
                        {
                            foreach (var ingrediente in produto.ProductIngredients)
                            {
                                if (ingrediente.Ingredient != null)
                                {
                                    sb.AppendLine($"  - {ingrediente.Ingredient.Name} x{item.Quantity}");
                                }
                            }
                        }
                    }
                }

                sb.AppendLine(new string('-', larguraMaxima));
                sb.AppendLine($"Total itens: {itensCozinha.Sum(i => i.Quantity)}");
                sb.AppendLine(linha);
                sb.Append("\x1B\x21\x30");
                sb.AppendLine(CenterText("PREPARAR IMEDIATAMENTE", larguraMaxima));
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(CenterText(DateTime.Now.ToString("HH:mm:ss"), larguraMaxima));
                sb.AppendLine(linha);
                sb.AppendLine(CenterText("*** PEDIDO COZINHA ***", larguraMaxima));
                sb.AppendLine(linha);

                sb.Append("\x1B\x64\x03");
                sb.Append("\x1D\x56\x41\x00");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"💥 ERRO ao gerar recibo cozinha: {ex.Message}");
                return "ERRO AO GERAR PEDIDO DA COZINHA";
            }
        }

        private string TruncarTexto(string texto, int maxLength)
        {
            if (string.IsNullOrEmpty(texto) || texto.Length <= maxLength)
                return texto;

            return texto.Substring(0, maxLength - 3) + "...";
        }

        private string CenterText(string text, int width)
        {
            if (string.IsNullOrEmpty(text)) return new string(' ', width);
            if (text.Length > width) text = text.Substring(0, width);
            int spaces = (width - text.Length) / 2;
            return new string(' ', spaces) + text + new string(' ', width - text.Length - spaces);
        }

        private string GerarTextoReciboCozinhaCompleto(List<CartItem> itensCozinha)
        {
            try
            {
                var sb = new StringBuilder();
                int larguraMaxima = 42;
                string linhaSimples = new string('-', larguraMaxima);

                sb.Append("\x1B\x40");
                sb.Append("\x1B\x21\x00");
                sb.Append("\x1B\x74\x13");

                sb.Append("\x1B\x21\x30");
                sb.AppendLine(CenterText("COZINHA", larguraMaxima));
                sb.Append("\x1B\x21\x00");

                sb.AppendLine(CenterText(_config?.RestaurantName?.ToUpper() ?? "RESTAURANTE", larguraMaxima));
                sb.AppendLine(linhaSimples);

                sb.Append("\x1B\x21\x08");
                sb.AppendLine("Mesa:     " + (_mesaAtual?.Number ?? "Balcão"));
                sb.AppendLine("Comanda:  " + (_ordemAtual?.Id.ToString("D6") ?? "000000"));
                sb.AppendLine("Talao:    " + _numeroTalaoAtual.ToString("D6"));
                sb.AppendLine("Data:     " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                sb.AppendLine("Atendente:" + (AppSession.CurrentUser?.Username ?? "N/A"));
                sb.Append("\x1B\x21\x00");

                sb.AppendLine(linhaSimples);

                sb.Append("\x1B\x21\x08");
                sb.AppendLine("ITENS:");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(linhaSimples);

                int totalItens = 0;

                foreach (var item in itensCozinha)
                {
                    totalItens += item.Quantity;

                    string nomeProduto = item.ProductName ?? "Produto";
                    nomeProduto = RemoverCaracteresInvalidos(nomeProduto);

                    if (nomeProduto.Length > 28)
                    {
                        nomeProduto = nomeProduto.Substring(0, 25) + "...";
                    }

                    sb.AppendLine($" {item.Quantity}x {nomeProduto}");

                    try
                    {
                        using (var db = new AyGestRestContext())
                        {
                            var produto = db.Products
                                .AsNoTracking()
                                .FirstOrDefault(p => p.Id == item.ProductId);

                            if (produto != null && produto.IsComposite)
                            {
                                var ingredientes = db.ProductIngredients
                                    .Where(pi => pi.ProductId == item.ProductId)
                                    .Join(db.Ingredients,
                                        pi => pi.IngredientId,
                                        i => i.Id,
                                        (pi, i) => i.Name)
                                    .ToList();

                                if (ingredientes.Any())
                                {
                                    sb.AppendLine("   Ingredientes: " + string.Join(", ", ingredientes));
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                sb.AppendLine(linhaSimples);
                sb.AppendLine($"Total: {totalItens} itens");

                if (!string.IsNullOrEmpty(_ordemAtual?.Observations))
                {
                    sb.AppendLine("Obs: " + RemoverCaracteresInvalidos(_ordemAtual.Observations));
                }

                sb.AppendLine(linhaSimples);
                sb.AppendLine(CenterText(DateTime.Now.ToString("HH:mm:ss"), larguraMaxima));
                sb.AppendLine(CenterText("URGENTE", larguraMaxima));

                sb.Append("\x1B\x64\x04");
                sb.Append("\x1D\x56\x42\x00");
                sb.Append("\x0A\x0A\x0A");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro recibo: {ex.Message}");

                var fallback = new StringBuilder();
                fallback.Append("\x1B\x40");
                fallback.AppendLine("COZINHA");
                fallback.AppendLine($"Mesa: {_mesaAtual?.Number ?? "Balcão"}");
                fallback.AppendLine($"Comanda: {_ordemAtual?.Id.ToString("D6") ?? "000000"}");

                foreach (var item in itensCozinha)
                {
                    fallback.AppendLine($"{item.Quantity}x {item.ProductName}");
                }

                fallback.Append("\x1D\x56\x41\x00");

                return fallback.ToString();
            }
        }

        private string RemoverCaracteresInvalidos(string texto)
        {
            if (string.IsNullOrEmpty(texto))
                return "";

            var caracteresSeguros = new HashSet<char>();

            for (char c = 'A'; c <= 'Z'; c++) caracteresSeguros.Add(c);
            for (char c = 'a'; c <= 'z'; c++) caracteresSeguros.Add(c);
            for (char c = '0'; c <= '9'; c++) caracteresSeguros.Add(c);

            string especiaisSeguros = " .,;:!?@#$%&*()-_+=[]{}<>/\\|'\"";
            foreach (char c in especiaisSeguros) caracteresSeguros.Add(c);

            var mapaSubstituicao = new Dictionary<char, char>
            {
                { 'ç', 'c' }, { 'Ç', 'C' },
                { 'á', 'a' }, { 'à', 'a' }, { 'ã', 'a' }, { 'â', 'a' }, { 'ä', 'a' },
                { 'Á', 'A' }, { 'À', 'A' }, { 'Ã', 'A' }, { 'Â', 'A' }, { 'Ä', 'A' },
                { 'é', 'e' }, { 'è', 'e' }, { 'ê', 'e' }, { 'ë', 'e' },
                { 'É', 'E' }, { 'È', 'E' }, { 'Ê', 'E' }, { 'Ë', 'E' },
                { 'í', 'i' }, { 'ì', 'i' }, { 'î', 'i' }, { 'ï', 'i' },
                { 'Í', 'I' }, { 'Ì', 'I' }, { 'Î', 'I' }, { 'Ï', 'I' },
                { 'ó', 'o' }, { 'ò', 'o' }, { 'õ', 'o' }, { 'ô', 'o' }, { 'ö', 'o' },
                { 'Ó', 'O' }, { 'Ò', 'O' }, { 'Õ', 'O' }, { 'Ô', 'O' }, { 'Ö', 'O' },
                { 'ú', 'u' }, { 'ù', 'u' }, { 'û', 'u' }, { 'ü', 'u' },
                { 'Ú', 'U' }, { 'Ù', 'U' }, { 'Û', 'U' }, { 'Ü', 'U' },
                { 'ñ', 'n' }, { 'Ñ', 'N' },
                { 'º', '.' }, { 'ª', '.' }, { '°', '.' }
            };

            var resultado = new StringBuilder();
            foreach (char c in texto)
            {
                if (caracteresSeguros.Contains(c))
                {
                    resultado.Append(c);
                }
                else if (mapaSubstituicao.TryGetValue(c, out char substituto))
                {
                    resultado.Append(substituto);
                }
            }

            return resultado.ToString().Trim();
        }

        private void AbrirGavetaDinheiro(string printerName = null)
        {
            try
            {
                if (_ultimaAberturaGaveta.HasValue &&
                    (DateTime.Now - _ultimaAberturaGaveta.Value).TotalMilliseconds < 500)
                {
                    return;
                }

                _ultimaAberturaGaveta = DateTime.Now;

                string nomeImpressora = printerName ?? _config?.PrinterName;
                if (string.IsNullOrEmpty(nomeImpressora)) return;

                string cashDrawerCommand = "\x1B\x70\x00\x19\xFA";
                bool enviado = RawPrinterHelper.SendStringToPrinter(nomeImpressora, cashDrawerCommand);

                if (enviado)
                {
                    Debug.WriteLine($"✅ Gaveta aberta: {nomeImpressora}");
                }
            }
            catch { }
        }

        private void AtualizarNumeroTalao()
        {
            try
            {
                if (txtUltimoTalao != null)
                {
                    txtUltimoTalao.Text = _numeroTalaoAtual.ToString("D6");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarNumeroTalao: {ex.Message}");
            }
        }

        #endregion

        #region Sistema de Tela Secundária

        private void AbrirTelaSecundaria()
        {
            if (_customerDisplay != null)
            {
                try
                {
                    _customerDisplay.Activate();
                    _customerDisplay.WindowState = WindowState.Maximized;
                    return;
                }
                catch
                {
                    _customerDisplay = null;
                }
            }

            try
            {
                _customerDisplay = new CustomerDisplayWindow(Carrinho)
                {
                    Title = $"Tela do Cliente - {RestaurantName}",
                    WindowStartupLocation = WindowStartupLocation.Manual
                };

                if (System.Windows.Forms.Screen.AllScreens.Length > 1)
                {
                    var segundaTela = System.Windows.Forms.Screen.AllScreens[1];
                    _customerDisplay.Left = segundaTela.WorkingArea.Left;
                    _customerDisplay.Top = segundaTela.WorkingArea.Top;
                    _customerDisplay.Width = segundaTela.WorkingArea.Width;
                    _customerDisplay.Height = segundaTela.WorkingArea.Height;
                    _customerDisplay.WindowState = WindowState.Normal;
                }
                else
                {
                    _customerDisplay.WindowState = WindowState.Maximized;
                }

                _customerDisplay.Closed += (sender, e) =>
                {
                    _customerDisplay = null;
                };

                _customerDisplay.Show();
                AtualizarTelaCliente();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao abrir tela secundária: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                _customerDisplay = null;
            }
        }

        private void FecharTelaSecundaria()
        {
            try
            {
                if (_customerDisplay != null)
                {
                    _customerDisplay.Close();
                    _customerDisplay = null;
                }
            }
            catch { }
        }

        #endregion

        #region Sistema de Popup Recibo

        private void AbrirPopupRecibo()
        {
            try
            {
                if (!_popupAberto)
                {
                    popupRecibo.HorizontalAlignment = HorizontalAlignment.Left;
                    popupRecibo.VerticalAlignment = VerticalAlignment.Bottom;
                    popupRecibo.Margin = new Thickness(10, 0, 0, 80);

                    popupTransform.X = 0;
                    popupTransform.Y = 0;

                    popupRecibo.Visibility = Visibility.Visible;
                    _popupAberto = true;

                    AtualizarPopupRecibo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AbrirPopupRecibo: {ex.Message}");
            }
        }

        private void FecharPopupRecibo()
        {
            try
            {
                _popupAberto = false;
                popupRecibo.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no FecharPopupRecibo: {ex.Message}");
            }
        }

        private void AlternarPopupRecibo()
        {
            try
            {
                if (_popupAberto)
                    FecharPopupRecibo();
                else
                    AbrirPopupRecibo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AlternarPopupRecibo: {ex.Message}");
            }
        }

        private void PopupRecibo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                _isDraggingPopup = true;
                _popupDragStartPoint = e.GetPosition(this);
                _popupOriginalPosition = new Point(popupTransform.X, popupTransform.Y);

                var border = sender as Border;
                if (border != null)
                {
                    border.CaptureMouse();
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no PopupRecibo_MouseLeftButtonDown: {ex.Message}");
            }
        }

        private void PopupRecibo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_isDraggingPopup)
                {
                    _isDraggingPopup = false;

                    var border = sender as Border;
                    if (border != null)
                    {
                        border.ReleaseMouseCapture();
                    }

                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no PopupRecibo_MouseLeftButtonUp: {ex.Message}");
            }
        }

        private void PopupRecibo_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (_isDraggingPopup && e.LeftButton == MouseButtonState.Pressed)
                {
                    Point currentPosition = e.GetPosition(this);

                    double deltaX = currentPosition.X - _popupDragStartPoint.X;
                    double deltaY = currentPosition.Y - _popupDragStartPoint.Y;

                    double maxX = this.ActualWidth - popupRecibo.ActualWidth;
                    double maxY = this.ActualHeight - popupRecibo.ActualHeight;

                    double newX = Math.Max(0, Math.Min(_popupOriginalPosition.X + deltaX, maxX));
                    double newY = Math.Max(0, Math.Min(_popupOriginalPosition.Y + deltaY, maxY));

                    popupTransform.X = newX;
                    popupTransform.Y = newY;

                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no PopupRecibo_MouseMove: {ex.Message}");
            }
        }

        private void BtnFecharRecibo_Click(object sender, RoutedEventArgs e)
        {
            FecharPopupRecibo();
        }

        private void BtnVisualizarRecibo_Click(object sender, RoutedEventArgs e)
        {
            AbrirPopupRecibo();
        }

        private void AtualizarPopupRecibo()
        {
            try
            {
                if (!_popupAberto || popupRecibo.Visibility != Visibility.Visible) return;

                txtReciboMesa.Text = $"Mesa: {_mesaAtual?.Number ?? "Balcão"}";
                txtReciboData.Text = $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}";
                txtReciboOperador.Text = $"Operador: {AppSession.CurrentUser?.Username ?? "N/A"}";

                itemsRecibo.ItemsSource = null;
                itemsRecibo.ItemsSource = Carrinho;

                decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                txtReciboSubtotal.Text = $"{subtotal:F2} {CurrencySymbol}";

                decimal descontoValor = 0;
                bool temDesconto = false;

                if (cmbDescontoTipo != null && txtDesconto != null)
                {
                    bool descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
                    decimal valorDesconto = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;

                    if (valorDesconto > 0)
                    {
                        temDesconto = true;
                        descontoValor = descontoPorcentagem ? subtotal * (valorDesconto / 100m) : valorDesconto;
                        txtReciboDesconto.Text = $"-{descontoValor:F2} {CurrencySymbol}";
                    }
                }

                gridReciboDesconto.Visibility = temDesconto ? Visibility.Visible : Visibility.Collapsed;

                decimal totalFinal = Math.Max(subtotal - descontoValor, 0);
                txtReciboTotal.Text = $"{totalFinal:F2} {CurrencySymbol}";

                txtReciboPagamento.Text = $"Forma de pagamento: Múltipla";

                if (decimal.TryParse(txtValorRecebido?.Text ?? "0", out decimal recebido) && recebido > 0)
                {
                    if (recebido >= totalFinal)
                    {
                        txtReciboStatus.Text = "Pagamento confirmado";
                        txtReciboStatus.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                    }
                    else
                    {
                        txtReciboStatus.Text = "Pagamento pendente";
                        txtReciboStatus.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
                    }
                }
                else
                {
                    txtReciboStatus.Text = "Aguardando pagamento";
                    txtReciboStatus.Foreground = new SolidColorBrush(Color.FromRgb(243, 156, 18));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no AtualizarPopupRecibo: {ex.Message}");
            }
        }

        #endregion

        #region Atalhos de Teclado

        private void POSControl_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case Key.F1:
                        AtivarModoSelecaoMesa();
                        e.Handled = true;
                        break;

                    case Key.F2:
                        BtnFinalizarVenda_Click(sender, e);
                        e.Handled = true;
                        break;

                    case Key.F3:
                        SafeFocusControl(txtPesquisa);
                        e.Handled = true;
                        break;

                    case Key.F4:
                        BtnLimparCarrinho_Click(sender, e);
                        e.Handled = true;
                        break;

                    case Key.F5:
                        BtnImprimirRecibo_Click(sender, e);
                        e.Handled = true;
                        break;

                    case Key.F6:
                        BtnPrevisualizarRecibo_Click(sender, e);
                        e.Handled = true;
                        break;

                    case Key.F8:
                        BtnClienteAleatorio_Click(sender, e);
                        e.Handled = true;
                        break;

                    case Key.F9:
                        SafeFocusControl(txtPesquisa);
                        e.Handled = true;
                        break;

                    case Key.Escape:
                        if (_modoSelecaoMesa)
                        {
                            _modoSelecaoMesa = false;
                            borderMensagemStatus.Visibility = Visibility.Collapsed;
                            txtInfoMesa.Text = "Selecione uma mesa para começar";
                        }
                        else if (_popupAberto)
                        {
                            FecharPopupRecibo();
                        }
                        else if (popupPagamentoMultiplo.Visibility == Visibility.Visible)
                        {
                            BtnFecharPagamentoMultiplo_Click(sender, e);
                        }
                        else if (popupPagamentoSimplificado.Visibility == Visibility.Visible)
                        {
                            BtnFecharPagamentoSimplificado_Click(sender, e);
                        }
                        else if (popupAtivarMesa.Visibility == Visibility.Visible)
                        {
                            FecharPopupAtivacao();
                        }
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no POSControl_KeyDown: {ex.Message}");
            }
        }

        #endregion

        #region VFD Display

        private void TryOpenVFD()
        {
            if (_vfdConfig == null)
            {
                Debug.WriteLine("VFDConfig não inicializado");
                return;
            }

            try
            {
                if (_vfdPort != null && _vfdPort.IsOpen)
                    _vfdPort.Close();

                _vfdPort = new SerialPort(_vfdConfig.COMPort, _vfdConfig.BaudRate)
                {
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None
                };
                _vfdPort.Open();

                byte[] clear = { 0x0C };
                _vfdPort.Write(clear, 0, clear.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao abrir VFD: {ex.Message}");
            }
        }

        private void UpdateVFDDisplay(string line1, string line2 = "")
        {
            if (_vfdConfig == null || !_vfdConfig.IsEnabled || _vfdPort == null || !_vfdPort.IsOpen)
                return;

            try
            {
                byte[] clear = { 0x0C };
                _vfdPort.Write(clear, 0, clear.Length);

                string text = CenterText(line1, _vfdConfig.DisplayColumns);
                if (_vfdConfig.DisplayLines >= 2 && !string.IsNullOrEmpty(line2))
                    text += "\n" + CenterText(line2, _vfdConfig.DisplayColumns);

                byte[] data = Encoding.ASCII.GetBytes(text);
                _vfdPort.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar display VFD: {ex.Message}");
            }
        }

        private void UpdateVFDTotal()
        {
            if (_vfdConfig == null || !_vfdConfig.IsEnabled || !_vfdConfig.ShowRealTimeTotal)
                return;

            if (txtTotalFinal == null || string.IsNullOrEmpty(txtTotalFinal.Text))
                return;

            try
            {
                decimal total = decimal.Parse(txtTotalFinal.Text.Replace(" Mtn", "").Replace("MT", ""),
                    CultureInfo.InvariantCulture);
                string totalStr = $"TOTAL: {total:F2} {CurrencySymbol}";
                UpdateVFDDisplay(_config?.RestaurantName ?? "RESTAURANTE", totalStr);
            }
            catch (FormatException)
            {
                Debug.WriteLine("Erro ao converter total para VFD");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar VFD: {ex.Message}");
            }
        }

        #endregion

        #region Outros Métodos

        private async void BtnImprimirRecibo_Click(object sender, RoutedEventArgs e)
        {
            if (!Carrinho.Any())
            {
                MessageBox.Show("Não há itens no carrinho para imprimir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal subtotal = Carrinho.Sum(i => i.SubTotal);
            bool descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
            decimal descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
            decimal descontoAplicado = descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor;
            decimal totalFinal = Math.Max(subtotal - descontoAplicado, 0);
            decimal recebido = decimal.TryParse(txtValorRecebido.Text ?? "0", out var r) ? r : 0m;
            decimal troco = recebido - totalFinal;

            string textoRecibo = GerarTextoRecibo(totalFinal, recebido, troco >= 0 ? troco : 0m);

            await ImprimirReciboAsync(textoRecibo);
        }

        private async Task ImprimirReciboAsync(string textoRecibo)
        {
            string printerPrincipal = _config?.PrinterName;

            if (string.IsNullOrEmpty(printerPrincipal) || printerPrincipal == "(Nenhuma)")
            {
                MessageBox.Show("ERRO: Nenhuma impressora principal configurada!",
                    "Erro Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                bool principalOK = await ImprimirDocumentoAsync(
                    textoRecibo,
                    printerPrincipal,
                    "Recibo Principal",
                    _config?.CopiesCount ?? 1
                );

                if (!principalOK)
                {
                    MessageBox.Show($"Falha na impressão principal.\nVerifique a impressora '{printerPrincipal}'.",
                        "Erro de Impressão", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AbrirGavetaDinheiro(printerPrincipal);

                if (_config?.PrintOrderToKitchen == true)
                {
                    string textoCozinha = GerarTextoReciboCozinha();

                    if (!string.IsNullOrEmpty(textoCozinha))
                    {
                        string printerParaCozinha = string.IsNullOrEmpty(_config?.KitchenPrinterName) ||
                                                  _config.KitchenPrinterName == "(Nenhuma)" ?
                                                  printerPrincipal : _config.KitchenPrinterName;

                        bool cozinhaOK = await ImprimirDocumentoAsync(
                            textoCozinha,
                            printerParaCozinha,
                            "Recibo Cozinha",
                            1
                        );

                        if (!cozinhaOK)
                        {
                            TentarImpressaoAlternativaCozinha(textoCozinha, printerParaCozinha);
                        }
                    }
                }

                MostrarMensagemStatus("Venda finalizada! Recibos impressos.", Brushes.Green, 3);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro crítico na impressão:\n{ex.Message}",
                    "Erro Fatal", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TentarImpressaoAlternativaCozinha(string textoCozinha, string printerName)
        {
            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), $"cozinha_{DateTime.Now:HHmmss}.txt");
                File.WriteAllText(tempFile, textoCozinha);

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C print /D:\"{printerName}\" \"{tempFile}\"",
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                Process.Start(psi);

                Task.Delay(3000).ContinueWith(_ =>
                {
                    try { File.Delete(tempFile); } catch { }
                });
            }
            catch (Exception exAlt)
            {
                Debug.WriteLine($"❌ Método alternativo também falhou: {exAlt.Message}");

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var printDlg = new PrintDialog();
                        if (printDlg.ShowDialog() == true)
                        {
                            var doc = new FlowDocument(
                                new Paragraph(new Run(textoCozinha))
                                {
                                    FontFamily = new FontFamily("Consolas"),
                                    FontSize = 12
                                })
                            {
                                PagePadding = new Thickness(30)
                            };
                            printDlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Pedido Cozinha");
                        }
                    }
                    catch (Exception exDialog)
                    {
                        Debug.WriteLine($"💥 Falha até no diálogo: {exDialog.Message}");
                    }
                });
            }
        }

        private string GerarTextoRecibo(decimal totalFinal, decimal recebido, decimal troco)
        {
            try
            {
                var sb = new StringBuilder();
                int larguraMaxima = 40;
                string linhaFina = new string('-', larguraMaxima);
                string linhaGrossa = new string('=', larguraMaxima);

                string formaPagamento = _selectedPaymentType.ToString();
                decimal descontoValor = 0;
                bool descontoPorcentagem = false;
                string mesaNumero = _mesaAtual?.Number ?? "Balcão";
                string operadorNome = AppSession.CurrentUser?.Username ?? "N/A";
                string nomeRestaurante = _config?.RestaurantName?.ToUpper() ?? "RESTAURANTE";
                string nif = _config?.NIF ?? "";
                string telefone = _config?.Phone ?? "";
                string endereco = _config?.Address ?? "";

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    if (cmbDescontoTipo != null && txtDesconto != null)
                    {
                        descontoPorcentagem = cmbDescontoTipo.SelectedIndex == 0;
                        descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
                    }
                }

                sb.AppendLine(CenterText(nomeRestaurante, larguraMaxima));
                sb.AppendLine(linhaGrossa);

                if (!string.IsNullOrEmpty(endereco))
                {
                    sb.AppendLine(CenterText(TruncarTexto(endereco, larguraMaxima), larguraMaxima));
                }

                if (!string.IsNullOrEmpty(telefone))
                {
                    sb.AppendLine(CenterText($"Tel: {telefone}", larguraMaxima));
                }

                if (!string.IsNullOrEmpty(nif))
                {
                    sb.AppendLine(CenterText($"NUIT: {nif}", larguraMaxima));
                }

                sb.AppendLine(linhaFina);

                sb.AppendLine($"MESA : {mesaNumero,-12} TALÃO: {_numeroTalaoAtual:D6}");
                sb.AppendLine($"DATA : {DateTime.Now:dd/MM/yyyy HH:mm}");
                sb.AppendLine($"OPER : {TruncarTexto(operadorNome, 15)}");
                sb.AppendLine($"COMANDA: {_ordemAtual?.Id.ToString("D6") ?? "000000"}");
                sb.AppendLine(linhaFina);

                sb.Append("\x1B\x21\x08");
                sb.AppendLine("ITEM                QTD    PREÇO     TOTAL");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(new string('-', larguraMaxima));

                decimal subtotal = 0;
                foreach (var item in Carrinho)
                {
                    string nome = TruncarTexto(item.ProductName, 18);
                    sb.AppendLine($"{nome,-18} {item.Quantity,3}x{item.UnitPrice,7:F2} {item.SubTotal,8:F2}");
                    subtotal += item.SubTotal;

                    if (item.ProductName.Length > 18)
                    {
                        string nomeRestante = TruncarTexto(item.ProductName.Substring(18), 18);
                        if (!string.IsNullOrEmpty(nomeRestante))
                        {
                            sb.AppendLine($"  {nomeRestante,-16}");
                        }
                    }
                }

                sb.AppendLine(linhaFina);

                decimal desconto = descontoValor > 0 ?
                    (descontoPorcentagem ? subtotal * (descontoValor / 100m) : descontoValor) : 0;

                sb.AppendLine($"SUBTOTAL:{subtotal,25:F2}{CurrencySymbol}");

                if (desconto > 0)
                {
                    string tipoDesc = descontoPorcentagem ? $"{descontoValor}%" : "Valor";
                    sb.AppendLine($"DESCONTO ({tipoDesc}):{desconto,18:F2}{CurrencySymbol}");
                    sb.AppendLine(linhaFina);
                }

                sb.Append("\x1B\x21\x10");
                sb.AppendLine($"TOTAL:{totalFinal,29:F2}{CurrencySymbol}");
                sb.Append("\x1B\x21\x00");
                sb.AppendLine(linhaGrossa);

                formaPagamento = TruncarTexto(formaPagamento.ToUpper(), 12);

                sb.AppendLine($"FORMA PAGTO: {formaPagamento,-12}");
                sb.AppendLine($"RECEBIDO   : {recebido,25:F2}{CurrencySymbol}");

                if (troco > 0)
                    sb.AppendLine($"TROCO      : {troco,25:F2}{CurrencySymbol}");
                else if (recebido < totalFinal)
                    sb.AppendLine($"FALTA      : {Math.Abs(troco),25:F2}{CurrencySymbol}");

                sb.AppendLine(linhaGrossa);

                sb.AppendLine(CenterText("*** PAGAMENTO CONCLUÍDO ***", larguraMaxima));
                sb.AppendLine(CenterText("OBRIGADO PELA PREFERÊNCIA", larguraMaxima));
                sb.AppendLine(linhaFina);
                sb.AppendLine(CenterText("Software: AyGestRest", larguraMaxima));
                sb.AppendLine(CenterText(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), larguraMaxima));

                sb.Append("\x1B\x64\x03");
                sb.Append("\x1D\x56\x41\x00");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"💥 ERRO CRÍTICO ao gerar recibo: {ex.Message}\n{ex.StackTrace}");
                return $"ERRO AO GERAR RECIBO: {ex.Message}";
            }
        }

        private void BtnPrevisualizarRecibo_Click(object sender, RoutedEventArgs e)
        {
            if (!Carrinho.Any())
            {
                MessageBox.Show("Não há itens no carrinho para pré-visualizar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            decimal subtotal = Carrinho.Sum(i => i.SubTotal);
            decimal descontoValor = decimal.TryParse(txtDesconto.Text ?? "0", out var d) ? d : 0m;
            bool isPercent = cmbDescontoTipo.SelectedIndex == 0;
            decimal desconto = isPercent ? subtotal * (descontoValor / 100m) : descontoValor;
            decimal totalFinal = subtotal - desconto;
            decimal recebido = decimal.TryParse(txtValorRecebido.Text ?? "0", out var r) ? r : 0m;
            decimal troco = recebido - totalFinal;

            string texto = GerarTextoRecibo(totalFinal, recebido, troco >= 0 ? troco : 0);

            var win = new Window
            {
                Title = "Pré-visualização do Recibo",
                Width = 520,
                Height = 780,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Content = new TextBox
                {
                    Text = texto,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    IsReadOnly = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(20),
                    Background = Brushes.WhiteSmoke
                }
            };
            win.ShowDialog();
        }

        private async void TxtPesquisa_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(txtPesquisa.Text))
                {
                    string searchText = txtPesquisa.Text.Trim();

                    if (int.TryParse(searchText, out int productId) && _db != null)
                    {
                        var produtoEncontrado = await _db.Products.FindAsync(productId);

                        if (produtoEncontrado == null)
                        {
                            produtoEncontrado = await _db.Products
                                .FirstOrDefaultAsync(p => p.Barcode == searchText);
                        }

                        if (produtoEncontrado != null)
                        {
                            if (_ordemAtual == null)
                            {
                                MessageBox.Show("Selecione uma mesa primeiro para iniciar uma venda.",
                                    "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }

                            AdicionarProduto(produtoEncontrado);

                            txtPesquisa.Clear();

                            SafeFocusControl(txtPesquisa);

                            MostrarMensagemStatus($"{produtoEncontrado.Name} adicionado ao carrinho!",
                                Brushes.Green, 2);

                            e.Handled = true;
                            return;
                        }
                    }

                    await CarregarProdutosAsync();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    txtPesquisa.Text = "";
                    await CarregarProdutosAsync();
                    e.Handled = true;
                }
                else if (e.Key == Key.F3)
                {
                    SafeFocusControl(txtPesquisa);
                    txtPesquisa.SelectAll();
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no TxtPesquisa_KeyDown: {ex.Message}");
            }
        }

        private async void SelecionarCategoria(int categoriaId)
        {
            try
            {
                foreach (Button btn in pnlCategorias.Children)
                {
                    if (btn.Tag is int id)
                    {
                        if (id == categoriaId)
                        {
                            btn.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219));
                            btn.Foreground = Brushes.White;
                            btn.FontWeight = FontWeights.Bold;
                            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(41, 128, 185));
                        }
                        else
                        {
                            btn.Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
                            btn.Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80));
                            btn.FontWeight = FontWeights.SemiBold;
                            btn.BorderBrush = new SolidColorBrush(Color.FromRgb(209, 217, 224));
                        }
                    }
                }

                await CarregarProdutosAsync(categoriaId);

                txtPesquisa.Text = "";
                SafeFocusControl(txtPesquisa);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no SelecionarCategoria: {ex.Message}");
            }
        }

        private async void BtnLimparPesquisa_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtPesquisa.Text = "";
                await CarregarProdutosAsync();
                SafeFocusControl(txtPesquisa);
                MostrarMensagemStatus("Pesquisa limpa", Brushes.Blue, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no BtnLimparPesquisa_Click: {ex.Message}");
            }
        }

        #endregion

        #region Classes Auxiliares

        public class CartItem
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; } = "";
            public decimal UnitPrice { get; set; }
            public int Quantity { get; set; } = 1;
            public decimal SubTotal => UnitPrice * Quantity;
        }

        public class CurrencyConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is decimal decimalValue)
                {
                    return $"{decimalValue:F2} Mtn";
                }
                return value?.ToString() ?? "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }

    #region Classe RawPrinterHelper

    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

        public static bool SendStringToPrinter(string szPrinterName, string szString)
        {
            IntPtr pBytes = Marshal.StringToCoTaskMemAnsi(szString);
            try
            {
                return SendBytesToPrinter(szPrinterName, pBytes, szString.Length);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pBytes);
            }
        }

        private static bool SendBytesToPrinter(string szPrinterName, IntPtr pBytes, Int32 dwCount)
        {
            Int32 dwWritten = 0;
            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOA di = new DOCINFOA
            {
                pDocName = "Recibo POS",
                pDataType = "RAW"
            };
            bool success = false;

            if (OpenPrinter(szPrinterName, out hPrinter, IntPtr.Zero))
            {
                if (StartDocPrinter(hPrinter, 1, di))
                {
                    if (StartPagePrinter(hPrinter))
                    {
                        success = WritePrinter(hPrinter, pBytes, dwCount, out dwWritten);
                        EndPagePrinter(hPrinter);
                    }
                    EndDocPrinter(hPrinter);
                }
                ClosePrinter(hPrinter);
            }

            return success;
        }
    }

    #endregion

    #region Classe BarcodeScannerManager

    public class BarcodeScannerManager : IDisposable
    {
        private readonly TextBox _searchTextBox;
        private readonly Action<string> _onBarcodeScanned;
        private readonly AyGestRestContext _db;
        private StringBuilder _barcodeBuffer = new StringBuilder();
        private DispatcherTimer _bufferTimer;
        private bool _isScannerInput = false;

        public BarcodeScannerManager(TextBox searchTextBox, AyGestRestContext db, Action<string> onBarcodeScanned)
        {
            _searchTextBox = searchTextBox ?? throw new ArgumentNullException(nameof(searchTextBox));
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _onBarcodeScanned = onBarcodeScanned ?? throw new ArgumentNullException(nameof(onBarcodeScanned));

            _bufferTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100),
                IsEnabled = false
            };
            _bufferTimer.Tick += BufferTimer_Tick;

            _searchTextBox.PreviewKeyDown += SearchTextBox_PreviewKeyDown;
            _searchTextBox.KeyDown += SearchTextBox_KeyDown;
        }

        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key >= Key.D0 && e.Key <= Key.D9 ||
                e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9 ||
                e.Key == Key.Enter)
            {
                _isScannerInput = true;
            }
            else if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Escape)
            {
                _isScannerInput = false;
                _barcodeBuffer.Clear();
            }
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_isScannerInput) return;

            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ProcessBarcode();
            }
            else if (!e.Key.ToString().StartsWith("Oem") && e.Key != Key.LeftShift && e.Key != Key.RightShift)
            {
                _bufferTimer.Stop();
                _bufferTimer.Start();
            }
        }

        private void BufferTimer_Tick(object sender, EventArgs e)
        {
            _bufferTimer.Stop();

            if (!string.IsNullOrEmpty(_searchTextBox.Text))
            {
                _barcodeBuffer.Append(_searchTextBox.Text);
                _searchTextBox.Clear();

                if (_barcodeBuffer.Length >= 8 && _barcodeBuffer.Length <= 13)
                {
                    ProcessBarcode();
                }
            }
        }

        private async void ProcessBarcode()
        {
            string barcode = _barcodeBuffer.ToString().Trim();
            _barcodeBuffer.Clear();
            _isScannerInput = false;

            if (string.IsNullOrEmpty(barcode) || barcode.Length < 3)
                return;

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        var produto = await _db.Products
                            .FirstOrDefaultAsync(p => p.Barcode == barcode);

                        if (produto != null)
                        {
                            _onBarcodeScanned?.Invoke(produto.Id.ToString());
                        }
                        else
                        {
                            if (int.TryParse(barcode, out int productId))
                            {
                                produto = await _db.Products.FindAsync(productId);
                                if (produto != null)
                                {
                                    _onBarcodeScanned?.Invoke(productId.ToString());
                                }
                            }
                        }

                        _searchTextBox.Clear();
                        _searchTextBox.Focus();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao processar barcode: {ex.Message}");
                    }
                });
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            try
            {
                _bufferTimer.Stop();
                _bufferTimer.Tick -= BufferTimer_Tick;

                if (_searchTextBox != null)
                {
                    _searchTextBox.PreviewKeyDown -= SearchTextBox_PreviewKeyDown;
                    _searchTextBox.KeyDown -= SearchTextBox_KeyDown;
                }
            }
            catch
            {
            }
        }
    }

    #endregion
}