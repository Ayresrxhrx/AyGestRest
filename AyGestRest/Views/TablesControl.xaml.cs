using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AyGestRest.Views
{
    public partial class TablesControl : UserControl
    {
        private readonly AyGestRestContext _db = new();
        private List<RestaurantTable> _tables = new();
        private FrameworkElement? _draggingElement;
        private Point _dragStartPosition;
        private bool _isDragging;
        private readonly ScaleTransform _scaleTransform = new(1, 1);

        public TablesControl()
        {
            InitializeComponent();
            TablesCanvas.LayoutTransform = _scaleTransform;
            Loaded += TablesControl_Loaded;
            Unloaded += TablesControl_Unloaded;
        }

        private void TablesControl_Unloaded(object sender, RoutedEventArgs e)
        {
            AppEvents.SpecificTableStatusChanged -= OnSpecificTableStatusChanged;
        }

        private async void OnSpecificTableStatusChanged(int tableId)
        {
            try
            {
                if (!IsLoaded) return;
                await Dispatcher.InvokeAsync(async () => await CarregarMesas());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao actualizar mesa {tableId}: {ex.Message}");
            }
        }

        private void TxtValorRecebido_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox) textBox.SelectAll();
        }

        private async void TablesControl_Loaded(object sender, RoutedEventArgs e)
        {
            AppEvents.SpecificTableStatusChanged -= OnSpecificTableStatusChanged;
            AppEvents.SpecificTableStatusChanged += OnSpecificTableStatusChanged;
            await CarregarMesas();
        }

        private async Task CarregarMesas()
        {
            _tables = await _db.Tables.AsNoTracking().ToListAsync();
            DrawTables();
        }

        private void DrawTables()
        {
            TablesCanvas.Children.Clear();
            foreach (var table in _tables)
            {
                var border = new Border
                {
                    Width = 110,
                    Height = 110,
                    CornerRadius = new CornerRadius(20),
                    Background = GetStatusBrush(table.Status),
                    BorderBrush = table.Selecionado ? Brushes.Gold : Brushes.WhiteSmoke,
                    BorderThickness = new Thickness(table.Selecionado ? 5 : 2),
                    Cursor = Cursors.Hand,
                    Tag = table
                };

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = table.Number, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                stack.Children.Add(new TextBlock { Text = $"({table.Capacity} pessoas)", Foreground = Brushes.WhiteSmoke, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center });
                border.Child = stack;

                var tooltipStack = new StackPanel { Margin = new Thickness(10) };
                tooltipStack.Children.Add(new TextBlock { Text = $"Mesa: {table.Number}", FontWeight = FontWeights.Bold });
                tooltipStack.Children.Add(new TextBlock { Text = $"Capacidade: {table.Capacity}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Status: {table.Status}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Notas: {table.Notes ?? "Nenhuma"}", TextWrapping = TextWrapping.Wrap });
                if (table.Status == TableStatus.Ocupada)
                    tooltipStack.Children.Add(new TextBlock { Text = "Venda aberta — clique para abrir", FontWeight = FontWeights.SemiBold });
                border.ToolTip = new ToolTip { Content = tooltipStack };

                border.MouseLeftButtonUp += Mesa_MouseLeftButtonUp;
                border.MouseRightButtonUp += Mesa_MouseRightButtonUp;
                Canvas.SetLeft(border, table.PositionX);
                Canvas.SetTop(border, table.PositionY);
                TablesCanvas.Children.Add(border);
            }
        }

        private Brush GetStatusBrush(TableStatus status) => status switch
        {
            TableStatus.Livre => Brushes.Gray,
            TableStatus.Ocupada => Brushes.Green,
            TableStatus.Reservada => Brushes.Orange,
            TableStatus.Pendente => Brushes.Yellow,
            _ => Brushes.Gray
        };

        private void Mesa_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not RestaurantTable table) return;
            if (Keyboard.Modifiers == ModifierKeys.Control) table.Selecionado = !table.Selecionado;
            else
            {
                foreach (var t in _tables) t.Selecionado = false;
                table.Selecionado = true;
            }
            DrawTables();
            // O POS recebe a mesa. Se estiver ocupada, o POS deve carregar a venda aberta; se livre, abre nova venda.
            OnOpenTableCommand?.Invoke(this, table);
        }

        private void Mesa_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (chkModoEdicao.IsChecked != true) return;
            if (sender is FrameworkElement element && element.Tag is RestaurantTable table)
            {
                var menu = new ContextMenu();
                var editar = new MenuItem { Header = "Editar Mesa" };
                editar.Click += (_, __) => EditarMesa(table);
                menu.Items.Add(editar);
                var excluir = new MenuItem { Header = "Excluir Mesa" };
                excluir.Click += async (_, __) => await ExcluirMesa(table);
                menu.Items.Add(excluir);
                menu.Items.Add(new Separator());
                foreach (var s in new[] { "Livre", "Ocupada", "Reservada", "Pendente" })
                {
                    var item = new MenuItem { Header = s };
                    item.Click += async (_, __) => await MudarStatus(table, s);
                    menu.Items.Add(item);
                }
                menu.IsOpen = true;
            }
        }

        private void EditarMesa(RestaurantTable table)
        {
            var window = CriarJanelaCadastro(table.Number, table.Capacity.ToString(), table.Notes ?? "", async (num, capStr, notas) =>
            {
                table.Number = num;
                if (int.TryParse(capStr, out int c)) table.Capacity = c;
                table.Notes = notas;
                await _db.SaveChangesAsync();
                DrawTables();
            });
            window.ShowDialog();
        }

        private async Task ExcluirMesa(RestaurantTable table)
        {
            if (MessageBox.Show($"Excluir permanentemente a mesa {table.Number}?", "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _db.Tables.Remove(table);
            await _db.SaveChangesAsync();
            await CarregarMesas();
        }

        private async Task MudarStatus(RestaurantTable table, string statusTexto)
        {
            table.Status = statusTexto switch
            {
                "Livre" => TableStatus.Livre,
                "Ocupada" => TableStatus.Ocupada,
                "Reservada" => TableStatus.Reservada,
                "Pendente" => TableStatus.Pendente,
                _ => table.Status
            };
            await _db.SaveChangesAsync();
            DrawTables();
            AppEvents.RaiseSpecificTableStatusChanged(table.Id);
        }

        private void TablesCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (chkModoEdicao.IsChecked != true) return;
            var pos = e.GetPosition(TablesCanvas);
            var hit = TablesCanvas.InputHitTest(pos) as FrameworkElement;
            if (hit?.Tag is RestaurantTable)
            {
                _draggingElement = hit;
                _dragStartPosition = pos;
                _isDragging = true;
            }
        }

        private void TablesCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _draggingElement == null) return;
            var pos = e.GetPosition(TablesCanvas);
            Canvas.SetLeft(_draggingElement, Canvas.GetLeft(_draggingElement) + pos.X - _dragStartPosition.X);
            Canvas.SetTop(_draggingElement, Canvas.GetTop(_draggingElement) + pos.Y - _dragStartPosition.Y);
            _dragStartPosition = pos;
        }

        private async void TablesCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && _draggingElement?.Tag is RestaurantTable table)
            {
                table.PositionX = Canvas.GetLeft(_draggingElement);
                table.PositionY = Canvas.GetTop(_draggingElement);
                await _db.SaveChangesAsync();
                AppEvents.RaiseSpecificTableStatusChanged(table.Id);
            }
            _isDragging = false;
            _draggingElement = null;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            var scaleFactor = e.Delta > 0 ? 1.1 : 0.9;
            _scaleTransform.ScaleX *= scaleFactor;
            _scaleTransform.ScaleY *= scaleFactor;
        }

        private async void BtnOrganizar_Click(object sender, RoutedEventArgs e)
        {
            const int cols = 4, spacing = 150, x = 50, y = 50;
            int count = 0;
            foreach (var table in _tables)
            {
                table.PositionX = x + (count % cols) * spacing;
                table.PositionY = y + (count / cols) * spacing;
                count++;
            }
            await _db.SaveChangesAsync();
            DrawTables();
        }

        private void TablesCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (chkModoEdicao.IsChecked != true) return;
            var pos = e.GetPosition(TablesCanvas);
            var window = CriarJanelaCadastro("", "", "", async (num, capStr, notas) =>
            {
                if (string.IsNullOrWhiteSpace(num) || !int.TryParse(capStr, out int cap))
                {
                    MessageBox.Show("Número e capacidade são obrigatórios!", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _db.Tables.Add(new RestaurantTable { Number = num, Capacity = cap, Notes = notas, PositionX = pos.X, PositionY = pos.Y, Status = TableStatus.Livre, Selecionado = false });
                await _db.SaveChangesAsync();
                await CarregarMesas();
            });
            window.ShowDialog();
        }

        private Window CriarJanelaCadastro(string numAtual, string capAtual, string notasAtual, Action<string, string, string> onSave)
        {
            var window = new Window { Title = string.IsNullOrEmpty(numAtual) ? "Cadastrar Mesa" : "Editar Mesa", Width = 350, Height = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this) };
            var stack = new StackPanel { Margin = new Thickness(20) };
            stack.Children.Add(new TextBlock { Text = "Número da Mesa:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            stack.Children.Add(new TextBox { Text = numAtual, Margin = new Thickness(0, 0, 0, 15) });
            var txtNum = (TextBox)stack.Children[^1];
            stack.Children.Add(new TextBlock { Text = "Capacidade:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            stack.Children.Add(new TextBox { Text = capAtual, Margin = new Thickness(0, 0, 0, 15) });
            var txtCap = (TextBox)stack.Children[^1];
            stack.Children.Add(new TextBlock { Text = "Notas:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            stack.Children.Add(new TextBox { Text = notasAtual, Height = 100, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 15) });
            var txtNotas = (TextBox)stack.Children[^1];
            var btnSave = new Button { Content = "Salvar", Height = 40, Margin = new Thickness(0, 20, 0, 0) };
            btnSave.Click += (_, __) => { onSave(txtNum.Text.Trim(), txtCap.Text.Trim(), txtNotas.Text.Trim()); window.Close(); };
            stack.Children.Add(btnSave);
            window.Content = new ScrollViewer { Content = stack };
            return window;
        }

        public event EventHandler<RestaurantTable>? OnOpenTableCommand;
    }
}
