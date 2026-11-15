using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    /// <summary>
    /// Reserva stock para pedidos ainda abertos. Os movimentos funcionam como livro-razão,
    /// permitindo actualizar reservas sem voltar a descontar quantidades já reservadas.
    /// </summary>
    public sealed class OrderStockReservationService
    {
        private const string ReserveReason = "Reserva Pedido";
        private const string ReleaseReason = "Liberação Reserva";
        private readonly AyGestRestContext _db;

        public OrderStockReservationService(AyGestRestContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public void ReserveForOrderInCurrentTransaction(int orderId)
        {
            var order = _db.Orders
                .Include(o => o.Items)
                .FirstOrDefault(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Pedido não encontrado para reservar stock.");

            if (order.IsPaid || order.IsClosed || order.Status == OrderStatus.Fechado)
                throw new InvalidOperationException("Não é possível reservar stock para um pedido já fechado.");

            var targets = BuildTargets(order);
            var current = ReadCurrentReservations(orderId);
            var changes = new List<(string Key, int ProductId, decimal Delta)>();

            foreach (var key in current.Keys.Union(targets.Keys).ToList())
            {
                var reserved = current.TryGetValue(key, out var value) ? value.Quantity : 0m;
                var desired = targets.TryGetValue(key, out var target) ? target.Quantity : 0m;
                var delta = decimal.Round(desired - reserved, 4, MidpointRounding.AwayFromZero);
                if (delta > 0m)
                {
                    // Valida todas as necessidades antes de alterar qualquer saldo.
                    var available = ReadStock(key);
                    if (available < delta)
                        throw new InvalidOperationException(
                            $"Stock insuficiente para reservar {DescribeResource(key)}. Necessário: {delta:N2}; disponível: {available:N2}.");
                    changes.Add((key, target.ProductId, delta));
                }
                else if (delta < 0m)
                {
                    var productId = targets.TryGetValue(key, out var targetForRelease)
                        ? targetForRelease.ProductId
                        : current[key].ProductId;
                    changes.Add((key, productId, delta));
                }
            }

            foreach (var change in changes)
                ApplyStockChange(order, change.Key, change.ProductId, Math.Abs(change.Delta), reserve: change.Delta > 0m);
        }

        public void ReleaseForOrderInCurrentTransaction(int orderId)
        {
            var order = _db.Orders.FirstOrDefault(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Pedido não encontrado para libertar a reserva.");

            foreach (var entry in ReadCurrentReservations(orderId).Where(x => x.Value.Quantity > 0m))
                ApplyStockChange(order, entry.Key, entry.Value.ProductId, entry.Value.Quantity, reserve: false);
        }

        public bool HasActiveReservation(int orderId) =>
            ReadCurrentReservations(orderId).Values.Any(x => x.Quantity > 0.0001m);

        public void MarkReservationConsumedForSaleInCurrentTransaction(int orderId)
        {
            var order = _db.Orders.FirstOrDefault(o => o.Id == orderId)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            var reservations = ReadCurrentReservations(orderId)
                .Where(x => x.Value.Quantity > 0.0001m)
                .ToList();

            if (reservations.Count == 0)
                throw new InvalidOperationException("A reserva de stock do pedido não está activa.");

            foreach (var entry in reservations)
            {
                var stock = ReadStock(entry.Key);
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = entry.Value.ProductId,
                    MovementType = "Consumo Reserva",
                    Quantity = (int)Math.Ceiling(entry.Value.Quantity),
                    PreviousStock = stock,
                    NewStock = stock,
                    Reason = "Venda",
                    Notes = $"Pedido #{orderId}",
                    UserId = order.UserId,
                    CreatedAt = DateTime.Now
                });
            }
        }

        private Dictionary<string, ReservationTarget> BuildTargets(Order order)
        {
            var targets = new Dictionary<string, ReservationTarget>();

            // SalvarOrdemAtual já carregou todas as linhas e actualizou o ChangeTracker.
            // Usar Local inclui linhas novas/alteradas e exclui as que serão removidas.
            var orderItems = _db.OrderItems.Local
                .Where(i => i.OrderId == order.Id && i.Quantity > 0 &&
                            _db.Entry(i).State != EntityState.Deleted)
                .ToList();

            foreach (var item in orderItems)
            {
                var product = _db.Products
                    .Include(p => p.ProductIngredients)
                    .ThenInclude(pi => pi.Ingredient)
                    .FirstOrDefault(p => p.Id == item.ProductId)
                    ?? throw new InvalidOperationException($"Produto do pedido não encontrado (ID {item.ProductId}).");

                if (!product.Active)
                    throw new InvalidOperationException($"O produto {product.Name} está inactivo.");

                if (product.IsComposite)
                {
                    var recipe = product.ProductIngredients?.ToList() ?? new List<ProductIngredient>();
                    if (recipe.Count == 0)
                        throw new InvalidOperationException($"O produto composto {product.Name} não possui receita.");

                    foreach (var recipeItem in recipe)
                    {
                        if (recipeItem.Ingredient == null)
                            throw new InvalidOperationException($"A receita de {product.Name} contém um ingrediente inexistente.");

                        var required = Convert.ToDecimal(recipeItem.QuantityUsed) * item.Quantity;
                        if (required <= 0m) continue;
                        AddTarget(targets, $"I:{recipeItem.IngredientId}", product.Id, required);
                    }
                }
                else if (product.TrackInventory)
                {
                    AddTarget(targets, $"P:{product.Id}", product.Id, item.Quantity);
                }
            }

            return targets;
        }

        private static void AddTarget(Dictionary<string, ReservationTarget> targets, string key, int productId, decimal quantity)
        {
            if (targets.TryGetValue(key, out var current))
                targets[key] = current with { Quantity = current.Quantity + quantity };
            else
                targets[key] = new ReservationTarget(productId, quantity);
        }

        private Dictionary<string, ReservationTarget> ReadCurrentReservations(int orderId)
        {
            var prefix = $"Pedido #{orderId}; Reserva=";
            var movements = _db.InventoryMovements
                .AsNoTracking()
                .Where(m => (m.Reason == ReserveReason || m.Reason == ReleaseReason) && m.Notes.StartsWith(prefix))
                .ToList();

            var result = new Dictionary<string, ReservationTarget>();
            foreach (var movement in movements)
            {
                var key = movement.Notes.Substring(prefix.Length).Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var change = movement.Reason == ReserveReason
                    ? movement.PreviousStock - movement.NewStock
                    : -(movement.NewStock - movement.PreviousStock);

                if (result.TryGetValue(key, out var existing))
                    result[key] = existing with { Quantity = existing.Quantity + change };
                else
                    result[key] = new ReservationTarget(movement.ProductId, change);
            }

            foreach (var key in result.Keys.ToList())
                if (result[key].Quantity < 0.0001m)
                    result[key] = result[key] with { Quantity = 0m };

            return result;
        }

        private void ApplyStockChange(Order order, string key, int productId, decimal quantity, bool reserve)
        {
            if (quantity <= 0m) return;

            var now = DateTime.Now;
            decimal previous;
            decimal next;
            var movementType = reserve ? "Saída" : "Entrada";
            var reason = reserve ? ReserveReason : ReleaseReason;

            if (key.StartsWith("P:", StringComparison.Ordinal))
            {
                var id = int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture);
                var affected = reserve
                    ? _db.Database.ExecuteSqlInterpolated($@"UPDATE Products SET Stock = Stock - {quantity}
                        WHERE Id = {id} AND TrackInventory = 1 AND Stock >= {quantity};")
                    : _db.Database.ExecuteSqlInterpolated($@"UPDATE Products SET Stock = Stock + {quantity}
                        WHERE Id = {id};");

                if (affected != 1)
                {
                    var name = _db.Products.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault() ?? $"produto #{id}";
                    throw new InvalidOperationException(reserve
                        ? $"Stock insuficiente para reservar {name}. A quantidade solicitada é {quantity:N2}."
                        : $"Não foi possível libertar o stock de {name}.");
                }

                next = _db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.Stock).First();
                previous = reserve ? next + quantity : next - quantity;

                // Sincroniza entidades já carregadas no contexto para que um SaveChanges
                // posterior não volte a escrever o stock antigo sobre o valor reservado.
                var trackedProduct = _db.ChangeTracker.Entries<Product>()
                    .FirstOrDefault(e => e.Entity.Id == id);
                if (trackedProduct != null)
                {
                    trackedProduct.Entity.Stock = next;
                    trackedProduct.Property(p => p.Stock).IsModified = false;
                }

                _db.StockMovements.Add(new StockMovement
                {
                    ProductId = id,
                    Date = now,
                    Type = movementType,
                    Quantity = (int)Math.Ceiling(quantity),
                    BalanceAfter = next
                });
            }
            else if (key.StartsWith("I:", StringComparison.Ordinal))
            {
                var id = int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture);
                var affected = reserve
                    ? _db.Database.ExecuteSqlInterpolated($@"UPDATE Ingredients
                        SET Stock = Stock - {quantity}, Quantity = CAST(MAX(0, Stock - {quantity}) AS INTEGER)
                        WHERE Id = {id} AND Stock >= {quantity};")
                    : _db.Database.ExecuteSqlInterpolated($@"UPDATE Ingredients
                        SET Stock = Stock + {quantity}, Quantity = CAST(MAX(0, Stock + {quantity}) AS INTEGER)
                        WHERE Id = {id};");

                if (affected != 1)
                {
                    var ingredient = _db.Ingredients.AsNoTracking().FirstOrDefault(i => i.Id == id);
                    throw new InvalidOperationException(reserve
                        ? $"Stock insuficiente do ingrediente {ingredient?.Name ?? ("#" + id)}. Necessário: {quantity:N2}."
                        : $"Não foi possível libertar o ingrediente #{id}.");
                }

                next = _db.Ingredients.AsNoTracking().Where(i => i.Id == id).Select(i => i.Stock).First();
                previous = reserve ? next + quantity : next - quantity;

                var trackedIngredient = _db.ChangeTracker.Entries<Ingredient>()
                    .FirstOrDefault(e => e.Entity.Id == id);
                if (trackedIngredient != null)
                {
                    trackedIngredient.Entity.Stock = next;
                    trackedIngredient.Property(i => i.Stock).IsModified = false;
                    trackedIngredient.Entity.Quantity = (int)Math.Floor(next);
                    trackedIngredient.Property(i => i.Quantity).IsModified = false;
                }
            }
            else
            {
                throw new InvalidOperationException($"Tipo de reserva desconhecido: {key}.");
            }

            _db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = productId,
                MovementType = movementType,
                Quantity = (int)Math.Ceiling(quantity),
                PreviousStock = previous,
                NewStock = next,
                Reason = reason,
                Notes = $"Pedido #{order.Id}; Reserva={key}",
                UserId = order.UserId,
                CreatedAt = now
            });
        }

        private string DescribeResource(string key)
        {
            if (key.StartsWith("P:", StringComparison.Ordinal))
                return _db.Products.AsNoTracking()
                    .Where(p => p.Id == int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture))
                    .Select(p => p.Name).FirstOrDefault() ?? key;

            return _db.Ingredients.AsNoTracking()
                .Where(i => i.Id == int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture))
                .Select(i => i.Name).FirstOrDefault() ?? key;
        }

        private decimal ReadStock(string key)
        {
            if (key.StartsWith("P:", StringComparison.Ordinal))
            {
                var id = int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture);
                return _db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.Stock).FirstOrDefault();
            }

            var ingredientId = int.Parse(key.Substring(2), System.Globalization.CultureInfo.InvariantCulture);
            return _db.Ingredients.AsNoTracking().Where(i => i.Id == ingredientId).Select(i => i.Stock).FirstOrDefault();
        }

        private sealed record ReservationTarget(int ProductId, decimal Quantity);
    }
}