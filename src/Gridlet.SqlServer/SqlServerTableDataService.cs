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
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                """
                SELECT c.name, CONVERT(int, ISNULL(pk.key_ordinal, 0))
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
                """;
            columnsCommand.Parameters.AddWithValue("@name", qualifiedName);

            await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                var columnName = columnsReader.GetString(0);
                columnNames.Add(columnName);
                var keyOrdinal = columnsReader.GetInt32(1);
                if (keyOrdinal > 0)
                {
                    primaryKeyColumns.Add((columnName, keyOrdinal));
                }
            }
        }

        if (columnNames.Count == 0)
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

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
            primaryKeyColumns.OrderBy(column => column.KeyOrdinal).Select(column => column.Name).ToArray());
        command.Parameters.AddWithValue("@Offset", (long)(request.Page - 1) * request.PageSize);
        command.Parameters.AddWithValue("@PageSize", request.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = new ResultColumn[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns[i] = new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i));
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = SqlServerValues.Materialize(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return new TableDataPage(columns, rows, request.Page, request.PageSize, totalRows);
    }
}
