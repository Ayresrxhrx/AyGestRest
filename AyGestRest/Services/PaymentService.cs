using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;

namespace AyGestRest.Services
{
    public sealed class PaymentService
    {
        private readonly AyGestRestContext _db;
        private readonly ProductionPlatformService _platform;

        public PaymentService(AyGestRestContext db, ProductionPlatformService? platform = null)
        {
            _db = db;
            _platform = platform ?? new ProductionPlatformService();
        }

        public async Task<PaymentResult> PayAsync(int orderId, IReadOnlyCollection<PaymentPart> parts, int? userId, string terminalId, int? cashRegisterId = null, CancellationToken cancellationToken = default)
        {
            if (parts.Count == 0) throw new InvalidOperationException("Indique pelo menos um método de pagamento.");
            if (string.IsNullOrWhiteSpace(terminalId)) throw new InvalidOperationException("Terminal não identificado.");

            var order = await _db.Orders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");
            if (order.IsPaid || order.IsClosed) throw new InvalidOperationException("O pedido já foi liquidado.");
            if (order.Total <= 0 && order.TotalAmount <= 0) throw new InvalidOperationException("O pedido não possui valor a pagar.");

            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            var paidBefore = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
            var requested = parts.Sum(p => p.Amount);
            if (requested <= 0) throw new InvalidOperationException("O valor pago deve ser maior que zero.");

            var due = Math.Max(0m, total - paidBefore);
            if (requested < due)
                throw new InvalidOperationException($"Pagamento insuficiente. Faltam {due - requested:N2} MT.");

            var change = Math.Max(0m, requested - due);
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                foreach (var part in parts)
                {
                    if (part.Amount <= 0) throw new InvalidOperationException("Existe um pagamento com valor inválido.");
                    if (string.IsNullOrWhiteSpace(part.Method)) throw new InvalidOperationException("Existe um pagamento sem método.");

                    _db.Payments.Add(new Payment
                    {
                        OrderId = orderId,
                        Amount = part.Amount,
                        Valor = part.Amount,
                        Type = ParsePaymentType(part.Method),
                        TipoPagamento = part.Method.Trim(),
                        Reference = part.Reference?.Trim() ?? string.Empty,
                        Notes = part.Notes?.Trim() ?? string.Empty,
                        PaymentDate = DateTime.Now,
                        Data = DateTime.Now
                    });
                }

                order.IsPaid = true;
                order.IsClosed = true;
                order.Status = OrderStatus.Fechado;
                order.CloseDate = DateTime.Now;
                order.ClosedAt = DateTime.Now;

                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                if (cashRegisterId.HasValue)
                {
                    foreach (var part in parts)
                    {
                        await _platform.AddCashMovementAsync(cashRegisterId.Value, "VENDA", Math.Min(part.Amount, due), part.Method, part.Reference, $"Pedido #{orderId}", userId, cancellationToken);
                    }
                }

                await _platform.AuditAsync("PAYMENT", "Order", orderId.ToString(), $"Total={total:N2};Paid={requested:N2};Change={change:N2}", terminalId, userId, cancellationToken);
                return new PaymentResult(total, paidBefore + requested, change, parts.ToArray());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private static PaymentType ParsePaymentType(string method)
        {
            var normalized = method.Trim().ToUpperInvariant();
            return normalized switch
            {
                "CASH" or "DINHEIRO" => PaymentType.Dinheiro,
                "CARD" or "CARTÃO" or "CARTAO" => PaymentType.Cartao,
                "MPESA" or "M-PESA" => PaymentType.MPesa,
                "EMOLA" or "E-MOLA" => PaymentType.Emola,
                "MKESH" => PaymentType.Outro,
                "QR" or "QR CODE" => PaymentType.Outro,
                _ => PaymentType.Outro
            };
        }
    }

    public sealed record PaymentPart(decimal Amount, string Method, string? Reference = null, string? Notes = null);
    public sealed record PaymentResult(decimal Total, decimal TotalPaid, decimal Change, IReadOnlyList<PaymentPart> Parts);
}
