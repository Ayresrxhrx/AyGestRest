using AyGestRest.Data;
using AyGestRest.Models;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using BCrypt.Net;

namespace AyGestRest
{
    public partial class LoginWindow : Window
    {
        private AyGestRestContext _context;
        private ApiClient _apiClient;
        private bool _isClientMode = false;

        public LoginWindow()
        {
            InitializeComponent();
            txtUsername.Focus();

            _context = new AyGestRestContext();

            // 🔹 Cria Admin antes de qualquer login
            using var db = new AyGestRestContext();
            db.EnsureAdminUser();
        }

        // Construtor para modo cliente (com API)
        public LoginWindow(ApiClient apiClient) : this()
        {
            _apiClient = apiClient;
            _isClientMode = true;
            _context = null; // Não usar contexto local no modo cliente
        }

        private void btnEntrar_Click(object sender, RoutedEventArgs e)
        {
            txtErrorMessage.Visibility = Visibility.Collapsed;

            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MostrarErro("Preencha utilizador e senha.");
                return;
            }

            try
            {
                if (_isClientMode)
                {
                    // Modo cliente: autenticar via API
                    AuthenticateViaApi(username, password);
                }
                else
                {
                    // Modo local/servidor: autenticar via banco local
                    AuthenticateViaLocalDb(username, password);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("🔥 ERRO LOGIN: " + ex);
                MostrarErro("Erro ao ligar à base de dados.");
            }
        }

        private async void AuthenticateViaApi(string username, string password)
        {
            try
            {
                var loginData = new { Username = username, Password = password };
                var result = await _apiClient.PostAsync<object, dynamic>("api/login", loginData);

                if (result.success == true)
                {
                    // Converter resultado para User
                    var user = new User
                    {
                        Id = result.user.id,
                        Username = result.user.username,
                        FullName = result.user.fullName,
                        Role = (UserRole)result.user.role,
                        IsActive = result.user.isActive
                    };

                    AppSession.SetUser(user);
                    Debug.WriteLine($"✅ LOGIN OK (API): {user.Username} ({user.Role})");

                    Dispatcher.Invoke(() =>
                    {
                        DialogResult = true;
                        Close();
                    });
                }
                else
                {
                    MostrarErro("Credenciais inválidas.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Erro na autenticação via API: {ex.Message}");
                MostrarErro("Erro ao conectar ao servidor.");
            }
        }

        private void AuthenticateViaLocalDb(string username, string password)
        {
            using var db = new AyGestRestContext();

            // 🔹 Procura usuário ativo
            var user = db.Users.FirstOrDefault(u =>
                u.Username.Trim().ToLower() == username.ToLower() &&
                u.IsActive);

            if (user == null)
            {
                MostrarErro("Utilizador não encontrado ou inativo.");
                return;
            }

            bool senhaValida = false;

            // 🔹 Se for Admin antigo com senha plana
            if (user.Username.ToLower() == "admin" && user.PasswordHash == "1304")
            {
                senhaValida = password == "1304";
            }
            else
            {
                // 🔹 Outros users: compara via BCrypt
                senhaValida = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            }

            if (!senhaValida)
            {
                MostrarErro("Senha incorreta.");
                return;
            }

            // 🔹 Login ok
            AppSession.SetUser(user);

            Debug.WriteLine($"✅ LOGIN OK: {user.Username} ({user.Role})");

            DialogResult = true;
            Close();
        }

        private void MostrarErro(string mensagem)
        {
            txtErrorMessage.Text = mensagem;
            txtErrorMessage.Visibility = Visibility.Visible;
        }
    }
}