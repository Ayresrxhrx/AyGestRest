using AyGestRest.Data;
using AyGestRest.Models;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace AyGestRest.Services
{
    public sealed class FaturacaoService
    {
        private readonly AyGestRestContext _db;
        private readonly AppConfig _config;

        public FaturacaoService(AyGestRestContext db, AppConfig? config = null)
        {
            _db = db;
            _config = config ?? AppConfig.Load();
        }

        public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Faturas (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Numero TEXT NOT NULL,
                    Serie TEXT NOT NULL,
                    DataEmissao TEXT NOT NULL,
                    OrderId INTEGER NULL,
                    ClienteId INTEGER NULL,
                    ClienteNome TEXT NOT NULL DEFAULT '',
                    ClienteNuit TEXT NOT NULL DEFAULT '',
                    Moeda TEXT NOT NULL DEFAULT 'MZN',
                    Subtotal NUMERIC NOT NULL DEFAULT 0,
                    Desconto NUMERIC NOT NULL DEFAULT 0,
                    Imposto NUMERIC NOT NULL DEFAULT 0,
                    Total NUMERIC NOT NULL DEFAULT 0,
                    ValorPago NUMERIC NOT NULL DEFAULT 0,
                    Troco NUMERIC NOT NULL DEFAULT 0,
                    MetodoPagamento TEXT NOT NULL DEFAULT '',
                    NUITEmitente TEXT NOT NULL DEFAULT '',
                    NomeEmitente TEXT NOT NULL DEFAULT '',
                    Estado INTEGER NOT NULL DEFAULT 1,
                    Observacoes TEXT NOT NULL DEFAULT '',
                    TerminalId TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    AnuladaEm TEXT NULL,
                    MotivoAnulacao TEXT NOT NULL DEFAULT ''
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Faturas_Numero ON Faturas(Numero);
                CREATE INDEX IF NOT EXISTS IX_Faturas_OrderId ON Faturas(OrderId);
                CREATE INDEX IF NOT EXISTS IX_Faturas_DataEmissao ON Faturas(DataEmissao);
                CREATE TABLE IF NOT EXISTS FaturaItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FaturaId INTEGER NOT NULL,
                    ProductId INTEGER NULL,
                    Descricao TEXT NOT NULL,
                    Quantidade NUMERIC NOT NULL DEFAULT 0,
                    PrecoUnitario NUMERIC NOT NULL DEFAULT 0,
                    Desconto NUMERIC NOT NULL DEFAULT 0,
                    TaxaIVA NUMERIC NOT NULL DEFAULT 0,
                    ValorIVA NUMERIC NOT NULL DEFAULT 0,
                    Total NUMERIC NOT NULL DEFAULT 0,
                    FOREIGN KEY(FaturaId) REFERENCES Faturas(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_FaturaItems_FaturaId ON FaturaItems(FaturaId);
                """;

            await _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }

        public async Task<Fatura> EmitirFaturaAsync(int orderId, int? clienteId = null, string? clienteNome = null, string? clienteNuit = null, decimal? taxaIva = null, CancellationToken cancellationToken = default)
        {
            await EnsureSchemaAsync(cancellationToken);

            var order = await _db.Orders.Include(o => o.Items).ThenInclude(i => i.Product).Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order == null) throw new InvalidOperationException("O pedido indicado não existe.");

            var existente = await FindByOrderIdAsync(orderId, cancellationToken);
            if (existente != null) return existente;

            var config = await _db.RestaurantConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var taxa = taxaIva ?? await ObterTaxaIvaAsync(cancellationToken);
            var subtotal = order.Items.Sum(i => i.Total > 0 ? i.Total : i.Quantity * i.PriceAtMoment);
            var total = order.TotalAmount > 0 ? order.TotalAmount : order.Total;
            var imposto = taxa > 0 ? Math.Round(total * taxa / (100m + taxa), 2) : 0m;
            var subtotalSemImposto = total - imposto;
            var valorPago = order.Payments.Sum(p => p.Amount > 0 ? p.Amount : p.Valor ?? 0m);

            var fatura = new Fatura
            {
                Numero = await GerarNumeroAsync(cancellationToken),
                Serie = _config.InvoiceSeries,
                DataEmissao = DateTime.Now,
                OrderId = order.Id,
                ClienteId = clienteId,
                ClienteNome = clienteNome ?? order.ClienteNome,
                ClienteNuit = clienteNuit ?? string.Empty,
                Moeda = _config.CurrencyCode,
                Subtotal = subtotalSemImposto,
                Desconto = Math.Max(0m, subtotal - total),
                Imposto = imposto,
                Total = total,
                ValorPago = valorPago,
                Troco = Math.Max(0m, valorPago - total),
                MetodoPagamento = string.Join(", ", order.Payments.Select(p => !string.IsNullOrWhiteSpace(p.TipoPagamento) ? p.TipoPagamento : p.Type.ToString()).Distinct()),
                NUITEmitente = config?.Nuit ?? string.Empty,
                NomeEmitente = config?.RestaurantName ?? _config.BusinessName,
                Estado = FaturaEstado.Emitida,
                TerminalId = _config.TerminalId,
                CreatedAt = DateTime.Now
            };

            await using var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO Faturas
                    (Numero, Serie, DataEmissao, OrderId, ClienteId, ClienteNome, ClienteNuit, Moeda, Subtotal, Desconto, Imposto, Total, ValorPago, Troco, MetodoPagamento, NUITEmitente, NomeEmitente, Estado, Observacoes, TerminalId, CreatedAt)
                    VALUES ($numero, $serie, $data, $orderId, $clienteId, $clienteNome, $clienteNuit, $moeda, $subtotal, $desconto, $imposto, $total, $valorPago, $troco, $metodo, $nuitEmitente, $nomeEmitente, $estado, $observacoes, $terminalId, $createdAt);
                    SELECT last_insert_rowid();
                    """;

                Add(command, "$numero", fatura.Numero); Add(command, "$serie", fatura.Serie); Add(command, "$data", fatura.DataEmissao.ToString("O"));
                Add(command, "$orderId", fatura.OrderId); Add(command, "$clienteId", fatura.ClienteId); Add(command, "$clienteNome", fatura.ClienteNome); Add(command, "$clienteNuit", fatura.ClienteNuit);
                Add(command, "$moeda", fatura.Moeda); Add(command, "$subtotal", fatura.Subtotal); Add(command, "$desconto", fatura.Desconto); Add(command, "$imposto", fatura.Imposto); Add(command, "$total", fatura.Total);
                Add(command, "$valorPago", fatura.ValorPago); Add(command, "$troco", fatura.Troco); Add(command, "$metodo", fatura.MetodoPagamento); Add(command, "$nuitEmitente", fatura.NUITEmitente);
                Add(command, "$nomeEmitente", fatura.NomeEmitente); Add(command, "$estado", (int)fatura.Estado); Add(command, "$observacoes", fatura.Observacoes); Add(command, "$terminalId", fatura.TerminalId); Add(command, "$createdAt", fatura.CreatedAt.ToString("O"));
                fatura.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

                foreach (var item in order.Items)
                {
                    var bruto = item.Total > 0 ? item.Total : item.Quantity * item.PriceAtMoment;
                    var itemIva = taxa > 0 ? Math.Round(bruto * taxa / (100m + taxa), 2) : 0m;
                    var preco = item.PriceAtMoment > 0 ? item.PriceAtMoment : item.UnitPrice;

                    await using var itemCommand = connection.CreateCommand();
                    itemCommand.Transaction = transaction;
                    itemCommand.CommandText = "INSERT INTO FaturaItems (FaturaId, ProductId, Descricao, Quantidade, PrecoUnitario, Desconto, TaxaIVA, ValorIVA, Total) VALUES ($faturaId, $productId, $descricao, $quantidade, $preco, 0, $taxa, $valorIva, $total);";
                    Add(itemCommand, "$faturaId", fatura.Id); Add(itemCommand, "$productId", item.ProductId); Add(itemCommand, "$descricao", item.DisplayName); Add(itemCommand, "$quantidade", item.Quantity); Add(itemCommand, "$preco", preco); Add(itemCommand, "$taxa", taxa); Add(itemCommand, "$valorIva", itemIva); Add(itemCommand, "$total", bruto);
                    await itemCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return fatura;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        public async Task AnularFaturaAsync(int faturaId, string motivo, CancellationToken cancellationToken = default)
        {
            await EnsureSchemaAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(motivo)) throw new ArgumentException("É obrigatório indicar o motivo da anulação.", nameof(motivo));

            await using var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Faturas SET Estado = $estado, AnuladaEm = $data, MotivoAnulacao = $motivo WHERE Id = $id AND Estado <> $estado;";
            Add(command, "$estado", (int)FaturaEstado.Anulada); Add(command, "$data", DateTime.Now.ToString("O")); Add(command, "$motivo", motivo.Trim()); Add(command, "$id", faturaId);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("Fatura não encontrada ou já anulada.");
        }

        private async Task<Fatura?> FindByOrderIdAsync(int orderId, CancellationToken cancellationToken)
        {
            await using var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Numero, Serie, DataEmissao, OrderId, ClienteId, ClienteNome, ClienteNuit, Moeda, Subtotal, Desconto, Imposto, Total, ValorPago, Troco, MetodoPagamento, NUITEmitente, NomeEmitente, Estado, Observacoes, TerminalId, CreatedAt, AnuladaEm, MotivoAnulacao FROM Faturas WHERE OrderId = $orderId AND Estado <> $anulada LIMIT 1;";
            Add(command, "$orderId", orderId); Add(command, "$anulada", (int)FaturaEstado.Anulada);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            return new Fatura { Id = reader.GetInt32(0), Numero = reader.GetString(1), Serie = reader.GetString(2), DataEmissao = DateTime.Parse(reader.GetString(3)), OrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4), ClienteId = reader.IsDBNull(5) ? null : reader.GetInt32(5), ClienteNome = reader.GetString(6), ClienteNuit = reader.GetString(7), Moeda = reader.GetString(8), Subtotal = reader.GetDecimal(9), Desconto = reader.GetDecimal(10), Imposto = reader.GetDecimal(11), Total = reader.GetDecimal(12), ValorPago = reader.GetDecimal(13), Troco = reader.GetDecimal(14), MetodoPagamento = reader.GetString(15), NUITEmitente = reader.GetString(16), NomeEmitente = reader.GetString(17), Estado = (FaturaEstado)reader.GetInt32(18), Observacoes = reader.GetString(19), TerminalId = reader.GetString(20), CreatedAt = DateTime.Parse(reader.GetString(21)), AnuladaEm = reader.IsDBNull(22) ? null : DateTime.Parse(reader.GetString(22)), MotivoAnulacao = reader.GetString(23) };
        }

        private async Task<decimal> ObterTaxaIvaAsync(CancellationToken cancellationToken)
        {
            var taxa = await _db.TaxasIVA.AsNoTracking().Where(t => t.Ativa).OrderByDescending(t => t.Id).FirstOrDefaultAsync(cancellationToken);
            return taxa?.Valor is double valor ? (decimal)valor : 0m;
        }

        private async Task<string> GerarNumeroAsync(CancellationToken cancellationToken)
        {
            var prefixo = $"{_config.InvoiceSeries}-{DateTime.Now:yyyy}";
            await using var connection = _db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Numero FROM Faturas WHERE Serie = $serie AND Numero LIKE $prefixo ORDER BY Id DESC LIMIT 1;";
            Add(command, "$serie", _config.InvoiceSeries); Add(command, "$prefixo", prefixo + "/%");
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var sequencia = 1;
            if (value is string ultimo && int.TryParse(ultimo.Split('/').LastOrDefault(), out var atual)) sequencia = atual + 1;
            return $"{prefixo}/{sequencia:000000}";
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
