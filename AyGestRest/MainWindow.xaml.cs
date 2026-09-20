using AyGest;
using AyGestRest.Controls;
using AyGestRest.Models;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AyGestRest
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timerHora;
        private RestaurantConfig _config => AppSession.RestaurantConfig;
        private bool _isTopbarCollapsed = false;
        private bool _isSidebarCollapsed = false;
        private Dictionary<Button, string> _buttonOriginalContent = new Dictionary<Button, string>();

        public MainWindow()
        {
            InitializeComponent();

            if (AppSession.RestaurantConfig == null)
                AppSession.RestaurantConfig = new RestaurantConfig();

            _timerHora = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timerHora.Tick += TimerHora_Tick;
            _timerHora.Start();

            AtualizarInformacoesTopo();

            // 🔥 Esconder os botões de Admin para Funcionário
            VerificarPermissoesUsuario();

            BtnCaixa_Click(null, null);
        }

        // =========================
        // CONTROLE DE PERMISSÕES
        // =========================

        private void VerificarPermissoesUsuario()
        {
            if (AppSession.CurrentUser == null)
                return;

            // Se for funcionário, esconder botões de administração
            if (AppSession.CurrentUser.Role == UserRole.Funcionario)
            {
                EsconderBotoesAdministracao();

                // Também pode ajustar os headers se necessário
                AjustarHeadersParaFuncionario();
            }
            else
            {
                // Se for Admin/Gerente, mostrar todos os botões
                MostrarTodosBotoes();
            }
        }

        private void EsconderBotoesAdministracao()
        {
            // Esconder botões específicos
            BtnDefinicoes.Visibility = Visibility.Collapsed;
            BtnUtilizadores.Visibility = Visibility.Collapsed;

            // Também remover da lista de botões para colapso
            _buttonOriginalContent.Remove(BtnDefinicoes);
            _buttonOriginalContent.Remove(BtnUtilizadores);
        }

        private void MostrarTodosBotoes()
        {
            BtnDefinicoes.Visibility = Visibility.Visible;
            BtnUtilizadores.Visibility = Visibility.Visible;
        }

        private void AjustarHeadersParaFuncionario()
        {
            // Se todos os botões da seção Admin estiverem escondidos, esconder o header também
            if (BtnDefinicoes.Visibility == Visibility.Collapsed &&
                BtnUtilizadores.Visibility == Visibility.Collapsed)
            {
                txtAdminHeader.Visibility = Visibility.Collapsed;
            }
        }

        // =========================
        // TOGGLE DAS BARRAS
        // =========================

        private void BtnToggleTopbar_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            if (_isTopbarCollapsed)
            {
                // Expandir barra superior
                TopRow.Height = new GridLength(90, GridUnitType.Pixel);
                AnimarRotacaoBotao(button, 0);
                txtNotifications.Visibility = Visibility.Visible;

                // Aumentar fontes quando expandido
                var logo = FindVisualChild<TextBlock>(TopBorder, "TextBlock");
                if (logo != null)
                {
                    logo.FontSize = 34;
                }
            }
            else
            {
                // Colapsar barra superior
                TopRow.Height = new GridLength(70, GridUnitType.Pixel);
                AnimarRotacaoBotao(button, 180);
                txtNotifications.Visibility = Visibility.Collapsed;

                // Reduzir fontes quando colapsado
                var logo = FindVisualChild<TextBlock>(TopBorder, "TextBlock");
                if (logo != null)
                {
                    logo.FontSize = 24;
                }
            }

            _isTopbarCollapsed = !_isTopbarCollapsed;
        }

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            if (_isSidebarCollapsed)
            {
                // Expandir barra lateral
                LeftColumn.Width = new GridLength(300, GridUnitType.Pixel);
                AnimarRotacaoBotao(button, 0);
                AtualizarSidebarParaExpandido();
            }
            else
            {
                // Colapsar barra lateral
                LeftColumn.Width = new GridLength(80, GridUnitType.Pixel);
                AnimarRotacaoBotao(button, 180);
                AtualizarSidebarParaColapsado();
            }

            _isSidebarCollapsed = !_isSidebarCollapsed;
        }

        private void AnimarRotacaoBotao(Button botao, double angulo)
        {
            var transform = new RotateTransform();
            botao.RenderTransform = transform;
            botao.RenderTransformOrigin = new Point(0.5, 0.5);

            var animacao = new DoubleAnimation
            {
                To = angulo,
                Duration = TimeSpan.FromSeconds(0.3),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            transform.BeginAnimation(RotateTransform.AngleProperty, animacao);
        }

        private void AtualizarSidebarParaColapsado()
        {
            // Esconder headers de texto
            txtOperacoesHeader.Visibility = Visibility.Collapsed;
            txtGestaoHeader.Visibility = Visibility.Collapsed;
            txtRelatoriosHeader.Visibility = Visibility.Collapsed;

            // Verificar se deve esconder o header de Admin
            if (AppSession.CurrentUser?.Role != UserRole.Funcionario)
            {
                txtAdminHeader.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Para funcionários, se os botões Admin estiverem escondidos, esconder o header também
                if (BtnDefinicoes.Visibility == Visibility.Collapsed &&
                    BtnUtilizadores.Visibility == Visibility.Collapsed)
                {
                    txtAdminHeader.Visibility = Visibility.Collapsed;
                }
            }

            // Coletar todos os botões VISÍVEIS
            var botoesVisiveis = new List<Button>();

            // Adicionar apenas botões visíveis
            if (BtnMesas.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnMesas);
            if (BtnCaixa.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnCaixa);
            if (BtnArtigos.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnArtigos);
            if (BtnReservas.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnReservas);
            if (BtnCategorias.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnCategorias);
            if (BtnClientes.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnClientes);
            if (BtnHistoricoPedidos.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnHistoricoPedidos);
            if (BtnFechoDiario.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnFechoDiario);
            if (BtnrRelatorios.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnrRelatorios);

            // Adicionar botões de admin apenas se visíveis
            if (BtnDefinicoes.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnDefinicoes);
            if (BtnUtilizadores.Visibility == Visibility.Visible) botoesVisiveis.Add(BtnUtilizadores);

            // Atualizar cada botão para modo ícone
            foreach (var botao in botoesVisiveis)
            {
                AtualizarBotaoParaModoColapsado(botao);
            }
        }

        private void AtualizarBotaoParaModoColapsado(Button button)
        {
            var conteudo = button.Content?.ToString();
            if (!string.IsNullOrEmpty(conteudo))
            {
                // Guardar o conteúdo original no dicionário
                if (!_buttonOriginalContent.ContainsKey(button))
                {
                    _buttonOriginalContent[button] = conteudo;
                }

                // Manter apenas o emoji (primeiros 2 caracteres)
                var emoji = GetEmojiFromContent(conteudo);
                button.Content = emoji;

                // Criar um estilo simples para modo colapsado
                var estiloColapsado = new Style(typeof(Button));
                estiloColapsado.Setters.Add(new Setter(Button.HeightProperty, 60.0));
                estiloColapsado.Setters.Add(new Setter(Button.MarginProperty, new Thickness(8, 4, 8, 4)));
                estiloColapsado.Setters.Add(new Setter(Button.FontSizeProperty, 20.0));
                estiloColapsado.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
                estiloColapsado.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
                estiloColapsado.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
                estiloColapsado.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
                estiloColapsado.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(0)));
                estiloColapsado.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));

                // Template para modo colapsado
                var template = new ControlTemplate(typeof(Button));
                var borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                });

                var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);

                borderFactory.AppendChild(contentPresenter);
                template.VisualTree = borderFactory;

                // Triggers
                var triggerMouseOver = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                triggerMouseOver.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(31, 41, 51))));

                var triggerPressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
                triggerPressed.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 99, 235))));

                estiloColapsado.Triggers.Add(triggerMouseOver);
                estiloColapsado.Triggers.Add(triggerPressed);

                estiloColapsado.Setters.Add(new Setter(Button.TemplateProperty, template));

                button.Style = estiloColapsado;

                // Adicionar tooltip com texto completo
                var textoCompleto = GetTextFromContent(conteudo);
                button.ToolTip = textoCompleto;
            }
        }

        private void AtualizarSidebarParaExpandido()
        {
            // Mostrar headers de texto (verificar para funcionários)
            txtOperacoesHeader.Visibility = Visibility.Visible;
            txtGestaoHeader.Visibility = Visibility.Visible;
            txtRelatoriosHeader.Visibility = Visibility.Visible;

            // Mostrar header de Admin apenas se não for funcionário ou se houver botões visíveis
            if (AppSession.CurrentUser?.Role != UserRole.Funcionario)
            {
                txtAdminHeader.Visibility = Visibility.Visible;
            }
            else
            {
                // Para funcionários, mostrar apenas se houver algum botão visível
                if (BtnDefinicoes.Visibility == Visibility.Visible ||
                    BtnUtilizadores.Visibility == Visibility.Visible)
                {
                    txtAdminHeader.Visibility = Visibility.Visible;
                }
            }

            // Restaurar botões para modo texto completo (apenas os visíveis)
            var botoesParaRestaurar = new List<Button>();

            if (BtnMesas.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnMesas);
            if (BtnCaixa.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnCaixa);
            if (BtnArtigos.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnArtigos);
            if (BtnReservas.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnReservas);
            if (BtnCategorias.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnCategorias);
            if (BtnClientes.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnClientes);
            if (BtnHistoricoPedidos.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnHistoricoPedidos);
            if (BtnFechoDiario.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnFechoDiario);
            if (BtnrRelatorios.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnrRelatorios);

            // Adicionar botões de admin apenas se visíveis
            if (BtnDefinicoes.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnDefinicoes);
            if (BtnUtilizadores.Visibility == Visibility.Visible) botoesParaRestaurar.Add(BtnUtilizadores);

            foreach (var botao in botoesParaRestaurar)
            {
                AtualizarBotaoParaModoExpandido(botao);
            }
        }

        private void AtualizarBotaoParaModoExpandido(Button button)
        {
            if (_buttonOriginalContent.TryGetValue(button, out var conteudoOriginal))
            {
                button.Content = conteudoOriginal;

                // Restaurar o estilo original
                var estiloOriginal = this.TryFindResource("MenuButtonStyle") as Style;
                if (estiloOriginal != null)
                {
                    button.Style = estiloOriginal;
                }
                else
                {
                    // Se não encontrar o recurso, criar um estilo similar
                    var estiloFallback = new Style(typeof(Button));
                    estiloFallback.Setters.Add(new Setter(Button.HeightProperty, 64.0));
                    estiloFallback.Setters.Add(new Setter(Button.MarginProperty, new Thickness(8, 4, 8, 4)));
                    estiloFallback.Setters.Add(new Setter(Button.FontSizeProperty, 17.0));
                    estiloFallback.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(229, 231, 235))));
                    estiloFallback.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
                    estiloFallback.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
                    estiloFallback.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
                    estiloFallback.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(24, 0, 24, 0)));
                    estiloFallback.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));

                    // Template para modo expandido
                    var template = new ControlTemplate(typeof(Button));
                    var borderFactory = new FrameworkElementFactory(typeof(Border));
                    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                    borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
                    {
                        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
                    });

                    var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                    contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

                    borderFactory.AppendChild(contentPresenter);
                    template.VisualTree = borderFactory;

                    // Triggers
                    var triggerMouseOver = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                    triggerMouseOver.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(31, 41, 51))));

                    var triggerPressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
                    triggerPressed.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(37, 99, 235))));

                    estiloFallback.Triggers.Add(triggerMouseOver);
                    estiloFallback.Triggers.Add(triggerPressed);

                    estiloFallback.Setters.Add(new Setter(Button.TemplateProperty, template));

                    button.Style = estiloFallback;
                }

                button.ToolTip = null;
            }
        }

        private string GetEmojiFromContent(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 2)
                return "🔘";

            // Retorna os primeiros 2 caracteres (emoji)
            return content.Substring(0, 2);
        }

        private string GetTextFromContent(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length <= 2)
                return content;

            // Retorna o texto sem o emoji
            return content.Substring(2).Trim();
        }

        // Método auxiliar para encontrar controles visuais
        private T FindVisualChild<T>(DependencyObject parent, string childName) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T frameworkElement && frameworkElement.Name == childName)
                {
                    return frameworkElement;
                }
                else
                {
                    var result = FindVisualChild<T>(child, childName);
                    if (result != null) return result;
                }
            }
            return null;
        }

        // =========================
        // TOPO
        // =========================
        private void TimerHora_Tick(object sender, EventArgs e)
        {
            txtCurrentTime.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void AtualizarInformacoesTopo()
        {
            if (AppSession.CurrentUser != null)
            {
                txtCurrentUser.Text =
                    $"Utilizador: {AppSession.CurrentUser.FullName} ({AppSession.CurrentUser.Role})";
            }
        }

        private void LimparConteudo()
        {
            MainContentArea.Content = null;
        }

        // =========================
        // NAVEGAÇÃO
        // =========================
        private void BtnMesas_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content =
                AppSession.CurrentUser.Role == UserRole.Funcionario
                    ? new Views.TablesCustomerView()
                    : new Views.TablesControl();
        }

        private void BtnArtigos_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content =
                AppSession.CurrentUser.Role == UserRole.Funcionario
                    ? new Views.EmployeesViewControl()
                    : new Views.ProductsManagementControl();
        }

        private void BtnCaixa_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.POSControl();
        }

        private void BtnClientes_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new ClienteCadastroControl();
        }

        private void BtnReservas_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.ReservationsControl();
        }

        private void BtnHistoricoPedidos_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.OrderHistoryControl();
        }

        private void BtnFechoDiario_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.DailyClosingsControl();
        }

        private void BtnDefinicoes_Click(object sender, RoutedEventArgs e)
        {
            // Verificar se o usuário tem permissão
            if (AppSession.CurrentUser?.Role == UserRole.Funcionario)
            {
                MessageBox.Show("Você não tem permissão para acessar as definições.", "Acesso Negado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LimparConteudo();
            MainContentArea.Content = new Views.SettingsControl();
        }

        private void BtnUtilizadores_Click(object sender, RoutedEventArgs e)
        {
            // Verificar se o usuário tem permissão
            if (AppSession.CurrentUser?.Role == UserRole.Funcionario)
            {
                MessageBox.Show("Você não tem permissão para gerenciar utilizadores.", "Acesso Negado",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LimparConteudo();
            MainContentArea.Content = new Views.UsersManagementControl();
        }

        private void BtnCategorias_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.CategoriesManagementControl();
        }
        private void BtnTurnos_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.LivroPontoControl();
        }
        private void BtnrRelatorios_Click(object sender, RoutedEventArgs e)
        {
            LimparConteudo();
            MainContentArea.Content = new Views.ReportsControl();
        }

        // =========================
        // SISTEMA
        // =========================

        private void BtnSair_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}