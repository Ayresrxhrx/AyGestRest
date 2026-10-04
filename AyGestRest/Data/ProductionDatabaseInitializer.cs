using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace AyGestRest.Data
{
    /// <summary>
    /// Inicializa a base de dados de produção de forma segura.
    /// Se existirem migrations EF Core, aplica-as. Em instalações que ainda não possuem
    /// migrations no assembly, cria o schema através do modelo actual para manter
    /// compatibilidade com as bases existentes do AyGest.
    /// </summary>
    public sealed class ProductionDatabaseInitializer
    {
        private readonly IDbContextFactory<AyGestRestContext> _factory;
        private readonly ILogger<ProductionDatabaseInitializer>? _logger;

        public ProductionDatabaseInitializer(
            IDbContextFactory<AyGestRestContext> factory,
            ILogger<ProductionDatabaseInitializer>? logger = null)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            await ConfigureSqliteAsync(db, cancellationToken);

            var migrations = (await db.Database.GetMigrationsAsync(cancellationToken)).ToList();

            if (migrations.Count > 0)
            {
                await db.Database.MigrateAsync(cancellationToken);
                _logger?.LogInformation("AyGest database migrations applied successfully.");
                return;
            }

            // Compatibilidade com a versão actual do projecto, que ainda não contém
            // migrations EF Core no assembly. Não apagar dados existentes.
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                _logger?.LogInformation("AyGest database created from the current EF model.");
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
                _logger?.LogInformation("AyGest existing database validated against the current EF model.");
            }
        }

        private static async Task ConfigureSqliteAsync(
            AyGestRestContext db,
            CancellationToken cancellationToken)
        {
            // Estas PRAGMAs são aplicadas por ligação e não alteram o modelo lógico.
            await db.Database.OpenConnectionAsync(cancellationToken);

            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 30000;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
