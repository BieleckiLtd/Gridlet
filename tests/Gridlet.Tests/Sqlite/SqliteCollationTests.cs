using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// A column's collation decides how every comparison on it behaves, so losing it during a designer
/// rebuild would change what queries return. It used to block the designer entirely.
/// </summary>
public sealed class SqliteCollationTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-collate-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Collate",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE People (
                Id INTEGER PRIMARY KEY,
                Name TEXT COLLATE NOCASE,
                Code TEXT COLLATE RTRIM,
                Plain TEXT
            );
            INSERT INTO People (Name, Code, Plain) VALUES ('ada', 'x  ', 'ada');
            """,
            new QueryRequestOptions(10, 30));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Each_columns_collation_is_reported()
    {
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "People");

        Assert.Equal("NOCASE", Column(definition, "Name").Collation);
        Assert.Equal("RTRIM", Column(definition, "Code").Collation);
        Assert.Null(Column(definition, "Plain").Collation);
    }

    [Fact]
    public async Task A_rebuild_keeps_the_collation_and_the_behaviour_that_depends_on_it()
    {
        await provider.Ddl.AddColumnAsync(context, "main", "People",
            new ColumnDesign("Nickname", "TEXT", Collation: "NOCASE"));
        // Renaming a column rebuilds the table, which is where a collation used to be dropped.
        await provider.Ddl.AlterColumnAsync(context, "main", "People", "Plain",
            new ColumnDesign("PlainText", "TEXT"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "People");
        Assert.Equal("NOCASE", Column(definition, "Name").Collation);
        Assert.Equal("NOCASE", Column(definition, "Nickname").Collation);
        Assert.Equal("RTRIM", Column(definition, "Code").Collation);

        // The behaviour, not just the metadata: NOCASE still matches case-insensitively.
        var matched = await provider.Query.ExecuteAsync(context,
            "SELECT COUNT(*) FROM People WHERE Name = 'ADA';", new QueryRequestOptions(10, 30));
        Assert.Equal(1L, Convert.ToInt64(matched.ResultSets[0].Rows[0][0]));
    }

    [Fact]
    public void A_collation_name_is_validated_rather_than_pasted_into_the_statement()
    {
        Assert.Contains("COLLATE NOCASE", SqliteDdlBuilder.BuildAddColumn(
            "main", "People", new ColumnDesign("Extra", "TEXT", Collation: "NOCASE")),
            StringComparison.Ordinal);
        Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.BuildAddColumn(
            "main", "People", new ColumnDesign("Extra", "TEXT", Collation: "NOCASE, x TEXT")));
    }

    private static ColumnInfo Column(TableDefinition definition, string name)
        => definition.Columns.Single(column => column.Name == name);
}
