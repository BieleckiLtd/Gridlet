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

    Task AddCheckConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CheckConstraintDesign checkConstraint,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support CHECK constraint management.");

    Task DropCheckConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ConstraintReference constraint,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support CHECK constraint management.");

    Task AddUniqueConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        UniqueConstraintDesign uniqueConstraint,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support UNIQUE constraint management.");

    Task DropUniqueConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ConstraintReference constraint,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support UNIQUE constraint management.");

    Task CreateIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IndexDesign index,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support index management.");

    Task DropIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string indexName,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support index management.");

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

    /// <summary>
    /// Renames a table, view, procedure, function or trigger within its schema. Renaming does not
    /// rewrite anything that refers to the object, so a provider that cannot do it safely says so.
    /// </summary>
    Task RenameObjectAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        DbObjectType type,
        string newName,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support renaming objects.");

    /// <summary>Renames an index on a table.</summary>
    Task RenameIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string indexName,
        string newName,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support renaming indexes.");

    /// <summary>
    /// Deletes every row of a table, keeping the table itself. Providers use the cheapest statement
    /// the engine offers and fall back to a delete where truncation is not allowed.
    /// </summary>
    Task TruncateTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException("This provider does not support emptying a table.");
}
