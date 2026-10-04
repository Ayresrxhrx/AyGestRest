using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    public sealed class PurchaseService
    {
        private readonly AyGestRestContext _db;

        public PurchaseService(AyGestRestContext db) => _db = db;

        public async Task ReceiveAsync(int purchaseOrderId, int? userId, CancellationToken cancellationToken = default)
        {
            var purchase = await _db.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken)
                ?? throw new InvalidOperationException("Compra não encontrada.");

            if (string.Equals(purchase.Status, "Recebida", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Esta compra já foi recebida.");
            if (string.Equals(purchase.Status, "Cancelada", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Não é possível receber uma compra cancelada.");

            var effectiveUserId = userId ?? await _db.Users.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync(cancellationToken);
            if (!effectiveUserId.HasValue) throw new InvalidOperationException("Não existe utilizador activo para registar a entrada de stock.");

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var item in purchase.Items)
                {
                    if (item.Quantity <= 0 || item.UnitPrice < 0)
                        throw new InvalidOperationException("Existe um item de compra inválido.");

                    var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                        ?? throw new InvalidOperationException($"Produto #{item.ProductId} não encontrado.");

                    var oldStock = product.Stock;
                    product.Stock += item.Quantity;

                    _db.InventoryMovements.Add(new InventoryMovement
                    {
                        ProductId = product.Id,
                        MovementType = "Entrada",
                        Quantity = item.Quantity,
                        PreviousStock = oldStock,
                        NewStock = product.Stock,
                        Reason = "Compra recebida",
                        Notes = purchase.PurchaseNumber,
                        UserId = effectiveUserId.Value,
                        CreatedAt = DateTime.Now
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

        public async Task CancelAsync(int purchaseOrderId, string reason, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Indique o motivo do cancelamento.", nameof(reason));
            var purchase = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken)
                ?? throw new InvalidOperationException("Compra não encontrada.");
            if (string.Equals(purchase.Status, "Recebida", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Uma compra já recebida não pode ser cancelada.");

            purchase.Status = "Cancelada";
            purchase.Notes = string.IsNullOrWhiteSpace(purchase.Notes) ? reason.Trim() : $"{purchase.Notes}\nCancelamento: {reason.Trim()}";
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
