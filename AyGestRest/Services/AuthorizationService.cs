using AyGestRest.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace AyGestRest.Services
{
    public sealed class AuthorizationService
    {
        private readonly string _databasePath;

        public AuthorizationService(string? databasePath = null)
        {
            _databasePath = databasePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AyGestRest", "AyGestRest.db");
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared;Mode=ReadWriteCreate");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS AyGestPermissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    Category TEXT NOT NULL DEFAULT '',
                    IsActive INTEGER NOT NULL DEFAULT 1
                );
                CREATE TABLE IF NOT EXISTS AyGestRolePermissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Role INTEGER NOT NULL,
                    PermissionId INTEGER NOT NULL,
                    UNIQUE(Role,PermissionId),
                    FOREIGN KEY(PermissionId) REFERENCES AyGestPermissions(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS AyGestUserPermissions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    PermissionId INTEGER NOT NULL,
                    Allowed INTEGER NOT NULL DEFAULT 1,
                    UNIQUE(UserId,PermissionId),
                    FOREIGN KEY(PermissionId) REFERENCES AyGestPermissions(Id) ON DELETE CASCADE
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var permissions = new (string Code, string Name, string Category)[]
            {
                ("POS.SALE","Realizar vendas","POS"), ("POS.CANCEL","Cancelar vendas","POS"), ("POS.DISCOUNT","Aplicar descontos","POS"),
                ("POS.PRICE_OVERRIDE","Alterar preço","POS"), ("POS.REFUND","Fazer devoluções","POS"), ("CASH.OPEN","Abrir caixa","Caixa"),
                ("CASH.CLOSE","Fechar caixa","Caixa"), ("CASH.MOVEMENT","Movimentos de caixa","Caixa"), ("BILLING.VOID","Anular factura","Facturação"),
                ("BILLING.CREDIT","Emitir nota de crédito","Facturação"), ("INVENTORY.ADJUST","Ajustar stock","Stock"), ("INVENTORY.PURCHASE","Receber compras","Stock"),
                ("REPORTS.VIEW","Ver relatórios","Relatórios"), ("USERS.MANAGE","Gerir utilizadores","Segurança"), ("SETTINGS.MANAGE","Alterar configurações","Sistema")
            };

            foreach (var permission in permissions)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT OR IGNORE INTO AyGestPermissions(Code,Name,Category,IsActive) VALUES ($code,$name,$category,1);";
                Add(insert, "$code", permission.Code); Add(insert, "$name", permission.Name); Add(insert, "$category", permission.Category);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            // Administrador recebe todas as permissões existentes.
            await using var admin = connection.CreateCommand();
            admin.CommandText = "INSERT OR IGNORE INTO AyGestRolePermissions(Role,PermissionId) SELECT 0,Id FROM AyGestPermissions WHERE IsActive=1;";
            await admin.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> CanAsync(int userId, string permissionCode, CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);
            await using var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared;Mode=ReadWriteCreate");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM AyGestUserPermissions up JOIN AyGestPermissions p ON p.Id=up.PermissionId
                    WHERE up.UserId=$user AND p.Code=$code AND p.IsActive=1 AND up.Allowed=1
                ) OR EXISTS (
                    SELECT 1 FROM Users u JOIN AyGestRolePermissions rp ON rp.Role=CAST(u.Role AS INTEGER)
                    JOIN AyGestPermissions p ON p.Id=rp.PermissionId
                    WHERE u.Id=$user AND p.Code=$code AND p.IsActive=1
                ) THEN 1 ELSE 0 END;
                """;
            Add(command, "$user", userId); Add(command, "$code", permissionCode);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }

        private static void Add(SqliteCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value ?? DBNull.Value; command.Parameters.Add(parameter);
        }
    }
}
