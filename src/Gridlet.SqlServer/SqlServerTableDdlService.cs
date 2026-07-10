using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerTableDdlService : ITableDdlService
{
    public Task CreateTableAsync(
        GridletConnectionContext context, TableDesign design, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildCreateTable(design), cancellationToken);

    public Task AddColumnAsync(
        GridletConnectionContext context, string schema, string table, ColumnDesign column,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildAddColumn(schema, table, column), cancellationToken);

    public async Task AlterColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName, ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);

        try
        {
            if (!string.Equals(columnName, column.Name, StringComparison.Ordinal))
            {
                // sp_rename takes the names as string parameters — no dynamic SQL involved.
                await using var rename = connection.CreateCommand();
                rename.CommandText = "EXEC sp_rename @objname, @newname, 'COLUMN';";
                rename.Parameters.AddWithValue("@objname",
                    $"{SqlServerIdentifier.QuoteQualified(schema, table)}.{SqlServerIdentifier.Quote(columnName)}");
                rename.Parameters.AddWithValue("@newname", column.Name);
                await rename.ExecuteNonQueryAsync(cancellationToken);
            }

            // An empty data type means rename-only (e.g. identity columns, which cannot be retyped).
            if (!string.IsNullOrWhiteSpace(column.DataType))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = SqlServerDdlBuilder.BuildAlterColumn(schema, table, column);
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    public Task DropColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropColumn(schema, table, columnName), cancellationToken);

    public Task DropTableAsync(
        GridletConnectionContext context, string schema, string table, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropTable(schema, table), cancellationToken);

    private static async Task ExecuteAsync(
        GridletConnectionContext context, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }
}
