using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerTableWriteService : ITableWriteService
{
    public Task<int> InsertRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, values, key: null,
            (setColumns, _) => SqlServerDmlBuilder.BuildInsert(schema, table, setColumns!),
            cancellationToken);

    public Task<int> UpdateRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, values, key,
            (setColumns, keyColumns) => SqlServerDmlBuilder.BuildUpdate(schema, table, setColumns!, keyColumns!),
            cancellationToken);

    public Task<int> DeleteRowAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?> key,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, schema, table, values: null, key,
            (_, keyColumns) => SqlServerDmlBuilder.BuildDelete(schema, table, keyColumns!),
            cancellationToken);

    private static async Task<int> ExecuteAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IReadOnlyDictionary<string, object?>? values,
        IReadOnlyDictionary<string, object?>? key,
        Func<IReadOnlyList<string>?, IReadOnlyList<string>?, string> buildSql,
        CancellationToken cancellationToken)
    {
        if (values is { Count: 0 })
        {
            throw new GridletValidationException("No column values were supplied.");
        }

        if (key is { Count: 0 })
        {
            throw new GridletValidationException("No key columns were supplied to identify the row.");
        }

        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, table);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);

        // Validate every incoming column name against live metadata before it reaches dynamic SQL.
        var columns = await LoadColumnsAsync(connection, qualifiedName, cancellationToken);
        if (columns.Count == 0)
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

        var setColumns = ValidateColumns(values, columns, forWrite: true, qualifiedName);
        var keyColumns = ValidateColumns(key, columns, forWrite: false, qualifiedName);

        await using var command = connection.CreateCommand();
        command.CommandText = buildSql(setColumns, keyColumns);
        if (setColumns is not null)
        {
            for (var i = 0; i < setColumns.Count; i++)
            {
                command.Parameters.AddWithValue("@v" + i, values![setColumns[i]] ?? DBNull.Value);
            }
        }

        if (keyColumns is not null)
        {
            for (var i = 0; i < keyColumns.Count; i++)
            {
                command.Parameters.AddWithValue("@k" + i, key![keyColumns[i]] ?? DBNull.Value);
            }
        }

        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    /// <summary>Returns matched canonical column names, rejecting unknown / identity / computed columns.</summary>
    private static List<string>? ValidateColumns(
        IReadOnlyDictionary<string, object?>? requested,
        Dictionary<string, (bool IsIdentity, bool IsComputed)> columns,
        bool forWrite,
        string qualifiedName)
    {
        if (requested is null)
        {
            return null;
        }

        var result = new List<string>(requested.Count);
        foreach (var name in requested.Keys)
        {
            if (!columns.TryGetValue(name, out var info))
            {
                throw new GridletValidationException($"Column '{name}' does not exist on {qualifiedName}.");
            }

            if (forWrite && (info.IsIdentity || info.IsComputed))
            {
                throw new GridletValidationException($"Column '{name}' is {(info.IsIdentity ? "an identity" : "a computed")} column and cannot be written.");
            }

            result.Add(name);
        }

        return result;
    }

    private static async Task<Dictionary<string, (bool IsIdentity, bool IsComputed)>> LoadColumnsAsync(
        SqlConnection connection, string qualifiedName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.name, c.is_identity, c.is_computed FROM sys.columns c WHERE c.object_id = OBJECT_ID(@name);";
        command.Parameters.AddWithValue("@name", qualifiedName);

        var columns = new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));
        }

        return columns;
    }
}
