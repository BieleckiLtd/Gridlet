using Gridlet.AgentFramework;
using Xunit;

namespace Gridlet.Tests.AgentFramework;

public sealed class GridletReadOnlySqlGuardTests
{
    [Theory]
    [InlineData("SELECT Id, Name FROM dbo.Customers WHERE Id = 1")]
    [InlineData("WITH totals AS (SELECT CustomerId, COUNT(*) AS N FROM Orders GROUP BY CustomerId) SELECT * FROM totals")]
    [InlineData("-- comment\nSELECT '; DROP TABLE x' AS Harmless;")]
    public void Allows_one_read_only_query(string sql)
        => Assert.True(GridletReadOnlySqlGuard.TryValidate(sql, out var error), error);

    [Theory]
    [InlineData("SELECT * INTO BackupCustomers FROM Customers")]
    [InlineData("SELECT 1; DELETE FROM Customers")]
    [InlineData("WITH doomed AS (SELECT 1 AS X) DELETE FROM Customers")]
    [InlineData("PRAGMA table_info(Customers)")]
    [InlineData("EXEC dbo.RefreshCustomers")]
    [InlineData("SELECT * FROM OPENROWSET('provider', 'secret', 'query')")]
    public void Rejects_mutation_dynamic_sql_and_multiple_statements(string sql)
        => Assert.False(GridletReadOnlySqlGuard.TryValidate(sql, out _));

    [Fact]
    public void Ignores_dangerous_words_inside_identifiers_and_literals()
    {
        const string sql = "SELECT [Drop], 'DELETE FROM x' AS [Text] FROM [Create]";

        Assert.True(GridletReadOnlySqlGuard.TryValidate(sql, out var error), error);
    }
}
