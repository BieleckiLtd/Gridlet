using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Schema changes (CREATE/ALTER/DROP) driven by the table designer.</summary>
public interface ITableDdlService
{
    Task CreateSchemaAsync(
        GridletConnectionContext context,
        SchemaDesign design,
        CancellationToken cancellationToken = default);

    Task AlterSchemaOwnerAsync(
        GridletConnectionContext context,
        string schema,
        string owner,
        CancellationToken cancellationToken = default);

    Task DropSchemaAsync(
        GridletConnectionContext context,
        string schema,
        CancellationToken cancellationToken = default);

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

    Task AddPrimaryKeyAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        PrimaryKeyDesign primaryKey,
        CancellationToken cancellationToken = default);

    Task AddForeignKeyAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ForeignKeyDesign foreignKey,
        CancellationToken cancellationToken = default);

    Task DropConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string constraintName,
        CancellationToken cancellationToken = default);

    Task DropTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default);

    Task DropObjectAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        DbObjectType type,
        CancellationToken cancellationToken = default);
}
