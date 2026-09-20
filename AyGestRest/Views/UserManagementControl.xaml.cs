using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AyGestRest.Data;
using AyGestRest.Models;

namespace AyGestRest.Views
{
    public partial class UsersManagementControl : UserControl
    {
        private ObservableCollection<UtilizadorViewModel> _utilizadores = new();
        private CollectionViewSource _viewSource;
        private UtilizadorViewModel _utilizadorEmEdicao;

        public UsersManagementControl()
        {
            InitializeComponent();
            Loaded += UsersManagementControl_Loaded;
        }

        private void UsersManagementControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Inicializa o DataGrid
                _viewSource = new CollectionViewSource { Source = _utilizadores };
                dgUtilizadores.ItemsSource = _viewSource.View;

                // Configura o ComboBox de perfis
                cmbPopupRole.ItemsSource = Enum.GetValues(typeof(UserRole)).Cast<UserRole>();
                cmbPopupRole.SelectedIndex = 0;

                // Carrega os dados
                CarregarUtilizadoresDoDB();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao inicializar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CarregarUtilizadoresDoDB()
        {
            try
            {
                panelLoading.Visibility = Visibility.Visible;
                dgUtilizadores.Visibility = Visibility.Collapsed;
                panelVazio.Visibility = Visibility.Collapsed;

                using var db = new AyGestRestContext();

                // Carrega utilizadores
                var users = db.Users.OrderBy(u => u.Username).ToList();

                _utilizadores.Clear();
                foreach (var u in users)
                {
                    _utilizadores.Add(new UtilizadorViewModel
                    {
                        Id = u.Id,
                        Username = u.Username,
                        FullName = u.FullName,
                        Role = u.Role,
                        IsActive = u.IsActive,
                        UltimoLogin = u.LastLogin,
                        SenhaHash = u.PasswordHash
                    });
                }

                // Atualiza contadores
                int total = _utilizadores.Count;
                int ativos = _utilizadores.Count(u => u.IsActive);
                int inativos = total - ativos;

                txtTotal.Text = total.ToString();
                txtAtivos.Text = ativos.ToString();
                txtInativos.Text = inativos.ToString();

                // Atualiza visibilidade
                if (_utilizadores.Count == 0)
                {
                    panelVazio.Visibility = Visibility.Visible;
                    dgUtilizadores.Visibility = Visibility.Collapsed;
                }
                else
                {
                    panelVazio.Visibility = Visibility.Collapsed;
                    dgUtilizadores.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar utilizadores:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                panelLoading.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtPesquisa_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                if (_viewSource?.View != null)
                {
                    _viewSource.View.Filter = item =>
                    {
                        if (item is not UtilizadorViewModel user) return false;

                        string filtro = txtPesquisa.Text.Trim().ToLower();
                        if (string.IsNullOrEmpty(filtro)) return true;

                        return user.Username.ToLower().Contains(filtro) ||
                               user.FullName.ToLower().Contains(filtro) ||
                               user.Role.ToString().ToLower().Contains(filtro);
                    };
                }
            }
            catch (Exception)
            {
                // Ignora erro de filtro
            }
        }

        private void BtnMostrarPopup_Click(object sender, RoutedEventArgs e)
        {
            _utilizadorEmEdicao = null;
            txtTituloPopup.Text = "Cadastrar Novo Utilizador";
            lblSenha.Text = "Senha*";
            LimparFormularioPopup();

            popupCadastro.Visibility = Visibility.Visible;
            overlay.Visibility = Visibility.Visible;

            txtPopupUsername.Focus();
        }

        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is UtilizadorViewModel user)
            {
                _utilizadorEmEdicao = user;
                txtTituloPopup.Text = "Editar Utilizador";
                lblSenha.Text = "Senha (deixe vazio para manter atual)";

                txtPopupUsername.Text = user.Username;
                txtPopupFullName.Text = user.FullName;
                cmbPopupRole.SelectedItem = user.Role;
                chkPopupIsActive.IsChecked = user.IsActive;
                txtPopupSenha.Password = "";

                txtPopupUsername.IsEnabled = false;

                popupCadastro.Visibility = Visibility.Visible;
                overlay.Visibility = Visibility.Visible;

                txtPopupFullName.Focus();
            }
        }

        private void BtnFecharPopup_Click(object sender, RoutedEventArgs e)
        {
            popupCadastro.Visibility = Visibility.Collapsed;
            overlay.Visibility = Visibility.Collapsed;
            txtPopupUsername.IsEnabled = true;
        }

        private void LimparFormularioPopup()
        {
            txtPopupUsername.Text = "";
            txtPopupFullName.Text = "";
            txtPopupSenha.Password = "";
            cmbPopupRole.SelectedIndex = 0;
            chkPopupIsActive.IsChecked = true;
            txtPopupUsername.IsEnabled = true;
        }

