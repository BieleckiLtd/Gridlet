namespace Gridlet.Abstractions;

/// <summary>
/// The provider boundary. Each database engine (SQL Server, and later PostgreSQL, MySQL,
/// SQLite, ...) ships one implementation composed of stateless capability services that
/// receive a <see cref="Models.GridletConnectionContext"/> per call.
/// </summary>
public interface IGridletProvider
{
    /// <summary>Unique provider name matched against <see cref="GridletConnectionOptions.ProviderName"/>.</summary>
    GridletProviderNames ProviderName { get; }

    /// <summary>Reads databases, objects, and object structure.</summary>
    ISchemaReader Schema { get; }

    /// <summary>Reads table/view data in pages.</summary>
    ITableDataService Data { get; }

    /// <summary>Executes ad-hoc SQL authored by the user.</summary>
    IQueryRunner Query { get; }

    /// <summary>Row-level writes (INSERT/UPDATE/DELETE).</summary>
    ITableWriteService Writes { get; }

    /// <summary>Schema changes driven by the table designer.</summary>
    ITableDdlService Ddl { get; }
}

/// <summary>
/// Optional provider metadata used to tailor the Gridlet UI. Keeping this separate from
/// <see cref="IGridletProvider"/> preserves compatibility with existing third-party providers.
/// </summary>
public interface IGridletProviderMetadata
{
    Models.GridletProviderCapabilities Capabilities { get; }
}

/// <summary>
/// Optional provider metadata used to tell database agents which SQL dialect and engine version
/// they are working with. Providers that do not implement this interface remain supported.
/// </summary>
public interface IGridletDatabaseSystemInfoProvider
{
    /// <summary>Returns non-secret information about the database engine for the target connection.</summary>
    Task<Models.GridletDatabaseSystemInfo> GetDatabaseSystemInfoAsync(
        Models.GridletConnectionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional narrow lookup capability used to render and edit declared foreign keys without
/// returning unrelated columns from the referenced table.
/// </summary>
public interface IForeignKeyLookupProvider
{
    Task<IReadOnlyList<Models.ForeignKeyLookupItem>> LookupForeignKeyAsync(
        Models.GridletConnectionContext context,
        string schema,
        string table,
        string keyColumn,
        string labelColumn,
        IReadOnlyList<object?> keys,
        string? search,
        int limit,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional bulk-import capability. Providers commit every supplied row as one unit.</summary>
public interface ITableImportProvider
{
    Task<Models.TableImportResult> ImportAsync(
        Models.GridletConnectionContext context,
        string schema,
        string table,
        Models.TableImport import,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional sequence creation and restart capability.</summary>
public interface ISequenceProvider
{
    Task CreateSequenceAsync(
        Models.GridletConnectionContext context,
        Models.SequenceDesign design,
        CancellationToken cancellationToken = default);

    Task RestartSequenceAsync(
        Models.GridletConnectionContext context,
        string schema,
        string name,
        string value,
        CancellationToken cancellationToken = default);
}
