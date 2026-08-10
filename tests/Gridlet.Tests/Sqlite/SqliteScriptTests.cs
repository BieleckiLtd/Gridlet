using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteScriptTests : IAsyncLifetime
{
    /// <summary>
    /// Scripts always break lines with \n. The expected text below is normalised to it, so the
    /// assertion holds however git materialised this file - CRLF on a Windows checkout, LF on CI.
    /// </summary>
    private const string Newline = "\n";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-script-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Script",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");

        await provider.Query.ExecuteAsync(context,
            """
            CREATE TABLE Customers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Balance NUMERIC,
                Photo BLOB,
                Shout TEXT GENERATED ALWAYS AS (upper(Name)) VIRTUAL
            );
            INSERT INTO Customers (Name, Balance, Photo) VALUES
                ('O''Hara', 12.5, x'AB01'),
                ('Ada', NULL, NULL);
            """,
            new QueryRequestOptions(100, 30));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The scripted rows have to be runnable as they stand, which is what makes scripting an escape
    /// hatch rather than a preview: quoting, blobs and NULLs all have to come back correctly.
    /// </summary>
    [Fact]
    public async Task Rows_are_scripted_as_inserts_that_reproduce_them()
    {
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        var page = await provider.Data.GetPageAsync(context, "main", "Customers", new TableDataRequest(1, 10));

        var script = provider.Ddl.BuildInsertScript(definition, page.Columns, page.Rows);

        Assert.Equal(
            """
            INSERT INTO "main"."Customers" ("Id", "Name", "Balance", "Photo") VALUES (1, 'O''Hara', 12.5, X'AB01');
            INSERT INTO "main"."Customers" ("Id", "Name", "Balance", "Photo") VALUES (2, 'Ada', NULL, NULL);
            """.ReplaceLineEndings(Newline),
            script);

        // Round-trip: the script rebuilds the same rows in an empty copy of the table.
        await provider.Ddl.TruncateTableAsync(context, "main", "Customers");
        await provider.Query.ExecuteAsync(context, script, new QueryRequestOptions(100, 30));
        var restored = await provider.Data.GetPageAsync(context, "main", "Customers", new TableDataRequest(1, 10));
        Assert.Equal(2, restored.TotalRows);
        Assert.Equal("O'HARA", restored.Rows[0][4]);
    }

    [Fact]
    public async Task A_generated_column_is_not_scripted_because_it_cannot_be_written()
    {
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");
        var page = await provider.Data.GetPageAsync(context, "main", "Customers", new TableDataRequest(1, 10));

        var script = provider.Ddl.BuildInsertScript(definition, page.Columns, page.Rows);

        Assert.DoesNotContain("Shout", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_object_can_be_scripted_as_a_drop()
    {
        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Customers");

        Assert.Equal(
            "DROP TABLE \"main\".\"Customers\";",
            provider.Ddl.BuildDropScript(definition.Object));
    }
}
