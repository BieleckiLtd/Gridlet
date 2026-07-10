using System.Diagnostics;
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
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>
/// Invokes published API endpoints at <c>{mount}/pub/{route}</c>. Routes are dispatched
/// dynamically from the store, so publishing needs no application restart. The dispatcher
/// sits inside the Gridlet route group and therefore inherits its authorization; endpoints
/// can additionally demand their own policy.
/// </summary>
internal static class GridletPublishedEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapMethods("/pub/{**route}", ["GET", "POST"], Invoke).ExcludeFromDescription();
    }

    private static Task<IResult> Invoke(
        string route,
        HttpContext httpContext,
        IPublishedEndpointStore store,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var endpoint = await store.FindAsync(httpContext.Request.Method, route.Trim('/'), cancellationToken);
            if (endpoint is null || !endpoint.Enabled)
            {
                return Results.NotFound(new GridletErrorResponse($"No published endpoint at '{route}'."));
            }

            if (endpoint.AuthorizationPolicy is not null)
            {
                var authorization = httpContext.RequestServices.GetService<IAuthorizationService>()
                    ?? throw new InvalidOperationException(
                        $"Published endpoint '{endpoint.Name}' requires policy '{endpoint.AuthorizationPolicy}' but authorization services are not registered.");
                var decision = await authorization.AuthorizeAsync(httpContext.User, endpoint.AuthorizationPolicy);
                if (!decision.Succeeded)
                {
                    return Forbidden($"This endpoint requires the '{endpoint.AuthorizationPolicy}' policy.");
                }
            }

            var arguments = await BindParametersAsync(endpoint, httpContext, cancellationToken);
            var resolved = resolver.Resolve(endpoint.ConnectionName, endpoint.Database);
            var limits = options.CurrentValue.Limits;

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await resolved.Provider.Query.ExecuteAsync(
                    resolved.Context,
                    endpoint.Sql,
                    new QueryRequestOptions(limits.MaxQueryResultRows, limits.CommandTimeoutSeconds),
                    arguments,
                    cancellationToken);

                await AuditAsync(audit, httpContext, "api.invoke", endpoint.ConnectionName, endpoint.Database,
                    endpoint.Route, null, succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
                return Results.Ok(Shape(result));
            }
            catch (Exception ex)
            {
                await AuditAsync(audit, httpContext, "api.invoke", endpoint.ConnectionName, endpoint.Database,
                    endpoint.Route, null, succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    /// <summary>Binds declared parameters from the query string (GET) or JSON body (POST). Missing optional parameters become NULL.</summary>
    private static async Task<Dictionary<string, object?>> BindParametersAsync(
        PublishedEndpoint endpoint, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (endpoint.Parameters.Count == 0)
        {
            return arguments;
        }

        Dictionary<string, JsonElement>? body = null;
        if (HttpMethods.IsPost(httpContext.Request.Method) &&
            (httpContext.Request.ContentLength ?? 0) > 0)
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
                value = ToClrValue(element);
                supplied = true;
            }
            else if (httpContext.Request.Query.TryGetValue(parameter.Name, out var queryValue))
            {
                value = queryValue.ToString();
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

    /// <summary>Shapes the first result set as an API-friendly array of objects.</summary>
    private static object Shape(QueryResult result)
    {
        var set = result.ResultSets.FirstOrDefault();
        if (set is null)
        {
            return new { recordsAffected = result.RecordsAffected };
        }

        var names = new string[set.Columns.Count];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < set.Columns.Count; i++)
        {
            var name = string.IsNullOrEmpty(set.Columns[i].Name) ? $"column{i}" : set.Columns[i].Name;
            while (!seen.Add(name))
            {
                name += "_";
            }

            names[i] = name;
        }

        var rows = set.Rows
            .Select(row =>
            {
                var item = new Dictionary<string, object?>(row.Length);
                for (var i = 0; i < row.Length; i++)
                {
                    item[names[i]] = row[i];
                }

                return item;
            })
            .ToList();

        return new { rows, rowCount = rows.Count, truncated = set.Truncated };
    }
}
