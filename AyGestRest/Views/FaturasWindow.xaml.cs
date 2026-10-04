using AyGestRest.Data;
using AyGestRest.Models;
using AyGestRest.Services;
using Microsoft.Data.Sqlite;
using System.Collections.ObjectModel;
using System.Windows;

namespace AyGestRest.Views
{
    public partial class FaturasWindow : Window
    {
        private readonly ObservableCollection<FaturaRow> _items = new();
        private readonly FaturacaoService _faturacao;

        public FaturasWindow()
        {
            InitializeComponent();
            using var db = new AyGestRestContext();
            _faturacao = new FaturacaoService(db);
            InvoicesGrid.ItemsSource = _items;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            _items.Clear();
            await _faturacao.EnsureSchemaAsync();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest", "AyGestRest.db");
            await using var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Mode=ReadWriteCreate");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,Numero,DataEmissao,ClienteNome,ClienteNuit,Subtotal,Imposto,Total,Estado,TerminalId FROM Faturas ORDER BY Id DESC LIMIT 200;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                _items.Add(new FaturaRow
                {
                    Id = reader.GetInt32(0), Numero = reader.GetString(1), DataEmissao = DateTime.Parse(reader.GetString(2)),
                    ClienteNome = reader.GetString(3), ClienteNuit = reader.GetString(4), Subtotal = Convert.ToDecimal(reader.GetValue(5)),
                    Imposto = Convert.ToDecimal(reader.GetValue(6)), Total = Convert.ToDecimal(reader.GetValue(7)), Estado = (FaturaEstado)reader.GetInt32(8), TerminalId = reader.GetString(9)
                });
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var term = SearchBox.Text.Trim();
            InvoicesGrid.ItemsSource = _items.Where(x => string.IsNullOrWhiteSpace(term) || x.Numero.Contains(term, StringComparison.OrdinalIgnoreCase) || x.ClienteNome.Contains(term, StringComparison.OrdinalIgnoreCase) || x.ClienteNuit.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private async void Void_Click(object sender, RoutedEventArgs e)
        {
            if (InvoicesGrid.SelectedItem is not FaturaRow selected) return;
            if (selected.Estado == FaturaEstado.Anulada)
            {
                MessageBox.Show(this, "Esta factura já está anulada.", "Facturação", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new TextInputDialog("Motivo da anulação", "");
            dialog.Owner = this;
            if (dialog.ShowDialog() != true) return;

            try
            {
                await _faturacao.AnularFaturaAsync(selected.Id, dialog.Value);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Facturação", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private sealed class FaturaRow
        {
            public int Id { get; set; }
            public string Numero { get; set; } = string.Empty;
            public DateTime DataEmissao { get; set; }
            public string ClienteNome { get; set; } = string.Empty;
            public string ClienteNuit { get; set; } = string.Empty;
            public decimal Subtotal { get; set; }
            public decimal Imposto { get; set; }
            public decimal Total { get; set; }
            public FaturaEstado Estado { get; set; }
            public string TerminalId { get; set; } = string.Empty;
        }
    }
}
