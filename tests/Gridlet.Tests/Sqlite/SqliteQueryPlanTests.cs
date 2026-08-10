using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteQueryPlanTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-plan-{Guid.NewGuid():N}.db");
    private readonly SqliteQueryRunner runner = new();
    private GridletConnectionContext context = null!;

    private static readonly QueryRequestOptions Request = new(100, 30);

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Plan",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await runner.ExecuteAsync(context,
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL, Total NUMERIC);
            CREATE INDEX IX_Orders_CustomerId ON Orders (CustomerId);
            """,
            Request);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_join_is_explained_as_a_tree_of_what_the_planner_chose()
    {
        var plan = await runner.GetPlanAsync(
            context,
            "SELECT c.Name, o.Total FROM Customers c JOIN Orders o ON o.CustomerId = c.Id;",
            QueryPlanMode.Estimated,
            Request);

        Assert.Equal("sqlite-query-plan", plan.Format);
        Assert.Equal(QueryPlanMode.Estimated, plan.Mode);
        // The planner drives from Orders and looks each customer up by rowid; the point of the
        // assertion is that both steps and their operations survive the flat-list-to-tree mapping.
        var described = Flatten(plan.Roots).Select(node => $"{node.Operation} {node.Detail}").ToArray();
        Assert.Contains("SCAN o", described);
        Assert.Contains(described, entry => entry.StartsWith("SEARCH c ", StringComparison.Ordinal));
        Assert.NotNull(plan.RawText);
    }

    [Fact]
    public async Task A_subquery_becomes_a_child_of_the_step_that_runs_it()
    {
        var plan = await runner.GetPlanAsync(
            context,
            "SELECT * FROM Customers WHERE Id IN (SELECT CustomerId FROM Orders WHERE Total > 10);",
            QueryPlanMode.Estimated,
            Request);

        // SQLite reports the subquery with the outer step as its parent; a flat list would lose that.
        Assert.Contains(Flatten(plan.Roots), node => node.Children is { Count: > 0 });
    }

    [Fact]
    public async Task Asking_for_an_actual_plan_says_SQLite_has_none_rather_than_pretending()
    {
        var plan = await runner.GetPlanAsync(
            context, "SELECT * FROM Customers;", QueryPlanMode.Actual, Request);

        Assert.Equal(QueryPlanMode.Estimated, plan.Mode);
        Assert.Contains(plan.Messages!, message => message.Contains("no actual execution plan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_statement_the_planner_rejects_reports_the_engines_message()
    {
        var exception = await Assert.ThrowsAsync<GridletQueryException>(
            () => runner.GetPlanAsync(context, "SELECT * FROM Nope;", QueryPlanMode.Estimated, Request));

        Assert.Contains("Nope", exception.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<QueryPlanNode> Flatten(IEnumerable<QueryPlanNode> nodes)
        => nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children ?? [])));
}
