using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace AyGestRest.Data
{
    public static class DbUpdater
    {
        public static void AtualizarDb(AyGestRestContext context)
        {
            DatabaseAutoSyncService.EnsureDatabaseUpToDate(context);
        }

        /// <summary>
        /// Ponto único de entrada para preparar a BD do AyGest.
        /// Executa migrations oficiais e, depois, reconcilia instalações antigas.
        /// </summary>
        public static void InitializeDatabase(this AyGestRestContext context)
        {
            DatabaseAutoSyncService.EnsureDatabaseUpToDate(context);
        }

        public static void ConfigureConnection(AyGestRestContext context)
        {
            var connection = context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=15000; PRAGMA synchronous=NORMAL;";
            command.ExecuteNonQuery();
        }
    }
}
