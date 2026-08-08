using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteTableDataPagingTests
{
    [Fact]
    public async Task Composite_primary_key_provides_stable_default_page_order()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var first = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "Items", new TableDataRequest(1, 2));
            var second = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "Items", new TableDataRequest(2, 2));

            Assert.Equal(["1:1", "1:2"], Keys(first));
            Assert.Equal(["2:1", "2:2"], Keys(second));
            Assert.Empty(Keys(first).Intersect(Keys(second), StringComparer.Ordinal));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Explicit_non_unique_sort_appends_primary_key_tie_breakers_across_pages()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var request = new TableDataRequest(1, 2, "Category", SortDirection.Descending);
            var first = await fixture.Service.GetPageAsync(fixture.Context, "main", "Items", request);
            var second = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "Items", request with { Page = 2 });
            var repeatedFirst = await fixture.Service.GetPageAsync(fixture.Context, "main", "Items", request);

            Assert.Equal(["1:1", "1:2"], Keys(first));
            Assert.Equal(["2:1", "2:2"], Keys(second));
            Assert.Equal(Keys(first), Keys(repeatedFirst));
            Assert.Empty(Keys(first).Intersect(Keys(second), StringComparer.Ordinal));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static string[] Keys(TableDataPage page)
        => page.Rows.Select(row => $"{row[0]}:{row[1]}").ToArray();

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gridlet-paging-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE Items (
                    TenantId INTEGER NOT NULL,
                    ItemId INTEGER NOT NULL,
                    Category TEXT NOT NULL,
                    PRIMARY KEY (TenantId, ItemId)
                );
                INSERT INTO Items VALUES
                    (2, 2, 'same'),
                    (1, 2, 'same'),
                    (2, 1, 'same'),
                    (1, 1, 'same'),
                    (3, 1, 'same');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Paging",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");
        return new Fixture(path, context, new SqliteTableDataService());
    }

    private sealed record Fixture(
        string Path,
        GridletConnectionContext Context,
        SqliteTableDataService Service) : IDisposable
    {
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
