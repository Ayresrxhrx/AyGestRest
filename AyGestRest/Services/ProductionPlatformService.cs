using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace AyGestRest.Services
{
    /// <summary>
    /// Núcleo transacional do AyGest POS. Mantém as operações que precisam de
    /// consistência fora da UI e prepara a aplicação para vários terminais.
    /// </summary>
    public sealed class ProductionPlatformService
    {
        private readonly string _databasePath;

        public ProductionPlatformService(string? databasePath = null)
        {
            _databasePath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AyGestRest", "AyGestRest.db");
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
            await using var connection = await OpenAsync(cancellationToken);

            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000;", cancellationToken);

            const string sql = """
                CREATE TABLE IF NOT EXISTS AyGestTerminals (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TerminalId TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    HostName TEXT NOT NULL DEFAULT '',
                    IpAddress TEXT NOT NULL DEFAULT '',
                    Mode INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    LastSeenAt TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestTerminals_LastSeenAt ON AyGestTerminals(LastSeenAt);

                CREATE TABLE IF NOT EXISTS AyGestCashRegisters (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TerminalId TEXT NOT NULL,
                    OperatorId INTEGER NULL,
                    OpenedAt TEXT NOT NULL,
                    ClosedAt TEXT NULL,
                    OpeningAmount NUMERIC NOT NULL DEFAULT 0,
                    ExpectedAmount NUMERIC NOT NULL DEFAULT 0,
                    CountedAmount NUMERIC NOT NULL DEFAULT 0,
                    Difference NUMERIC NOT NULL DEFAULT 0,
                    Status INTEGER NOT NULL DEFAULT 1,
                    Notes TEXT NOT NULL DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestCashRegisters_Terminal ON AyGestCashRegisters(TerminalId, Status);

                CREATE TABLE IF NOT EXISTS AyGestCashMovements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CashRegisterId INTEGER NOT NULL,
                    Type TEXT NOT NULL,
                    Amount NUMERIC NOT NULL,
                    PaymentMethod TEXT NOT NULL DEFAULT 'Dinheiro',
                    Reference TEXT NOT NULL DEFAULT '',
                    Notes TEXT NOT NULL DEFAULT '',
                    OperatorId INTEGER NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY(CashRegisterId) REFERENCES AyGestCashRegisters(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestCashMovements_Register ON AyGestCashMovements(CashRegisterId, CreatedAt);

                CREATE TABLE IF NOT EXISTS AyGestPaymentMethods (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    RequiresReference INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS AyGestDocumentSeries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE,
                    DocumentType TEXT NOT NULL,
                    CurrentNumber INTEGER NOT NULL DEFAULT 0,
                    Year INTEGER NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );

                CREATE TABLE IF NOT EXISTS AyGestOfflineQueue (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OperationId TEXT NOT NULL UNIQUE,
                    TerminalId TEXT NOT NULL,
                    OperationType TEXT NOT NULL,
                    Payload TEXT NOT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    Attempts INTEGER NOT NULL DEFAULT 0,
                    LastError TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    ProcessedAt TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestOfflineQueue_Status ON AyGestOfflineQueue(Status, CreatedAt);

                CREATE TABLE IF NOT EXISTS AyGestAuditTrail (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NULL,
                    TerminalId TEXT NOT NULL DEFAULT '',
                    Action TEXT NOT NULL,
                    EntityName TEXT NOT NULL DEFAULT '',
                    EntityId TEXT NOT NULL DEFAULT '',
                    Details TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestAuditTrail_CreatedAt ON AyGestAuditTrail(CreatedAt);
                CREATE INDEX IF NOT EXISTS IX_AyGestAuditTrail_Entity ON AyGestAuditTrail(EntityName, EntityId);

                CREATE TABLE IF NOT EXISTS AyGestPriceHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    OldPrice NUMERIC NOT NULL,
                    NewPrice NUMERIC NOT NULL,
                    ChangedBy INTEGER NULL,
                    Reason TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS AyGestStockReservations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductId INTEGER NOT NULL,
                    OrderId INTEGER NULL,
                    Quantity NUMERIC NOT NULL,
                    Status INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    ReleasedAt TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_AyGestStockReservations_Product ON AyGestStockReservations(ProductId, Status);

                CREATE TABLE IF NOT EXISTS AyGestBackups (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL,
                    FileSize INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    Trigger TEXT NOT NULL DEFAULT 'automatic',
                    Success INTEGER NOT NULL DEFAULT 1,
                    Error TEXT NOT NULL DEFAULT ''
                );
                """;

            await ExecuteAsync(connection, sql, cancellationToken);
            await SeedPaymentMethodsAsync(connection, cancellationToken);
        }

        public async Task<int> OpenCashAsync(string terminalId, int? operatorId, decimal openingAmount, string? notes = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("TerminalId é obrigatório.", nameof(terminalId));
            if (openingAmount < 0) throw new ArgumentOutOfRangeException(nameof(openingAmount));

            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT Id FROM AyGestCashRegisters WHERE TerminalId=$terminal AND Status=1 LIMIT 1;";
                Add(check, "$terminal", terminalId);
                if (await check.ExecuteScalarAsync(cancellationToken) is not null)
                    throw new InvalidOperationException("Já existe um caixa aberto neste terminal.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO AyGestCashRegisters (TerminalId,OperatorId,OpenedAt,OpeningAmount,ExpectedAmount,Status,Notes) VALUES ($terminal,$operator,$opened,$amount,$amount,1,$notes); SELECT last_insert_rowid();";
            Add(command, "$terminal", terminalId); Add(command, "$operator", operatorId); Add(command, "$opened", DateTime.Now.ToString("O"));
            Add(command, "$amount", openingAmount); Add(command, "$notes", notes?.Trim() ?? string.Empty);
            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);
            return id;
        }

        public async Task AddCashMovementAsync(int cashRegisterId, string type, decimal amount, string paymentMethod, string? reference = null, string? notes = null, int? operatorId = null, CancellationToken cancellationToken = default)
        {
            if (cashRegisterId <= 0) throw new ArgumentOutOfRangeException(nameof(cashRegisterId));
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("Tipo de movimento é obrigatório.", nameof(type));

            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT Status FROM AyGestCashRegisters WHERE Id=$id LIMIT 1;";
                Add(check, "$id", cashRegisterId);
                var status = await check.ExecuteScalarAsync(cancellationToken);
                if (status is null) throw new InvalidOperationException("Caixa não encontrado.");
                if (Convert.ToInt32(status, CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("O caixa está fechado.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO AyGestCashMovements (CashRegisterId,Type,Amount,PaymentMethod,Reference,Notes,OperatorId,CreatedAt) VALUES ($register,$type,$amount,$method,$reference,$notes,$operator,$created);";
            Add(command, "$register", cashRegisterId); Add(command, "$type", type.Trim()); Add(command, "$amount", amount); Add(command, "$method", paymentMethod?.Trim() ?? "Dinheiro");
            Add(command, "$reference", reference?.Trim() ?? string.Empty); Add(command, "$notes", notes?.Trim() ?? string.Empty); Add(command, "$operator", operatorId); Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);

            var sign = type.Equals("SAIDA", StringComparison.OrdinalIgnoreCase) || type.Equals("SANGRIA", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE AyGestCashRegisters SET ExpectedAmount=ExpectedAmount + ($amount * $sign) WHERE Id=$id;";
            Add(update, "$amount", amount); Add(update, "$sign", sign); Add(update, "$id", cashRegisterId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task CloseCashAsync(int cashRegisterId, decimal countedAmount, string? notes = null, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE AyGestCashRegisters SET ClosedAt=$closed,CountedAmount=$counted,Difference=$difference,Status=0,Notes=CASE WHEN $notes='' THEN Notes ELSE $notes END WHERE Id=$id AND Status=1; SELECT changes();";
            Add(command, "$closed", DateTime.Now.ToString("O")); Add(command, "$counted", countedAmount); Add(command, "$difference", countedAmount - await GetExpectedAmountAsync(connection, transaction, cashRegisterId, cancellationToken));
            Add(command, "$notes", notes?.Trim() ?? string.Empty); Add(command, "$id", cashRegisterId);
            var changed = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (changed == 0) throw new InvalidOperationException("Caixa não encontrado ou já fechado.");
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task RegisterTerminalAsync(string terminalId, string name, int mode, string? ipAddress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("TerminalId é obrigatório.", nameof(terminalId));
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AyGestTerminals (TerminalId,Name,HostName,IpAddress,Mode,IsActive,LastSeenAt,CreatedAt) VALUES ($id,$name,$host,$ip,$mode,1,$seen,$created) ON CONFLICT(TerminalId) DO UPDATE SET Name=excluded.Name,HostName=excluded.HostName,IpAddress=excluded.IpAddress,Mode=excluded.Mode,IsActive=1,LastSeenAt=excluded.LastSeenAt;";
            Add(command, "$id", terminalId); Add(command, "$name", name?.Trim() ?? Environment.MachineName); Add(command, "$host", Environment.MachineName); Add(command, "$ip", ipAddress?.Trim() ?? string.Empty); Add(command, "$mode", mode); Add(command, "$seen", DateTime.Now.ToString("O")); Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task QueueOfflineOperationAsync(string operationId, string terminalId, string operationType, object payload, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(terminalId)) throw new ArgumentException("Identificadores de operação e terminal são obrigatórios.");
            var json = JsonSerializer.Serialize(payload);
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR IGNORE INTO AyGestOfflineQueue (OperationId,TerminalId,OperationType,Payload,Status,Attempts,CreatedAt) VALUES ($id,$terminal,$type,$payload,0,0,$created);";
            Add(command, "$id", operationId); Add(command, "$terminal", terminalId); Add(command, "$type", operationType); Add(command, "$payload", json); Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task AuditAsync(string action, string entityName, string entityId, string? details, string terminalId, int? userId, CancellationToken cancellationToken = default)
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AyGestAuditTrail (UserId,TerminalId,Action,EntityName,EntityId,Details,CreatedAt) VALUES ($user,$terminal,$action,$entity,$entityId,$details,$created);";
            Add(command, "$user", userId); Add(command, "$terminal", terminalId); Add(command, "$action", action); Add(command, "$entity", entityName); Add(command, "$entityId", entityId); Add(command, "$details", details ?? string.Empty); Add(command, "$created", DateTime.Now.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<string> CreateBackupAsync(string? trigger = "manual", CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            var backupDir = Path.Combine(Path.GetDirectoryName(_databasePath)!, "Backups");
            Directory.CreateDirectory(backupDir);
            var destination = Path.Combine(backupDir, $"AyGestRest_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db");

            await using var source = new SqliteConnection($"Data Source={_databasePath};Mode=ReadWrite;Cache=Shared");
            await source.OpenAsync(cancellationToken);
            await using var target = new SqliteConnection($"Data Source={destination}");
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);

            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AyGestBackups (FilePath,FileSize,CreatedAt,Trigger,Success) VALUES ($path,$size,$created,$trigger,1);";
            Add(command, "$path", destination); Add(command, "$size", new FileInfo(destination).Length); Add(command, "$created", DateTime.Now.ToString("O")); Add(command, "$trigger", trigger ?? "manual");
            await command.ExecuteNonQueryAsync(cancellationToken);
            return destination;
        }

        private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
        {
            var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True");
            await connection.OpenAsync(cancellationToken);
            return connection;
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<decimal> GetExpectedAmountAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, int id, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT ExpectedAmount FROM AyGestCashRegisters WHERE Id=$id AND Status=1;";
            Add(command, "$id", id);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null) throw new InvalidOperationException("Caixa não encontrado ou já fechado.");
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }

        private static async Task SeedPaymentMethodsAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            const string sql = """
                INSERT OR IGNORE INTO AyGestPaymentMethods(Code,Name,IsActive,RequiresReference,SortOrder) VALUES
                ('CASH','Dinheiro',1,0,1),
                ('CARD','Cartão',1,1,2),
                ('MPESA','M-Pesa',1,1,3),
                ('EMOLA','e-Mola',1,1,4),
                ('MKESH','mKesh',1,1,5),
                ('QR','QR Code',1,1,6),
                ('CREDIT','Crédito',1,0,7);
                """;
            await ExecuteAsync(connection, sql, cancellationToken);
        }

        private static void Add(SqliteCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
