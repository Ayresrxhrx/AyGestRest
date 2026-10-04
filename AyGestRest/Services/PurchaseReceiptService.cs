using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    /// <summary>
    /// Recepção de compras: transforma uma compra recebida em stock real numa única transacção.
    /// Uma compra recebida duas vezes é rejeitada para evitar duplicação de stock.
    /// </summary>
    public sealed class PurchaseReceiptService
    {
        private readonly AyGestRestContext _db;

        public PurchaseReceiptService(AyGestRestContext db) => _db = db;

        public async Task ReceiveAsync(int purchaseOrderId, int userId, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var purchase = await _db.PurchaseOrders
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken)
                    ?? throw new InvalidOperationException("Compra não encontrada.");

                if (string.Equals(purchase.Status, "Recebida", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"A compra {purchase.PurchaseNumber} já foi recebida.");

                if (string.Equals(purchase.Status, "Cancelada", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Não é possível receber uma compra cancelada.");

                if (purchase.Items == null || purchase.Items.Count == 0)
                    throw new InvalidOperationException("A compra não possui itens para receber.");

                foreach (var item in purchase.Items)
                {
                    if (item.Quantity <= 0)
                        throw new InvalidOperationException($"Quantidade inválida no item {item.Id}.");
                    if (item.UnitPrice < 0)
                        throw new InvalidOperationException($"Preço inválido no item {item.Id}.");

                    var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                        ?? throw new InvalidOperationException($"Produto {item.ProductId} não encontrado.");

                    var previousStock = product.Stock;
                    product.Stock += item.Quantity;
                    product.TrackInventory = true;

                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = product.Id,
                        MovementType = "Entrada",
                        Quantity = item.Quantity,
                        PreviousStock = previousStock,
                        NewStock = product.Stock,
                        Reason = "Compra",
                        Notes = $"Compra #{purchase.Id} - {purchase.PurchaseNumber}",
                        UserId = userId,
                        CreatedAt = DateTime.Now
                    });

                    _db.StockMovements.Add(new StockMovement
                    {
                        ProductId = product.Id,
                        Date = DateTime.Now,
                        Type = "Entrada",
                        Quantity = item.Quantity,
                        BalanceAfter = product.Stock
                    });
                }

                purchase.Status = "Recebida";
                purchase.ReceivedAt = DateTime.Now;

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }
}
