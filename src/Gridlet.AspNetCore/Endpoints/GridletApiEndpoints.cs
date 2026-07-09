using System.Diagnostics;
using System.Reflection;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Gridlet.AspNetCore;

/// <summary>The JSON API consumed by the embedded UI (and usable directly).</summary>
internal static class GridletApiEndpoints
{
    private static readonly string Version =
        typeof(GridletApiEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "dev";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/meta", GetMeta);
        api.MapGet("/connections/{connection}/databases", GetDatabases);
        api.MapGet("/connections/{connection}/databases/{database}/objects", GetObjects);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/data", GetObjectData);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/structure", GetObjectStructure);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/definition", GetObjectDefinition);
        api.MapPost("/connections/{connection}/databases/{database}/query", ExecuteQuery);
    }

    private static IResult GetMeta(IOptionsMonitor<GridletOptions> options)
    {
        var connections = options.CurrentValue.Connections
            .Select(c => new GridletConnectionSummary(c.Name, c.ProviderName, c.AllowSqlExecution))
            .ToArray();
        return Results.Ok(new GridletMetaResponse(Version, connections));
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

    private static Task<IResult> ExecuteQuery(
        string connection,
        string database,
        QueryRequestBody body,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowSqlExecution)
            {
                return Results.Json(
                    new GridletErrorResponse($"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'."),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var limits = options.CurrentValue.Limits;
            var sql = body.Sql ?? "";
            var user = httpContext.User.Identity?.IsAuthenticated == true
                ? httpContext.User.Identity.Name
                : null;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await resolved.Provider.Query.ExecuteAsync(
                    resolved.Context,
                    sql,
                    new QueryRequestOptions(limits.MaxQueryResultRows, limits.CommandTimeoutSeconds),
                    cancellationToken);

                await WriteAuditAsync(succeeded: true, error: null);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                await WriteAuditAsync(succeeded: false, error: ex.Message);
                throw;
            }

            async ValueTask WriteAuditAsync(bool succeeded, string? error)
                => await audit.WriteAsync(
                    new GridletAuditEvent(
                        Timestamp: DateTimeOffset.UtcNow,
                        UserName: user,
                        Action: "query.execute",
                        ConnectionName: resolved.Context.ConnectionName,
                        Database: database,
                        ObjectName: null,
                        Sql: sql,
                        Succeeded: succeeded,
                        DurationMs: stopwatch.ElapsedMilliseconds,
                        Error: error),
                    CancellationToken.None);
        });

    private static DbObjectDto ToDto(DbObjectInfo info)
        => new(info.Schema, info.Name, info.Type.ToString());

    /// <summary>Maps Gridlet exceptions onto HTTP status codes with a consistent error body.</summary>
    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (GridletUnknownConnectionException ex)
        {
            return Results.NotFound(new GridletErrorResponse(ex.Message));
        }
        catch (GridletObjectNotFoundException ex)
        {
            return Results.NotFound(new GridletErrorResponse(ex.Message));
        }
        catch (GridletValidationException ex)
        {
            return Results.BadRequest(new GridletErrorResponse(ex.Message));
        }
        catch (GridletQueryException ex)
        {
            return Results.BadRequest(new GridletErrorResponse(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Gridlet is an operator tool: surfacing the underlying message (e.g. login failed,
            // server unreachable) is intentional and more useful than a generic 500.
            return Results.Json(
                new GridletErrorResponse(ex.Message),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
