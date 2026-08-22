using System.Text.Json;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gridlet.AspNetCore;

internal static class GridletEndpointHelpers
{
    private static readonly AsyncLocal<ILogger?> CurrentLogger = new();

    /// <summary>
    /// Endpoint filter that publishes the logger for unexpected failures, taken from the request's
    /// own service provider. Gridlet may be mapped more than once in a process (several hosts, or
    /// parallel test servers), so the logger is scoped to the request being served rather than held
    /// in shared state that the most recent mapping would win.
    /// </summary>
    public static async ValueTask<object?> PublishRequestLogger(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var previous = CurrentLogger.Value;
        CurrentLogger.Value = context.HttpContext.RequestServices
            .GetService<ILoggerFactory>()?.CreateLogger("Gridlet.Endpoints");
        try
        {
            return await next(context);
        }
        finally
        {
            CurrentLogger.Value = previous;
        }
    }

    /// <summary>Maps Gridlet exceptions onto HTTP status codes with a consistent error body.</summary>
    public static async Task<IResult> Execute(Func<Task<IResult>> action)
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
        catch (GridletSessionNotFoundException ex)
        {
            return Results.NotFound(new GridletErrorResponse(ex.Message));
        }
        catch (GridletSessionBusyException ex)
        {
            return Results.Json(
                new GridletErrorResponse(ex.Message), statusCode: StatusCodes.Status409Conflict);
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
            // server unreachable) is intentional and more useful than a generic 500. It is logged
            // as well, so a failure the caller merely retries still leaves a record on the server.
            (CurrentLogger.Value ?? NullLogger.Instance).LogError(ex, "Gridlet request failed: {Message}", ex.Message);
            return Results.Json(
                new GridletErrorResponse(ex.Message),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public static IResult Forbidden(string message)
        => Results.Json(new GridletErrorResponse(message), statusCode: StatusCodes.Status403Forbidden);

    public static string? UserName(HttpContext httpContext)
        => httpContext.User.Identity?.IsAuthenticated == true ? httpContext.User.Identity.Name : null;

    public static ValueTask AuditAsync(
        IGridletAuditSink audit,
        HttpContext httpContext,
        string action,
        string connectionName,
        string? database,
        string? objectName,
        string? sql,
        bool succeeded,
        long durationMs,
        string? error)
        => audit.WriteAsync(
            new GridletAuditEvent(
                DateTimeOffset.UtcNow, UserName(httpContext), action, connectionName,
                database, objectName, sql, succeeded, durationMs, error),
            CancellationToken.None);

    /// <summary>Converts a JSON body value into a CLR value suitable for a SQL parameter.</summary>
    public static object? ToClrValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l
                : element.TryGetDecimal(out var d) ? d
                : element.GetDouble(),
            _ => element.GetRawText(),
        };

    public static Dictionary<string, object?> ToClrMap(Dictionary<string, JsonElement>? map)
        => map is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : map.ToDictionary(kv => kv.Key, kv => ToClrValue(kv.Value), StringComparer.OrdinalIgnoreCase);
}
