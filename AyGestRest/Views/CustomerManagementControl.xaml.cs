using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AyGestRest.Views
{
    public partial class CustomerManagementControl : UserControl
    {
        private readonly AyGestRestContext _db = new AyGestRestContext();
        private readonly ObservableCollection<Cliente> clientes = new ObservableCollection<Cliente>();

        public CustomerManagementControl()
        {
            InitializeComponent();
            ClientesDataGrid.ItemsSource = clientes;

            // Carregar clientes do banco de dados ao iniciar
            _ = CarregarClientesDoBancoAsync();

            // Definir a data atual no DatePicker
            UltimaVisitaPicker.SelectedDate = DateTime.Now;
        }

        private async Task CarregarClientesDoBancoAsync()
        {
            try
            {
                var clientesDoDb = await _db.Clientes.OrderBy(c => c.Nome).ToListAsync();
                clientes.Clear();
                foreach (var cliente in clientesDoDb)
                {
                    clientes.Add(cliente);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar clientes do banco de dados:\n{ex.Message}",
                    "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void AdicionarCliente_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NomeTextBox.Text))
            {
                MessageBox.Show("O nome é obrigatório!", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PontosTextBox.Text, out int pontos) || pontos < 0)
            {
                MessageBox.Show("Pontos inválidos! Insira um número positivo.", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var novoCliente = new Cliente
            {
                CodigoUnico = GenerateUniqueCode(),
                Nome = NomeTextBox.Text.Trim(),
                Telefone = TelefoneTextBox.Text?.Trim(),
                Email = EmailTextBox.Text?.Trim(),
                Pontos = pontos,
                UltimaVisita = UltimaVisitaPicker.SelectedDate ?? DateTime.Today
            };

            try
            {
                // Adiciona ao banco de dados
                _db.Clientes.Add(novoCliente);
                await _db.SaveChangesAsync();

                // Adiciona à coleção local (que está ligada à grid)
                clientes.Add(novoCliente);

                MessageBox.Show("Cliente salvo com sucesso!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);

                LimparCampos();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao salvar o cliente no banco de dados:\n{ex.Message}",
                    "Erro de Salvamento", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LimparCampos()
        {
            NomeTextBox.Text = "";
            TelefoneTextBox.Text = "";
            EmailTextBox.Text = "";
            PontosTextBox.Text = "0";
            UltimaVisitaPicker.SelectedDate = DateTime.Now;
            NomeTextBox.Focus();
        }

        private string GenerateUniqueCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rnd = new Random();
            string code;

            do
            {
                char[] stringChars = new char[6];
                for (int i = 0; i < stringChars.Length; i++)
                {
                    stringChars[i] = chars[rnd.Next(chars.Length)];
                }
                code = new string(stringChars);
            }
            while (clientes.Any(c => c.CodigoUnico == code) ||
                   _db.Clientes.Any(c => c.CodigoUnico == code)); // evita duplicação mesmo no DB

            return code;
        }
    }
}