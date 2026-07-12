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
        SqliteIdentifier.RequireMainSchema(schema);
        var qualifiedName = SqliteIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, name, cancellationToken);

        string? sortColumn = null;
        if (!string.IsNullOrWhiteSpace(request.SortColumn))
        {
            sortColumn = definition.Columns.FirstOrDefault(
                c => string.Equals(c.Name, request.SortColumn, StringComparison.OrdinalIgnoreCase))?.Name
                ?? throw new GridletValidationException(
                    $"Sort column '{request.SortColumn}' does not exist on {qualifiedName}.");
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"SELECT COUNT(*) FROM {qualifiedName};";
        var totalRows = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = connection.CreateCommand();
        var orderBy = sortColumn is null
            ? ""
            : $" ORDER BY {SqliteIdentifier.Quote(sortColumn)} " +
              (request.SortDirection == SortDirection.Descending ? "DESC" : "ASC");
        command.CommandText = $"SELECT * FROM {qualifiedName}{orderBy} LIMIT @pageSize OFFSET @offset;";
        command.Parameters.AddWithValue("@pageSize", request.PageSize);
        command.Parameters.AddWithValue("@offset", (long)(request.Page - 1) * request.PageSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)))
            .ToArray();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = SqliteValues.Materialize(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return new TableDataPage(columns, rows, request.Page, request.PageSize, totalRows);
    }
}
