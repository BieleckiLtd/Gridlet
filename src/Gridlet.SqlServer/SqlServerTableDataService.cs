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
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                "SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID(@name) ORDER BY c.column_id;";
            columnsCommand.Parameters.AddWithValue("@name", qualifiedName);

            await using var columnsReader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await columnsReader.ReadAsync(cancellationToken))
            {
                columnNames.Add(columnsReader.GetString(0));
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
        command.CommandText = SqlServerSqlBuilder.BuildPageSql(schema, name, sortColumn, request.SortDirection);
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
