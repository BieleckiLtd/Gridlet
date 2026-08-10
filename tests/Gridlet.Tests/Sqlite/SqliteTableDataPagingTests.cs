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

    [Fact]
    public async Task Implicit_rowid_breaks_ties_between_duplicate_nullable_primary_keys()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var request = new TableDataRequest(1, 1, "KeyPart", SortDirection.Ascending);
            var first = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "NullableKeys", request);
            var second = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "NullableKeys", request with { Page = 2 });
            var third = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "NullableKeys", request with { Page = 3 });

            Assert.Equal("first", Assert.Single(first.Rows)[2]);
            Assert.Equal("second", Assert.Single(second.Rows)[2]);
            Assert.Equal("third", Assert.Single(third.Rows)[2]);
            var repeatedSecond = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "NullableKeys", request with { Page = 2 });
            Assert.Equal("second", Assert.Single(repeatedSecond.Rows)[2]);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Paging_uses_an_unshadowed_rowid_alias()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var page = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "ShadowedRowId", new TableDataRequest(1, 10));

            Assert.Equal([30L, 20L, 10L], page.Rows.Select(row => row[0]).ToArray());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task All_shadowed_rowid_aliases_fall_back_to_remaining_visible_columns()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var request = new TableDataRequest(1, 1);
            var first = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "AllAliasesShadowed", request);
            var second = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "AllAliasesShadowed", request with { Page = 2 });
            var third = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "AllAliasesShadowed", request with { Page = 3 });

            Assert.Equal("alpha", Assert.Single(first.Rows)[5]);
            Assert.Equal("bravo", Assert.Single(second.Rows)[5]);
            Assert.Equal("charlie", Assert.Single(third.Rows)[5]);
            var repeatedSecond = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "AllAliasesShadowed", request with { Page = 2 });
            Assert.Equal("bravo", Assert.Single(repeatedSecond.Rows)[5]);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Without_rowid_tables_rely_on_their_primary_key_order()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var page = await fixture.Service.GetPageAsync(
                fixture.Context, "main", "CompactItems", new TableDataRequest(1, 10));

            Assert.Equal(["a", "b"], page.Rows.Select(row => row[0]).ToArray());
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
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
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
                CREATE TABLE NullableKeys (
                    NullablePart TEXT,
                    KeyPart INTEGER,
                    Payload TEXT NOT NULL,
                    PRIMARY KEY (NullablePart, KeyPart)
                );
                INSERT INTO NullableKeys VALUES
                    (NULL, 1, 'first'),
                    (NULL, 1, 'second'),
                    (NULL, 1, 'third');
                CREATE TABLE ShadowedRowId (
                    rowid INTEGER,
                    NullablePart TEXT,
                    KeyPart INTEGER,
                    PRIMARY KEY (NullablePart, KeyPart)
                );
                INSERT INTO ShadowedRowId VALUES
                    (30, NULL, 1),
                    (20, NULL, 1),
                    (10, NULL, 1);
                CREATE TABLE AllAliasesShadowed (
                    rowid INTEGER,
                    _rowid_ INTEGER,
                    oid INTEGER,
                    NullablePart TEXT,
                    KeyPart INTEGER,
                    Payload TEXT NOT NULL,
                    PRIMARY KEY (NullablePart, KeyPart)
                );
                INSERT INTO AllAliasesShadowed VALUES
                    (0, 0, 0, NULL, 1, 'charlie'),
                    (0, 0, 0, NULL, 1, 'alpha'),
                    (0, 0, 0, NULL, 1, 'bravo');
                CREATE TABLE CompactItems (
                    Code TEXT PRIMARY KEY,
                    Payload TEXT
                ) WITHOUT ROWID;
                INSERT INTO CompactItems VALUES ('b', 'second'), ('a', 'first');
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
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
