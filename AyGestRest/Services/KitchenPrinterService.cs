using AyGestRest.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AyGestRest.Services
{
    public static class KitchenPrinterService
    {
        public static async Task<KitchenPrintResult> TryPrintOrderAsync(Order order, RestaurantConfig config)
        {
            if (order == null)
                return KitchenPrintResult.Skipped("Pedido inválido.");

            if (config?.PrintOrderToKitchen != true)
                return KitchenPrintResult.Skipped("Impressão de cozinha desactivada.");

            var printerName = config.KitchenPrinterName?.Trim();
            if (string.IsNullOrWhiteSpace(printerName))
                return KitchenPrintResult.Skipped("Nenhuma impressora de cozinha configurada.");

            if (!PrinterSettings.InstalledPrinters.Cast<string>()
                    .Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase)))
            {
                return KitchenPrintResult.Skipped($"A impressora de cozinha '{printerName}' não está instalada/disponível.");
            }

            var kitchenItems = order.Items
                .Where(i => i.Product?.PrintToKitchen == true || i.Product == null)
                .ToList();

            if (kitchenItems.Count == 0)
                return KitchenPrintResult.Skipped("O pedido não possui itens destinados à cozinha.");

            try
            {
                await Task.Run(() => Print(printerName, order, kitchenItems, config.CopiesCount));
                return KitchenPrintResult.Success(printerName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao imprimir pedido Mobile na cozinha: {ex}");
                return KitchenPrintResult.Skipped($"Falha na impressão: {ex.Message}");
            }
        }

        private static void Print(string printerName, Order order, List<OrderItem> items, int copies)
        {
            using var document = new PrintDocument
            {
                PrinterSettings = new PrinterSettings
                {
                    PrinterName = printerName,
                    Copies = (short)Math.Clamp(copies, 1, 10)
                },
                DocumentName = $"AyGest Rest - Pedido #{order.Id}"
            };

            document.PrintPage += (_, e) =>
            {
                using var titleFont = new Font("Consolas", 13, FontStyle.Bold);
                using var bodyFont = new Font("Consolas", 10, FontStyle.Regular);
                using var boldFont = new Font("Consolas", 10, FontStyle.Bold);

                float y = 10;
                float width = e.PageBounds.Width - 20;

                e.Graphics.DrawString("AYGEST REST", titleFont, Brushes.Black, 10, y);
                y += 28;
                e.Graphics.DrawString("PEDIDO PARA COZINHA", boldFont, Brushes.Black, 10, y);
                y += 22;
                e.Graphics.DrawString($"Pedido: #{order.Id}", bodyFont, Brushes.Black, 10, y);
                y += 18;
                e.Graphics.DrawString($"Mesa: {order.Table?.Number ?? "—"}", bodyFont, Brushes.Black, 10, y);
                y += 18;
                e.Graphics.DrawString($"Data: {DateTime.Now:dd/MM/yyyy HH:mm:ss}", bodyFont, Brushes.Black, 10, y);
                y += 24;
                e.Graphics.DrawLine(Pens.Black, 10, y, width, y);
                y += 10;

                foreach (var item in items)
                {
                    var name = item.Name ?? item.Product?.Name ?? $"Produto #{item.ProductId}";
                    e.Graphics.DrawString($"{item.Quantity}x {name}", boldFont, Brushes.Black, 10, y);
                    y += 19;

                    if (!string.IsNullOrWhiteSpace(item.Notes))
                    {
                        var notes = "Obs: " + item.Notes.Trim();
                        foreach (var line in Wrap(notes, 52))
                        {
                            e.Graphics.DrawString(line, bodyFont, Brushes.Black, 20, y);
                            y += 17;
                        }
                    }

                    y += 5;
                }

                e.Graphics.DrawLine(Pens.Black, 10, y, width, y);
                y += 10;
                e.Graphics.DrawString("*** NÃO É RECIBO DE PAGAMENTO ***", bodyFont, Brushes.Black, 10, y);
            };

            document.Print();
        }

        private static IEnumerable<string> Wrap(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                yield break;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = new StringBuilder();

            foreach (var word in words)
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > maxLength)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                if (line.Length > 0)
                    line.Append(' ');

                line.Append(word);
            }

            if (line.Length > 0)
                yield return line.ToString();
        }
    }

    public sealed record KitchenPrintResult(bool Printed, string Message, string? PrinterName)
    {
        public static KitchenPrintResult Success(string printerName) => new(true, "Pedido enviado para a impressora da cozinha.", printerName);
        public static KitchenPrintResult Skipped(string message) => new(false, message, null);
    }
}
