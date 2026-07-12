using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

internal static class SqliteConnectionFactory
{
    public static async Task<SqliteConnection> OpenAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Database is not null &&
            !string.Equals(context.Database, SqliteIdentifier.MainSchema, StringComparison.OrdinalIgnoreCase))
        {
            throw new GridletValidationException(
                $"SQLite connection '{context.ConnectionName}' does not contain database '{context.Database}'.");
        }

        var connection = new SqliteConnection(context.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
