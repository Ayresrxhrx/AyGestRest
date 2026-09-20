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
    // Classe estática global para eventos de mesas (pode colocar num ficheiro separado se quiseres, ex: AppEvents.cs)
  

    public partial class TablesControl : UserControl
    {
        private readonly AyGestRestContext _db = new();
        private List<RestaurantTable> _tables = new();
        private FrameworkElement? _draggingElement;
        private Point _dragStartPosition;
        private bool _isDragging = false;
        private readonly ScaleTransform _scaleTransform = new(1, 1);

        public TablesControl()
        {
            InitializeComponent();
            TablesCanvas.LayoutTransform = _scaleTransform;
            Loaded += TablesControl_Loaded;
        }
        private void TxtValorRecebido_GotFocus(object sender, RoutedEventArgs e)
        {
            // Seleciona todo o texto quando o campo recebe foco
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }
        private async void TablesControl_Loaded(object sender, RoutedEventArgs e)
        {
            await CarregarMesas();
        }

        private async Task CarregarMesas()
        {
            _tables = await _db.Tables.ToListAsync();
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
                    Cursor = chkModoEdicao.IsChecked == true ? Cursors.Hand : Cursors.Arrow,
                    Tag = table
                };

                var stack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                stack.Children.Add(new TextBlock
                {
                    Text = table.Number,
                    Foreground = Brushes.White,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                stack.Children.Add(new TextBlock
                {
                    Text = $"({table.Capacity} pessoas)",
                    Foreground = Brushes.WhiteSmoke,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                border.Child = stack;

                var tooltipStack = new StackPanel { Margin = new Thickness(10) };
                tooltipStack.Children.Add(new TextBlock { Text = $"Mesa: {table.Number}", FontWeight = FontWeights.Bold });
                tooltipStack.Children.Add(new TextBlock { Text = $"Capacidade: {table.Capacity}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Status: {table.Status}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Notas: {table.Notes ?? "Nenhuma"}", TextWrapping = TextWrapping.Wrap });
                border.ToolTip = new ToolTip { Content = tooltipStack };

                border.MouseLeftButtonUp += Mesa_MouseLeftButtonUp;
                border.MouseRightButtonUp += Mesa_MouseRightButtonUp;

                Canvas.SetLeft(border, table.PositionX);
                Canvas.SetTop(border, table.PositionY);
                TablesCanvas.Children.Add(border);
            }
        }

        private Brush GetStatusBrush(TableStatus status)
        {
            return status switch
            {
                TableStatus.Livre => Brushes.Gray,
                TableStatus.Ocupada => Brushes.Green,
                TableStatus.Reservada => Brushes.Orange,
                TableStatus.Pendente => Brushes.Yellow,
                _ => Brushes.Gray
            };
        }

        private void Mesa_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is RestaurantTable table)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    table.Selecionado = !table.Selecionado;
                }
                else
                {
                    foreach (var t in _tables) t.Selecionado = false;
                    table.Selecionado = true;
                }
                DrawTables();

                OnOpenTableCommand?.Invoke(this, table);
            }
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

                var statuses = new[] { "Livre", "Ocupada", "Reservada", "Pendente" };
                foreach (var s in statuses)
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
            if (MessageBox.Show($"Excluir permanentemente a mesa {table.Number}?", "Confirmação", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _db.Tables.Remove(table);
                await _db.SaveChangesAsync();
                await CarregarMesas();
            }
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

            // Notifica todas as telas (POSControl, etc.)
            AppEvents.RaiseSpecificTableStatusChanged(table.Id);
        }

        // Drag & Drop
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
            double dx = pos.X - _dragStartPosition.X;
            double dy = pos.Y - _dragStartPosition.Y;

            Canvas.SetLeft(_draggingElement, Canvas.GetLeft(_draggingElement) + dx);
            Canvas.SetTop(_draggingElement, Canvas.GetTop(_draggingElement) + dy);

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

        // Zoom
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;

            double scaleFactor = e.Delta > 0 ? 1.1 : 0.9;
            _scaleTransform.ScaleX *= scaleFactor;
            _scaleTransform.ScaleY *= scaleFactor;
        }

        // Organizar automaticamente
        private async void BtnOrganizar_Click(object sender, RoutedEventArgs e)
        {
            const int cols = 4;
            const int spacing = 150;
            int x = 50, y = 50;
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

        // Criação de nova mesa
        private void TablesCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (chkModoEdicao.IsChecked != true) return;

            Point pos = e.GetPosition(TablesCanvas);

            var window = CriarJanelaCadastro("", "", "", async (num, capStr, notas) =>
            {
                if (string.IsNullOrWhiteSpace(num) || !int.TryParse(capStr, out int cap))
                {
                    MessageBox.Show("Número e capacidade são obrigatórios!", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newTable = new RestaurantTable
                {
                    Number = num,
                    Capacity = cap,
                    Notes = notas,
                    PositionX = pos.X,
                    PositionY = pos.Y,
                    Status = TableStatus.Livre,
                    Selecionado = false
                };

                _db.Tables.Add(newTable);
                await _db.SaveChangesAsync();
                await CarregarMesas();
            });

            window.ShowDialog();
        }

        private Window CriarJanelaCadastro(string numAtual, string capAtual, string notasAtual, Action<string, string, string> onSave)
        {
            var window = new Window
            {
                Title = string.IsNullOrEmpty(numAtual) ? "Cadastrar Mesa" : "Editar Mesa",
                Width = 350,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            var stack = new StackPanel { Margin = new Thickness(20) };

            stack.Children.Add(new TextBlock { Text = "Número da Mesa:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            var txtNum = new TextBox { Text = numAtual, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(txtNum);

            stack.Children.Add(new TextBlock { Text = "Capacidade:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            var txtCap = new TextBox { Text = capAtual, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(txtCap);

            stack.Children.Add(new TextBlock { Text = "Notas:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            var txtNotas = new TextBox { Text = notasAtual, Height = 100, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 15) };
            stack.Children.Add(txtNotas);

            var btnSave = new Button { Content = "Salvar", Height = 40, Margin = new Thickness(0, 20, 0, 0) };
            btnSave.Click += (_, __) =>
            {
                onSave(txtNum.Text.Trim(), txtCap.Text.Trim(), txtNotas.Text.Trim());
                window.Close();
            };
            stack.Children.Add(btnSave);

            window.Content = new ScrollViewer { Content = stack };
            return window;
        }

        // Evento para abrir comanda no POS
        public event EventHandler<RestaurantTable>? OnOpenTableCommand;
    }
}