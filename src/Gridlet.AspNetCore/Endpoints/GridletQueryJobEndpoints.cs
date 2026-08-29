using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

internal static partial class GridletApiEndpoints
{
    private static void MapQueryJobs(RouteGroupBuilder api)
    {
        api.MapPost(
            "/connections/{connection}/databases/{database}/query/jobs", StartQueryJob);
        api.MapGet(
            "/connections/{connection}/databases/{database}/query/jobs/{jobId}", GetQueryJob);
        api.MapDelete(
            "/connections/{connection}/databases/{database}/query/jobs/{jobId}", CancelQueryJob);
    }

    private static Task<IResult> StartQueryJob(
        string connection,
        string database,
        QueryRequestBody body,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        GridletQueryJobManager jobs,
        HttpContext httpContext)
        => Execute(() =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowSqlExecution)
            {
                return Task.FromResult(Forbidden(
                    $"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'."));
            }

            var limits = options.CurrentValue.Limits;
            var maxRows = Math.Clamp(body.MaxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
            var response = jobs.Start(
                resolved,
                QueryJobOwner(httpContext),
                UserName(httpContext),
                body.Sql ?? string.Empty,
                new QueryRequestOptions(maxRows, limits.CommandTimeoutSeconds));
            return Task.FromResult(Results.Accepted(value: response));
        });

    private static Task<IResult> GetQueryJob(
        string connection,
        string database,
        string jobId,
        int? after,
        int? waitMs,
        GridletQueryJobManager jobs,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var response = await jobs.GetAsync(
                jobId,
                QueryJobOwner(httpContext),
                connection,
                database,
                after ?? 0,
                waitMs ?? 1_000,
                cancellationToken);
            return response is null
                ? Results.NotFound(new GridletErrorResponse("That query job is no longer available."))
                : Results.Ok(response);
        });

    private static Task<IResult> CancelQueryJob(
        string connection,
        string database,
        string jobId,
        GridletQueryJobManager jobs,
        HttpContext httpContext)
        => Execute(() =>
        {
            var response = jobs.Cancel(
                jobId, QueryJobOwner(httpContext), connection, database);
            return Task.FromResult(response is null
                ? Results.NotFound(new GridletErrorResponse("That query job is no longer available."))
                : Results.Ok(response));
        });

    private static string? QueryJobOwner(HttpContext httpContext)
    {
        var identity = httpContext.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return null;
        }

        var issuer = httpContext.User.FindFirst("iss")?.Value
            ?? identity.AuthenticationType
            ?? "authenticated";
        var subject = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? identity.Name
            ?? throw new GridletValidationException(
                "The authenticated user has no stable identifier for query jobs.");
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{issuer}\u001f{subject}")));
    }
}
