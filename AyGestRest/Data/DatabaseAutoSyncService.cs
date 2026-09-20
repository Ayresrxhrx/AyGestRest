using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace AyGestRest.Data
{
    public static class DatabaseAutoSyncService
    {
        public static void EnsureDatabaseUpToDate(DbContext db)
        {
            // Abrir conexão
            if (db.Database.GetDbConnection().State != ConnectionState.Open)
                db.Database.OpenConnection();

            db.Database.EnsureCreated(); // garante DB existe

            using var transaction = db.Database.BeginTransaction();

            try
            {
                var model = db.Model;

                foreach (var entity in model.GetEntityTypes())
                {
                    string tableName = entity.GetTableName()?.Trim();
                    if (string.IsNullOrWhiteSpace(tableName)) continue;

                    if (!TableExists(db, tableName))
                        CreateTable(db, entity, tableName);

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

        // ----------------------------
        // TABELAS
        // ----------------------------
        private static void CreateTable(DbContext db, IEntityType entity, string tableName)
        {
            var sql = new StringBuilder();
            sql.Append($"CREATE TABLE [{tableName}] (");

            var props = entity.GetProperties()
                .Where(p => !p.IsShadowProperty())
                .OrderBy(p => p.IsPrimaryKey() ? 0 : 1)
                .ToList();

            bool first = true;
            foreach (var prop in props)
            {
                if (!first) sql.Append(", ");
                first = false;

                string colName = prop.GetColumnName();
                string colType = MapToSqliteType(prop);

                sql.Append($"[{colName}] {colType}");

                if (prop.IsPrimaryKey())
                {
                    sql.Append(" PRIMARY KEY");
                    if (colType == "INTEGER") sql.Append(" AUTOINCREMENT");
                }

                if (!prop.IsNullable && !prop.IsPrimaryKey())
                    sql.Append(" NOT NULL DEFAULT " + GetSafeDefaultValue(prop));
            }

            sql.Append(");");
            ExecuteNonQuery(db, sql.ToString());
        }

        // ----------------------------
        // COLUNAS
        // ----------------------------
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

                string sql = $"ALTER TABLE [{tableName}] ADD COLUMN [{colName}] {MapToSqliteType(prop)}";
                if (!prop.IsNullable && !prop.IsPrimaryKey())
                    sql += " NOT NULL DEFAULT " + GetSafeDefaultValue(prop);
                sql += ";";

                ExecuteNonQuery(db, sql);
                existingColumns.Add(normalized);
            }
        }

        // ----------------------------
        // ÍNDICES
        // ----------------------------
        private static void EnsureIndexes(DbContext db, IEntityType entity, string tableName)
        {
            var existingIndexes = GetExistingIndexes(db, tableName);

            foreach (var index in entity.GetIndexes())
            {
                string indexName = index.GetDatabaseName();
                if (string.IsNullOrEmpty(indexName) || existingIndexes.Contains(indexName)) continue;

                string columns = string.Join(", ", index.Properties.Select(p => $"[{p.GetColumnName()}]"));
                string unique = index.IsUnique ? "UNIQUE " : "";
                string sql = $"CREATE {unique}INDEX [{indexName}] ON [{tableName}] ({columns});";

                ExecuteNonQuery(db, sql);
            }
        }

        // ----------------------------
        // HELPERS
        // ----------------------------
        private static bool TableExists(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name COLLATE NOCASE";
            var param = cmd.CreateParameter();
            param.ParameterName = "@name";
            param.Value = tableName;
            cmd.Parameters.Add(param);

            if (cmd.Connection.State != ConnectionState.Open) cmd.Connection.Open();
            int count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }

        private static List<string> GetExistingColumns(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}]);";

            if (cmd.Connection.State != ConnectionState.Open) cmd.Connection.Open();
            var list = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) list.Add(reader.GetString(1)?.Trim());
            return list;
        }

        private static HashSet<string> GetExistingIndexes(DbContext db, string tableName)
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=@table COLLATE NOCASE";
            var param = cmd.CreateParameter();
            param.ParameterName = "@table";
            param.Value = tableName;
            cmd.Parameters.Add(param);

            if (cmd.Connection.State != ConnectionState.Open) cmd.Connection.Open();

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
            if (cmd.Connection.State != ConnectionState.Open) cmd.Connection.Open();
            cmd.ExecuteNonQuery();
        }
    }
}
