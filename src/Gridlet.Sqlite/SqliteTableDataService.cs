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
            connection, definition, name, cancellationToken);
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

    private static async Task<(bool IsRowIdTable, string? Alias)> GetRowIdOrderingAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        TableDefinition definition,
        string table,
        CancellationToken cancellationToken)
    {
        if (definition.Object.Type != DbObjectType.Table)
        {
            return (false, null);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM main.sqlite_schema WHERE type = 'table' AND name = @table;";
        command.Parameters.AddWithValue("@table", table);
        var createSql = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        if (SqliteSqlInspection.ContainsKeywordSequence(createSql, "WITHOUT", "ROWID"))
        {
            return (false, null);
        }

        string[] aliases = ["rowid", "_rowid_", "oid"];
        return (true, aliases.FirstOrDefault(alias => definition.Columns.All(column =>
            !string.Equals(column.Name, alias, StringComparison.OrdinalIgnoreCase))));
    }
}
