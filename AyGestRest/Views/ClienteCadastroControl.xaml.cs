using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AyGestRest.Data;
using AyGestRest.Models;
using System.Windows.Media;

namespace AyGestRest.Controls
{
    public partial class ClienteCadastroControl : UserControl
    {
        private AyGestRestContext _context;
        private Cliente _clienteAtual;
        private bool _editando = false;
        private RestaurantConfig _config;

        public ObservableCollection<Cliente> Clientes { get; set; }

        public ClienteCadastroControl()
        {
            InitializeComponent();
            InitializeContext();
            LoadConfig();
            LoadClientes();
            ConfigurarEventos();
            LimparFormulario();
            InitializePlaceholder();
            AtualizarEstatisticasLista();
            BtnTabCadastro_Click(null, null);
        }

        private void InitializeContext()
        {
            try
            {
                _context = new AyGestRestContext();
                _context.Database.EnsureCreated();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao conectar ao banco de dados: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadConfig()
        {
            try
            {
                _config = _context.RestaurantConfigs.FirstOrDefault();
                if (_config == null)
                {
                    _config = new RestaurantConfig();
                    _context.RestaurantConfigs.Add(_config);
                    _context.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar configuração: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                _config = new RestaurantConfig();
            }
        }

        private void LoadClientes()
        {
            try
            {
                Clientes = new ObservableCollection<Cliente>(_context.Clientes.OrderBy(c => c.Nome).ToList());
                dgClientes.ItemsSource = Clientes;
                txtTotalClientes.Text = $"{Clientes.Count} clientes";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar clientes: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ConfigurarEventos()
        {
            txtTelefone.TextChanged += TxtTelefone_TextChanged;
            txtEmail.LostFocus += TxtEmail_LostFocus;
            txtNome.LostFocus += TxtNome_LostFocus;

            if (string.IsNullOrEmpty(txtCodigoUnico.Text))
            {
                GerarCodigoUnico();
            }

            txtPontos.PreviewTextInput += TxtPontos_PreviewTextInput;
        }

        private void GerarCodigoUnico()
        {
            try
            {
                var ultimoCodigo = _context.Clientes
                    .OrderByDescending(c => c.Id)
                    .Select(c => c.CodigoUnico)
                    .FirstOrDefault();

                int proximoNumero = 1;
                if (!string.IsNullOrEmpty(ultimoCodigo) && ultimoCodigo.StartsWith("CLI"))
                {
                    if (int.TryParse(ultimoCodigo.Substring(3), out int numeroAtual))
                    {
                        proximoNumero = numeroAtual + 1;
                    }
                }

                txtCodigoUnico.Text = $"CLI{proximoNumero:0000}";
            }
            catch
            {
                var random = new Random();
                txtCodigoUnico.Text = $"CLI{random.Next(1000, 9999)}";
            }
        }

        private bool ValidarFormulario()
        {
            bool valido = true;

            if (string.IsNullOrWhiteSpace(txtNome.Text))
            {
                txtNomeError.Visibility = Visibility.Visible;
                valido = false;
            }
            else
            {
                txtNomeError.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrWhiteSpace(txtEmail.Text) && !ValidarEmail(txtEmail.Text))
            {
                txtEmailError.Visibility = Visibility.Visible;
                valido = false;
            }
            else
            {
                txtEmailError.Visibility = Visibility.Collapsed;
            }

            return valido;
        }

        private bool ValidarEmail(string email)
        {
            try
            {
                var regex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                return regex.IsMatch(email);
            }
            catch
            {
                return false;
            }
        }

        private void FormatarTelefone(string telefone)
        {
            if (string.IsNullOrWhiteSpace(telefone)) return;

            var apenasNumeros = new string(telefone.Where(char.IsDigit).ToArray());

            if (apenasNumeros.Length <= 10)
            {
                if (apenasNumeros.Length >= 2)
                {
                    txtTelefone.Text = $"({apenasNumeros.Substring(0, 2)}) {apenasNumeros.Substring(2)}";
                }
            }
            else
            {
                if (apenasNumeros.Length >= 2)
                {
                    var ddd = apenasNumeros.Substring(0, 2);
                    var restante = apenasNumeros.Substring(2);

                    if (restante.Length >= 5)
                    {
                        txtTelefone.Text = $"({ddd}) {restante.Substring(0, 5)}-{restante.Substring(5)}";
                    }
                    else
                    {
                        txtTelefone.Text = $"({ddd}) {restante}";
                    }
                }
            }

            txtTelefone.CaretIndex = txtTelefone.Text.Length;
        }

        #region Métodos de Navegação

        private void BtnTabCadastro_Click(object sender, RoutedEventArgs e)
        {
            scrollCadastro.Visibility = Visibility.Visible;
            pnlListaClientes.Visibility = Visibility.Collapsed;

            btnTabCadastro.Style = (Style)FindResource("ActiveTabButtonStyle");
            btnTabLista.Style = (Style)FindResource("TabButtonStyle");
        }

        private void BtnTabLista_Click(object sender, RoutedEventArgs e)
        {
            scrollCadastro.Visibility = Visibility.Collapsed;
            pnlListaClientes.Visibility = Visibility.Visible;

            btnTabCadastro.Style = (Style)FindResource("TabButtonStyle");
            btnTabLista.Style = (Style)FindResource("ActiveTabButtonStyle");

            AtualizarEstatisticasLista();
        }

        #endregion

        #region Métodos do Formulário

        private void BtnGerarCodigo_Click(object sender, RoutedEventArgs e)
        {
            GerarCodigoUnico();
        }

        private void BtnAdicionarPontos_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtPontos.Text, out int pontos))
            {
                pontos += 10;
                txtPontos.Text = pontos.ToString();
            }
        }

        private void BtnRemoverPontos_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(txtPontos.Text, out int pontos) && pontos >= 10)
            {
                pontos -= 10;
                txtPontos.Text = pontos.ToString();
            }
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidarFormulario())
            {
                MessageBox.Show("Por favor, corrija os erros no formulário.",
                    "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_editando && _clienteAtual != null)
                {
                    AtualizarClienteExistente();
                }
                else
                {
                    CriarNovoCliente();
                }

                _context.SaveChanges();
                MostrarSucesso(_editando ? "Cliente atualizado com sucesso!" : "Cliente cadastrado com sucesso!");

                LoadClientes();
                LimparFormulario();
                AtualizarEstatisticasLista();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar cliente: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AtualizarClienteExistente()
        {
            _clienteAtual.Nome = txtNome.Text.Trim();
            _clienteAtual.Telefone = string.IsNullOrWhiteSpace(txtTelefone.Text) ? null : txtTelefone.Text.Trim();
            _clienteAtual.Email = string.IsNullOrWhiteSpace(txtEmail.Text) ? null : txtEmail.Text.Trim();
            _clienteAtual.Pontos = int.TryParse(txtPontos.Text, out int pontos) ? pontos : 0;
            _clienteAtual.UltimaVisita = dpUltimaVisita.SelectedDate ?? DateTime.Today;
        }

        private void CriarNovoCliente()
        {
            var novoCliente = new Cliente
            {
                CodigoUnico = txtCodigoUnico.Text,
                Nome = txtNome.Text.Trim(),
                Telefone = string.IsNullOrWhiteSpace(txtTelefone.Text) ? null : txtTelefone.Text.Trim(),
                Email = string.IsNullOrWhiteSpace(txtEmail.Text) ? null : txtEmail.Text.Trim(),
                Pontos = int.TryParse(txtPontos.Text, out int pontos) ? pontos : 0,
                UltimaVisita = dpUltimaVisita.SelectedDate ?? DateTime.Today
            };

            _context.Clientes.Add(novoCliente);
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            if (_editando)
            {
                var resultado = MessageBox.Show("Deseja cancelar a edição? Todas as alterações serão perdidas.",
                    "Cancelar Edição", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (resultado == MessageBoxResult.Yes)
                {
                    LimparFormulario();
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(txtNome.Text) ||
                    !string.IsNullOrWhiteSpace(txtTelefone.Text) ||
                    !string.IsNullOrWhiteSpace(txtEmail.Text))
                {
                    var resultado = MessageBox.Show("Deseja limpar o formulário?",
                        "Limpar Formulário", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (resultado == MessageBoxResult.Yes)
                    {
                        LimparFormulario();
                    }
                }
            }
        }

        private void BtnLimpar_Click(object sender, RoutedEventArgs e)
        {
            LimparFormulario();
        }

        #endregion

        #region Métodos da Lista

        private void AtualizarEstatisticasLista()
        {
            try
            {
                int totalClientes = _context.Clientes.Count();
                int pontosTotais = _context.Clientes.Sum(c => c.Pontos);

                txtContagemClientes.Text = $"Total: {totalClientes}";
                txtClientesAtivos.Text = $"Ativos: {totalClientes}";
                txtPontosTotais.Text = $"Pontos: {pontosTotais}";
                txtTotalClientes.Text = $"{totalClientes} clientes";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar estatísticas: {ex.Message}");
            }
        }

        private void TxtPesquisaCliente_TextChanged(object sender, TextChangedEventArgs e)
        {
            string termo = txtPesquisaCliente.Text.Trim();

            if (termo == "Pesquisar por nome, telefone ou código...")
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(termo))
            {
                dgClientes.ItemsSource = Clientes;
            }
            else
            {
                var resultados = Clientes
                    .Where(c =>
                        (c.Nome != null && c.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                        (c.CodigoUnico != null && c.CodigoUnico.Contains(termo, StringComparison.OrdinalIgnoreCase)) ||
                        (c.Telefone != null && c.Telefone.Contains(termo)) ||
                        (c.Email != null && c.Email.Contains(termo, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                dgClientes.ItemsSource = resultados;
            }
        }

        private void BtnLimparPesquisa_Click(object sender, RoutedEventArgs e)
        {
            txtPesquisaCliente.Text = "Pesquisar por nome, telefone ou código...";
            txtPesquisaCliente.Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166));
            dgClientes.ItemsSource = Clientes;
        }

        private void BtnNovoClienteLista_Click(object sender, RoutedEventArgs e)
        {
            BtnTabCadastro_Click(sender, e);
            LimparFormulario();
        }

        private void BtnVisualizarCliente_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var cliente = _context.Clientes.FirstOrDefault(c => c.Id == id);
                if (cliente != null)
                {
                    MessageBox.Show(
                        $"Detalhes do Cliente:\n\n" +
                        $"Código: {cliente.CodigoUnico}\n" +
                        $"Nome: {cliente.Nome}\n" +
                        $"Telefone: {cliente.Telefone ?? "Não informado"}\n" +
                        $"E-mail: {cliente.Email ?? "Não informado"}\n" +
                        $"Pontos: {cliente.Pontos}\n" +
                        $"Última Visita: {cliente.UltimaVisita:dd/MM/yyyy}\n\n" +
                        $"Nome para exibição:\n{cliente.DisplayName}",
                        "Detalhes do Cliente",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }

        private void BtnExportarClientes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var clientes = _context.Clientes.ToList();
                if (clientes.Count == 0)
                {
                    MessageBox.Show("Não há clientes para exportar.", "Exportação", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var csv = new StringBuilder();
                csv.AppendLine("Código;Nome;Telefone;E-mail;Pontos;Última Visita");

                foreach (var cliente in clientes)
                {
                    csv.AppendLine($"{cliente.CodigoUnico};{cliente.Nome};{cliente.Telefone ?? ""};{cliente.Email ?? ""};{cliente.Pontos};{cliente.UltimaVisita:dd/MM/yyyy}");
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Arquivo CSV (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = $"clientes_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, csv.ToString(), Encoding.UTF8);
                    MessageBox.Show($"Clientes exportados com sucesso!\n\nArquivo: {saveDialog.FileName}",
                        "Exportação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao exportar clientes: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Métodos de Edição/Exclusão

        private void DgClientes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgClientes.SelectedItem is Cliente cliente)
            {
                CarregarClienteParaEdicao(cliente);
                BtnTabCadastro_Click(null, null);
            }
        }

        private void BtnEditarCliente_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var cliente = _context.Clientes.FirstOrDefault(c => c.Id == id);
                if (cliente != null)
                {
                    CarregarClienteParaEdicao(cliente);
                    dgClientes.SelectedItem = cliente;
                    BtnTabCadastro_Click(null, null);
                }
            }
        }

        private void BtnExcluirCliente_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var resultado = MessageBox.Show("Tem certeza que deseja excluir este cliente? Esta ação não pode ser desfeita.",
                    "Confirmar Exclusão", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (resultado == MessageBoxResult.Yes)
                {
                    try
                    {
                        var cliente = _context.Clientes.FirstOrDefault(c => c.Id == id);
                        if (cliente != null)
                        {
                            _context.Clientes.Remove(cliente);
                            _context.SaveChanges();

                            LoadClientes();
                            AtualizarEstatisticasLista();

                            if (_editando && _clienteAtual?.Id == id)
                            {
                                LimparFormulario();
                            }

                            MostrarSucesso("Cliente excluído com sucesso!");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao excluir cliente: {ex.Message}",
                            "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion

        #region Event Handlers

        private void TxtTelefone_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (txtTelefone.IsFocused)
            {
                FormatarTelefone(txtTelefone.Text);
            }
        }

        private void TxtEmail_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtEmail.Text) && !ValidarEmail(txtEmail.Text))
            {
                txtEmailError.Visibility = Visibility.Visible;
            }
            else
            {
                txtEmailError.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtNome_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtNome.Text))
            {
                txtNomeError.Visibility = Visibility.Visible;
            }
            else
            {
                txtNomeError.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtPontos_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        #endregion

        #region Métodos Auxiliares

        private void CarregarClienteParaEdicao(Cliente cliente)
        {
            _clienteAtual = cliente;
            _editando = true;

            txtCodigoUnico.Text = cliente.CodigoUnico;
            txtNome.Text = cliente.Nome;
            txtTelefone.Text = cliente.Telefone ?? "";
            txtEmail.Text = cliente.Email ?? "";
            txtPontos.Text = cliente.Pontos.ToString();
            dpUltimaVisita.SelectedDate = cliente.UltimaVisita;

            btnSalvar.Content = "💾 Atualizar Cliente";
            txtSuccessMessage.Text = "Cliente atualizado com sucesso!";

            var random = new Random();
            txtTotalCompras.Text = random.Next(1, 50).ToString();

            // Usar a moeda configurada aqui
            decimal valorMedio = random.Next(30, 150);
            txtValorMedio.Text = FormatCurrency(valorMedio);

            var diasDesdeUltimaVisita = (DateTime.Today - cliente.UltimaVisita).Days;
            txtDiasSemVisitar.Text = $"{diasDesdeUltimaVisita} dias";
        }

        private void LimparFormulario()
        {
            _clienteAtual = null;
            _editando = false;

            GerarCodigoUnico();
            txtNome.Text = "";
            txtTelefone.Text = "";
            txtEmail.Text = "";
            txtPontos.Text = "0";
            dpUltimaVisita.SelectedDate = DateTime.Today;
            cmbStatus.SelectedIndex = 0;

            txtNomeError.Visibility = Visibility.Collapsed;
            txtEmailError.Visibility = Visibility.Collapsed;

            btnSalvar.Content = "💾 Salvar Cliente";
            txtSuccessMessage.Text = "Cliente salvo com sucesso!";

            // Resetar estatísticas usando a moeda configurada
            txtTotalCompras.Text = "0";
            txtValorMedio.Text = FormatCurrency(0);
            txtDiasSemVisitar.Text = "0 dias";

            dgClientes.SelectedItem = null;
        }

        private void MostrarSucesso(string mensagem)
        {
            txtSuccessMessage.Text = mensagem;

            var storyboard = (Storyboard)FindResource("SuccessAnimation");
            if (storyboard != null)
            {
                storyboard.Begin(successIndicator);
            }
        }

        private void InitializePlaceholder()
        {
            txtPesquisaCliente.GotFocus += (s, e) =>
            {
                if (txtPesquisaCliente.Text == "Pesquisar por nome, telefone ou código...")
                {
                    txtPesquisaCliente.Text = "";
                    txtPesquisaCliente.Foreground = new SolidColorBrush(Colors.Black);
                }
            };

            txtPesquisaCliente.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPesquisaCliente.Text))
                {
                    txtPesquisaCliente.Text = "Pesquisar por nome, telefone ou código...";
                    txtPesquisaCliente.Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166));
                }
            };

            txtPesquisaCliente.Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166));
        }

        // Método para formatar valores monetários com a moeda configurada
        private string FormatCurrency(decimal value)
        {
            return $"{_config.CurrencySymbol} {value:N2}";
        }

        // Propriedade para binding (se necessário)
        public string CurrencyText
        {
            get { return FormatCurrency(0); }
        }

        #endregion

        #region Métodos Públicos

        public void CarregarClientePorId(int clienteId)
        {
            try
            {
                var cliente = _context.Clientes.FirstOrDefault(c => c.Id == clienteId);
                if (cliente != null)
                {
                    CarregarClienteParaEdicao(cliente);
                    BtnTabCadastro_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar cliente: {ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void MostrarListaClientes(bool mostrar)
        {
            if (mostrar)
            {
                BtnTabLista_Click(null, null);
            }
            else
            {
                BtnTabCadastro_Click(null, null);
            }
        }

        #endregion
    }
}