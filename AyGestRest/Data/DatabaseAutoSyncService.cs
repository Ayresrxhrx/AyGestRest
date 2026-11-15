using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace AyGestRest.Data
{
    /// <summary>
    /// Ponto único de preparação da BD do AyGest.
    ///
    /// Instalações que já têm migrations usam Migrate().
    /// Instalações antigas deste projecto que ainda não têm migrations usam
    /// EnsureCreated() apenas quando a BD ainda não existe. Depois disso,
    /// a reconciliação de colunas/índices preserva os dados existentes.
    /// </summary>
    public static class DatabaseAutoSyncService
    {
        public static void EnsureDatabaseUpToDate(DbContext db)
        {
            ConfigureSqlite(db);

            var migrations = db.Database.GetMigrations().ToList();
            bool databaseExists = db.Database.CanConnect();

            if (migrations.Count > 0)
            {
                // Quando o assembly contém migrations oficiais, este é o único
                // caminho responsável por evolução estrutural.
                db.Database.Migrate();
            }
            else if (!databaseExists)
            {
                // Compatibilidade com a árvore actual do AyGest, que ainda não
                // possui migrations EF Core no assembly.
                db.Database.EnsureCreated();
            }

            using var transaction = db.Database.BeginTransaction();
            try
            {
                foreach (var entity in db.Model.GetEntityTypes())
                {
                    string? tableName = entity.GetTableName()?.Trim();
                    if (string.IsNullOrWhiteSpace(tableName)) continue;

                    // Em bases antigas, uma tabela pode ainda não existir.
                    // Se não há migrations, EnsureCreated trata instalações novas;
                    // não criamos tabelas individuais numa instalação com dados.
                    if (!TableExists(db, tableName)) continue;

                    SyncColumns(db, entity, tableName);
                    EnsureIndexes(db, entity, tableName);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                if (db.Database.GetDbConnection().State == ConnectionState.Open)
                    db.Database.CloseConnection();
            }
        }

        private static void ConfigureSqlite(DbContext db)
        {
            if (db.Database.GetDbConnection().State != ConnectionState.Open)
                db.Database.OpenConnection();

            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=30000; PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();

            using var walCommand = db.Database.GetDbConnection().CreateCommand();
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            walCommand.ExecuteScalar();
        }

        private static void SyncColumns(DbContext db, IEntityType entity, string tableName)
        {
            var existingColumns = GetExistingColumns(db, tableName)
                .Select(c => c.Trim().ToLowerInvariant())
                .ToHashSet();

            foreach (var prop in entity.GetProperties().Where(p => !p.IsShadowProperty()))
            {
                string colName = prop.GetColumnName();
                string normalized = colName.Trim().ToLowerInvariant();
                if (existingColumns.Contains(normalized)) continue;

                // Reconsulta imediatamente antes do ALTER TABLE. Mais do que um
                // contexto pode arrancar ao mesmo tempo e tentar sincronizar a mesma coluna.
                if (ColumnExists(db, tableName, colName))
                {
                    existingColumns.Add(normalized);
                    continue;
                }

                string sql = $"ALTER TABLE [{tableName}] ADD COLUMN [{colName}] {MapToSqliteType(prop)}";
                if (!prop.IsNullable && !prop.IsPrimaryKey())
                    sql += " NOT NULL DEFAULT " + GetSafeDefaultValue(prop);
                sql += ";";

                try
                {
                    ExecuteNonQuery(db, sql);
                }
                catch (SqliteException ex) when (
                    ex.SqliteErrorCode == 1 &&
                    ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase) &&
                    ColumnExists(db, tableName, colName))
                {
                    // Outra inicialização adicionou a coluna entre a verificação e o ALTER.
                    // Só ignoramos o erro depois de confirmar que a coluna existe mesmo.
                }

                existingColumns.Add(normalized);
            }
        }

        private static void EnsureIndexes(DbContext db, IEntityType entity, string tableName)
        {
            var existingIndexes = GetExistingIndexes(db, tableName);
            foreach (var index in entity.GetIndexes())
            {
                string? indexName = index.GetDatabaseName();
                if (string.IsNullOrEmpty(indexName) || existingIndexes.Contains(indexName)) continue;

                string columns = string.Join(", ", index.Properties.Select(p => $"[{p.GetColumnName()}]"));
                string unique = index.IsUnique ? "UNIQUE " : "";
                ExecuteNonQuery(db, $"CREATE {unique}INDEX [{indexName}] ON [{tableName}] ({columns});");
            }
        }

        private static bool TableExists(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name COLLATE NOCASE";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = tableName;
            cmd.Parameters.Add(parameter);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static bool ColumnExists(DbContext db, string tableName, string columnName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}]);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1).Trim(), columnName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<string> GetExistingColumns(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}]);";
            var list = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(reader.GetString(1).Trim());
            return list;
        }

        private static HashSet<string> GetExistingIndexes(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=@table COLLATE NOCASE";
            var parameter = cmd.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = tableName;
            cmd.Parameters.Add(parameter);

            var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) indexes.Add(reader.GetString(0));
            return indexes;
        }

        private static string MapToSqliteType(IProperty prop)
        {
            var type = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
            if (type == typeof(int) || type == typeof(long) || type == typeof(bool)) return "INTEGER";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "REAL";
            if (type == typeof(byte[])) return "BLOB";
            return "TEXT";
        }

        private static string GetSafeDefaultValue(IProperty prop)
        {
            var type = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
            if (type == typeof(int) || type == typeof(long) || type == typeof(bool)) return "0";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "0.0";
            if (type == typeof(DateTime)) return "'1970-01-01'";
            if (type == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
            return "''";
        }

        private static void ExecuteNonQuery(DbContext db, string sql)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.ExecuteNonQuery();
        }
    }
}
