using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace AyGestRest.Services
{
    public sealed class BillingDocumentService
    {
        private readonly AyGestRestContext _db;
        private readonly FaturacaoService _faturacao;

        public BillingDocumentService(AyGestRestContext db)
        {
            _db = db;
            _faturacao = new FaturacaoService(db);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _faturacao.EnsureSchemaAsync(cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            const string sql = """
                CREATE TABLE IF NOT EXISTS BillingDocuments (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Number TEXT NOT NULL UNIQUE,
                    Series TEXT NOT NULL,
                    DocumentType TEXT NOT NULL,
                    ParentInvoiceId INTEGER NULL,
                    OrderId INTEGER NULL,
                    CustomerId INTEGER NULL,
                    CustomerName TEXT NOT NULL DEFAULT '',
                    CustomerNuit TEXT NOT NULL DEFAULT '',
                    Subtotal NUMERIC NOT NULL DEFAULT 0,
                    Tax NUMERIC NOT NULL DEFAULT 0,
                    Total NUMERIC NOT NULL DEFAULT 0,
                    Status INTEGER NOT NULL DEFAULT 1,
                    Reason TEXT NOT NULL DEFAULT '',
                    IssuedAt TEXT NOT NULL,
                    TerminalId TEXT NOT NULL DEFAULT '',
                    UserId INTEGER NULL
                );
                CREATE INDEX IF NOT EXISTS IX_BillingDocuments_Date ON BillingDocuments(IssuedAt);
                CREATE INDEX IF NOT EXISTS IX_BillingDocuments_Type ON BillingDocuments(DocumentType,Status);
                """;
            await ExecuteAsync(connection, sql, cancellationToken);
        }

        public async Task<IReadOnlyList<BillingDocumentSummary>> ListAsync(DateTime from, DateTime to, string? documentType = null, CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            var start = from.Date.ToString("O");
            var end = to.Date.AddDays(1).ToString("O");
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id,Number,Series,DocumentType,OrderId,CustomerName,CustomerNuit,Subtotal,Tax,Total,Status,IssuedAt,TerminalId FROM BillingDocuments WHERE IssuedAt >= $start AND IssuedAt < $end AND ($type='' OR DocumentType=$type) ORDER BY Id DESC;";
            Add(command, "$start", start); Add(command, "$end", end); Add(command, "$type", documentType?.Trim() ?? string.Empty);
            var result = new List<BillingDocumentSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(new BillingDocumentSummary(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                    Convert.ToDecimal(reader.GetValue(7)), Convert.ToDecimal(reader.GetValue(8)), Convert.ToDecimal(reader.GetValue(9)),
                    reader.GetInt32(10), DateTime.Parse(reader.GetString(11)), reader.GetString(12)));
            }
            return result;
        }

        public async Task<int> CreateAdjustmentAsync(string documentType, string series, decimal subtotal, decimal tax, decimal total, string reason, int? parentInvoiceId, int? orderId, int? customerId, string customerName, string customerNuit, string terminalId, int? userId, CancellationToken cancellationToken = default)
        {
            if (documentType is not ("NotaCredito" or "NotaDebito")) throw new ArgumentException("Tipo de documento inválido.", nameof(documentType));
            if (total <= 0) throw new ArgumentOutOfRangeException(nameof(total));
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("O motivo é obrigatório.", nameof(reason));
            await InitializeAsync(cancellationToken);

            var number = await NextNumberAsync(series, documentType, cancellationToken);
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO BillingDocuments(Number,Series,DocumentType,ParentInvoiceId,OrderId,CustomerId,CustomerName,CustomerNuit,Subtotal,Tax,Total,Status,Reason,IssuedAt,TerminalId,UserId) VALUES ($number,$series,$type,$parent,$order,$customer,$name,$nuit,$subtotal,$tax,$total,1,$reason,$date,$terminal,$user); SELECT last_insert_rowid();";
            Add(command, "$number", number); Add(command, "$series", series); Add(command, "$type", documentType); Add(command, "$parent", parentInvoiceId); Add(command, "$order", orderId); Add(command, "$customer", customerId); Add(command, "$name", customerName ?? string.Empty); Add(command, "$nuit", customerNuit ?? string.Empty); Add(command, "$subtotal", subtotal); Add(command, "$tax", tax); Add(command, "$total", total); Add(command, "$reason", reason.Trim()); Add(command, "$date", DateTime.Now.ToString("O")); Add(command, "$terminal", terminalId); Add(command, "$user", userId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }

        private async Task<string> NextNumberAsync(string series, string type, CancellationToken cancellationToken)
        {
            var prefix = $"{series}-{DateTime.Now:yyyy}";
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Number FROM BillingDocuments WHERE Series=$series AND DocumentType=$type AND Number LIKE $prefix ORDER BY Id DESC LIMIT 1;";
            Add(command, "$series", series); Add(command, "$type", type); Add(command, "$prefix", prefix + "/%");
            var value = await command.ExecuteScalarAsync(cancellationToken);
            var next = 1;
            if (value is string number && int.TryParse(number.Split('/').LastOrDefault(), out var current)) next = current + 1;
            return $"{prefix}/{next:000000}";
        }

        private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest", "AyGestRest.db");
            var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Mode=ReadWriteCreate;Pooling=True");
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static void Add(SqliteCommand command, string name, object? value)
        {
            var p = command.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; command.Parameters.Add(p);
        }
    }

    public sealed record BillingDocumentSummary(int Id, string Number, string Series, string DocumentType, int? OrderId, string CustomerName, string CustomerNuit, decimal Subtotal, decimal Tax, decimal Total, int Status, DateTime IssuedAt, string TerminalId);
}
