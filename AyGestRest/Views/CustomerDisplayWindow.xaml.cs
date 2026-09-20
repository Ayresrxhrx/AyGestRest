using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.ComponentModel;

namespace AyGestRest.Views
{
    public partial class CustomerDisplayWindow : Window, INotifyPropertyChanged
    {
        private readonly AyGestRestContext _db = new();
        private RestaurantConfig _config;
        private DispatcherTimer _timer;
        private DispatcherTimer _obrigadoTimer;
        private int _segundosRestantes = 10;
        private DateTime? _inicioVenda;
        private decimal _desconto = 0;
        private string _formaPagamento = "Dinheiro";
        private DispatcherTimer _animacaoTimer;
        private Random _random = new Random();
        private DispatcherTimer _particleTimer;

        public ObservableCollection<POSControl.CartItem> Carrinho { get; } = new();
        public string RestaurantName => _config?.RestaurantName ?? "RESTAURANTE";

        // Propriedades para binding
        private int _totalQuantity;
        public int TotalQuantity
        {
            get => _totalQuantity;
            set
            {
                if (_totalQuantity != value)
                {
                    _totalQuantity = value;
                    OnPropertyChanged(nameof(TotalQuantity));
                }
            }
        }

        private string _tempoVenda = "00:00:00";
        public string TempoVenda
        {
            get => _tempoVenda;
            set
            {
                if (_tempoVenda != value)
                {
                    _tempoVenda = value;
                    OnPropertyChanged(nameof(TempoVenda));
                }
            }
        }

        private decimal _valorMedio;
        public decimal ValorMedio
        {
            get => _valorMedio;
            set
            {
                if (_valorMedio != value)
                {
                    _valorMedio = value;
                    OnPropertyChanged(nameof(ValorMedio));
                }
            }
        }

        public CustomerDisplayWindow(ObservableCollection<POSControl.CartItem> carrinhoExterno)
        {
            InitializeComponent();
            DataContext = this;

            // Recebe referência do carrinho da tela principal
            carrinhoExterno.CollectionChanged += (s, e) =>
            {
                Carrinho.Clear();
                foreach (var item in carrinhoExterno)
                    Carrinho.Add(item);
                AtualizarDisplay();
            };

            // Copia itens iniciais
            foreach (var item in carrinhoExterno)
                Carrinho.Add(item);

            CarregarConfiguracoes();
            IniciarTimer();
            IniciarAnimacoes();
            InicializarParticulas();
            AtualizarDisplay();
        }

        private async void CarregarConfiguracoes()
        {
            _config = await _db.RestaurantConfigs.FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(_config?.LogoPath) && File.Exists(_config.LogoPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(_config.LogoPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    imgLogo.Source = bitmap;
                }
                catch { /* Ignorar erros de carregamento de imagem */ }
            }

            // Carregar nome do operador
            txtOperador.Text = AppSession.CurrentUser?.Username ?? "Operador";
        }

        private void IniciarTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                txtDataHora.Text = DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy • HH:mm:ss");

