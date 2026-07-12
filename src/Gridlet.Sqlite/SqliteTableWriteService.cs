using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

public sealed class SqliteTableWriteService : ITableWriteService
{
    public Task<int> InsertRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, values, null, cancellationToken);

    public Task<int> UpdateRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, values, key, cancellationToken);

    public Task<int> DeleteRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, null, key, cancellationToken);

    private static async Task<int> ExecuteAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?>? values,
        IReadOnlyDictionary<string, object?>? key,
        CancellationToken cancellationToken)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        if (values is { Count: 0 }) throw new GridletValidationException("No column values were supplied.");
        if (key is { Count: 0 }) throw new GridletValidationException("No key columns were supplied to identify the row.");

        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, table, cancellationToken);
        if (definition.Object.Type != DbObjectType.Table)
        {
            throw new GridletValidationException($"Rows cannot be written through {schema}.{table} because it is not a table.");
        }

        var setColumns = ValidateColumns(values, definition, forWrite: true);
        var keyColumns = ValidateColumns(key, definition, forWrite: false);
        var target = SqliteIdentifier.QuoteQualified(schema, table);
        await using var command = connection.CreateCommand();
        if (values is not null && key is null)
        {
            var insertColumns = setColumns!;
            command.CommandText = $"INSERT INTO {target} " +
                $"({string.Join(", ", insertColumns.Select(SqliteIdentifier.Quote))}) VALUES " +
                $"({string.Join(", ", insertColumns.Select((_, i) => "@v" + i))});";
        }
        else if (values is not null)
        {
            command.CommandText = $"UPDATE {target} SET " +
                $"{string.Join(", ", setColumns!.Select((c, i) => $"{SqliteIdentifier.Quote(c)} = @v{i}"))} " +
                $"WHERE {BuildKeyPredicate(keyColumns!)};";
        }
        else
        {
            command.CommandText = $"DELETE FROM {target} WHERE {BuildKeyPredicate(keyColumns!)};";
        }

        AddParameters(command, "@v", setColumns, values);
        AddParameters(command, "@k", keyColumns, key);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static List<string>? ValidateColumns(
        IReadOnlyDictionary<string, object?>? requested,
        TableDefinition definition,
        bool forWrite)
    {
        if (requested is null) return null;
        var result = new List<string>(requested.Count);
        foreach (var requestedName in requested.Keys)
        {
            var column = definition.Columns.FirstOrDefault(
                c => string.Equals(c.Name, requestedName, StringComparison.OrdinalIgnoreCase))
                ?? throw new GridletValidationException(
                    $"Column '{requestedName}' does not exist on {definition.Object.Schema}.{definition.Object.Name}.");
            if (forWrite && (column.IsIdentity || column.IsComputed))
            {
                throw new GridletValidationException(
                    $"Column '{requestedName}' is {(column.IsIdentity ? "an identity" : "a computed")} column and cannot be written.");
            }

            result.Add(column.Name);
        }

        return result;
    }

    private static string BuildKeyPredicate(IReadOnlyList<string> columns)
        => string.Join(" AND ", columns.Select((column, index) =>
            $"{SqliteIdentifier.Quote(column)} IS @k{index}"));

    private static void AddParameters(
        SqliteCommand command,
        string prefix,
        IReadOnlyList<string>? columns,
        IReadOnlyDictionary<string, object?>? values)
    {
        if (columns is null || values is null) return;
        for (var i = 0; i < columns.Count; i++)
        {
            var pair = values.First(p => string.Equals(p.Key, columns[i], StringComparison.OrdinalIgnoreCase));
            command.Parameters.AddWithValue(prefix + i, pair.Value ?? DBNull.Value);
        }
    }
}
