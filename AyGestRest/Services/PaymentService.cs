using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace AyGestRest.Services
{
    /// <summary>
    /// Finaliza pagamentos de pedidos de forma atómica.
    /// Pagamento, estado do pedido, movimento de caixa e auditoria usam a mesma
    /// conexão/transação para evitar estados parciais.
    /// </summary>
    public sealed class PaymentService
    {
        private readonly AyGestRestContext _db;

        public PaymentService(AyGestRestContext db)
        {
            _db = db;
        }

        public async Task<PaymentResult> PayAsync(
            int orderId,
            IReadOnlyCollection<PaymentPart> parts,
            int? userId,
            string terminalId,
            int? cashRegisterId = null,
            CancellationToken cancellationToken = default)
        {
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
            if (parts is null || parts.Count == 0) throw new InvalidOperationException("Indique pelo menos um método de pagamento.");
            if (string.IsNullOrWhiteSpace(terminalId)) throw new InvalidOperationException("Terminal não identificado.");

            var order = await _db.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            if (order.IsPaid || order.IsClosed)
                throw new InvalidOperationException("O pedido já foi liquidado.");

            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            if (total <= 0)
                throw new InvalidOperationException("O pedido não possui valor a pagar.");

            var paidBefore = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
            var due = Math.Max(0m, total - paidBefore);
            var requested = parts.Sum(p => p.Amount);

            if (requested <= 0)
                throw new InvalidOperationException("O valor pago deve ser maior que zero.");
            if (requested < due)
                throw new InvalidOperationException($"Pagamento insuficiente. Faltam {due - requested:N2} MT.");

            foreach (var part in parts)
            {
                if (part.Amount <= 0)
                    throw new InvalidOperationException("Existe um pagamento com valor inválido.");
                if (string.IsNullOrWhiteSpace(part.Method))
                    throw new InvalidOperationException("Existe um pagamento sem método.");
            }

            if (cashRegisterId.HasValue)
                await ValidateCashRegisterAsync(cashRegisterId.Value, terminalId, cancellationToken);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Revalidar dentro da transação para proteger contra duplo clique/concorrrência.
                var lockedOrder = await _db.Orders
                    .Include(o => o.Payments)
                    .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                    ?? throw new InvalidOperationException("Pedido não encontrado.");

                if (lockedOrder.IsPaid || lockedOrder.IsClosed)
                    throw new InvalidOperationException("O pedido já foi liquidado.");

                var lockedTotal = lockedOrder.TotalAmount > 0 ? lockedOrder.TotalAmount : lockedOrder.Total;
                var lockedPaidBefore = lockedOrder.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
                var lockedDue = Math.Max(0m, lockedTotal - lockedPaidBefore);
                if (requested < lockedDue)
                    throw new InvalidOperationException($"Pagamento insuficiente. Faltam {lockedDue - requested:N2} MT.");

                foreach (var part in parts)
                {
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

                lockedOrder.IsPaid = true;
                lockedOrder.IsClosed = true;
                lockedOrder.Status = OrderStatus.Fechado;
                lockedOrder.CloseDate = DateTime.Now;
                lockedOrder.ClosedAt = DateTime.Now;

                await _db.SaveChangesAsync(cancellationToken);

                if (cashRegisterId.HasValue)
                {
                    foreach (var part in parts)
                    {
                        // O valor efectivo que entra no caixa nunca inclui o troco.
                        // Para pagamentos mistos, o excesso é absorvido pelo último pagamento.
                        var cashAmount = Math.Min(part.Amount, lockedDue);
                        if (cashAmount <= 0) continue;

                        await InsertCashMovementAsync(
                            transaction,
                            cashRegisterId.Value,
                            "VENDA",
                            cashAmount,
                            part.Method,
                            part.Reference,
                            $"Pedido #{orderId}",
                            userId,
                            cancellationToken);
                    }
                }

                await InsertAuditAsync(
                    transaction,
                    "PAYMENT",
                    "Order",
                    orderId.ToString(),
                    $"Total={lockedTotal:N2};Paid={requested:N2};Change={Math.Max(0m, requested - lockedDue):N2}",
                    terminalId,
                    userId,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new PaymentResult(
                    lockedTotal,
                    lockedPaidBefore + requested,
                    Math.Max(0m, requested - lockedDue),
                    parts.ToArray());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task ValidateCashRegisterAsync(int cashRegisterId, string terminalId, CancellationToken cancellationToken)
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Status, TerminalId FROM AyGestCashRegisters WHERE Id=$id LIMIT 1;";
            Add(command, "$id", cashRegisterId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Caixa não encontrado.");

            var status = reader.GetInt32(0);
            var registerTerminal = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);

            if (status != 1)
                throw new InvalidOperationException("O caixa está fechado.");
            if (!string.Equals(registerTerminal, terminalId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O caixa pertence a outro terminal.");
        }

        private async Task InsertCashMovementAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
            int cashRegisterId,
            string type,
            decimal amount,
            string paymentMethod,
            string? reference,
            string? notes,
            int? operatorId,
            CancellationToken cancellationToken)
        {
            var connection = _db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                INSERT INTO AyGestCashMovements
                    (CashRegisterId,Type,Amount,PaymentMethod,Reference,Notes,OperatorId,CreatedAt)
                VALUES
                    ($register,$type,$amount,$method,$reference,$notes,$operator,$created);

                UPDATE AyGestCashRegisters
                SET ExpectedAmount = ExpectedAmount + $amount
                WHERE Id = $register AND Status = 1;
                """;

            Add(command, "$register", cashRegisterId);
            Add(command, "$type", type);
            Add(command, "$amount", amount);
            Add(command, "$method", paymentMethod.Trim());
            Add(command, "$reference", reference?.Trim() ?? string.Empty);
            Add(command, "$notes", notes?.Trim() ?? string.Empty);
            Add(command, "$operator", operatorId);
            Add(command, "$created", DateTime.Now.ToString("O"));

            if (await command.ExecuteNonQueryAsync(cancellationToken) < 1)
                throw new InvalidOperationException("Não foi possível registar o movimento de caixa.");
        }

        private async Task InsertAuditAsync(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
            string action,
            string entityName,
            string entityId,
            string details,
            string terminalId,
            int? userId,
            CancellationToken cancellationToken)
        {
            var connection = _db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = """
                INSERT INTO AyGestAuditTrail
                    (UserId,TerminalId,Action,EntityName,EntityId,Details,CreatedAt)
                VALUES
                    ($user,$terminal,$action,$entity,$entityId,$details,$created);
                """;

            Add(command, "$user", userId);
            Add(command, "$terminal", terminalId);
            Add(command, "$action", action);
            Add(command, "$entity", entityName);
            Add(command, "$entityId", entityId);
            Add(command, "$details", details);
            Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void Add(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static PaymentType ParsePaymentType(string method)
        {
            return method.Trim().ToUpperInvariant() switch
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
