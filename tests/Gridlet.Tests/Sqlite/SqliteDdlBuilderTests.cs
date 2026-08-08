using Gridlet.Models;
using Gridlet.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteDdlBuilderTests
{
    [Theory]
    [InlineData("integer", "INTEGER")]
    [InlineData("VARCHAR ( 100 )", "VARCHAR(100)")]
    [InlineData("decimal(10, 2)", "DECIMAL(10,2)")]
    [InlineData("double precision", "DOUBLE PRECISION")]
    public void Normalises_supported_types(string input, string expected)
        => Assert.Equal(expected, SqliteDdlBuilder.NormalizeDataType(input));

    [Theory]
    [InlineData("")]
    [InlineData("TEXT; DROP TABLE widgets")]
    [InlineData("frobnicator")]
    public void Rejects_unsafe_or_unknown_types(string input)
        => Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.NormalizeDataType(input));

    [Fact]
    public void Builds_identity_primary_key_defaults_and_foreign_keys()
    {
        var sql = SqliteDdlBuilder.BuildCreateTable(
            new TableDesign("main", "Orders",
            [
                new ColumnDesign("Id", "INTEGER", IsNullable: false, IsIdentity: true, IsPrimaryKey: true),
                new ColumnDesign("CustomerId", "INTEGER", IsNullable: false),
                new ColumnDesign("Status", "TEXT", IsNullable: false, DefaultExpression: "'new'"),
            ]),
            "PK_Orders",
            [new ForeignKeyDesign("FK_Orders_Customers", "main", "Customers",
                [new ForeignKeyColumnPair("CustomerId", "Id")], OnDelete: "CASCADE")]);

        Assert.Contains("CREATE TABLE \"main\".\"Orders\"", sql);
        Assert.Contains("\"Id\" INTEGER PRIMARY KEY AUTOINCREMENT", sql);
        Assert.Contains("\"Status\" TEXT NOT NULL DEFAULT ('new')", sql);
        Assert.Contains("CONSTRAINT \"FK_Orders_Customers\" FOREIGN KEY (\"CustomerId\")", sql);
        Assert.Contains("REFERENCES \"Customers\" (\"Id\") ON DELETE CASCADE", sql);
    }

    [Fact]
    public void Rejects_non_main_schemas_and_nonstandard_identity_sequences()
    {
        Assert.Throws<GridletValidationException>(() =>
            SqliteDdlBuilder.BuildCreateTable(new TableDesign("dbo", "T",
                [new ColumnDesign("Id", "INTEGER")])));
        Assert.Throws<GridletValidationException>(() =>
            SqliteDdlBuilder.BuildCreateTable(new TableDesign("main", "T",
                [new ColumnDesign("Id", "INTEGER", false, true, true, IdentityIncrement: 2)])));
    }

    [Fact]
    public void Builds_drop_trigger()
        => Assert.Equal(
            "DROP TRIGGER \"main\".\"AuditWidgets\";",
            SqliteDdlBuilder.BuildDropObject("main", "AuditWidgets", DbObjectType.Trigger));

    [Fact]
    public void Double_quotes_identifiers_and_escapes_embedded_quotes()
    {
        Assert.Equal("\"a]b\"", SqliteIdentifier.Quote("a]b"));
        Assert.Equal("\"a\"\"b\"", SqliteIdentifier.Quote("a\"b"));
    }

    [Theory]
    [InlineData("0)); DROP TABLE victim; /*")]
    [InlineData("1 -- comment")]
    [InlineData("1 /* comment */")]
    [InlineData("(1 + 2")]
    [InlineData("1 + 2)")]
    public void Rejects_non_expression_default_payloads(string expression)
        => Assert.Throws<GridletValidationException>(() =>
            SqliteDdlBuilder.BuildAddColumn(
                "main", "Widgets", new ColumnDesign("Value", "INTEGER", DefaultExpression: expression)));

    [Fact]
    public void Allows_balanced_nested_and_quoted_default_expressions()
    {
        var sql = SqliteDdlBuilder.BuildAddColumn(
            "main", "Widgets",
            new ColumnDesign("Value", "TEXT", DefaultExpression: "COALESCE(NULLIF('semi;--/*', ''), (1 + 2))"));

        Assert.Contains("COALESCE(NULLIF('semi;--/*', ''), (1 + 2))", sql);
    }
}
