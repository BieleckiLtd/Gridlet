using System.Text.Json;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Microsoft.AspNetCore.Http;

namespace Gridlet.AspNetCore;

internal static class GridletEndpointHelpers
{
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
