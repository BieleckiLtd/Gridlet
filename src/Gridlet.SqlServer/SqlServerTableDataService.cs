using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

public sealed class SqlServerTableDataService : ITableDataService
{
    public async Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        TableDataRequest request,
        CancellationToken cancellationToken = default)
    {
        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, name);

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);

        // Validate the object exists and the sort column is a real column before any
        // identifier reaches dynamic SQL.
        var columnNames = new List<string>();
        var primaryKeyColumns = new List<(string Name, int KeyOrdinal)>();
        var nullableColumns = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var uniqueKeys = new List<SqlServerRowIdentity.UniqueKey>();
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                """
                SELECT c.name, CONVERT(int, ISNULL(pk.key_ordinal, 0)), c.is_nullable
                FROM sys.columns c
                LEFT JOIN (
                    SELECT ic.object_id, ic.column_id, ic.key_ordinal
                    FROM sys.indexes i
                    JOIN sys.index_columns ic
                      ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    WHERE i.is_primary_key = 1 AND ic.key_ordinal > 0
                ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
                WHERE c.object_id = OBJECT_ID(@name)
                ORDER BY c.column_id;

                SELECT i.index_id, i.name, i.is_disabled,
                       CASE WHEN i.filter_definition IS NULL THEN 0 ELSE 1 END AS is_filtered,
                       col.name AS column_name
                FROM sys.indexes i
                JOIN sys.index_columns ic
                  ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
                JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(@name)
                  AND i.is_unique = 1
                  AND i.is_primary_key = 0
                  AND i.type IN (1, 2)
                  AND EXISTS (SELECT 1 FROM sys.tables t WHERE t.object_id = i.object_id)
                ORDER BY i.index_id, ic.key_ordinal;
                """;
            columnsCommand.Parameters.AddWithValue("@name", qualifiedName);

            await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                var columnName = columnsReader.GetString(0);
                columnNames.Add(columnName);
                nullableColumns[columnName] = columnsReader.GetBoolean(2);
                var keyOrdinal = columnsReader.GetInt32(1);
                if (keyOrdinal > 0)
                {
                    primaryKeyColumns.Add((columnName, keyOrdinal));
                }
            }

            await columnsReader.NextResultAsync(cancellationToken);
            var uniqueKeyColumns = new Dictionary<int, (string Name, bool IsDisabled, bool IsFiltered, List<string> Columns)>();
            var uniqueKeyOrder = new List<int>();
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                var indexId = columnsReader.GetInt32(0);
                if (!uniqueKeyColumns.TryGetValue(indexId, out var entry))
                {
                    entry = (
                        columnsReader.IsDBNull(1) ? $"index_id {indexId}" : columnsReader.GetString(1),
                        columnsReader.GetBoolean(2),
                        columnsReader.GetInt32(3) != 0,
                        []);
                    uniqueKeyColumns[indexId] = entry;
                    uniqueKeyOrder.Add(indexId);
                }

                entry.Columns.Add(columnsReader.GetString(4));
            }

            uniqueKeys.AddRange(uniqueKeyOrder.Select(indexId => new SqlServerRowIdentity.UniqueKey(
                uniqueKeyColumns[indexId].Name,
                uniqueKeyColumns[indexId].Columns,
                uniqueKeyColumns[indexId].IsDisabled,
                uniqueKeyColumns[indexId].IsFiltered)));
        }

        if (columnNames.Count == 0)
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

        var rowIdentity = SqlServerRowIdentity.Resolve(
            primaryKeyColumns.OrderBy(column => column.KeyOrdinal).Select(column => column.Name).ToArray(),
            uniqueKeys,
            nullableColumns);

        string? sortColumn = null;
        if (!string.IsNullOrEmpty(request.SortColumn))
        {
            sortColumn = columnNames.FirstOrDefault(
                c => string.Equals(c, request.SortColumn, StringComparison.OrdinalIgnoreCase))
                ?? throw new GridletValidationException(
                    $"Sort column '{request.SortColumn}' does not exist on {qualifiedName}.");
        }

        long totalRows;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = SqlServerSqlBuilder.BuildCountSql(schema, name);
            totalRows = (long)(await countCommand.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = SqlServerSqlBuilder.BuildPageSql(
            schema,
            name,
            sortColumn,
            request.SortDirection,
            // The identity columns are unique and non-nullable, so they order pages deterministically
            // even on a heap, where there is no primary key to fall back on.
            rowIdentity?.Columns);
        command.Parameters.AddWithValue("@Offset", (long)(request.Page - 1) * request.PageSize);
        command.Parameters.AddWithValue("@PageSize", request.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new ResultColumn[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i));
        }

        var keyOrdinals = KeyOrdinals(rowIdentity, columns);
        var rows = new List<object?[]>();
        var rowKeys = keyOrdinals is null ? null : new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = SqlServerValues.Materialize(reader.GetValue(i));
            }

            rows.Add(row);
            rowKeys?.Add(keyOrdinals!.Select(ordinal => row[ordinal]).ToArray());
        }

        return new TableDataPage(
            columns, rows, request.Page, request.PageSize, totalRows,
            keyOrdinals is null ? null : rowIdentity,
            rowKeys);
    }

    /// <summary>
    /// Maps an identity's columns onto positions in the result, or returns <see langword="null"/>
    /// when the result does not carry every identifying value.
    /// </summary>
    private static int[]? KeyOrdinals(RowIdentityInfo? identity, IReadOnlyList<ResultColumn> columns)
    {
        if (identity is null) return null;
        var ordinals = new int[identity.Columns.Count];
        for (var i = 0; i < ordinals.Length; i++)
        {
            var name = identity.Columns[i];
            var ordinal = -1;
            for (var candidate = 0; candidate < columns.Count; candidate++)
            {
                if (string.Equals(columns[candidate].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    ordinal = candidate;
                    break;
                }
            }

            if (ordinal < 0) return null;
            ordinals[i] = ordinal;
        }

        return ordinals;
    }
}
