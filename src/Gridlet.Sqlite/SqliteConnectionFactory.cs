using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

internal static class SqliteConnectionFactory
{
    public static async Task<SqliteConnection> OpenAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken)
    {
        var selected = SqliteIdentifier.SelectedSchema(context);
        if (!selected.Equals(SqliteIdentifier.MainSchema, StringComparison.OrdinalIgnoreCase) &&
            !context.Connection.SqliteAttachments.ContainsKey(selected))
        {
            throw new GridletValidationException(
                $"SQLite connection '{context.ConnectionName}' does not contain database '{context.Database}'.");
        }

        var connection = new SqliteConnection(context.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var attached = new List<string>();
            await using (var databases = connection.CreateCommand())
            {
                databases.CommandText = "PRAGMA database_list;";
                await using var reader = await databases.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(1);
                    if (name is not ("main" or "temp")) attached.Add(name);
                }
            }
            // ATTACH state belongs to the native handle and survives a round trip through the
            // Microsoft.Data.Sqlite pool. Clear it so configurations sharing a primary connection
            // string can never inherit one another's allowlisted databases.
            foreach (var name in attached)
            {
                await using var detach = connection.CreateCommand();
                detach.CommandText = $"DETACH DATABASE {SqliteIdentifier.Quote(name)};";
                await detach.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var attachment in context.Connection.SqliteAttachments)
            {
                await using var attach = connection.CreateCommand();
                attach.CommandText = $"ATTACH DATABASE @filename AS {SqliteIdentifier.Quote(attachment.Key)};";
                attach.Parameters.AddWithValue("@filename", attachment.Value);
                await attach.ExecuteNonQueryAsync(cancellationToken);
            }
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
