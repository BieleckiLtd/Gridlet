using Gridlet.Models;

namespace Gridlet.Abstractions;

/// <summary>
/// Optional capability of an <see cref="IQueryRunner"/>: explain how the engine would run a
/// statement, or how it did run it.
/// </summary>
public interface IQueryPlanRunner
{
    /// <summary>
    /// Returns the plan for <paramref name="sql"/>. An estimated plan does not run the statement; an
    /// actual plan does, and its result sets are discarded because the plan is what was asked for.
    /// </summary>
    Task<QueryPlan> GetPlanAsync(
        GridletConnectionContext context,
        string sql,
        QueryPlanMode mode,
        QueryRequestOptions options,
        CancellationToken cancellationToken = default);
}
