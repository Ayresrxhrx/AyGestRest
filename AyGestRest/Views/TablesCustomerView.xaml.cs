using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AyGestRest.Views
{
    public partial class TablesCustomerView : UserControl
    {
        private readonly AyGestRestContext _db = new();
        private List<RestaurantTable> _tables = new();
        private readonly ScaleTransform _scaleTransform = new(1, 1);

        public TablesCustomerView()
        {
            InitializeComponent();
            TablesCanvas.LayoutTransform = _scaleTransform;
            Loaded += TablesCustomerView_Loaded;
        }

        private async void TablesCustomerView_Loaded(object sender, RoutedEventArgs e)
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
                    BorderBrush = Brushes.WhiteSmoke,
                    BorderThickness = new Thickness(2),
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

                // Tooltip com informações da mesa
                var tooltipStack = new StackPanel { Margin = new Thickness(10) };
                tooltipStack.Children.Add(new TextBlock { Text = $"Mesa: {table.Number}", FontWeight = FontWeights.Bold });
                tooltipStack.Children.Add(new TextBlock { Text = $"Capacidade: {table.Capacity}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Status: {table.Status}" });
                tooltipStack.Children.Add(new TextBlock { Text = $"Notas: {table.Notes ?? "Nenhuma"}", TextWrapping = TextWrapping.Wrap });
                border.ToolTip = new ToolTip { Content = tooltipStack };

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
    }
}