                // Atualizar tempo da venda
                if (_inicioVenda.HasValue)
                {
                    TempoVenda = (DateTime.Now - _inicioVenda.Value).ToString(@"hh\:mm\:ss");
                }
            };
            _timer.Start();
        }

        private void IniciarAnimacoes()
        {
            // Animar o loading
            var rotateAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(2),
                RepeatBehavior = RepeatBehavior.Forever
            };

            if (loadingEllipse != null)
            {
                var rotateTransform = new RotateTransform();
                loadingEllipse.RenderTransform = rotateTransform;
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotateAnimation);
            }
        }

        private void InicializarParticulas()
        {
            // Criar partículas de fundo
            for (int i = 0; i < 50; i++)
            {
                var particle = new Ellipse
                {
                    Width = _random.Next(2, 8),
                    Height = _random.Next(2, 8),
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    Opacity = _random.NextDouble() * 0.3 + 0.1
                };

                Canvas.SetLeft(particle, _random.NextDouble() * 1000);
                Canvas.SetTop(particle, _random.NextDouble() * 800);

                particleCanvas.Children.Add(particle);
            }

            // Animar partículas
            _particleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _particleTimer.Tick += (s, e) =>
            {
                foreach (var element in particleCanvas.Children)
                {
                    if (element is Ellipse particle)
                    {
                        double currentLeft = Canvas.GetLeft(particle);
                        Canvas.SetLeft(particle, currentLeft - 0.5);

                        if (currentLeft < -10)
                        {
                            Canvas.SetLeft(particle, 1000);
                            Canvas.SetTop(particle, _random.NextDouble() * 800);
                        }
                    }
                }
            };
            _particleTimer.Start();
        }

        public void AtualizarDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                if (Carrinho.Count == 0)
                {
                    panelBoasVindas.Visibility = Visibility.Visible;
                    panelCarrinho.Visibility = Visibility.Collapsed;
                    panelObrigado.Visibility = Visibility.Collapsed;
                    txtStatusVenda.Text = "🛒 Aguardando seu pedido...";
                    _inicioVenda = null;
                    TempoVenda = "00:00:00";
                }
                else
                {
                    panelBoasVindas.Visibility = Visibility.Collapsed;
                    panelCarrinho.Visibility = Visibility.Visible;
                    panelObrigado.Visibility = Visibility.Collapsed;

                    if (!_inicioVenda.HasValue)
                        _inicioVenda = DateTime.Now;

                    // Atualizar contadores
                    TotalQuantity = Carrinho.Sum(i => i.Quantity);
                    txtContadorProdutos.Text = Carrinho.Count.ToString();
                    txtContadorQuantidade.Text = TotalQuantity.ToString();

                    // Calcular valor médio
                    decimal subtotal = Carrinho.Sum(i => i.SubTotal);
                    ValorMedio = Carrinho.Count > 0 ? subtotal / Carrinho.Count : 0;
                    txtValorMedio.Text = $"{ValorMedio:F2}";

                    // Atualizar itens
                    itemsControlItens.ItemsSource = null;
                    itemsControlItens.ItemsSource = Carrinho;

                    // Calcular totais
                    decimal total = Math.Max(subtotal - _desconto, 0);

                    // Atualizar textos
                    txtSubtotal.Text = $"{subtotal:F2} MT";
                    txtDesconto.Text = $"- {_desconto:F2} MT";
                    txtTotalFinal.Text = $"{total:F2} MT";
                    txtContadorItens.Text = $"{TotalQuantity} itens";
                    txtStatusVenda.Text = "✅ Pedido em andamento...";

                    // Atualizar status baseado no valor recebido
                    AtualizarStatusPagamento(total);
                }
            });
        }

        private void AtualizarStatusPagamento(decimal total)
        {
            txtStatusPagamento.Text = "🔄 Aguardando pagamento";
            txtStatusPagamento.Foreground = Brushes.White;
        }

        public void AtualizarInformacoesVenda(string numeroComanda, string mesa, decimal desconto, string formaPagamento)
        {
            Dispatcher.Invoke(() =>
            {
                txtNumeroComanda.Text = $"#{numeroComanda}";
                txtMesaAtual.Text = mesa;
                _desconto = desconto;
                _formaPagamento = formaPagamento;
                txtFormaPagamento.Text = formaPagamento;
                AtualizarDisplay();
            });
        }

        public void MostrarObrigado()
        {
            Dispatcher.Invoke(() =>
            {
                panelBoasVindas.Visibility = Visibility.Collapsed;
                panelCarrinho.Visibility = Visibility.Collapsed;
                panelObrigado.Visibility = Visibility.Visible;

                // Adicionar confetes
                AdicionarConfetes();

                // Configurar barra de progresso
                progressBar.Width = 0;
                _segundosRestantes = 10;
                txtContadorRegressivo.Text = $"Voltando em {_segundosRestantes}s...";

                // Timer para contador regressivo
                _obrigadoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _obrigadoTimer.Tick += (s, e) =>
                {
                    _segundosRestantes--;
                    txtContadorRegressivo.Text = $"Voltando em {_segundosRestantes}s...";

                    // Animar barra de progresso
                    double progressWidth = 400 * ((10 - _segundosRestantes) / 10.0);
                    var progressAnimation = new DoubleAnimation
                    {
                        To = progressWidth,
                        Duration = TimeSpan.FromSeconds(1),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    progressBar.BeginAnimation(Border.WidthProperty, progressAnimation);

                    if (_segundosRestantes <= 0)
                    {
                        _obrigadoTimer.Stop();
                        if (Carrinho.Count == 0)
                            AtualizarDisplay();
                    }
                };
                _obrigadoTimer.Start();
            });
        }

        private void AdicionarConfetes()
        {
            confettiVisual.Children.Clear();

            for (int i = 0; i < 30; i++)
            {
                var confetti = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(Color.FromRgb(
                        (byte)_random.Next(150, 256),
                        (byte)_random.Next(150, 256),
                        (byte)_random.Next(150, 256))),
                    Opacity = 0.8,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };

                // Formas diferentes para confetes
                if (_random.Next(3) == 0)
                {
                    // Estrela
                    confetti.Data = Geometry.Parse("M50,5 L61,37 L95,37 L67,57 L78,91 L50,71 L22,91 L33,57 L5,37 L39,37 Z");
                }
                else if (_random.Next(2) == 0)
                {
                    // Círculo
                    confetti.Data = Geometry.Parse("M25,50 A25,25 0 1 1 75,50 A25,25 0 1 1 25,50");
                }
                else
                {
                    // Retângulo
                    confetti.Data = Geometry.Parse("M10,10 L90,10 L90,90 L10,90 Z");
                }

                confetti.Width = _random.Next(15, 30);
                confetti.Height = confetti.Width;

                // Posição inicial
                Canvas.SetLeft(confetti, _random.Next(0, 180));
                Canvas.SetTop(confetti, -30);

                // Animações
                var fallAnim = new DoubleAnimation
                {
                    To = 210,
                    Duration = TimeSpan.FromSeconds(_random.Next(2, 4)),
                    BeginTime = TimeSpan.FromSeconds(_random.NextDouble() * 1)
                };

                var rotateAnim = new DoubleAnimation
                {
                    To = _random.Next(2, 5) * 360,
                    Duration = TimeSpan.FromSeconds(_random.Next(3, 5)),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                var fadeAnim = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromSeconds(1),
                    BeginTime = TimeSpan.FromSeconds(_random.Next(3, 4))
                };

                var transform = new RotateTransform();
                confetti.RenderTransform = transform;

                confetti.BeginAnimation(Canvas.TopProperty, fallAnim);
                transform.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
                confetti.BeginAnimation(System.Windows.Shapes.Path.OpacityProperty, fadeAnim);

                confettiVisual.Children.Add(confetti);
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Ajustar escala baseado no tamanho da tela
            AjustarEscalaParaTela();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Ajustar escala inicial
            AjustarEscalaParaTela();
        }

        private void AjustarEscalaParaTela()
        {
            double screenWidth = ActualWidth;
            double screenHeight = ActualHeight;

            // Calcular escala baseado no tamanho da tela
            double widthScale = screenWidth / 900;
            double heightScale = screenHeight / 650;

            // Usar o menor fator
            double scale = Math.Min(widthScale, heightScale);

            // Limitar escala
            scale = Math.Max(0.8, Math.Min(scale, 1.2));

            // Ajustar tamanhos mínimos
            ContentGrid.MinWidth = 900 * scale;
            ContentGrid.MinHeight = 650 * scale;

            // Ajustar margens
            double margin = 40 * scale;
            ContentGrid.Margin = new Thickness(margin);
        }

        // Implementação do INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            _obrigadoTimer?.Stop();
            _animacaoTimer?.Stop();
            _particleTimer?.Stop();
            base.OnClosed(e);
        }
    }
}