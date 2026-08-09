namespace Gridlet.Models;

/// <summary>Which kind of execution plan to ask a provider for.</summary>
public enum QueryPlanMode
{
    /// <summary>
    /// The plan the engine would use, obtained without running the statement. Safe to ask for on any
    /// statement, including one that changes data.
    /// </summary>
    Estimated,

    /// <summary>
    /// The plan the engine actually used, with runtime counters. The statement runs, so it has
    /// whatever effect it would normally have; its result sets are not returned.
    /// </summary>
    Actual,
}

/// <summary>One operator in an execution plan.</summary>
/// <param name="Operation">The operator, such as "Clustered Index Seek" or "SCAN Customers".</param>
/// <param name="Detail">What the operator works on, where the engine says so.</param>
/// <param name="EstimatedRows">Rows the engine expected this operator to produce.</param>
/// <param name="ActualRows">Rows it produced, when the plan is an actual one.</param>
/// <param name="EstimatedCost">
/// The engine's cost for this operator and everything under it. Providers with no cost model leave
/// it null.
/// </param>
/// <param name="Warnings">Plan warnings attached to this operator, such as a missing index.</param>
/// <param name="Children">Operators feeding this one.</param>
public sealed record QueryPlanNode(
    string Operation,
    string? Detail = null,
    double? EstimatedRows = null,
    double? ActualRows = null,
    double? EstimatedCost = null,
    IReadOnlyList<string>? Warnings = null,
    IReadOnlyList<QueryPlanNode>? Children = null);

/// <summary>
/// An execution plan, normalized so the UI can render any provider's plan the same way while the
/// engine's own text stays available.
/// </summary>
/// <param name="Mode">Whether the plan is estimated or actual.</param>
/// <param name="Format">
/// The provider's own format for <paramref name="RawText"/>, such as <c>showplan-xml</c> or
/// <c>sqlite-query-plan</c>. Clients that only render the tree can ignore it.
/// </param>
/// <param name="Roots">The plan's statements, one root per statement.</param>
/// <param name="RawText">The plan exactly as the engine produced it, for copying out.</param>
/// <param name="Messages">
/// Anything the engine reported while producing the plan, such as SQL Server's STATISTICS IO and
/// TIME output.
/// </param>
public sealed record QueryPlan(
    QueryPlanMode Mode,
    string Format,
    IReadOnlyList<QueryPlanNode> Roots,
    string? RawText = null,
    IReadOnlyList<string>? Messages = null);
