using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace AyGestRest.Views
{
    public partial class ReservationsControl : UserControl
    {
        private readonly AyGestRestContext _context;
        private Reservation? _selectedReserva;
        private string _filtroAtual = "Todas";
        private bool _editando = false;
        private DispatcherTimer _timerVerificacao;

        public ReservationsControl()
        {
            InitializeComponent();

            try
            {
                _context = new AyGestRestContext();
                InitializeUI();
                CarregarDados();
                InicializarTimerVerificacao();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar sistema de reservas: {ex.Message}\n\nO sistema funcionará em modo limitado.",
                    "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
                InitializeUIBasico();
            }
        }

        private void InitializeUI()
        {
            try
            {
                // Preenche combos com verificação de null
                CarregarMesas();
                CarregarClientes();

                ComboEstado.ItemsSource = Enum.GetValues(typeof(ReservationStatus))
                    .Cast<ReservationStatus>()
                    .Select(e => new ComboBoxItem
                    {
                        Content = e.ToString(),
                        Tag = e
                    })
                    .ToList();

                // Configura data inicial
                CalendarReservas.SelectedDate = DateTime.Today;
                DpInicio.SelectedDate = DateTime.Today;
                DpFim.SelectedDate = DateTime.Today;

                // Configura filtros iniciais
                ConfigurarFiltros();

                // Configura timer para verificação automática
                InicializarTimerVerificacao();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao inicializar UI: {ex.Message}");
            }
        }

        private void InitializeUIBasico()
        {
            // UI básica sem banco de dados
            CalendarReservas.SelectedDate = DateTime.Today;
            DpInicio.SelectedDate = DateTime.Today;
            DpFim.SelectedDate = DateTime.Today;
            TbHoraInicio.Text = DateTime.Now.ToString("HH:mm");
            TbHoraFim.Text = DateTime.Now.AddHours(2).ToString("HH:mm");
            TbNumPessoas.Text = "2";
            TxtCodigoVerificacao.Text = GerarCodigoVerificacao();
        }

        private void InicializarTimerVerificacao()
        {
            _timerVerificacao = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _timerVerificacao.Tick += TimerVerificacao_Tick;
            _timerVerificacao.Start();
        }

        private async void TimerVerificacao_Tick(object sender, EventArgs e)
        {
            try
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_context != null)
                    {
                        await VerificarReservasAtrasadas();
                        await VerificarReservasParaAtivar();
                        await VerificarReservasParaConcluir();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro no timer: {ex.Message}");
            }
        }

        private async Task VerificarReservasAtrasadas()
        {
            try
            {
                if (_context == null) return;

                var agora = DateTime.Now;
                var reservasAtrasadas = await _context.Reservations
                    .Where(r => r.Estado == ReservationStatus.Confirmada &&
                                r.Inicio < agora.AddMinutes(-15) &&
                                r.Fim > agora &&
                                !r.MesaAtiva)
                    .ToListAsync();

                foreach (var reserva in reservasAtrasadas)
                {
                    reserva.Estado = ReservationStatus.Pendente;
                }

                if (reservasAtrasadas.Any())
                {
                    await _context.SaveChangesAsync();
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar reservas atrasadas: {ex.Message}");
            }
        }

        private async Task VerificarReservasParaAtivar()
        {
            try
            {
                if (_context == null) return;

                var agora = DateTime.Now;
                var reservasParaAtivar = await _context.Reservations
                    .Include(r => r.RestaurantTable)
                    .Where(r => (r.Estado == ReservationStatus.Confirmada ||
                                r.Estado == ReservationStatus.Pendente) &&
                                r.Inicio <= agora.AddMinutes(30) &&
                                r.Fim >= agora &&
                                !r.MesaAtiva)
                    .ToListAsync();

                if (reservasParaAtivar.Any() && CalendarReservas.SelectedDate.HasValue)
                {
                    await CarregarReservas(CalendarReservas.SelectedDate.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar reservas para ativar: {ex.Message}");
            }
        }

        private async Task VerificarReservasParaConcluir()
        {
            try
            {
                if (_context == null) return;

                var agora = DateTime.Now;
                var reservasParaConcluir = await _context.Reservations
                    .Where(r => r.Estado == ReservationStatus.Ativa &&
                                r.Fim < agora)
                    .ToListAsync();

                foreach (var reserva in reservasParaConcluir)
                {
                    reserva.Estado = ReservationStatus.Concluida;
                    reserva.MesaAtiva = false;
                    reserva.DataDesativacao = agora;

                    var mesa = await _context.RestaurantTables.FindAsync(reserva.RestaurantTableId);
                    if (mesa != null)
                    {
                        mesa.Status = TableStatus.Livre;
                    }
                }

                if (reservasParaConcluir.Any())
                {
                    await _context.SaveChangesAsync();
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao verificar reservas para concluir: {ex.Message}");
            }
        }

        private async void BtnImprimirCodigo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedReserva == null)
                {
                    MessageBox.Show("Selecione uma reserva para imprimir o código.", "Aviso",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await ImprimirCodigoReserva(_selectedReserva);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao imprimir código: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ImprimirCodigoReserva(Reservation reserva)
{
    try
    {
        // Obter configuração da impressora
        var config = await _context.RestaurantConfigs.FirstOrDefaultAsync();
        if (config == null || string.IsNullOrEmpty(config.PrinterName))
        {
            // Tenta usar impressora padrão se não configurada
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                MessageBox.Show("Configure uma impressora ou selecione uma impressora padrão.", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Se o usuário selecionou uma impressora, continua
        }
        else
        {
            // Usa impressora configurada
            var printDialog = new PrintDialog();
            try
            {
                var printServer = new System.Printing.PrintServer();
                var printQueue = printServer.GetPrintQueues()
                    .FirstOrDefault(pq => pq.Name.Equals(config.PrinterName, StringComparison.OrdinalIgnoreCase));
                
                if (printQueue == null)
                {
                    MessageBox.Show($"Impressora '{config.PrinterName}' não encontrada. Selecione uma impressora manualmente.",
                        "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                        
                    var manualPrintDialog = new PrintDialog();
                    if (manualPrintDialog.ShowDialog() != true)
                    {
                        return;
                    }
                }
                else
                {
                    printDialog.PrintQueue = printQueue;
                }
            }
            catch
            {
                // Se falhar, usa o diálogo padrão
                var manualPrintDialog = new PrintDialog();
                if (manualPrintDialog.ShowDialog() != true)
                {
                    return;
                }
            }
        }

        // Criar visual para impressão
        var visual = CreatePrintVisual(reserva);
        
        // Definir tamanho do papel (80mm = 283.46 pixels, altura dinâmica)
        var printDialogFinal = new PrintDialog();
        
        // Se não temos diálogo configurado, cria um novo
        if (printDialogFinal.PrintQueue == null)
        {
            if (printDialogFinal.ShowDialog() != true)
            {
                return;
            }
        }

        // Define as dimensões para papel de 80mm
        printDialogFinal.PrintTicket.PageMediaSize = new System.Printing.PageMediaSize(
            System.Printing.PageMediaSizeName.ISOA4); // Ou custom size
        
        // Alternativa: usar FixedDocument para melhor controle
        PrintFixedDocument(reserva, printDialogFinal);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Erro na impressão: {ex.Message}\n\nDetalhes: {ex.StackTrace}", "Erro",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void PrintFixedDocument(Reservation reserva, PrintDialog printDialog)
{
    try
    {
        // Criar um FixedDocument
        FixedDocument document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(283, double.PositiveInfinity); // 80mm width

        // Criar uma página
        FixedPage page = new FixedPage();
        page.Width = 283;
        page.Height = 500; // Altura estimada
        
        // Criar conteúdo
        StackPanel contentPanel = new StackPanel
        {
            Width = 273, // Largura menor que a página para margens
            Margin = new Thickness(5),
            Background = Brushes.White
        };

        // Adicionar conteúdo
        TextBlock header = new TextBlock
        {
            Text = "CÓDIGO DE RESERVA",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 10, 0, 20)
        };
        contentPanel.Children.Add(header);

        // Código grande
        TextBlock code = new TextBlock
        {
            Text = reserva.CodigoVerificacao,
            FontSize = 32,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 20)
        };
        contentPanel.Children.Add(code);

        // Informações
        TextBlock info = new TextBlock
        {
            Text = $"Cliente: {reserva.NomeCliente}\n" +
                   $"Mesa: {reserva.RestaurantTable?.Number}\n" +
                   $"Horário: {reserva.Inicio:HH:mm} - {reserva.Fim:HH:mm}\n" +
                   $"Pessoas: {reserva.NumPessoas}\n" +
                   $"Status: {reserva.EstadoDisplay}",
            FontSize = 10,
            TextAlignment = TextAlignment.Left,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 0, 0, 20)
        };
        contentPanel.Children.Add(info);

        // Data de impressão
        TextBlock date = new TextBlock
        {
            Text = $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}",
            FontSize = 8,
            TextAlignment = TextAlignment.Center,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 20, 0, 10)
        };
        contentPanel.Children.Add(date);

        // Adicionar ao FixedPage
        page.Children.Add(contentPanel);
        
        // Criar PageContent
        PageContent pageContent = new PageContent();
        ((IAddChild)pageContent).AddChild(page);
        
        // Adicionar ao documento
        document.Pages.Add(pageContent);

        // IMPRIMIR - Este é o ponto crítico
        printDialog.PrintDocument(document.DocumentPaginator, $"Código Reserva {reserva.CodigoVerificacao}");

        MessageBox.Show($"Código impresso com sucesso!\n\nCódigo: {reserva.CodigoVerificacao}",
            "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        throw new Exception($"Erro ao imprimir documento: {ex.Message}", ex);
    }
}
        
        // Método alternativo usando DrawingVisual
        private DrawingVisual CreatePrintVisual(Reservation reserva)
        {
            DrawingVisual visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                // Fundo branco
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 283, 500));

                // Configurar fonte
                Typeface typeface = new Typeface("Arial");

                // Título
                FormattedText title = new FormattedText(
                    "CÓDIGO DE RESERVA",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    14,
                    Brushes.Black,
                    1.0);
                title.TextAlignment = TextAlignment.Center;
                dc.DrawText(title, new Point(141.5, 20)); // Centro horizontal

                // Código
                FormattedText code = new FormattedText(
                    reserva.CodigoVerificacao,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    32,
                    Brushes.Black,
                    1.0);
                code.TextAlignment = TextAlignment.Center;
                dc.DrawText(code, new Point(141.5, 60));

                // Linha separadora
                dc.DrawLine(new Pen(Brushes.Black, 1), new Point(20, 120), new Point(263, 120));

                // Informações
                string infoText = $"Cliente: {reserva.NomeCliente}\n" +
                                 $"Mesa: {reserva.RestaurantTable?.Number}\n" +
                                 $"Horário: {reserva.Inicio:HH:mm} - {reserva.Fim:HH:mm}\n" +
                                 $"Pessoas: {reserva.NumPessoas}\n" +
                                 $"Status: {reserva.EstadoDisplay}";

                FormattedText info = new FormattedText(
                    infoText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    10,
                    Brushes.Black,
                    1.0);
                info.TextAlignment = TextAlignment.Left;
                dc.DrawText(info, new Point(20, 130));

                // Data
                FormattedText date = new FormattedText(
                    $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    8,
                    Brushes.Black,
                    1.0);
                date.TextAlignment = TextAlignment.Center;
                dc.DrawText(date, new Point(141.5, 220));
            }

            return visual;
        }
        private async void BtnImprimirTodas_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var reservas = ListaReservas.ItemsSource as IEnumerable<Reservation>;
                if (reservas == null || !reservas.Any())
                {
                    MessageBox.Show("Não há reservas para imprimir.", "Aviso",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Deseja imprimir códigos para {reservas.Count()} reserva(s)?",
                    "Confirmar Impressão", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes) return;

                foreach (var reserva in reservas.Where(r => r != null))
                {
                    await ImprimirCodigoReserva(reserva);
                    await Task.Delay(500); // Pequeno delay entre impressões
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CarregarMesas()
        {
            try
            {
                if (_context == null) return;

                var mesas = await _context.RestaurantTables
                    .Where(m => m.Status != TableStatus.Inativa)
                    .OrderBy(m => m.Number)
                    .ToListAsync();

                ComboMesa.ItemsSource = mesas ?? new List<RestaurantTable>();
            }
            catch (Exception ex)
            {
                ComboMesa.ItemsSource = new List<RestaurantTable>();
                Debug.WriteLine($"Erro ao carregar mesas: {ex.Message}");
            }
        }

        private async void CarregarClientes()
        {
            try
            {
                if (_context == null) return;

                var clientes = await _context.Clientes
                    .OrderBy(c => c.Nome)
                    .ToListAsync();

                ComboCliente.ItemsSource = clientes ?? new List<Cliente>();
            }
            catch (Exception ex)
            {
                ComboCliente.ItemsSource = new List<Cliente>();
                Debug.WriteLine($"Erro ao carregar clientes: {ex.Message}");
            }
        }

        private void ConfigurarFiltros()
        {
            try
            {
                BtnFiltroTodas.Tag = "Todas";
                BtnFiltroConfirmadas.Tag = "Confirmadas";
                BtnFiltroPendentes.Tag = "Pendentes";
                BtnFiltroAtivas.Tag = "Ativas";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao configurar filtros: {ex.Message}");
            }
        }

        private async void CarregarDados()
        {
            try
            {
                if (CalendarReservas.SelectedDate.HasValue && _context != null)
                {
                    await CarregarReservas(CalendarReservas.SelectedDate.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao carregar dados: {ex.Message}");
            }
        }

        private async void CalendarReservas_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (CalendarReservas.SelectedDate.HasValue && _context != null)
                {
                    await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    AtualizarCabecalhoData();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao mudar data: {ex.Message}");
            }
        }

        private void AtualizarCabecalhoData()
        {
            try
            {
                if (CalendarReservas.SelectedDate.HasValue)
                {
                    var data = CalendarReservas.SelectedDate.Value;
                    TxtDataSelecionada.Text = data.ToString("dddd, dd 'de' MMMM 'de' yyyy");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar cabeçalho: {ex.Message}");
            }
        }

        private async Task CarregarReservas(DateTime dia)
        {
            try
            {
                if (_context == null)
                {
                    // Se não há contexto, limpa a lista e mostra mensagem
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ListaReservas.ItemsSource = new List<Reservation>();
                        AtualizarContadores(new List<Reservation>());
                        BorderSemReservas.Visibility = Visibility.Visible;
                    });
                    return;
                }

                var query = _context.Reservations
                    .Include(r => r.RestaurantTable)
                    .Include(r => r.Cliente)
                    .Where(r => r.Inicio.Date == dia.Date);

                // Aplica filtro
                switch (_filtroAtual)
                {
                    case "Confirmadas":
                        query = query.Where(r => r.Estado == ReservationStatus.Confirmada);
                        break;
                    case "Pendentes":
                        query = query.Where(r => r.Estado == ReservationStatus.Pendente);
                        break;
                    case "Ativas":
                        query = query.Where(r => r.Estado == ReservationStatus.Ativa);
                        break;
                }

                var reservas = await query
                    .OrderBy(r => r.Inicio)
                    .ToListAsync() ?? new List<Reservation>();

                // Calcula propriedades de exibição
                var agora = DateTime.Now;
                foreach (var reserva in reservas)
                {
                    // Verifica se o objeto e suas propriedades não são nulos
                    if (reserva != null)
                    {
                        reserva.EstadoDisplay = reserva.Estado.ToString();

                        // Verifica se está atrasada
                        if (reserva.Estado == ReservationStatus.Confirmada &&
                            !reserva.MesaAtiva &&
                            reserva.Inicio < agora.AddMinutes(-15))
                        {
                            reserva.Atrasada = true;
                            reserva.EstadoDisplay = "Atrasada";
                        }
                        else
                        {
                            reserva.Atrasada = false;
                        }
                    }
                }

                // Usa Dispatcher para atualizar a UI
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ListaReservas.ItemsSource = reservas;
                        AtualizarContadores(reservas);
                        AtualizarCabecalhoData();
                        BorderSemReservas.Visibility = reservas.Any() ? Visibility.Collapsed : Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erro ao atualizar UI: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    Debug.WriteLine($"Erro ao carregar reservas: {ex.Message}");
                    ListaReservas.ItemsSource = new List<Reservation>();
                    AtualizarContadores(new List<Reservation>());
                    BorderSemReservas.Visibility = Visibility.Visible;
                });
            }
        }

        private void AtualizarContadores(List<Reservation> reservas)
        {
            try
            {
                if (reservas == null)
                {
                    TxtConfirmadas.Text = "0";
                    TxtPendentes.Text = "0";
                    TxtAtivas.Text = "0";
                    TxtCanceladas.Text = "0";
                    TxtTotalReservas.Text = "(0)";
                    return;
                }

                var confirmadas = reservas.Count(r => r != null && r.Estado == ReservationStatus.Confirmada);
                var pendentes = reservas.Count(r => r != null && r.Estado == ReservationStatus.Pendente);
                var ativas = reservas.Count(r => r != null && r.Estado == ReservationStatus.Ativa);
                var canceladas = reservas.Count(r => r != null && r.Estado == ReservationStatus.Cancelada);

                TxtConfirmadas.Text = confirmadas.ToString();
                TxtPendentes.Text = pendentes.ToString();
                TxtAtivas.Text = ativas.ToString();
                TxtCanceladas.Text = canceladas.ToString();

                TxtTotalReservas.Text = $"({reservas.Count})";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao atualizar contadores: {ex.Message}");
            }
        }

        private void ListaReservas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ListaReservas.SelectedItem is Reservation reserva)
                {
                    _selectedReserva = reserva;
                    _editando = true;

                    // Habilita botões
                    BtnAtivarReserva.IsEnabled = reserva.Estado == ReservationStatus.Confirmada ||
                                                reserva.Estado == ReservationStatus.Pendente;
                    BtnCancelarReserva.IsEnabled = reserva.Estado != ReservationStatus.Cancelada &&
                                                  reserva.Estado != ReservationStatus.Concluida;

                    // Habilita botão de impressão
                    BtnImprimirCodigo.IsEnabled = reserva != null;

                }
                else
                {
                    _selectedReserva = null;
                    BtnAtivarReserva.IsEnabled = false;
                    BtnCancelarReserva.IsEnabled = false;
                    BtnImprimirCodigo.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao selecionar reserva: {ex.Message}");
            }
        }

        private void PrepararFormularioEdicao(Reservation reserva)
        {
            try
            {
                if (reserva == null) return;

                TxtTituloForm.Text = "✏️ Editar Reserva";

                // Preenche cliente
                if (ComboCliente.ItemsSource != null)
                {
                    var cliente = ComboCliente.ItemsSource
                        .Cast<Cliente>()
                        .FirstOrDefault(c => c != null && c.Id == reserva.ClienteId);
                    ComboCliente.SelectedItem = cliente;
                }

                // Preenche mesa
                if (ComboMesa.ItemsSource != null)
                {
                    var mesa = ComboMesa.ItemsSource
                        .Cast<RestaurantTable>()
                        .FirstOrDefault(m => m != null && m.Id == reserva.RestaurantTableId);
                    ComboMesa.SelectedItem = mesa;
                }

                // Preenche datas e horas
                DpInicio.SelectedDate = reserva.Inicio.Date;
                TbHoraInicio.Text = reserva.Inicio.ToString("HH:mm");
                DpFim.SelectedDate = reserva.Fim.Date;
                TbHoraFim.Text = reserva.Fim.ToString("HH:mm");
                TbNumPessoas.Text = reserva.NumPessoas.ToString();

                // Preenche estado
                if (ComboEstado.Items != null)
                {
                    var estadoItem = ComboEstado.Items
                        .Cast<ComboBoxItem>()
                        .FirstOrDefault(i => i != null && i.Tag is ReservationStatus status && status == reserva.Estado);
                    ComboEstado.SelectedItem = estadoItem;
                }

                // Preenche código
                TxtCodigoVerificacao.Text = !string.IsNullOrEmpty(reserva.CodigoVerificacao)
                    ? reserva.CodigoVerificacao
                    : GerarCodigoVerificacao();

                // Mostra formulário
                FormularioOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao preparar formulário: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnNovaReserva_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _selectedReserva = null;
                _editando = false;

                TxtTituloForm.Text = "➕ Nova Reserva";

                // Limpa campos
                ComboCliente.SelectedIndex = -1;
                ComboMesa.SelectedIndex = -1;
                DpInicio.SelectedDate = CalendarReservas.SelectedDate ?? DateTime.Today;
                TbHoraInicio.Text = DateTime.Now.ToString("HH:mm");
                DpFim.SelectedDate = CalendarReservas.SelectedDate ?? DateTime.Today;
                TbHoraFim.Text = DateTime.Now.AddHours(2).ToString("HH:mm");
                TbNumPessoas.Text = "2";

                if (ComboEstado.Items != null && ComboEstado.Items.Count > 0)
                    ComboEstado.SelectedIndex = 0;

                TxtCodigoVerificacao.Text = GerarCodigoVerificacao();

                // Mostra formulário
                FormularioOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao criar nova reserva: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GerarCodigoVerificacao()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private void BtnGerarCodigo_Click(object sender, RoutedEventArgs e)
        {
            TxtCodigoVerificacao.Text = GerarCodigoVerificacao();
        }

        private async void BtnSalvarReserva_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidarCampos()) return;

                // Verifica se o contexto está disponível
                if (_context == null)
                {
                    MessageBox.Show("Sistema de banco de dados não disponível.", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Converte dados
                var inicio = DpInicio.SelectedDate!.Value.Date + TimeSpan.Parse(TbHoraInicio.Text);
                var fim = DpFim.SelectedDate!.Value.Date + TimeSpan.Parse(TbHoraFim.Text);

                // Valida consistência temporal
                if (inicio >= fim)
                {
                    MessageBox.Show("A hora de início deve ser anterior à hora de fim!",
                        "Erro de Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Obtém os objetos selecionados
                var cliente = ComboCliente.SelectedItem as Cliente;
                var mesa = ComboMesa.SelectedItem as RestaurantTable;
                var estado = ComboEstado.SelectedItem != null
                    ? (ReservationStatus)((ComboBoxItem)ComboEstado.SelectedItem).Tag
                    : ReservationStatus.Confirmada;

                if (cliente == null || mesa == null)
                {
                    MessageBox.Show("Selecione cliente e mesa!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Verifica disponibilidade da mesa
                if (!await VerificarDisponibilidadeMesa(mesa.Id, inicio, fim, _selectedReserva?.Id))
                {
                    MessageBox.Show($"A mesa {mesa.Number} já está reservada neste horário!",
                        "Mesa Indisponível", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_selectedReserva == null)
                {
                    // Nova reserva
                    var nova = new Reservation
                    {
                        NomeCliente = cliente.Nome,
                        ClienteId = cliente.Id,
                        Cliente = cliente,
                        RestaurantTableId = mesa.Id,
                        RestaurantTable = mesa,
                        Inicio = inicio,
                        Fim = fim,
                        NumPessoas = int.Parse(TbNumPessoas.Text),
                        Estado = estado,
                        CodigoVerificacao = TxtCodigoVerificacao.Text,
                        MesaAtiva = false,
                        DataCriacao = DateTime.Now
                    };

                    _context.Reservations.Add(nova);
                    await _context.SaveChangesAsync();

                    // Atualiza status da mesa para reservada
                    mesa.Status = TableStatus.Reservada;
                    await _context.SaveChangesAsync();

                    MessageBox.Show($"✅ Reserva criada com sucesso!\n\n" +
                                   $"Código: {nova.CodigoVerificacao}\n" +
                                   $"Entregue este código ao cliente!",
                                   "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Editar existente
                    var mesaAntigaId = _selectedReserva.RestaurantTableId;

                    _selectedReserva.NomeCliente = cliente.Nome;
                    _selectedReserva.ClienteId = cliente.Id;
                    _selectedReserva.Cliente = cliente;
                    _selectedReserva.RestaurantTableId = mesa.Id;
                    _selectedReserva.RestaurantTable = mesa;
                    _selectedReserva.Inicio = inicio;
                    _selectedReserva.Fim = fim;
                    _selectedReserva.NumPessoas = int.Parse(TbNumPessoas.Text);
                    _selectedReserva.Estado = estado;
                    _selectedReserva.CodigoVerificacao = TxtCodigoVerificacao.Text;
                    _selectedReserva.DataAtualizacao = DateTime.Now;

                    await _context.SaveChangesAsync();

                    // Se mudou de mesa, atualiza status das mesas
                    if (mesaAntigaId != mesa.Id)
                    {
                        var mesaAntiga = await _context.RestaurantTables.FindAsync(mesaAntigaId);
                        if (mesaAntiga != null)
                        {
                            // Verifica se há outras reservas para esta mesa
                            var temOutrasReservas = await _context.Reservations
                                .AnyAsync(r => r.RestaurantTableId == mesaAntigaId &&
                                              r.Estado != ReservationStatus.Cancelada &&
                                              r.Estado != ReservationStatus.Concluida &&
                                              r.Id != _selectedReserva.Id);

                            if (!temOutrasReservas)
                            {
                                mesaAntiga.Status = TableStatus.Livre;
                            }
                        }

                        mesa.Status = TableStatus.Reservada;
                        await _context.SaveChangesAsync();
                    }

                    MessageBox.Show($"✅ Reserva atualizada com sucesso!\n\n" +
                                   $"Código: {_selectedReserva.CodigoVerificacao}",
                                   "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Fecha formulário e atualiza lista
                FecharFormulario();
                if (CalendarReservas.SelectedDate.HasValue)
                {
                    await CarregarReservas(CalendarReservas.SelectedDate.Value);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar reserva: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> VerificarDisponibilidadeMesa(int mesaId, DateTime inicio, DateTime fim, int? reservaId = null)
        {
            try
            {
                if (_context == null) return true;

                var query = _context.Reservations
                    .Where(r => r.RestaurantTableId == mesaId &&
                               r.Estado != ReservationStatus.Cancelada &&
                               r.Estado != ReservationStatus.Concluida &&
                               r.Inicio < fim && r.Fim > inicio);

                if (reservaId.HasValue)
                {
                    query = query.Where(r => r.Id != reservaId.Value);
                }

                return !await query.AnyAsync();
            }
            catch
            {
                return true; // Se houver erro, assume que está disponível
            }
        }

        private async void BtnAtivarReserva_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedReserva == null) return;
                if (_context == null)
                {
                    MessageBox.Show("Sistema de banco de dados não disponível.", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Cria diálogo para ativação
                var dialog = new AtivarReservaDialog(_selectedReserva.CodigoVerificacao);
                if (dialog.ShowDialog() == true)
                {
                    var codigoInserido = dialog.CodigoInserido;

                    if (codigoInserido == _selectedReserva.CodigoVerificacao)
                    {
                        // Ativa a reserva
                        _selectedReserva.Estado = ReservationStatus.Ativa;
                        _selectedReserva.MesaAtiva = true;
                        _selectedReserva.DataAtivacao = DateTime.Now;

                        // Atualiza status da mesa para ocupada
                        var mesa = await _context.RestaurantTables.FindAsync(_selectedReserva.RestaurantTableId);
                        if (mesa != null)
                        {
                            mesa.Status = TableStatus.Ocupada;
                        }

                        await _context.SaveChangesAsync();

                        MessageBox.Show($"✅ Reserva ativada com sucesso!\n" +
                                       $"Mesa {mesa?.Number} agora está ocupada.",
                                       "Ativada", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Atualiza lista
                        if (CalendarReservas.SelectedDate.HasValue)
                        {
                            await CarregarReservas(CalendarReservas.SelectedDate.Value);
                        }

                        // Desabilita botão de ativar
                        BtnAtivarReserva.IsEnabled = false;
                    }
                    else
                    {
                        MessageBox.Show("❌ Código incorreto!", "Erro",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao ativar reserva: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCancelarReserva_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedReserva == null) return;
                if (_context == null)
                {
                    MessageBox.Show("Sistema de banco de dados não disponível.", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = MessageBox.Show(
                    $"Tem certeza que deseja cancelar a reserva?\n\n" +
                    $"Cliente: {_selectedReserva.NomeCliente}\n" +
                    $"Mesa: {_selectedReserva.RestaurantTable?.Number}\n" +
                    $"Horário: {_selectedReserva.Inicio:HH:mm} - {_selectedReserva.Fim:HH:mm}",
                    "Confirmar Cancelamento",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                _selectedReserva.Estado = ReservationStatus.Cancelada;
                _selectedReserva.MesaAtiva = false;
                _selectedReserva.DataAtualizacao = DateTime.Now;

                // Libera a mesa se não estiver ocupada
                if (!_selectedReserva.MesaAtiva)
                {
                    var mesa = await _context.RestaurantTables.FindAsync(_selectedReserva.RestaurantTableId);
                    if (mesa != null)
                    {
                        mesa.Status = TableStatus.Livre;
                    }
                }

                await _context.SaveChangesAsync();

                MessageBox.Show("✅ Reserva cancelada com sucesso!", "Cancelada",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Atualiza lista
                if (CalendarReservas.SelectedDate.HasValue)
                {
                    await CarregarReservas(CalendarReservas.SelectedDate.Value);
                }

                // Desabilita botões
                BtnAtivarReserva.IsEnabled = false;
                BtnCancelarReserva.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao cancelar reserva: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidarCampos()
        {
            try
            {
                bool valido = true;
                string mensagens = "";

                // Valida cliente
                if (ComboCliente.SelectedItem == null)
                {
                    mensagens += "• Selecione um cliente\n";
                    valido = false;
                }

                // Valida mesa
                if (ComboMesa.SelectedItem == null)
                {
                    mensagens += "• Selecione uma mesa\n";
                    valido = false;
                }

                // Valida hora início
                if (!TimeSpan.TryParse(TbHoraInicio.Text, out _))
                {
                    mensagens += "• Formato de hora de início inválido (use HH:mm)\n";
                    valido = false;
                }

                // Valida hora fim
                if (!TimeSpan.TryParse(TbHoraFim.Text, out _))
                {
                    mensagens += "• Formato de hora de fim inválido (use HH:mm)\n";
                    valido = false;
                }

                // Valida número de pessoas
                if (!int.TryParse(TbNumPessoas.Text, out var numPessoas) || numPessoas <= 0)
                {
                    mensagens += "• Número de pessoas inválido (mínimo: 1)\n";
                    valido = false;
                }

                // Valida código
                if (string.IsNullOrWhiteSpace(TxtCodigoVerificacao.Text))
                {
                    mensagens += "• Gere um código de verificação\n";
                    valido = false;
                }

                if (!valido)
                {
                    MessageBox.Show($"Corrija os seguintes erros:\n\n{mensagens}",
                        "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return valido;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro na validação: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void FecharFormulario()
        {
            FormularioOverlay.Visibility = Visibility.Collapsed;
            ListaReservas.SelectedItem = null;
        }

        private void BtnFecharForm_Click(object sender, RoutedEventArgs e)
        {
            FecharFormulario();
        }

        private void BtnCancelarForm_Click(object sender, RoutedEventArgs e)
        {
            FecharFormulario();
        }

        private void BtnAtualizar_Click(object sender, RoutedEventArgs e)
        {
            CarregarDados();
        }

        private async void BtnFiltroTodas_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnFiltroTodas.IsChecked == true)
                {
                    _filtroAtual = "Todas";
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao aplicar filtro: {ex.Message}");
            }
        }

        private async void BtnFiltroConfirmadas_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnFiltroConfirmadas.IsChecked == true)
                {
                    _filtroAtual = "Confirmadas";
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao aplicar filtro: {ex.Message}");
            }
        }

        private async void BtnFiltroPendentes_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnFiltroPendentes.IsChecked == true)
                {
                    _filtroAtual = "Pendentes";
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao aplicar filtro: {ex.Message}");
            }
        }

        private async void BtnFiltroAtivas_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (BtnFiltroAtivas.IsChecked == true)
                {
                    _filtroAtual = "Ativas";
                    if (CalendarReservas.SelectedDate.HasValue)
                    {
                        await CarregarReservas(CalendarReservas.SelectedDate.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erro ao aplicar filtro: {ex.Message}");
            }
        }

        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var data = CalendarReservas.SelectedDate ?? DateTime.Today;
                var reservas = ListaReservas.ItemsSource as IEnumerable<Reservation> ?? new List<Reservation>();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Relatório de Reservas - {data:dd/MM/yyyy}");
                sb.AppendLine("=".PadRight(50, '='));
                sb.AppendLine();

                foreach (var reserva in reservas)
                {
                    if (reserva != null)
                    {
                        sb.AppendLine($"Cliente: {reserva.NomeCliente}");
                        sb.AppendLine($"Mesa: {reserva.RestaurantTable?.Number}");
                        sb.AppendLine($"Horário: {reserva.Inicio:HH:mm} - {reserva.Fim:HH:mm}");
                        sb.AppendLine($"Pessoas: {reserva.NumPessoas}");
                        sb.AppendLine($"Status: {reserva.Estado}");
                        sb.AppendLine($"Código: {reserva.CodigoVerificacao}");
                        sb.AppendLine();
                    }
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"Reservas_{data:yyyyMMdd}.txt",
                    Filter = "Arquivo de Texto|*.txt|Todos os arquivos|*.*"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(saveDialog.FileName, sb.ToString());
                    MessageBox.Show("Relatório exportado com sucesso!", "Exportado",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar: {ex.Message}", "Erro",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _timerVerificacao?.Stop();
            _timerVerificacao = null;
        }
    }

    // Classe auxiliar para diálogo de ativação
    public class AtivarReservaDialog : Window
    {
        public string CodigoInserido { get; private set; }

        public AtivarReservaDialog(string codigoEsperado)
        {
            Title = "🔓 Ativar Reserva";
            Width = 400;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var stack = new StackPanel { Margin = new Thickness(20) };

            // Título
            var titulo = new TextBlock
            {
                Text = "Ativar Reserva",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.DarkSlateBlue,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stack.Children.Add(titulo);

            // Instruções
            var instrucoes = new TextBlock
            {
                Text = "Peça ao cliente o código de verificação\n" +
                       "e insira abaixo para ativar a mesa:",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            stack.Children.Add(instrucoes);

            // Campo do código
            var txtCodigo = new TextBox
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Height = 50,
                Margin = new Thickness(0, 20, 0, 20),
                MaxLength = 6,
                CharacterCasing = CharacterCasing.Upper
            };
            stack.Children.Add(txtCodigo);

            // Botões
            var gridBotoes = new Grid();
            gridBotoes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridBotoes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnCancelar = new Button
            {
                Content = "Cancelar",
                Height = 40,
                Margin = new Thickness(0, 0, 5, 0),
                Background = Brushes.LightGray
            };
            btnCancelar.Click += (s, e) => { DialogResult = false; Close(); };
            Grid.SetColumn(btnCancelar, 0);
            gridBotoes.Children.Add(btnCancelar);

            var btnConfirmar = new Button
            {
                Content = "✅ Ativar",
                Height = 40,
                Margin = new Thickness(5, 0, 0, 0),
                Background = Brushes.LimeGreen,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            btnConfirmar.Click += (s, e) =>
            {
                if (txtCodigo.Text == codigoEsperado)
                {
                    CodigoInserido = txtCodigo.Text;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Código incorreto!", "Erro",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    txtCodigo.Focus();
                    txtCodigo.SelectAll();
                }
            };
            Grid.SetColumn(btnConfirmar, 1);
            gridBotoes.Children.Add(btnConfirmar);

            stack.Children.Add(gridBotoes);

            Content = stack;

            // Foca no campo do código
            Loaded += (s, e) => txtCodigo.Focus();
        }
    }
}