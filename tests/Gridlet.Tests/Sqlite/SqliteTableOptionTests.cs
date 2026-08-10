using Gridlet.Models;
using Gridlet.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// WITHOUT ROWID and STRICT change what a table is. The designer used to refuse to touch such a
/// table at all; now it can create one and, more importantly, must not quietly drop the option when
/// it rebuilds one.
/// </summary>
public sealed class SqliteTableOptionTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"gridlet-options-{Guid.NewGuid():N}.db");
    private readonly SqliteGridletProvider provider = new();
    private GridletConnectionContext context = null!;

    public Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        context = new GridletConnectionContext(
            new GridletConnectionOptions
            {
                Name = "Options",
                ConnectionString = connectionString,
                ProviderName = GridletProviderNames.Sqlite,
            },
            "main");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(databasePath)) File.Delete(databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public void A_create_statement_carries_the_options_it_was_given()
    {
        var design = new TableDesign("main", "Codes",
            [
                new ColumnDesign("Code", "TEXT", IsNullable: false, IsPrimaryKey: true),
                new ColumnDesign("Label", "TEXT"),
            ],
            [SqliteTableOptions.WithoutRowId, SqliteTableOptions.Strict]);

        Assert.EndsWith(") WITHOUT ROWID, STRICT;", SqliteDdlBuilder.BuildCreateTable(design), StringComparison.Ordinal);
    }

    [Fact]
    public void A_without_rowid_table_needs_a_primary_key()
    {
        var design = new TableDesign("main", "Codes",
            [new ColumnDesign("Code", "TEXT")], [SqliteTableOptions.WithoutRowId]);

        var exception = Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.BuildCreateTable(design));
        Assert.Contains("primary key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_strict_table_names_the_column_whose_type_it_cannot_accept()
    {
        var design = new TableDesign("main", "Codes",
            [
                new ColumnDesign("Code", "TEXT", IsNullable: false, IsPrimaryKey: true),
                new ColumnDesign("Amount", "NUMERIC"),
            ],
            [SqliteTableOptions.Strict]);

        var exception = Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.BuildCreateTable(design));
        Assert.Contains("Amount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_option_is_rejected()
        => Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.BuildCreateTable(
            new TableDesign("main", "Codes", [new ColumnDesign("Code", "TEXT")], ["WITH ROWID"])));

    [Fact]
    public async Task Both_options_are_reported_on_the_table_they_belong_to()
    {
        await provider.Ddl.CreateTableAsync(context, new TableDesign("main", "Codes",
            [
                new ColumnDesign("Code", "TEXT", IsNullable: false, IsPrimaryKey: true),
                new ColumnDesign("Label", "TEXT"),
            ],
            [SqliteTableOptions.WithoutRowId, SqliteTableOptions.Strict]));
        await provider.Ddl.CreateTableAsync(context, new TableDesign("main", "Plain",
            [new ColumnDesign("Id", "INTEGER", IsNullable: false, IsPrimaryKey: true)]));

        var codes = await provider.Schema.GetTableDefinitionAsync(context, "main", "Codes");
        var plain = await provider.Schema.GetTableDefinitionAsync(context, "main", "Plain");

        Assert.Equal(["WITHOUT ROWID", "STRICT"], codes.TableOptions);
        Assert.Empty(plain.TableOptions!);
    }

    /// <summary>
    /// The designer rebuilds a SQLite table to change a column. Losing STRICT there would turn a
    /// table that rejects wrong types into one that silently accepts them.
    /// </summary>
    [Fact]
    public async Task A_rebuild_keeps_the_options_and_the_rows()
    {
        await provider.Ddl.CreateTableAsync(context, new TableDesign("main", "Codes",
            [
                new ColumnDesign("Code", "TEXT", IsNullable: false, IsPrimaryKey: true),
                new ColumnDesign("Label", "TEXT"),
            ],
            [SqliteTableOptions.WithoutRowId, SqliteTableOptions.Strict]));
        await provider.Query.ExecuteAsync(context,
            "INSERT INTO Codes VALUES ('a', 'alpha');", new QueryRequestOptions(10, 30));

        await provider.Ddl.AddColumnAsync(context, "main", "Codes", new ColumnDesign("Note", "TEXT"));
        await provider.Ddl.AlterColumnAsync(context, "main", "Codes", "Label",
            new ColumnDesign("Caption", "TEXT"));

        var definition = await provider.Schema.GetTableDefinitionAsync(context, "main", "Codes");
        Assert.Equal(["WITHOUT ROWID", "STRICT"], definition.TableOptions);
        Assert.Contains(definition.Columns, column => column.Name == "Caption");
        var page = await provider.Data.GetPageAsync(context, "main", "Codes", new TableDataRequest(1, 10));
        Assert.Equal(1, page.TotalRows);
    }
}
