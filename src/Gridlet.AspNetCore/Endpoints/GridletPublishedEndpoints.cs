using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>
/// Invokes published API endpoints at <c>{mount}/pub/{route}</c> (or an independent configured
/// path). Routes are dispatched
/// dynamically from the store, so publishing needs no application restart. The dispatcher
/// sits inside the Gridlet route group and therefore inherits its authorization; endpoints
/// can additionally demand their own policy.
/// </summary>
internal static class GridletPublishedEndpoints
{
    private const string UnexpectedErrorMessage = "An unexpected server error occurred.";

    public static void Map(RouteGroupBuilder group, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            group.MapMethods(
                    "/{**route}", ["GET", "POST", "PUT", "PATCH", "DELETE"], Invoke)
                .ExcludeFromDescription();
            return;
        }

        if (!GridletRoutePath.TryNormalize(segment, out var normalized))
        {
            throw new InvalidOperationException(
                "PublishedApiRoutePrefix must contain safe, non-empty route segments.");
        }

        group.MapMethods(
                $"/{normalized}/{{**route}}", ["GET", "POST", "PUT", "PATCH", "DELETE"], Invoke)
            .ExcludeFromDescription();
    }

    private static async Task Invoke(
        string? route,
        HttpContext httpContext,
        IPublishedEndpointStore store,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var useNdjson = AcceptsNdjson(httpContext.Request);
        var requestedRoute = route?.Trim('/');
        if (string.IsNullOrEmpty(requestedRoute))
        {
            await WriteErrorAsync(
                httpContext,
                StatusCodes.Status404NotFound,
                "A published endpoint route is required.",
                useNdjson,
                cancellationToken);
            return;
        }

        PublishedEndpoint endpoint;
        ResolvedConnection resolved;
        Dictionary<string, object?> arguments;
        QueryRequestOptions queryOptions;

        // Preamble: routing, authorization, parameter binding and connection resolution all happen
        // before a single byte is written, so their failures can still return a clean status code.
        // These are not audited (they never reach the database), matching the previous behaviour.
        try
        {
            var found = await store.FindAsync(httpContext.Request.Method, requestedRoute, cancellationToken);
            if (found is null || !found.Enabled)
            {
                await WriteErrorAsync(httpContext, StatusCodes.Status404NotFound,
                    $"No published endpoint at '{requestedRoute}'.", useNdjson, cancellationToken);
                return;
            }

            endpoint = found;

            if (endpoint.AuthorizationPolicy is not null)
            {
                var authorization = httpContext.RequestServices.GetService<IAuthorizationService>()
                    ?? throw new InvalidOperationException(
                        $"Published endpoint '{endpoint.Name}' requires policy '{endpoint.AuthorizationPolicy}' but authorization services are not registered.");
                var decision = await authorization.AuthorizeAsync(httpContext.User, endpoint.AuthorizationPolicy);
                if (!decision.Succeeded)
                {
                    await WriteErrorAsync(httpContext, StatusCodes.Status403Forbidden,
                        $"This endpoint requires the '{endpoint.AuthorizationPolicy}' policy.",
                        useNdjson, cancellationToken);
                    return;
                }
            }

            arguments = await BindParametersAsync(endpoint, httpContext, cancellationToken);
            resolved = resolver.Resolve(endpoint.ConnectionName, endpoint.Database);

            var limits = options.CurrentValue.Limits;
            // Published endpoints are uncapped by default: null/omitted and 0-or-less both stream every
            // row. Only an explicit positive MaxRows applies a cap (the endpoint has opted into it).
            var cap = endpoint.MaxRows is > 0 ? endpoint.MaxRows.Value : 0;
            queryOptions = new QueryRequestOptions(cap, limits.CommandTimeoutSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await WriteMappedErrorAsync(
                httpContext, ex, useNdjson, loggerFactory, cancellationToken);
            return;
        }

        // JSON remains the default response. Callers can explicitly request NDJSON for simple
        // line-by-line consumption of large results and stable terminal completion/error events.
        httpContext.Response.ContentType = useNdjson
            ? "application/x-ndjson; charset=utf-8"
            : "application/json; charset=utf-8";
        string[]? columnNames = null;
        var openedBody = false;
        var firstRow = true;
        long rowCount = 0;
        var recordsAffected = -1;

        try
        {
            await foreach (var streamEvent in resolved.Provider.Query.StreamAsync(
                resolved.Context, endpoint.Sql, queryOptions, arguments, cancellationToken))
            {
                switch (streamEvent.Type)
                {
                    case "resultSet" when streamEvent.ResultSetIndex == 0 && streamEvent.Columns is not null:
                        columnNames = ResolveColumnNames(streamEvent.Columns);
                        if (!useNdjson)
                        {
                            await httpContext.Response.WriteAsync("{\"rows\":[", cancellationToken);
                            openedBody = true;
                        }
                        break;

                    case "rows" when streamEvent.ResultSetIndex == 0 && columnNames is not null && streamEvent.Rows is not null:
                        foreach (var row in streamEvent.Rows)
                        {
                            var record = ToRecord(columnNames, row);
                            if (useNdjson)
                            {
                                await WriteNdjsonEventAsync(
                                    httpContext,
                                    new PublishedRowEvent("row", record),
                                    cancellationToken);
                            }
                            else
                            {
                                if (!firstRow)
                                {
                                    await httpContext.Response.WriteAsync(",", cancellationToken);
                                }

                                firstRow = false;
                                await JsonSerializer.SerializeAsync(
                                    httpContext.Response.Body, record,
                                    JsonSerializerOptions.Web, cancellationToken);
                            }

                            rowCount++;
                        }

                        await httpContext.Response.Body.FlushAsync(cancellationToken);
                        break;

                    case "completed":
                        recordsAffected = streamEvent.RecordsAffected ?? -1;
                        break;
                }
            }

            if (useNdjson)
            {
                await WriteNdjsonEventAsync(
                    httpContext,
                    new PublishedCompletedEvent("completed", rowCount, recordsAffected),
                    cancellationToken);
            }
            else if (openedBody)
            {
                await httpContext.Response.WriteAsync(
                    $"],\"rowCount\":{rowCount}}}", cancellationToken);
            }
            else
            {
                // No result set (e.g. a non-query statement); mirror the previous buffering shape.
                await httpContext.Response.WriteAsJsonAsync(new { recordsAffected }, cancellationToken);
            }

            await httpContext.Response.Body.FlushAsync(cancellationToken);
            await AuditAsync(audit, httpContext, "api.invoke", endpoint.ConnectionName, endpoint.Database,
                endpoint.Route, null, succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; cancellation reached the provider and there is nothing to send.
        }
        catch (Exception ex)
        {
            // Audit reflects the true outcome: streaming is only recorded as succeeded once every row
            // has been written, so a mid-stream failure is audited as a failure here.
            await AuditAsync(audit, httpContext, "api.invoke", endpoint.ConnectionName, endpoint.Database,
                endpoint.Route, null, succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);

            if (!httpContext.Response.HasStarted)
            {
                await WriteMappedErrorAsync(
                    httpContext, ex, useNdjson, loggerFactory, cancellationToken);
                return;
            }

            var clientMessage = ClientErrorMessage(ex);
            LogUnexpectedError(loggerFactory, ex);

            if (useNdjson)
            {
                // The 200 status and previous rows are already on the wire. A terminal error event
                // keeps the NDJSON valid and lets streaming consumers reject the partial result.
                await TryWriteNdjsonErrorAsync(httpContext, rowCount, clientMessage);
            }
            else
            {
                // The 200 status and some rows are already on the wire, so the status cannot change.
                // Close the JSON with an "error" marker consumers can detect alongside the partial rows.
                await TryCloseWithErrorAsync(httpContext, openedBody, rowCount, clientMessage);
            }
        }
    }

    /// <summary>Maps Gridlet exceptions to an error result, matching the shared endpoint helper.</summary>
    private static IResult MapError(Exception ex)
        => ex switch
        {
            GridletUnknownConnectionException or GridletObjectNotFoundException
                => Results.NotFound(new GridletErrorResponse(ex.Message)),
            GridletValidationException or GridletQueryException
                => Results.BadRequest(new GridletErrorResponse(ex.Message)),
            _ => Results.Json(
                new GridletErrorResponse(UnexpectedErrorMessage),
                statusCode: StatusCodes.Status500InternalServerError),
        };

    private static int MapErrorStatusCode(Exception ex)
        => ex switch
        {
            GridletUnknownConnectionException or GridletObjectNotFoundException
                => StatusCodes.Status404NotFound,
            GridletValidationException or GridletQueryException
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };

    private static Task WriteMappedErrorAsync(
        HttpContext httpContext,
        Exception exception,
        bool useNdjson,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        LogUnexpectedError(loggerFactory, exception);
        return useNdjson
            ? WriteErrorAsync(
                httpContext,
                MapErrorStatusCode(exception),
                ClientErrorMessage(exception),
                useNdjson,
                cancellationToken)
            : WriteResultAsync(httpContext, MapError(exception));
    }

    private static string ClientErrorMessage(Exception exception)
        => IsExpectedError(exception) ? exception.Message : UnexpectedErrorMessage;

    private static bool IsExpectedError(Exception exception)
        => exception is GridletUnknownConnectionException or GridletObjectNotFoundException or
            GridletValidationException or GridletQueryException;

    private static void LogUnexpectedError(ILoggerFactory loggerFactory, Exception exception)
    {
        if (IsExpectedError(exception)) return;

        loggerFactory.CreateLogger("Gridlet.AspNetCore.PublishedEndpoints")
            .LogError(exception, "Unexpected published endpoint failure.");
    }

    private static async Task WriteErrorAsync(
        HttpContext httpContext,
        int statusCode,
        string message,
        bool useNdjson,
        CancellationToken cancellationToken)
    {
        if (!useNdjson)
        {
            await WriteResultAsync(
                httpContext,
                Results.Json(new GridletErrorResponse(message), statusCode: statusCode));
            return;
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        await WriteNdjsonEventAsync(
            httpContext,
            new PublishedErrorEvent("error", 0, message),
            cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static Task WriteResultAsync(HttpContext httpContext, IResult result)
        => result.ExecuteAsync(httpContext);

    private static async Task WriteNdjsonEventAsync<T>(
        HttpContext httpContext,
        T streamEvent,
        CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            streamEvent,
            JsonSerializerOptions.Web,
            cancellationToken);
        await httpContext.Response.WriteAsync("\n", cancellationToken);
    }

    private static async Task TryWriteNdjsonErrorAsync(
        HttpContext httpContext,
        long rowCount,
        string message)
    {
        try
        {
            await WriteNdjsonEventAsync(
                httpContext,
                new PublishedErrorEvent("error", rowCount, message),
                CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client disconnected between the provider failure and this terminal event.
        }
    }

    /// <summary>Best-effort close of an already-streaming response with an in-body error marker.</summary>
    private static async Task TryCloseWithErrorAsync(HttpContext httpContext, bool openedBody, long rowCount, string message)
    {
        try
        {
            var encoded = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
            var tail = openedBody
                ? $"],\"rowCount\":{rowCount},\"error\":{encoded}}}"
                : $"{{\"error\":{encoded}}}";
            await httpContext.Response.WriteAsync(tail, CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client disconnected between the failure and this write.
        }
    }

    private static bool AcceptsNdjson(HttpRequest request)
    {
        foreach (var headerValue in request.Headers.Accept)
        {
            foreach (var candidate in headerValue?.Split(',') ?? [])
            {
                var parts = candidate.Split(';', StringSplitOptions.TrimEntries);
                if (!parts[0].Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var quality = 1m;
                foreach (var parameter in parts.Skip(1))
                {
                    if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase) &&
                        decimal.TryParse(
                            parameter.AsSpan(2),
                            NumberStyles.Number,
                            CultureInfo.InvariantCulture,
                            out var parsedQuality))
                    {
                        quality = parsedQuality;
                    }
                }

                if (quality > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Binds declared parameters from the query string (GET) or JSON body (write methods). Missing optional parameters become NULL.</summary>
    private static async Task<Dictionary<string, object?>> BindParametersAsync(
        PublishedEndpoint endpoint, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (endpoint.Parameters.Count == 0)
        {
            return arguments;
        }

        Dictionary<string, JsonElement>? body = null;
        if (!HttpMethods.IsGet(httpContext.Request.Method) &&
            (httpContext.Request.ContentLength is > 0 || !string.IsNullOrWhiteSpace(httpContext.Request.ContentType)))
        {
            try
            {
                body = await httpContext.Request.ReadFromJsonAsync<Dictionary<string, JsonElement>>(cancellationToken);
            }
            catch (JsonException)
            {
                throw new GridletValidationException("The request body must be a JSON object of parameter values.");
            }
        }

        foreach (var parameter in endpoint.Parameters)
        {
            object? value = null;
            var supplied = false;

            if (body is not null && body.TryGetValue(parameter.Name, out var element))
            {
                value = ConvertParameter(parameter, ToClrValue(element));
                supplied = true;
            }
            else if (httpContext.Request.Query.TryGetValue(parameter.Name, out var queryValue))
            {
                value = ConvertParameter(parameter, queryValue.ToString());
                supplied = true;
            }

            if (!supplied && parameter.Required)
            {
                throw new GridletValidationException($"Missing required parameter '{parameter.Name}'.");
            }

            arguments[parameter.Name] = value;
        }

        return arguments;
    }

    private static object? ConvertParameter(PublishedParameter parameter, object? value)
    {
        if (value is null || parameter.Type.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        try
        {
            return parameter.Type.ToLowerInvariant() switch
            {
                "string" => text,
                "integer" => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "number" => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture),
                "boolean" => bool.Parse(text),
                _ => throw new GridletValidationException(
                    $"Parameter '{parameter.Name}' has an unsupported type '{parameter.Type}'."),
            };
        }
        catch (FormatException)
        {
            throw new GridletValidationException(
                $"Parameter '{parameter.Name}' must be a valid {parameter.Type} value.");
        }
        catch (OverflowException)
        {
            throw new GridletValidationException(
                $"Parameter '{parameter.Name}' is outside the supported {parameter.Type} range.");
        }
    }

    /// <summary>Produces unique, non-empty JSON property names for a result set's columns.</summary>
    private static string[] ResolveColumnNames(IReadOnlyList<ResultColumn> columns)
    {
        var names = new string[columns.Count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            var name = string.IsNullOrEmpty(columns[i].Name) ? $"column{i}" : columns[i].Name;
            while (!seen.Add(name))
            {
                name += "_";
            }

            names[i] = name;
        }

        return names;
    }

    /// <summary>Shapes one streamed row into an API-friendly object keyed by column name.</summary>
    private static Dictionary<string, object?> ToRecord(string[] names, object?[] row)
    {
        var item = new Dictionary<string, object?>(row.Length);
        for (var i = 0; i < row.Length && i < names.Length; i++)
        {
            item[names[i]] = row[i];
        }

        return item;
    }

    private sealed record PublishedRowEvent(
        string Type,
        IReadOnlyDictionary<string, object?> Row);

    private sealed record PublishedCompletedEvent(
        string Type,
        long RowCount,
        int RecordsAffected);

    private sealed record PublishedErrorEvent(
        string Type,
        long RowCount,
        string Error);
}
