using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>The JSON API consumed by the embedded UI (and usable directly).</summary>
internal static partial class GridletApiEndpoints
{
    private static readonly string Version =
        typeof(GridletApiEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "dev";

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-_/]*$")]
    private static partial Regex RoutePattern();

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/meta", GetMeta);
        api.MapGet("/connections/{connection}/databases", GetDatabases);
        api.MapGet("/connections/{connection}/databases/{database}/objects", GetObjects);
        api.MapGet("/connections/{connection}/databases/{database}/schemas", GetSchemas);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/data", GetObjectData);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/data/stream", StreamObjectData);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/structure", GetObjectStructure);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/definition", GetObjectDefinition);
        api.MapPost("/connections/{connection}/databases/{database}/query", ExecuteQuery);

        // Row editing (POST for update/delete so the JSON body binds on every server).
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows", InsertRow);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows/update", UpdateRow);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows/delete", DeleteRow);

        // Table designer.
        api.MapPost("/connections/{connection}/databases/{database}/schemas", CreateSchema);
        api.MapPut("/connections/{connection}/databases/{database}/schemas/{schema}", AlterSchema);
        api.MapDelete("/connections/{connection}/databases/{database}/schemas/{schema}", DropSchema);
        api.MapPost("/connections/{connection}/databases/{database}/tables", CreateTable);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns", AddColumn);
        api.MapPut("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns/{column}", AlterColumn);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns/{column}", DropColumn);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}", DropObject);

        // Saved queries.
        api.MapGet("/queries", GetSavedQueries);
        api.MapPost("/queries", SaveQuery);
        api.MapDelete("/queries/{id}", DeleteSavedQuery);

        // Published endpoint administration (invocation lives in GridletPublishedEndpoints).
        api.MapGet("/published", GetPublishedEndpoints);
        api.MapPost("/published", SavePublishedEndpoint);
        api.MapDelete("/published/{id}", DeletePublishedEndpoint);
    }

    // ---- meta & schema ----

    private static IResult GetMeta(IOptionsMonitor<GridletOptions> options)
    {
        var connections = options.CurrentValue.Connections
            .Select(c => new GridletConnectionSummary(c.Name, c.ProviderName, c.AllowSqlExecution, c.AllowWrites, c.AllowDdl))
            .ToArray();
        return Results.Ok(new GridletMetaResponse(
            Version, connections, options.CurrentValue.Limits.MaxQueryResultRows));
    }

    private static Task<IResult> GetDatabases(
        string connection,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection);
            var databases = await resolved.Provider.Schema.GetDatabasesAsync(resolved.Context, cancellationToken);
            return Results.Ok(databases);
        });

    private static Task<IResult> GetObjects(
        string connection,
        string database,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var objects = await resolved.Provider.Schema.GetObjectsAsync(resolved.Context, cancellationToken);
            return Results.Ok(objects.Select(ToDto).ToArray());
        });

    private static Task<IResult> GetSchemas(
        string connection,
        string database,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            return Results.Ok(await resolved.Provider.Schema.GetSchemasAsync(resolved.Context, cancellationToken));
        });

    private static Task<IResult> GetObjectData(
        string connection,
        string database,
        string schema,
        string name,
        int? page,
        int? pageSize,
        string? sort,
        string? dir,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var limits = options.CurrentValue.Limits;
            var request = new TableDataRequest(
                Page: Math.Max(1, page ?? 1),
                PageSize: Math.Clamp(pageSize ?? limits.DefaultPageSize, 1, limits.MaxPageSize),
                SortColumn: string.IsNullOrWhiteSpace(sort) ? null : sort,
                SortDirection: string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
                    ? SortDirection.Descending
                    : SortDirection.Ascending);

            var resolved = resolver.Resolve(connection, database);
            var dataPage = await resolved.Provider.Data.GetPageAsync(
                resolved.Context, schema, name, request, cancellationToken);
            return Results.Ok(dataPage);
        });

    private static async Task StreamObjectData(
        string connection, string database, string schema, string name,
        int? maxRows, string? sort, string? dir,
        IGridletConnectionResolver resolver, IOptionsMonitor<GridletOptions> options,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var limits = options.CurrentValue.Limits;
        var cap = Math.Clamp(maxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
        var pageSize = Math.Min(500, limits.MaxPageSize);
        var resolved = resolver.Resolve(connection, database);
        var direction = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending : SortDirection.Ascending;
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

        async Task WriteAsync(QueryStreamEvent value)
        {
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, value, JsonSerializerOptions.Web, cancellationToken);
            await httpContext.Response.WriteAsync("\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            var emitted = 0;
            var page = 1;
            long totalRows = 0;
            do
            {
                var data = await resolved.Provider.Data.GetPageAsync(resolved.Context, schema, name,
                    new TableDataRequest(page, Math.Min(pageSize, cap - emitted), sort, direction), cancellationToken);
                totalRows = data.TotalRows;
                if (page == 1) await WriteAsync(new QueryStreamEvent("resultSet", 0, data.Columns));
                if (data.Rows.Count == 0) break;
                await WriteAsync(new QueryStreamEvent("rows", 0, Rows: data.Rows));
                emitted += data.Rows.Count;
                page++;
            }
            while (emitted < cap && emitted < totalRows);

            await WriteAsync(new QueryStreamEvent("resultSetCompleted", 0, Truncated: emitted < totalRows));
            await WriteAsync(new QueryStreamEvent("completed", RecordsAffected: emitted));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = ex is GridletObjectNotFoundException ? 404 : 400;
                await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(ex.Message), cancellationToken);
                return;
            }
            await TryWriteStreamEventAsync(httpContext, new QueryStreamEvent("error", Message: ex.Message));
        }
    }

    private static Task<IResult> GetObjectStructure(
        string connection,
        string database,
        string schema,
        string name,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var definition = await resolved.Provider.Schema.GetTableDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            return Results.Ok(new TableStructureResponse(
                ToDto(definition.Object), definition.Columns, definition.Indexes, definition.ForeignKeys));
        });

    private static Task<IResult> GetObjectDefinition(
        string connection,
        string database,
        string schema,
        string name,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var definition = await resolved.Provider.Schema.GetObjectDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            return Results.Ok(new ObjectDefinitionResponse(definition));
        });

    // ---- ad-hoc queries ----

    private static async Task ExecuteQuery(
        string connection,
        string database,
        QueryRequestBody body,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var resolved = resolver.Resolve(connection, database);
        if (!resolved.Context.Connection.AllowSqlExecution)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse($"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'."),
                cancellationToken);
            return;
        }

        var limits = options.CurrentValue.Limits;
        var maxRows = Math.Clamp(body.MaxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
        var sql = body.Sql ?? "";
        var stopwatch = Stopwatch.StartNew();
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        try
        {
            await foreach (var queryEvent in resolved.Provider.Query.StreamAsync(
                resolved.Context, sql,
                new QueryRequestOptions(maxRows, limits.CommandTimeoutSeconds),
                parameters: null, cancellationToken))
            {
                await JsonSerializer.SerializeAsync(httpContext.Response.Body, queryEvent,
                    JsonSerializerOptions.Web, cancellationToken);
                await httpContext.Response.WriteAsync("\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            await AuditAsync(audit, httpContext, "query.execute", connection, database, null, sql,
                succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
        }
        catch (OperationCanceledException)
        {
            // The browser's AbortController closes the request and cancellation reaches the provider.
        }
        catch (Exception ex)
        {
            await AuditAsync(audit, httpContext, "query.execute", connection, database, null, sql,
                succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = ex switch
                {
                    GridletUnknownConnectionException or GridletObjectNotFoundException => StatusCodes.Status404NotFound,
                    GridletValidationException or GridletQueryException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError,
                };
                await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(ex.Message), cancellationToken);
                return;
            }
            var error = new QueryStreamEvent("error", Message: ex.Message, DurationMs: stopwatch.ElapsedMilliseconds);
            await TryWriteStreamEventAsync(httpContext, error);
        }
    }

    private static async Task TryWriteStreamEventAsync(HttpContext httpContext, QueryStreamEvent value)
    {
        if (httpContext.RequestAborted.IsCancellationRequested) return;
        try
        {
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body, value, JsonSerializerOptions.Web, CancellationToken.None);
            await httpContext.Response.WriteAsync("\n", CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client disconnected between the cancellation check and the response write.
        }
    }

    // ---- row editing ----

    private static Task<IResult> InsertRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.insert", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.InsertRowAsync(
                resolved.Context, schema, name, RequireMap(body.Values, "values"), ct),
            cancellationToken);

    private static Task<IResult> UpdateRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.update", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.UpdateRowAsync(
                resolved.Context, schema, name, RequireMap(body.Key, "key"), RequireMap(body.Values, "values"), ct),
            cancellationToken);

    private static Task<IResult> DeleteRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.delete", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.DeleteRowAsync(
                resolved.Context, schema, name, RequireMap(body.Key, "key"), ct),
            cancellationToken);

    private static Task<IResult> WriteRow(
        string connection, string database, string schema, string name, string action,
        IGridletConnectionResolver resolver, IGridletAuditSink audit, HttpContext httpContext,
        Func<ResolvedConnection, CancellationToken, Task<int>> write,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowWrites)
            {
                return Forbidden($"Row editing is disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var rows = await write(resolved, cancellationToken);
                await AuditAsync(audit, httpContext, action, connection, database, $"{schema}.{name}", null,
                    succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
                return Results.Ok(new RowWriteResponse(rows));
            }
            catch (Exception ex)
            {
                await AuditAsync(audit, httpContext, action, connection, database, $"{schema}.{name}", null,
                    succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    private static Dictionary<string, object?> RequireMap(
        Dictionary<string, JsonElement>? map, string what)
        => map is { Count: > 0 }
            ? ToClrMap(map)
            : throw new GridletValidationException($"The request must include non-empty '{what}'.");

    // ---- table designer ----

    private static Task<IResult> CreateSchema(
        string connection, string database, SchemaDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, body.Name, "ddl.createSchema", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.CreateSchemaAsync(resolved.Context, body, ct), cancellationToken);

    private static Task<IResult> AlterSchema(
        string connection, string database, string schema, SchemaDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(body.Owner)
            ? Task.FromResult<IResult>(Results.BadRequest(new GridletErrorResponse("An owner is required.")))
            : Ddl(connection, database, schema, "ddl.alterSchemaOwner", resolver, audit, httpContext,
                (resolved, ct) => resolved.Provider.Ddl.AlterSchemaOwnerAsync(resolved.Context, schema, body.Owner!, ct),
                cancellationToken);

    private static Task<IResult> DropSchema(
        string connection, string database, string schema,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, schema, "ddl.dropSchema", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropSchemaAsync(resolved.Context, schema, ct), cancellationToken);

    private static Task<IResult> CreateTable(
        string connection, string database, TableDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{body.Schema}.{body.Name}", "ddl.createTable", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.CreateTableAsync(resolved.Context, body, ct),
            cancellationToken);

    private static Task<IResult> AddColumn(
        string connection, string database, string schema, string name, ColumnDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}", "ddl.addColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddColumnAsync(resolved.Context, schema, name, body, ct),
            cancellationToken);

    private static Task<IResult> AlterColumn(
        string connection, string database, string schema, string name, string column, ColumnDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{column}", "ddl.alterColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AlterColumnAsync(resolved.Context, schema, name, column, body, ct),
            cancellationToken);

    private static Task<IResult> DropColumn(
        string connection, string database, string schema, string name, string column,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{column}", "ddl.dropColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropColumnAsync(resolved.Context, schema, name, column, ct),
            cancellationToken);

    private static Task<IResult> DropObject(
        string connection, string database, string schema, string name, DbObjectType? type,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}", "ddl.dropObject", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropObjectAsync(
                resolved.Context, schema, name, type ?? DbObjectType.Table, ct),
            cancellationToken);

    private static Task<IResult> Ddl(
        string connection, string database, string objectName, string action,
        IGridletConnectionResolver resolver, IGridletAuditSink audit, HttpContext httpContext,
        Func<ResolvedConnection, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowDdl)
            {
                return Forbidden($"Schema changes are disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await execute(resolved, cancellationToken);
                await AuditAsync(audit, httpContext, action, connection, database, objectName, null,
                    succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                await AuditAsync(audit, httpContext, action, connection, database, objectName, null,
                    succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    // ---- saved queries ----

    private static Task<IResult> GetSavedQueries(ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () => Results.Ok(await store.GetAllAsync(cancellationToken)));

    private static Task<IResult> SaveQuery(
        SavedQuerySaveRequest body, ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Sql) ||
                string.IsNullOrWhiteSpace(body.ConnectionName))
            {
                throw new GridletValidationException("A saved query needs a name, a connection, and SQL text.");
            }

            var saved = await store.SaveAsync(
                new SavedQuery(
                    string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("n") : body.Id,
                    body.Name.Trim(), body.ConnectionName, body.Database, body.Sql, DateTimeOffset.UtcNow),
                cancellationToken);
            return Results.Ok(saved);
        });

    private static Task<IResult> DeleteSavedQuery(string id, ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () => await store.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No saved query with id '{id}'.")));

    // ---- published endpoints (admin) ----

    private static Task<IResult> GetPublishedEndpoints(IPublishedEndpointStore store, CancellationToken cancellationToken)
        => Execute(async () => Results.Ok(await store.GetAllAsync(cancellationToken)));

    private static Task<IResult> SavePublishedEndpoint(
        PublishRequest body, IPublishedEndpointStore store, IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var method = body.Method?.ToUpperInvariant();
            if (method is not ("GET" or "POST"))
            {
                throw new GridletValidationException("Method must be GET or POST.");
            }

            var route = (body.Route ?? "").Trim('/', ' ');
            if (route.Length == 0 || !RoutePattern().IsMatch(route))
            {
                throw new GridletValidationException(
                    "Route must contain only letters, digits, '-', '_' and '/' segments (e.g. sales/top-customers).");
            }

            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Sql))
            {
                throw new GridletValidationException("A published endpoint needs a name and SQL text.");
            }

            resolver.Resolve(body.ConnectionName, body.Database); // throws for unknown connections

            var parameters = body.Parameters ?? [];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters)
            {
                if (!ParameterNamePattern().IsMatch(parameter.Name) || !names.Add(parameter.Name))
                {
                    throw new GridletValidationException($"Invalid or duplicate parameter name '{parameter.Name}'.");
                }

                if (parameter.Type.ToLowerInvariant() is not ("auto" or "string" or "integer" or "number" or "boolean"))
                {
                    throw new GridletValidationException(
                        $"Parameter '{parameter.Name}' has an unsupported type '{parameter.Type}'.");
                }
            }

            var saved = await store.SaveAsync(
                new PublishedEndpoint(
                    string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("n") : body.Id,
                    body.Name.Trim(), method, route, body.ConnectionName, body.Database, body.Sql,
                    parameters,
                    string.IsNullOrWhiteSpace(body.AuthorizationPolicy) ? null : body.AuthorizationPolicy.Trim(),
                    body.Enabled, DateTimeOffset.UtcNow),
                cancellationToken);
            return Results.Ok(saved);
        });

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex ParameterNamePattern();

    private static Task<IResult> DeletePublishedEndpoint(
        string id, IPublishedEndpointStore store, CancellationToken cancellationToken)
        => Execute(async () => await store.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No published endpoint with id '{id}'.")));

    private static DbObjectDto ToDto(DbObjectInfo info)
        => new(info.Schema, info.Name, info.Type.ToString());
}
