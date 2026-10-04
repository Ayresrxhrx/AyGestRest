using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    /// <summary>
    /// Mantém o stock sincronizado com vendas e devoluções.
    /// As operações podem ser executadas isoladamente ou dentro de uma transação externa,
    /// permitindo que pagamento + stock sejam confirmados atomicamente.
    /// </summary>
    public sealed class InventoryTransactionService
    {
        private readonly AyGestRestContext _db;

        public InventoryTransactionService(AyGestRestContext db) => _db = db;

        public async Task DeductForSaleAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await DeductForSaleInCurrentTransactionAsync(orderId, userId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task DeductForSaleInCurrentTransactionAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            if (await _db.InventoryMovements.AnyAsync(m => m.Reason == "Venda" && m.Notes == $"Pedido #{orderId}", cancellationToken))
                throw new InvalidOperationException($"O stock do pedido #{orderId} já foi processado.");

            int responsibleUserId = userId ?? order.UserId;

            foreach (var item in order.Items)
            {
                if (item.Quantity <= 0) continue;

                var product = await _db.Products
                    .Include(p => p.ProductIngredients)
                        .ThenInclude(pi => pi.Ingredient)
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken)
                    ?? throw new InvalidOperationException($"Produto do item {item.Id} não encontrado.");

                if (!product.Active)
                    throw new InvalidOperationException($"O produto {product.Name} está inactivo.");

                var quantity = Convert.ToDecimal(item.Quantity);

                if (!product.IsComposite && product.TrackInventory)
                {
                    if (product.Stock < quantity)
                        throw new InvalidOperationException($"Stock insuficiente para {product.Name}. Disponível: {product.Stock:N2}.");

                    var previous = product.Stock;
                    product.Stock -= quantity;
                    AddInventoryMovement(product.Id, previous, product.Stock, item.Quantity, orderId, responsibleUserId, "Saída", "Venda");
                    AddStockMovement(product.Id, (int)Math.Ceiling(quantity), product.Stock, "Saída");
                }

                if (product.IsComposite)
                {
                    var recipe = product.ProductIngredients?.ToList() ?? new List<ProductIngredient>();
                    if (recipe.Count == 0)
                        throw new InvalidOperationException($"O produto composto {product.Name} não possui receita.");

                    foreach (var recipeItem in recipe)
                    {
                        if (recipeItem.Ingredient == null)
                            throw new InvalidOperationException($"Receita inválida para {product.Name}: ingrediente inexistente.");

                        var required = recipeItem.QuantityUsed * quantity;
                        if (recipeItem.Ingredient.Stock < required)
                            throw new InvalidOperationException($"Ingrediente insuficiente: {recipeItem.Ingredient.Name}. Necessário {required:N2} {recipeItem.Ingredient.Unit}, disponível {recipeItem.Ingredient.Stock:N2}.");

                        var previousIngredientStock = recipeItem.Ingredient.Stock;
                        recipeItem.Ingredient.Stock -= required;
                        recipeItem.Ingredient.Quantity = Math.Max(0, (int)Math.Floor(recipeItem.Ingredient.Stock));

                        _db.InventoryMovements.Add(new InventoryMovement
                        {
                            ProductId = product.Id,
                            MovementType = "Saída",
                            Quantity = (int)Math.Ceiling(Math.Abs(required)),
                            PreviousStock = previousIngredientStock,
                            NewStock = recipeItem.Ingredient.Stock,
                            Reason = "Venda - Ingrediente",
                            Notes = $"Pedido #{orderId}; Ingrediente #{recipeItem.IngredientId}",
                            UserId = responsibleUserId,
                            CreatedAt = DateTime.Now
                        });
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task RestoreForSaleAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await RestoreForSaleInCurrentTransactionAsync(orderId, userId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task RestoreForSaleInCurrentTransactionAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            if (!await _db.InventoryMovements.AnyAsync(m => m.Reason == "Venda" && m.Notes == $"Pedido #{orderId}", cancellationToken))
                throw new InvalidOperationException($"O stock do pedido #{orderId} não foi baixado e não pode ser restaurado.");

            if (await _db.InventoryMovements.AnyAsync(m => m.Reason == "Devolução" && m.Notes == $"Pedido #{orderId}", cancellationToken))
                throw new InvalidOperationException($"O stock do pedido #{orderId} já foi restaurado.");

            int responsibleUserId = userId ?? order.UserId;

            foreach (var item in order.Items)
            {
                if (item.Quantity <= 0) continue;
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);
                if (product == null) continue;

                var quantity = Convert.ToDecimal(item.Quantity);
                if (!product.IsComposite && product.TrackInventory)
                {
                    var previous = product.Stock;
                    product.Stock += quantity;
                    AddInventoryMovement(product.Id, previous, product.Stock, item.Quantity, orderId, responsibleUserId, "Entrada", "Devolução");
                    AddStockMovement(product.Id, (int)Math.Ceiling(quantity), product.Stock, "Entrada");
                }

                if (product.IsComposite)
                {
                    var recipe = await _db.ProductIngredients.Include(pi => pi.Ingredient).Where(pi => pi.ProductId == product.Id).ToListAsync(cancellationToken);
                    foreach (var recipeItem in recipe)
                    {
                        if (recipeItem.Ingredient == null) continue;
                        var restored = recipeItem.QuantityUsed * quantity;
                        recipeItem.Ingredient.Stock += restored;
                        recipeItem.Ingredient.Quantity = Math.Max(0, (int)Math.Floor(recipeItem.Ingredient.Stock));
                    }
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private void AddInventoryMovement(int productId, decimal previous, decimal next, decimal quantity, int orderId, int userId, string type, string reason)
        {
            _db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = productId,
                MovementType = type,
                Quantity = (int)Math.Ceiling(Math.Abs(quantity)),
                PreviousStock = previous,
                NewStock = next,
                Reason = reason,
                Notes = $"Pedido #{orderId}",
                UserId = userId,
                CreatedAt = DateTime.Now
            });
        }

        private void AddStockMovement(int productId, int quantity, decimal balanceAfter, string type)
        {
            _db.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                Date = DateTime.Now,
                Type = type,
                Quantity = quantity,
                BalanceAfter = balanceAfter
            });
        }
    }
}
