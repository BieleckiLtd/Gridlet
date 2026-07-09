using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Reads schema metadata for one database engine.</summary>
public interface ISchemaReader
{
    /// <summary>Lists databases visible on the connection.</summary>
    Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Lists user tables, views, stored procedures, and functions in the target database.</summary>
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Returns columns, indexes, and foreign keys for a table or view.</summary>
    Task<TableDefinition> GetTableDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the source text of a view, stored procedure, or function, or <c>null</c> when unavailable.</summary>
    Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default);
}
