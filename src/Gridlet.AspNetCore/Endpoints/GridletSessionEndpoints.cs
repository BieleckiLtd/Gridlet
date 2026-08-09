using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Gridlet.Sessions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>
/// Pinned query sessions. Ordinary execution opens a connection per statement, which silently
/// discards an explicit transaction; a session keeps one connection so <c>BEGIN</c>, the statements
/// that follow it, and the final <c>COMMIT</c> or <c>ROLLBACK</c> are one unit of work the person
/// controls.
/// </summary>
internal static partial class GridletApiEndpoints
{
    private static void MapSessions(RouteGroupBuilder api)
    {
        api.MapPost("/connections/{connection}/databases/{database}/sessions", OpenSession);
        api.MapGet("/sessions", ListSessions);
        api.MapGet("/sessions/{sessionId}", GetSession);
        api.MapPost("/sessions/{sessionId}/query", ExecuteSessionQuery);
        api.MapPost("/sessions/{sessionId}/transaction", RunSessionTransactionCommand);
        api.MapDelete("/sessions/{sessionId}", CloseSession);
    }

    private static Task<IResult> OpenSession(
        string connection,
        string database,
        IGridletConnectionResolver resolver,
        GridletQuerySessionManager sessions,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowSqlExecution)
            {
                return SqlExecutionDisabled(resolved);
            }

            var session = await sessions.OpenAsync(resolved, UserName(httpContext), cancellationToken);
            await AuditAsync(audit, httpContext, "session.open", connection, database,
                session.Id, null, succeeded: true, 0, null);
            return Results.Ok(session);
        });

    private static IResult ListSessions(GridletQuerySessionManager sessions, HttpContext httpContext)
        => Results.Ok(sessions.List(UserName(httpContext)));

    private static Task<IResult> GetSession(
        string sessionId,
        GridletQuerySessionManager sessions,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
            Results.Ok(await sessions.GetAsync(sessionId, UserName(httpContext), cancellationToken)));

    private static Task<IResult> RunSessionTransactionCommand(
        string sessionId,
        SessionTransactionRequest body,
        IGridletConnectionResolver resolver,
        GridletQuerySessionManager sessions,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var command = (body.Command ?? "").Trim().ToLowerInvariant() switch
            {
                "begin" => TransactionCommand.Begin,
                "commit" => TransactionCommand.Commit,
                "rollback" => TransactionCommand.Rollback,
                _ => throw new GridletValidationException(
                    "Transaction command must be 'begin', 'commit' or 'rollback'."),
            };

            var owner = UserName(httpContext);
            var current = await sessions.GetAsync(sessionId, owner, cancellationToken);
            var denial = DenyWhenSqlExecutionDisabled(resolver, current);
            if (denial is not null)
            {
                return denial;
            }

            var updated = await sessions.RunTransactionCommandAsync(
                sessionId, owner, command, cancellationToken);
            await AuditAsync(audit, httpContext, "session.transaction." + command.ToString().ToLowerInvariant(),
                current.ConnectionName, current.Database, sessionId, null, succeeded: true, 0, null);
            return Results.Ok(updated);
        });

    private static Task<IResult> CloseSession(
        string sessionId,
        GridletQuerySessionManager sessions,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var owner = UserName(httpContext);
            var closed = await sessions.CloseAsync(sessionId, owner, cancellationToken);
            if (!closed)
            {
                return Results.NotFound(new GridletErrorResponse(
                    "That query session is no longer open."));
            }

            await AuditAsync(audit, httpContext, "session.close", "", null, sessionId, null,
                succeeded: true, 0, null);
            return Results.NoContent();
        });

    private static async Task ExecuteSessionQuery(
        string sessionId,
        QueryRequestBody body,
        IGridletConnectionResolver resolver,
        GridletQuerySessionManager sessions,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Reading the session first keeps an unknown id, a session owned by somebody else, a busy
        // session, and a connection whose SQL execution has since been turned off out of the NDJSON
        // stream, where they could only be reported as a trailing error event.
        GridletSessionInfo session;
        IResult? denial;
        try
        {
            session = await sessions.GetAsync(sessionId, UserName(httpContext), cancellationToken);

            // Resolving the session's connection belongs inside this block: a session outlives the
            // configuration it was opened against, so the connection can be gone by now, and that
            // has to reach the caller as a 404 rather than as an unhandled failure.
            denial = DenyWhenSqlExecutionDisabled(resolver, session);
        }
        catch (GridletSessionNotFoundException ex)
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status404NotFound, ex.Message, cancellationToken);
            return;
        }
        catch (GridletSessionBusyException ex)
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status409Conflict, ex.Message, cancellationToken);
            return;
        }
        catch (GridletUnknownConnectionException ex)
        {
            await WriteErrorAsync(httpContext, StatusCodes.Status404NotFound, ex.Message, cancellationToken);
            return;
        }

        if (denial is not null)
        {
            await denial.ExecuteAsync(httpContext);
            return;
        }

        var limits = options.CurrentValue.Limits;
        var maxRows = Math.Clamp(body.MaxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
        var sql = body.Sql ?? "";
        await WriteQueryStreamAsync(
            httpContext, audit, loggerFactory, session.ConnectionName, session.Database, sql,
            token => sessions.StreamAsync(
                sessionId,
                UserName(httpContext),
                sql,
                new QueryRequestOptions(maxRows, limits.CommandTimeoutSeconds),
                token),
            cancellationToken);
    }

    private static async Task WriteErrorAsync(
        HttpContext httpContext, int statusCode, string message, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(message), cancellationToken);
    }

    /// <summary>
    /// A session is long-lived, so its connection's permission is re-checked on every use rather
    /// than trusted from the moment it was opened.
    /// </summary>
    private static IResult? DenyWhenSqlExecutionDisabled(
        IGridletConnectionResolver resolver,
        GridletSessionInfo session)
    {
        var resolved = resolver.Resolve(session.ConnectionName, session.Database);
        return resolved.Context.Connection.AllowSqlExecution ? null : SqlExecutionDisabled(resolved);
    }

    private static IResult SqlExecutionDisabled(ResolvedConnection resolved)
        => Forbidden($"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'.");
}
