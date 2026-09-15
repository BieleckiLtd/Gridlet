using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

public sealed class SqliteTableDataService : ITableDataService
{
    public async Task<TableDataPage> GetPageAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        TableDataRequest request,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        var qualifiedName = SqliteIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, schema, name, cancellationToken);

        string? sortColumn = null;
        if (!string.IsNullOrWhiteSpace(request.SortColumn))
        {
            sortColumn = definition.Columns.FirstOrDefault(
                c => string.Equals(c.Name, request.SortColumn, StringComparison.OrdinalIgnoreCase))?.Name
                ?? throw new GridletValidationException(
                    $"Sort column '{request.SortColumn}' does not exist on {qualifiedName}.");
        }

        var filter = SqliteFilterBuilder.Build(request.Filters, definition.Columns, schema, name);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {qualifiedName}{filter.Clause};";
        AddFilterParameters(countCommand, filter.Parameters);
        var totalRows = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        var orderByColumns = new List<string>();
        if (sortColumn is not null)
        {
            orderByColumns.Add(
                $"{SqliteIdentifier.Quote(sortColumn)} " +
                (request.SortDirection == SortDirection.Descending ? "DESC" : "ASC"));
        }

        var primaryKey = definition.Indexes.FirstOrDefault(index => index.IsPrimaryKey);
        if (primaryKey is not null)
        {
            foreach (var primaryKeyColumn in primaryKey.Columns)
            {
                if (string.Equals(primaryKeyColumn, sortColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                orderByColumns.Add($"{SqliteIdentifier.Quote(primaryKeyColumn)} ASC");
            }
        }

        var rowIdOrdering = await GetRowIdOrderingAsync(
            connection, definition, schema, name, cancellationToken);
        if (rowIdOrdering.Alias is not null)
        {
            orderByColumns.Add($"{SqliteIdentifier.Quote(rowIdOrdering.Alias)} ASC");
        }
        else if (rowIdOrdering.IsRowIdTable)
        {
            var orderedVisibleColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sortColumn is not null)
            {
                orderedVisibleColumns.Add(sortColumn);
            }
            if (primaryKey is not null)
            {
                orderedVisibleColumns.UnionWith(primaryKey.Columns);
            }

            foreach (var column in definition.Columns)
            {
                if (orderedVisibleColumns.Add(column.Name))
                {
                    orderByColumns.Add($"{SqliteIdentifier.Quote(column.Name)} ASC");
                }
            }
        }

        var orderBy = orderByColumns.Count == 0
            ? ""
            : $" ORDER BY {string.Join(", ", orderByColumns)}";

        // A rowid identity is not one of the table's columns, so it is selected as an extra trailing
        // field and split back out of every row below.
        var identity = definition.RowIdentity;
        var rowIdKey = identity?.Kind == RowIdentityKinds.RowId ? identity.Columns[0] : null;
        var selectList = rowIdKey is null ? "*" : $"*, {SqliteIdentifier.Quote(rowIdKey)}";
        command.CommandText =
            $"SELECT {selectList} FROM {qualifiedName}{filter.Clause}{orderBy} LIMIT @pageSize OFFSET @offset;";
        command.Parameters.AddWithValue("@pageSize", request.PageSize);
        command.Parameters.AddWithValue("@offset", (long)(request.Page - 1) * request.PageSize);
        AddFilterParameters(command, filter.Parameters);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var visibleFieldCount = reader.FieldCount - (rowIdKey is null ? 0 : 1);
        var columns = Enumerable.Range(0, visibleFieldCount)
            .Select(i => new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)))
            .ToArray();
        int[]? keyOrdinals = rowIdKey is not null
            ? [visibleFieldCount]
            : KeyOrdinals(identity, columns);
        var rows = new List<object?[]>();
        var rowKeys = keyOrdinals is null ? null : new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[visibleFieldCount];
            for (var i = 0; i < visibleFieldCount; i++)
            {
                row[i] = SqliteValues.Materialize(reader.GetValue(i));
            }

            rows.Add(row);
            if (keyOrdinals is not null)
            {
                rowKeys!.Add(keyOrdinals
                    .Select(ordinal => SqliteValues.Materialize(reader.GetValue(ordinal)))
                    .ToArray());
            }
        }

        return new TableDataPage(
            columns, rows, request.Page, request.PageSize, totalRows,
            keyOrdinals is null ? null : identity,
            rowKeys);
    }

    public async Task<ColumnProfile> GetColumnProfileAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        ColumnProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        var qualifiedName = SqliteIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(
            connection, schema, name, cancellationToken);
        var column = definition.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.Column, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletValidationException(
                $"Profile column '{request.Column}' does not exist on {qualifiedName}.");
        if (column.IsHidden)
        {
            throw new GridletValidationException(
                $"Hidden column '{column.Name}' is not available in table data.");
        }

        var quotedColumn = SqliteIdentifier.Quote(column.Name);
        var filter = SqliteFilterBuilder.Build(
            request.Filters, definition.Columns, schema, name);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await using var aggregate = connection.CreateCommand();
        aggregate.Transaction = transaction;
        aggregate.CommandText =
            $"SELECT COUNT(*), COUNT({quotedColumn}), COUNT(DISTINCT {quotedColumn}), " +
            $"MIN({quotedColumn}), MAX({quotedColumn}) FROM {qualifiedName}{filter.Clause};";
        AddFilterParameters(aggregate, filter.Parameters);
        long totalCount;
        long nonNullCount;
        long distinctCount;
        object? minimum;
        object? maximum;
        await using (var reader = await aggregate.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            totalCount = reader.GetInt64(0);
            nonNullCount = reader.GetInt64(1);
            distinctCount = reader.GetInt64(2);
            minimum = SqliteValues.Materialize(reader.GetValue(3));
            maximum = SqliteValues.Materialize(reader.GetValue(4));
        }

        await using var top = connection.CreateCommand();
        top.Transaction = transaction;
        top.CommandText =
            $"SELECT {quotedColumn}, COUNT(*) AS frequency FROM {qualifiedName}{filter.Clause} " +
            $"GROUP BY {quotedColumn} ORDER BY 2 DESC, 1 ASC LIMIT @topValues;";
        AddFilterParameters(top, filter.Parameters);
        top.Parameters.AddWithValue("@topValues", Math.Clamp(request.TopValues, 1, 50));
        var topValues = new List<ColumnProfileValue>();
        await using (var topReader = await top.ExecuteReaderAsync(cancellationToken))
        {
            while (await topReader.ReadAsync(cancellationToken))
            {
                topValues.Add(new ColumnProfileValue(
                    SqliteValues.Materialize(topReader.GetValue(0)), topReader.GetInt64(1)));
            }
        }
        await transaction.CommitAsync(cancellationToken);

        return new ColumnProfile(
            column.Name,
            column.DataType,
            totalCount,
            totalCount - nonNullCount,
            distinctCount,
            minimum,
            maximum,
            topValues);
    }

    public async Task<ColumnFilterValues> GetColumnFilterValuesAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        ColumnFilterValuesRequest request,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        var qualifiedName = SqliteIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(
            connection, schema, name, cancellationToken);
        var column = definition.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.Column, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletValidationException(
                $"Filter column '{request.Column}' does not exist on {qualifiedName}.");
        if (column.IsHidden)
        {
            throw new GridletValidationException(
                $"Hidden column '{column.Name}' is not available in table data.");
        }

        var filter = SqliteFilterBuilder.Build(request.Filters, definition.Columns, schema, name);
        var limit = Math.Clamp(request.Limit, 1, 10_000);
        var search = string.IsNullOrEmpty(request.Search) ? null : request.Search;
        await using var command = connection.CreateCommand();
        command.CommandText = SqliteFilterBuilder.BuildFilterValuesSql(
            schema, name, column, filter.Clause, search is not null);
        AddFilterParameters(command, filter.Parameters);
        // One more than the limit, so a full list can be told apart from a cut-off one.
        command.Parameters.AddWithValue("@limit", limit + 1);
        if (search is not null)
        {
            command.Parameters.AddWithValue("@search", SqliteFilterBuilder.BuildFilterValuesSearch(search));
        }

        var values = new List<object?>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(SqliteValues.Materialize(reader.GetValue(0)));
        }

        await reader.NextResultAsync(cancellationToken);
        var hasBlanks = await reader.ReadAsync(cancellationToken) && reader.GetInt64(0) != 0;
        var isTruncated = values.Count > limit;
        if (isTruncated)
        {
            values.RemoveAt(limit);
        }

        return new ColumnFilterValues(values, hasBlanks, isTruncated);
    }

    public async Task<string> GetFilterSqlAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        IReadOnlyList<TableDataFilter>? filters,
        string? sortColumn,
        SortDirection sortDirection,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(
            connection, schema, name, cancellationToken);
        return SqliteFilterBuilder.BuildFilterDisplaySql(
            filters, definition.Columns, schema, name, sortColumn, sortDirection);
    }

    private static void AddFilterParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
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

    private static async Task<(bool IsRowIdTable, string? Alias)> GetRowIdOrderingAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        TableDefinition definition,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        if (definition.Object.Type != DbObjectType.Table
            || definition.Object.SubKind is "virtual" or "shadow")
        {
            return (false, null);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT wr FROM pragma_table_list WHERE schema = @schema AND name = @table AND type = 'table';";
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        var withoutRowId = await command.ExecuteScalarAsync(cancellationToken);
        if (withoutRowId is null or DBNull || Convert.ToInt64(withoutRowId) != 0)
        {
            return (false, null);
        }

        return (true, SqliteRowIdentity.RowIdAliases.FirstOrDefault(alias => definition.Columns.All(
            column => !string.Equals(column.Name, alias, StringComparison.OrdinalIgnoreCase))));
    }
}
