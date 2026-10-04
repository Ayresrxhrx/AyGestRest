using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;

namespace AyGestRest.Services
{
    /// <summary>
    /// Emissão de factura dentro da transacção EF actualmente aberta.
    /// Não cria nem confirma uma transacção própria.
    /// </summary>
    public sealed class InvoiceTransactionService
    {
        private readonly AyGestRestContext _db;
        private readonly AppConfig _config;

        public InvoiceTransactionService(AyGestRestContext db, AppConfig? config = null)
        {
            _db = db;
            _config = config ?? AppConfig.Load();
        }

        public async Task<Fatura> EmitirNaTransacaoActualAsync(Order order, string terminalId, CancellationToken cancellationToken = default)
        {
            if (_db.Database.CurrentTransaction is null)
                throw new InvalidOperationException("A emissão da factura exige uma transação de venda activa.");

            var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(cancellationToken);

            var transaction = _db.Database.CurrentTransaction.GetDbTransaction();

            await using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = "SELECT Id FROM Faturas WHERE OrderId=$orderId AND Estado<>$anulada LIMIT 1;";
                Add(existing, "$orderId", order.Id);
                Add(existing, "$anulada", (int)FaturaEstado.Anulada);
                if (await existing.ExecuteScalarAsync(cancellationToken) is not null)
                    throw new InvalidOperationException("Já existe uma factura emitida para este pedido.");
            }

            var config = await _db.RestaurantConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var taxaEntity = await _db.TaxasIVA.AsNoTracking().Where(t => t.Ativa).OrderByDescending(t => t.Id).FirstOrDefaultAsync(cancellationToken);
            var taxa = taxaEntity?.Valor is double d ? (decimal)d : 0m;
            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            var subtotal = order.Items.Sum(i => i.Total > 0 ? i.Total : i.Quantity * i.PriceAtMoment);
            var imposto = taxa > 0 ? Math.Round(total * taxa / (100m + taxa), 2) : 0m;
            var valorPago = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);
            var troco = Math.Max(0m, valorPago - total);
            var numero = await GenerateNumberAsync(connection, transaction, cancellationToken);
            var now = DateTime.Now;

            var fatura = new Fatura
            {
                Numero = numero,
                Serie = _config.InvoiceSeries,
                DataEmissao = now,
                OrderId = order.Id,
                ClienteNome = order.ClienteNome ?? string.Empty,
                ClienteNuit = string.Empty,
                Moeda = _config.CurrencyCode,
                Subtotal = total - imposto,
                Desconto = Math.Max(0m, subtotal - total),
                Imposto = imposto,
                Total = total,
                ValorPago = valorPago,
                Troco = troco,
                MetodoPagamento = string.Join(", ", order.Payments.Select(p => !string.IsNullOrWhiteSpace(p.TipoPagamento) ? p.TipoPagamento : p.Type.ToString()).Distinct()),
                NUITEmitente = config?.Nuit ?? string.Empty,
                NomeEmitente = config?.RestaurantName ?? _config.BusinessName,
                Estado = FaturaEstado.Emitida,
                TerminalId = terminalId,
                CreatedAt = now
            };

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO Faturas
                    (Numero,Serie,DataEmissao,OrderId,ClienteId,ClienteNome,ClienteNuit,Moeda,Subtotal,Desconto,Imposto,Total,ValorPago,Troco,MetodoPagamento,NUITEmitente,NomeEmitente,Estado,Observacoes,TerminalId,CreatedAt)
                    VALUES ($numero,$serie,$data,$orderId,NULL,$clienteNome,$clienteNuit,$moeda,$subtotal,$desconto,$imposto,$total,$valorPago,$troco,$metodo,$nuitEmitente,$nomeEmitente,$estado,$observacoes,$terminalId,$createdAt);
                    SELECT last_insert_rowid();
                    """;
                Add(command, "$numero", fatura.Numero); Add(command, "$serie", fatura.Serie); Add(command, "$data", now.ToString("O"));
                Add(command, "$orderId", order.Id); Add(command, "$clienteNome", fatura.ClienteNome); Add(command, "$clienteNuit", fatura.ClienteNuit);
                Add(command, "$moeda", fatura.Moeda); Add(command, "$subtotal", fatura.Subtotal); Add(command, "$desconto", fatura.Desconto);
                Add(command, "$imposto", fatura.Imposto); Add(command, "$total", fatura.Total); Add(command, "$valorPago", fatura.ValorPago);
                Add(command, "$troco", fatura.Troco); Add(command, "$metodo", fatura.MetodoPagamento); Add(command, "$nuitEmitente", fatura.NUITEmitente);
                Add(command, "$nomeEmitente", fatura.NomeEmitente); Add(command, "$estado", (int)fatura.Estado); Add(command, "$observacoes", string.Empty);
                Add(command, "$terminalId", terminalId); Add(command, "$createdAt", now.ToString("O"));
                fatura.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            }

            foreach (var item in order.Items)
            {
                var bruto = item.Total > 0 ? item.Total : item.Quantity * item.PriceAtMoment;
                var itemIva = taxa > 0 ? Math.Round(bruto * taxa / (100m + taxa), 2) : 0m;
                var preco = item.PriceAtMoment > 0 ? item.PriceAtMoment : item.UnitPrice;

                await using var itemCommand = connection.CreateCommand();
                itemCommand.Transaction = transaction;
                itemCommand.CommandText = "INSERT INTO FaturaItems (FaturaId,ProductId,Descricao,Quantidade,PrecoUnitario,Desconto,TaxaIVA,ValorIVA,Total) VALUES ($faturaId,$productId,$descricao,$quantidade,$preco,0,$taxa,$valorIva,$total);";
                Add(itemCommand, "$faturaId", fatura.Id); Add(itemCommand, "$productId", item.ProductId); Add(itemCommand, "$descricao", item.DisplayName);
                Add(itemCommand, "$quantidade", item.Quantity); Add(itemCommand, "$preco", preco); Add(itemCommand, "$taxa", taxa); Add(itemCommand, "$valorIva", itemIva); Add(itemCommand, "$total", bruto);
                await itemCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            return fatura;
        }

        private async Task<string> GenerateNumberAsync(DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
        {
            var prefix = $"{_config.InvoiceSeries}-{DateTime.Now:yyyy}";
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Numero FROM Faturas WHERE Serie=$serie AND Numero LIKE $prefix ORDER BY Id DESC LIMIT 1;";
            Add(command, "$serie", _config.InvoiceSeries); Add(command, "$prefix", prefix + "/%");
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var sequence = 1;
            if (value is string last && int.TryParse(last.Split('/').LastOrDefault(), out var current)) sequence = current + 1;
            return $"{prefix}/{sequence:000000}";
        }

        private static void Add(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
