using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Globalization;

namespace AyGestRest.Services
{
    /// <summary>
    /// Finaliza uma venda como uma única operação transacional.
    /// Pagamento, stock, caixa, factura e auditoria são confirmados juntos.
    /// </summary>
    public sealed class SaleCompletionService
    {
        private readonly AyGestRestContext _db;
        private readonly InventoryTransactionService _inventory;
        private readonly InvoiceTransactionService _invoice;

        public SaleCompletionService(AyGestRestContext db, InventoryTransactionService inventory)
        {
            _db = db;
            _inventory = inventory;
            _invoice = new InvoiceTransactionService(db);
        }

        public async Task<SaleCompletionResult> CompleteAsync(
            int orderId,
            IReadOnlyCollection<PaymentPart> payments,
            string terminalId,
            int? userId,
            int? cashRegisterId,
            CancellationToken cancellationToken = default)
        {
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId));
            if (string.IsNullOrWhiteSpace(terminalId)) throw new InvalidOperationException("Terminal não identificado.");
            if (payments.Count == 0) throw new InvalidOperationException("Indique pelo menos um método de pagamento.");

            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
                ?? throw new InvalidOperationException("Pedido não encontrado.");

            if (order.IsPaid || order.IsClosed)
                throw new InvalidOperationException("O pedido já foi liquidado.");

            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            if (total <= 0) throw new InvalidOperationException("O pedido não possui valor a pagar.");

            var alreadyPaid = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
            var requested = payments.Sum(p => p.Amount);
            var due = Math.Max(0m, total - alreadyPaid);

            if (requested < due)
                throw new InvalidOperationException($"Pagamento insuficiente. Faltam {(due - requested):N2} MT.");

            var change = Math.Max(0m, requested - due);
            if (change > 0 && !payments.Any(p => IsCash(p.Method)))
                throw new InvalidOperationException("O troco só pode ser calculado quando existe pagamento em dinheiro.");

            // A estrutura de facturação é preparada antes da transação para que a criação
            // das tabelas nunca faça parte do commit financeiro da venda.
            var faturacaoSchema = new FaturacaoService(_db);
            await faturacaoSchema.EnsureSchemaAsync(cancellationToken);

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 1. Verifica/baixa stock e ingredientes na mesma transação.
                await _inventory.DeductForSaleInCurrentTransactionAsync(orderId, userId, cancellationToken);

                // 2. Regista todos os pagamentos.
                foreach (var part in payments)
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
                order.PaidAt = DateTime.Now;
                order.CloseDate = DateTime.Now;
                order.ClosedAt = DateTime.Now;
                order.Fecha = true;

                await _db.SaveChangesAsync(cancellationToken);

                // 3. A factura é emitida ANTES do commit e usa a mesma transação.
                // Se a factura falhar, pagamento, stock e caixa também sofrem rollback.
                var invoice = await _invoice.EmitirNaTransacaoActualAsync(order, terminalId, cancellationToken);

                // 4. Caixa é gravado na mesma transação SQLite/EF.
                if (cashRegisterId.HasValue)
                {
                    await AddCashMovementsInCurrentTransactionAsync(
                        cashRegisterId.Value,
                        payments,
                        Math.Min(requested, due),
                        userId,
                        orderId,
                        cancellationToken);
                }

                // 5. Auditoria fica dentro da mesma transação.
                await AddAuditInCurrentTransactionAsync(
                    terminalId,
                    userId,
                    "SALE_COMPLETED",
                    "Order",
                    orderId.ToString(CultureInfo.InvariantCulture),
                    $"Total={total:N2};Paid={requested:N2};Change={change:N2};Invoice={invoice.Numero}",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return new SaleCompletionResult(
                    orderId,
                    total,
                    alreadyPaid + requested,
                    change,
                    payments.ToArray());
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        private async Task AddCashMovementsInCurrentTransactionAsync(
            int cashRegisterId,
            IReadOnlyCollection<PaymentPart> payments,
            decimal amountApplied,
            int? userId,
            int orderId,
            CancellationToken cancellationToken)
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            var transaction = _db.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("A transação da venda não está activa.");

            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT Status FROM AyGestCashRegisters WHERE Id=$id LIMIT 1;";
                Add(check, "$id", cashRegisterId);
                var status = await check.ExecuteScalarAsync(cancellationToken);
                if (status is null) throw new InvalidOperationException("Caixa não encontrado.");
                if (Convert.ToInt32(status, CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("O caixa está fechado.");
            }

            var remainingApplied = amountApplied;
            foreach (var part in payments)
            {
                if (remainingApplied <= 0) break;
                var applied = Math.Min(part.Amount, remainingApplied);
                if (applied <= 0) continue;

                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO AyGestCashMovements (CashRegisterId,Type,Amount,PaymentMethod,Reference,Notes,OperatorId,CreatedAt) VALUES ($register,'VENDA',$amount,$method,$reference,$notes,$operator,$created);";
                Add(insert, "$register", cashRegisterId);
                Add(insert, "$amount", applied);
                Add(insert, "$method", part.Method.Trim());
                Add(insert, "$reference", part.Reference?.Trim() ?? string.Empty);
                Add(insert, "$notes", $"Pedido #{orderId}");
                Add(insert, "$operator", userId);
                Add(insert, "$created", DateTime.Now.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken);
                remainingApplied -= applied;
            }

            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE AyGestCashRegisters SET ExpectedAmount=ExpectedAmount+$amount WHERE Id=$id AND Status=1;";
            Add(update, "$amount", amountApplied);
            Add(update, "$id", cashRegisterId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                throw new InvalidOperationException("O caixa deixou de estar aberto durante a finalização da venda.");
        }

        private async Task AddAuditInCurrentTransactionAsync(
            string terminalId,
            int? userId,
            string action,
            string entityName,
            string entityId,
            string details,
            CancellationToken cancellationToken)
        {
            var connection = _db.Database.GetDbConnection();
            var transaction = _db.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("A transação da venda não está activa.");

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO AyGestAuditTrail (UserId,TerminalId,Action,EntityName,EntityId,Details,CreatedAt) VALUES ($user,$terminal,$action,$entity,$entityId,$details,$created);";
            Add(command, "$user", userId);
            Add(command, "$terminal", terminalId);
            Add(command, "$action", action);
            Add(command, "$entity", entityName);
            Add(command, "$entityId", entityId);
            Add(command, "$details", details);
            Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static bool IsCash(string method) => method.Trim().Equals("DINHEIRO", StringComparison.OrdinalIgnoreCase) || method.Trim().Equals("CASH", StringComparison.OrdinalIgnoreCase);

        private static PaymentType ParsePaymentType(string method)
        {
            return method.Trim().ToUpperInvariant() switch
            {
                "CASH" or "DINHEIRO" => PaymentType.Dinheiro,
                "CARD" or "CARTÃO" or "CARTAO" => PaymentType.Cartao,
                "MPESA" or "M-PESA" => PaymentType.MPesa,
                "EMOLA" or "E-MOLA" => PaymentType.Emola,
                _ => PaymentType.Outro
            };
        }

        private static void Add(System.Data.Common.DbCommand command, string parameter, object? value)
        {
            var p = command.CreateParameter();
            p.ParameterName = parameter;
            p.Value = value ?? DBNull.Value;
            command.Parameters.Add(p);
        }
    }

    public sealed record SaleCompletionResult(
        int OrderId,
        decimal Total,
        decimal TotalPaid,
        decimal Change,
        IReadOnlyList<PaymentPart> Payments);
}
