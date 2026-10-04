using Microsoft.Data.Sqlite;
using System.Globalization;

namespace AyGestRest.Data;

public static class ProductionCashHardening
{
    public static async Task EnsureAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS UX_AyGestCashRegisters_OpenTerminal ON AyGestCashRegisters(TerminalId) WHERE Status=1;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<decimal> GetExpectedAsync(SqliteConnection connection, int cashRegisterId, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ExpectedAmount FROM AyGestCashRegisters WHERE Id=$id AND Status=1 LIMIT 1;";
        Add(command, "$id", cashRegisterId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null)
            throw new InvalidOperationException("Caixa não encontrado ou já fechado.");
        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
