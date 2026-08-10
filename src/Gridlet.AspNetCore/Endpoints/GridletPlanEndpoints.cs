using System.Diagnostics;
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

/// <summary>
/// Execution plans. "Why is this slow" is the question a query editor cannot answer with results
/// alone, and it is the one thing SSMS is reached for most often.
/// </summary>
internal static partial class GridletApiEndpoints
{
    private static void MapPlans(RouteGroupBuilder api)
        => api.MapPost("/connections/{connection}/databases/{database}/query/plan", GetQueryPlan);

    private static Task<IResult> GetQueryPlan(
        string connection,
        string database,
        QueryPlanRequestBody body,
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
                return Forbidden(
                    $"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            if (resolved.Provider.Query is not IQueryPlanRunner planner)
            {
                throw new GridletValidationException(
                    $"Connection '{resolved.Context.ConnectionName}' uses a provider that cannot explain queries.");
            }

            var mode = (body.Mode ?? "estimated").Trim().ToLowerInvariant() switch
            {
                "estimated" or "" => QueryPlanMode.Estimated,
                "actual" => QueryPlanMode.Actual,
                _ => throw new GridletValidationException("Plan mode must be 'estimated' or 'actual'."),
            };

            var sql = body.Sql ?? "";
            var limits = options.CurrentValue.Limits;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var plan = await planner.GetPlanAsync(
                    resolved.Context, sql, mode,
                    new QueryRequestOptions(limits.MaxQueryResultRows, limits.CommandTimeoutSeconds),
                    cancellationToken);

                // An actual plan runs the statement, so it is audited as an execution, not a read.
                await AuditAsync(audit, httpContext, PlanAction(mode), connection, database, null, sql,
                    succeeded: true, stopwatch.ElapsedMilliseconds, null);
                return Results.Ok(new QueryPlanResponse(
                    // The provider decides the mode it could honour: SQLite has no actual plan and
                    // says so by returning an estimated one.
                    plan.Mode == QueryPlanMode.Actual ? "actual" : "estimated",
                    plan.Format,
                    plan.Roots,
                    plan.RawText,
                    plan.Messages ?? []));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AuditAsync(audit, httpContext, PlanAction(mode), connection, database, null, sql,
                    succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    private static string PlanAction(QueryPlanMode mode)
        => mode == QueryPlanMode.Actual ? "query.execute" : "query.plan";
}
