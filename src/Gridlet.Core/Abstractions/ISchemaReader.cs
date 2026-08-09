using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>Reads schema metadata for one database engine.</summary>
public interface ISchemaReader
{
    /// <summary>Lists databases visible on the connection.</summary>
    Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Lists user tables, views, stored procedures, functions, and triggers in the target database.</summary>
    Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Lists database schemas, including empty schemas.</summary>
    Task<IReadOnlyList<SchemaInfo>> GetSchemasAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Returns columns, indexes, and foreign keys for a table or view.</summary>
    Task<TableDefinition> GetTableDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the source text of a view, stored procedure, function, or trigger, or <c>null</c> when unavailable.</summary>
    Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the parameters of a stored procedure or function, in declaration order, with the
    /// return value first where the engine has one. Providers with no routines return nothing.
    /// </summary>
    Task<RoutineDefinition> GetRoutineDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
        => throw new GridletValidationException(
            "This provider does not describe stored procedures or functions.");

    /// <summary>
    /// Builds a runnable script that calls <paramref name="routine"/> with the supplied arguments.
    /// The reader owns this because calling a routine is dialect-specific in the same way its
    /// metadata is: the script has to declare output parameters, capture a return value, and quote
    /// each argument for its declared type.
    /// </summary>
    /// <param name="routine">The routine and its parameters, as returned by <see cref="GetRoutineDefinitionAsync"/>.</param>
    /// <param name="arguments">
    /// The value for each parameter by name. A parameter that is absent from the map is left out of
    /// the call so the routine's own default applies.
    /// </param>
    string BuildRoutineExecuteScript(
        RoutineDefinition routine,
        IReadOnlyDictionary<string, RoutineArgument> arguments)
        => throw new GridletValidationException(
            "This provider cannot script calls to stored procedures or functions.");
}
