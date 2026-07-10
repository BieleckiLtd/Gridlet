namespace Gridlet.Abstractions;

/// <summary>
/// The provider boundary. Each database engine (SQL Server, and later PostgreSQL, MySQL,
/// SQLite, ...) ships one implementation composed of stateless capability services that
/// receive a <see cref="Models.GridletConnectionContext"/> per call.
/// </summary>
public interface IGridletProvider
{
    /// <summary>Unique provider name matched against <see cref="GridletConnectionOptions.ProviderName"/>.</summary>
    string ProviderName { get; }

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
