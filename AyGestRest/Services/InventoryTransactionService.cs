using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    /// <summary>
    /// Núcleo transaccional de stock. Todas as operações de venda/devolução podem participar
    /// de uma transação externa, evitando que pagamento, stock e factura fiquem em estados diferentes.
    ///
    /// As baixas usam UPDATE condicional no banco (Stock >= quantidade) para impedir que dois
    /// terminais consumam simultaneamente mais stock do que o disponível.
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
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
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
                    var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE Products
                        SET Stock = Stock - {quantity}
                        WHERE Id = {product.Id}
                          AND TrackInventory = 1
                          AND Stock >= {quantity};", cancellationToken);

                    if (updated != 1)
                        throw new InvalidOperationException($"Stock insuficiente ou alterado simultaneamente para {product.Name}.");

                    var newStock = product.Stock - quantity;
                    var previous = product.Stock;
                    product.Stock = newStock;
                    AddInventoryMovement(product.Id, previous, newStock, item.Quantity, orderId, responsibleUserId, "Saída", "Venda");
                    AddStockMovement(product.Id, (int)Math.Ceiling(quantity), newStock, "Saída");
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
                        if (required <= 0) continue;

                        var ingredient = recipeItem.Ingredient;
                        var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                            UPDATE Ingredients
                            SET Stock = Stock - {required},
                                Quantity = CAST(MAX(0, Stock - {required}) AS INTEGER)
                            WHERE Id = {ingredient.Id}
                              AND Stock >= {required};", cancellationToken);

                        if (updated != 1)
                            throw new InvalidOperationException($"Ingrediente insuficiente ou alterado simultaneamente: {ingredient.Name}. Necessário {required:N2} {ingredient.Unit}, disponível {ingredient.Stock:N2}.");

                        var previousIngredientStock = ingredient.Stock;
                        var newIngredientStock = previousIngredientStock - required;
                        ingredient.Stock = newIngredientStock;
                        ingredient.Quantity = Math.Max(0, (int)Math.Floor(newIngredientStock));

                        _db.InventoryMovements.Add(new InventoryMovement
                        {
                            ProductId = product.Id,
                            MovementType = "Saída",
                            Quantity = (int)Math.Ceiling(Math.Abs(required)),
                            PreviousStock = previousIngredientStock,
                            NewStock = newIngredientStock,
                            Reason = "Venda - Ingrediente",
                            Notes = $"Pedido #{orderId}; Ingrediente #{ingredient.Id}",
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
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
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
                    var recipe = await _db.ProductIngredients
                        .Include(pi => pi.Ingredient)
                        .Where(pi => pi.ProductId == product.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var recipeItem in recipe)
                    {
                        if (recipeItem.Ingredient == null) continue;

                        var restored = recipeItem.QuantityUsed * quantity;
                        if (restored <= 0) continue;

                        var ingredient = recipeItem.Ingredient;
                        ingredient.Stock += restored;
                        ingredient.Quantity = Math.Max(0, (int)Math.Floor(ingredient.Stock));

                        _db.InventoryMovements.Add(new InventoryMovement
                        {
                            ProductId = product.Id,
                            MovementType = "Entrada",
                            Quantity = (int)Math.Ceiling(restored),
                            PreviousStock = ingredient.Stock - restored,
                            NewStock = ingredient.Stock,
                            Reason = "Devolução - Ingrediente",
                            Notes = $"Pedido #{orderId}; Ingrediente #{ingredient.Id}",
                            UserId = responsibleUserId,
                            CreatedAt = DateTime.Now
                        });
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
