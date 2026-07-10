using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Schema changes (CREATE/ALTER/DROP) driven by the table designer.</summary>
public interface ITableDdlService
{
    Task CreateTableAsync(
        GridletConnectionContext context,
        TableDesign design,
        CancellationToken cancellationToken = default);

    Task AddColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ColumnDesign column,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames and/or retypes an existing column. <paramref name="columnName"/> is the current
    /// name; <paramref name="column"/> carries the new definition (an empty
    /// <see cref="ColumnDesign.DataType"/> means rename-only).
    /// </summary>
    Task AlterColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string columnName,
        ColumnDesign column,
        CancellationToken cancellationToken = default);

    Task DropColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string columnName,
        CancellationToken cancellationToken = default);

    Task DropTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default);
}
