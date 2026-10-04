using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    public sealed class InventoryTransactionService
    {
        private readonly AyGestRestContext _db;

        public InventoryTransactionService(AyGestRestContext db) => _db = db;

        public async Task DeductForSaleAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var item in order.Items)
                {
                    var product = await _db.Products
                        .Include(p => p.ProductIngredients)
                        .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);

                    if (product == null || !product.Active)
                        throw new InvalidOperationException($"O produto do item {item.Id} não está disponível.");

                    var quantity = Convert.ToDecimal(item.Quantity);
                    if (!product.IsComposite && product.TrackInventory)
                    {
                        if (product.Stock < quantity)
                            throw new InvalidOperationException($"Stock insuficiente para {product.Name}. Disponível: {product.Stock:N2}.");

                        var previous = product.Stock;
                        product.Stock -= quantity;
                        _db.InventoryMovements.Add(new InventoryMovement
                        {
                            ProductId = product.Id,
                            MovementType = "Saída",
                            Quantity = (int)Math.Ceiling(quantity),
                            PreviousStock = previous,
                            NewStock = product.Stock,
                            Reason = "Venda",
                            Notes = $"Pedido #{order.Id}",
                            UserId = userId ?? order.UserId,
                            CreatedAt = DateTime.Now
                        });
                    }

                    if (product.IsComposite)
                    {
                        var recipe = await _db.ProductIngredients
                            .Include(pi => pi.Ingredient)
                            .Where(pi => pi.ProductId == product.Id)
                            .ToListAsync(cancellationToken);

                        foreach (var recipeItem in recipe)
                        {
                            if (recipeItem.Ingredient == null)
                                throw new InvalidOperationException($"Receita inválida para {product.Name}: ingrediente inexistente.");

                            var required = recipeItem.QuantityUsed * quantity;
                            if (recipeItem.Ingredient.Stock < required)
                                throw new InvalidOperationException($"Ingrediente insuficiente: {recipeItem.Ingredient.Name}. Necessário {required:N2} {recipeItem.Ingredient.Unit}, disponível {recipeItem.Ingredient.Stock:N2}.");

                            recipeItem.Ingredient.Stock -= required;
                            recipeItem.Ingredient.Quantity = Math.Max(0, (int)Math.Floor(recipeItem.Ingredient.Stock));
                        }
                    }
                }

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task RestoreForSaleAsync(int orderId, int? userId = null, CancellationToken cancellationToken = default)
        {
            var order = await _db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var item in order.Items)
                {
                    var product = await _db.Products
                        .Include(p => p.ProductIngredients)
                        .FirstOrDefaultAsync(p => p.Id == item.ProductId, cancellationToken);
                    if (product == null) continue;

                    var quantity = Convert.ToDecimal(item.Quantity);
                    if (!product.IsComposite && product.TrackInventory)
                    {
                        var previous = product.Stock;
                        product.Stock += quantity;
                        _db.InventoryMovements.Add(new InventoryMovement
                        {
                            ProductId = product.Id,
                            MovementType = "Entrada",
                            Quantity = (int)Math.Ceiling(quantity),
                            PreviousStock = previous,
                            NewStock = product.Stock,
                            Reason = "Devolução",
                            Notes = $"Pedido #{order.Id}",
                            UserId = userId ?? order.UserId,
                            CreatedAt = DateTime.Now
                        });
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