        private void BtnSalvarPopup_Click(object sender, RoutedEventArgs e)
        {
            string username = txtPopupUsername.Text.Trim();
            string fullname = txtPopupFullName.Text.Trim();
            string senha = txtPopupSenha.Password;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullname))
            {
                MessageBox.Show("Preencha o nome de utilizador e nome completo.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (cmbPopupRole.SelectedItem is not UserRole role)
            {
                MessageBox.Show("Selecione um perfil/role.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool ativo = chkPopupIsActive.IsChecked == true;

            if (_utilizadorEmEdicao == null && string.IsNullOrWhiteSpace(senha))
            {
                MessageBox.Show("Defina uma senha para o novo utilizador.", "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var db = new AyGestRestContext();

                if (_utilizadorEmEdicao == null)
                {
                    var novo = new User
                    {
                        Username = username,
                        FullName = fullname,
                        Role = role,
                        IsActive = ativo,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(senha),
                        LastLogin = null
                    };

                    db.Users.Add(novo);
                    db.SaveChanges();

                    _utilizadores.Add(new UtilizadorViewModel
                    {
                        Id = novo.Id,
                        Username = novo.Username,
                        FullName = novo.FullName,
                        Role = novo.Role,
                        IsActive = novo.IsActive,
                        UltimoLogin = novo.LastLogin,
                        SenhaHash = novo.PasswordHash
                    });

                    MessageBox.Show($"Utilizador '{username}' adicionado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var existente = db.Users.FirstOrDefault(u => u.Id == _utilizadorEmEdicao.Id);
                    if (existente != null)
                    {
                        existente.FullName = fullname;
                        existente.Role = role;
                        existente.IsActive = ativo;

                        if (!string.IsNullOrWhiteSpace(senha))
                        {
                            existente.PasswordHash = BCrypt.Net.BCrypt.HashPassword(senha);
                        }

                        db.SaveChanges();

                        // Atualiza diretamente as propriedades
                        _utilizadorEmEdicao.FullName = fullname;
                        _utilizadorEmEdicao.Role = role;
                        _utilizadorEmEdicao.IsActive = ativo;

                        MessageBox.Show($"Utilizador '{username}' atualizado com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Recarrega para atualizar tudo
                CarregarUtilizadoresDoDB();
                BtnFecharPopup_Click(null, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAlterarSenha_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is UtilizadorViewModel user)
            {
                var dialog = new PasswordDialog(user.Username);
                if (dialog.ShowDialog() == true)
                {
                    string novaSenha = dialog.Password;
                    if (!string.IsNullOrWhiteSpace(novaSenha))
                    {
                        try
                        {
                            using var db = new AyGestRestContext();
                            var u = db.Users.FirstOrDefault(x => x.Id == user.Id);
                            if (u != null)
                            {
                                u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(novaSenha);
                                db.SaveChanges();
                                MessageBox.Show("Senha alterada com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Erro ao alterar senha:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private void BtnApagar_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is UtilizadorViewModel user)
            {
                if (MessageBox.Show($"Tem a certeza que pretende apagar o utilizador '{user.Username}'?\nEsta ação é irreversível.",
                    "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    try
                    {
                        using var db = new AyGestRestContext();
                        var u = db.Users.FirstOrDefault(x => x.Id == user.Id);
                        if (u != null)
                        {
                            db.Users.Remove(u);
                            db.SaveChanges();
                            _utilizadores.Remove(user);
                            CarregarUtilizadoresDoDB();
                            MessageBox.Show("Utilizador apagado com sucesso.", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao apagar:\n{ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        public void RecarregarDados()
        {
            CarregarUtilizadoresDoDB();
        }
    }

    // ViewModel MUITO SIMPLES - sem INotifyPropertyChanged complexo
    public class UtilizadorViewModel : INotifyPropertyChanged
    {
        private int _id;
        private string _username;
        private string _fullName;
        private UserRole _role;
        private bool _isActive;
        private DateTime? _ultimoLogin;
        private string _senhaHash;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged(nameof(Username));
                }
            }
        }

        public string FullName
        {
            get => _fullName;
            set
            {
                if (_fullName != value)
                {
                    _fullName = value;
                    OnPropertyChanged(nameof(FullName));
                }
            }
        }

        public UserRole Role
        {
            get => _role;
            set
            {
                if (_role != value)
                {
                    _role = value;
                    OnPropertyChanged(nameof(Role));
                    OnPropertyChanged(nameof(RoleDisplay));
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public DateTime? UltimoLogin
        {
            get => _ultimoLogin;
            set
            {
                if (_ultimoLogin != value)
                {
                    _ultimoLogin = value;
                    OnPropertyChanged(nameof(UltimoLogin));
                    OnPropertyChanged(nameof(UltimoLoginFormatado));
                }
            }
        }

        public string SenhaHash
        {
            get => _senhaHash;
            set
            {
                if (_senhaHash != value)
                {
                    _senhaHash = value;
                    OnPropertyChanged(nameof(SenhaHash));
                }
            }
        }

        // Propriedades calculadas SIMPLES
        public string UltimoLoginFormatado => UltimoLogin.HasValue
            ? UltimoLogin.Value.ToString("dd/MM/yyyy HH:mm")
            : "Nunca";

        public string RoleDisplay => Role.ToString();

        public string StatusText => IsActive ? "Ativo" : "Inativo";

        public System.Collections.Generic.IEnumerable<UserRole> FontePerfis =>
            Enum.GetValues(typeof(UserRole)).Cast<UserRole>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Janela para alterar senha
    public class PasswordDialog : Window
    {
        public string Password { get; private set; }

        public PasswordDialog(string username)
        {
            Title = $"Alterar Senha - {username}";
            Width = 400;
            Height = 250;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var stackPanel = new StackPanel { Margin = new Thickness(20) };

            stackPanel.Children.Add(new TextBlock
            {
                Text = $"Nova senha para {username}:",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var passwordBox = new PasswordBox
            {
                Height = 36,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 20)
            };

            stackPanel.Children.Add(passwordBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new Button
            {
                Content = "Cancelar",
                Width = 80,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0)
            };

            btnCancel.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            var btnOk = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 36,
                Background = Brushes.DodgerBlue,
                Foreground = Brushes.White
            };

            btnOk.Click += (s, e) =>
            {
                Password = passwordBox.Password;
                DialogResult = true;
                Close();
            };

            buttonPanel.Children.Add(btnCancel);
            buttonPanel.Children.Add(btnOk);
            stackPanel.Children.Add(buttonPanel);

            Content = stackPanel;
        }
    }
}