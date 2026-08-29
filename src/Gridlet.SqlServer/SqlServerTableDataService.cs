using System.Globalization;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;
using System.Data;

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

        var filter = SqlServerSqlBuilder.BuildFilterClause(request.Filters, columnNames);

        long totalRows;
        await using (var countCommand = connection.CreateCommand())
        {
            countCommand.CommandText = SqlServerSqlBuilder.BuildCountSql(schema, name, filter.Clause);
            AddFilterParameters(countCommand, filter.Parameters);
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
            rowIdentity?.Columns,
            filter.Clause);
        command.Parameters.AddWithValue("@Offset", (long)(request.Page - 1) * request.PageSize);
        command.Parameters.AddWithValue("@PageSize", request.PageSize);
        AddFilterParameters(command, filter.Parameters);

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

    public async Task<ColumnProfile> GetColumnProfileAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        ColumnProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        string? columnName = null;
        string? dataType = null;
        string? systemType = null;
        var objectExists = false;
        var filterColumnNames = new List<string>();
        await using (var metadata = connection.CreateCommand())
        {
            metadata.CommandText =
                "SELECT c.name, TYPE_NAME(c.user_type_id), TYPE_NAME(c.system_type_id), " +
                "CONVERT(int, ISNULL(COLUMNPROPERTY(c.object_id, c.name, 'IsHidden'), 0)) " +
                "FROM sys.columns c WHERE c.object_id = OBJECT_ID(@object) ORDER BY c.column_id;";
            metadata.Parameters.AddWithValue("@object", qualifiedName);
            await using var reader = await metadata.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                objectExists = true;
                var candidateName = reader.GetString(0);
                var isHidden = reader.GetInt32(3) != 0;
                filterColumnNames.Add(candidateName);
                if (!string.Equals(candidateName, request.Column, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                columnName = candidateName;
                dataType = reader.IsDBNull(1) ? null : reader.GetString(1);
                systemType = reader.IsDBNull(2) ? dataType : reader.GetString(2);
                if (isHidden)
                {
                    throw new GridletValidationException(
                        $"Hidden column '{columnName}' is not available in table data.");
                }
            }

            if (!objectExists)
            {
                throw new GridletObjectNotFoundException(qualifiedName);
            }
            if (columnName is null)
            {
                throw new GridletValidationException(
                    $"Profile column '{request.Column}' does not exist on {qualifiedName}.");
            }
            if (string.IsNullOrWhiteSpace(dataType))
            {
                throw new GridletValidationException(
                    $"The type of profile column '{columnName}' could not be determined.");
            }
        }

        var (canGroup, canRange) = SqlServerSqlBuilder.GetProfileCapabilities(systemType);
        var filter = SqlServerSqlBuilder.BuildFilterClause(request.Filters, filterColumnNames);
        var profileTransaction = await BeginProfileTransactionAsync(connection, cancellationToken);
        await using var transaction = profileTransaction.Transaction;

        await using var aggregate = connection.CreateCommand();
        aggregate.Transaction = transaction;
        aggregate.CommandText = SqlServerSqlBuilder.BuildProfileAggregateSql(
            schema, name, columnName, filter.Clause, canGroup, canRange);
        AddFilterParameters(aggregate, filter.Parameters);
        long totalCount;
        long nonNullCount;
        long? distinctCount;
        object? minimum;
        object? maximum;
        await using (var reader = await aggregate.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            totalCount = reader.GetInt64(0);
            nonNullCount = reader.GetInt64(1);
            distinctCount = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            minimum = SqlServerValues.Materialize(reader.GetValue(3));
            maximum = SqlServerValues.Materialize(reader.GetValue(4));
        }

        var topValues = new List<ColumnProfileValue>();
        if (canGroup && profileTransaction.HasConsistentSnapshot)
        {
            await using var top = connection.CreateCommand();
            top.Transaction = transaction;
            top.CommandText = SqlServerSqlBuilder.BuildProfileTopValuesSql(
                schema, name, columnName, filter.Clause);
            top.Parameters.AddWithValue("@topValues", Math.Clamp(request.TopValues, 1, 50));
            AddFilterParameters(top, filter.Parameters);
            await using (var reader = await top.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    topValues.Add(new ColumnProfileValue(
                        SqlServerValues.Materialize(reader.GetValue(0)), reader.GetInt64(1)));
                }
            }
        }
        await transaction.CommitAsync(cancellationToken);

        var limitations = new List<string>();
        if (!canGroup)
        {
            limitations.Add(
                $"The {dataType} type cannot be grouped; distinct count, range, and top values are unavailable.");
        }
        else
        {
            if (!canRange)
            {
                limitations.Add($"The {dataType} type does not support MIN/MAX; range is unavailable.");
            }
            if (!profileTransaction.HasConsistentSnapshot)
            {
                limitations.Add(
                    "Top values require snapshot isolation to remain consistent with the aggregate profile; " +
                    "enable ALLOW_SNAPSHOT_ISOLATION for this database to include them.");
            }
        }
        var limitation = limitations.Count == 0 ? null : string.Join(" ", limitations);
        return new ColumnProfile(
            columnName,
            dataType,
            totalCount,
            totalCount - nonNullCount,
            distinctCount,
            minimum,
            maximum,
            topValues,
            limitation);
    }

    private static async Task<(SqlTransaction Transaction, bool HasConsistentSnapshot)>
        BeginProfileTransactionAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var snapshotStatus = connection.CreateCommand();
        snapshotStatus.CommandText =
            "SELECT snapshot_isolation_state FROM sys.databases WHERE database_id = DB_ID();";
        var state = Convert.ToInt32(
            await snapshotStatus.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        // Snapshot gives both profile statements one non-blocking database view when enabled.
        // Read committed is the safe fallback for databases that have not opted into snapshot
        // isolation; unlike serializable, it cannot hold range locks across two full scans.
        var isolation = state == 1 ? IsolationLevel.Snapshot : IsolationLevel.ReadCommitted;
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            isolation, cancellationToken);
        return (transaction, state == 1);
    }

    private static void AddFilterParameters(
        Microsoft.Data.SqlClient.SqlCommand command,
        IReadOnlyList<(string Name, object? Value)> parameters)
    {
        foreach (var (parameterName, value) in parameters)
        {
            command.Parameters.AddWithValue(parameterName, value ?? DBNull.Value);
        }
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
