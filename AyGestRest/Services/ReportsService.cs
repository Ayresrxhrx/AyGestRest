using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    public sealed class ReportsService
    {
        private readonly AyGestRestContext _db;

        public ReportsService(AyGestRestContext db) => _db = db;

        public async Task<DashboardSnapshot> GetDashboardAsync(DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
        {
            var start = (from ?? DateTime.Today).Date;
            var end = (to ?? DateTime.Today).Date.AddDays(1);

            var paidOrders = _db.Orders.AsNoTracking().Where(o => o.IsPaid && o.Data >= start && o.Data < end);
            var payments = _db.Payments.AsNoTracking().Where(p => p.PaymentDate >= start && p.PaymentDate < end);
            var sales = await paidOrders.Select(o => o.TotalAmount > 0 ? o.TotalAmount : o.Total).SumAsync(cancellationToken);
            var orders = await paidOrders.CountAsync(cancellationToken);
            var cancelled = await _db.Orders.AsNoTracking().CountAsync(o => o.Status == Models.OrderStatus.Cancelado && o.Data >= start && o.Data < end, cancellationToken);
            var customers = await _db.Clientes.AsNoTracking().CountAsync(cancellationToken);
            var lowStockProducts = await _db.Products.AsNoTracking().CountAsync(p => p.Active && p.TrackInventory && p.Stock <= 0, cancellationToken);
            var lowStockIngredients = await _db.Ingredients.AsNoTracking().CountAsync(i => i.Stock <= 0, cancellationToken);
            var cash = await payments.GroupBy(p => p.TipoPagamento).Select(g => new PaymentSummary(g.Key, g.Sum(p => p.Amount))).ToListAsync(cancellationToken);

            var topProducts = await _db.OrderItems.AsNoTracking()
                .Where(i => i.Order != null && i.Order.IsPaid && i.Order.Data >= start && i.Order.Data < end)
                .GroupBy(i => new { i.ProductId, i.DisplayName })
                .Select(g => new ProductSalesSummary(g.Key.ProductId, g.Key.DisplayName, g.Sum(i => i.Quantity), g.Sum(i => i.Total)))
                .OrderByDescending(x => x.Revenue)
                .Take(10)
                .ToListAsync(cancellationToken);

            return new DashboardSnapshot(start, end, sales, orders, cancelled, customers, lowStockProducts + lowStockIngredients, cash, topProducts);
        }

        public async Task<IReadOnlyList<DailySalesPoint>> GetDailySalesAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var start = from.Date;
            var end = to.Date.AddDays(1);
            var data = await _db.Orders.AsNoTracking()
                .Where(o => o.IsPaid && o.Data >= start && o.Data < end)
                .GroupBy(o => o.Data.Date)
                .Select(g => new DailySalesPoint(g.Key, g.Sum(o => o.TotalAmount > 0 ? o.TotalAmount : o.Total), g.Count()))
                .OrderBy(x => x.Date)
                .ToListAsync(cancellationToken);
            return data;
        }

        public async Task<IReadOnlyList<PaymentSummary>> GetPaymentBreakdownAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
        {
            var start = from.Date;
            var end = to.Date.AddDays(1);
            return await _db.Payments.AsNoTracking()
                .Where(p => p.PaymentDate >= start && p.PaymentDate < end)
                .GroupBy(p => p.TipoPagamento)
                .Select(g => new PaymentSummary(g.Key, g.Sum(p => p.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToListAsync(cancellationToken);
        }
    }

    public sealed record DashboardSnapshot(
        DateTime From,
        DateTime To,
        decimal Sales,
        int PaidOrders,
        int CancelledOrders,
        int Customers,
        int LowStockItems,
        IReadOnlyList<PaymentSummary> PaymentMethods,
        IReadOnlyList<ProductSalesSummary> TopProducts);

    public sealed record PaymentSummary(string Method, decimal Amount);
    public sealed record ProductSalesSummary(int ProductId, string ProductName, decimal Quantity, decimal Revenue);
    public sealed record DailySalesPoint(DateTime Date, decimal Revenue, int Orders);
}
