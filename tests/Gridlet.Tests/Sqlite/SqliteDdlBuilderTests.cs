using Gridlet.Models;
using Gridlet.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

public sealed class SqliteDdlBuilderTests
{
    [Fact]
    public void Retains_the_public_three_parameter_create_table_abi()
    {
        var method = typeof(SqliteDdlBuilder).GetMethod(nameof(SqliteDdlBuilder.BuildCreateTable),
            [typeof(TableDesign), typeof(string), typeof(IReadOnlyList<ForeignKeyDesign>)]);
        Assert.NotNull(method);
        Assert.True(method!.IsPublic);
    }

    [Theory]
    [InlineData("integer", "INTEGER")]
    [InlineData("VARCHAR ( 100 )", "VARCHAR(100)")]
    [InlineData("decimal(10, 2)", "DECIMAL(10,2)")]
    [InlineData("double precision", "DOUBLE PRECISION")]
    // SQLite derives affinity from the declared text and accepts any name, so the designer does
    // too: ANY belongs to STRICT tables, and an application is free to invent the rest.
    [InlineData("any", "ANY")]
    [InlineData("json", "JSON")]
    [InlineData("varchar2(30)", "VARCHAR2(30)")]
    [InlineData("my_type", "MY_TYPE")]
    public void Normalises_supported_types(string input, string expected)
        => Assert.Equal(expected, SqliteDdlBuilder.NormalizeDataType(input));

    [Theory]
    [InlineData("")]
    [InlineData("TEXT; DROP TABLE widgets")]
    [InlineData("TEXT DEFAULT 'x', y INTEGER")]
    [InlineData("TEXT) --")]
    [InlineData("\"TEXT\"")]
    [InlineData("TEXT(1,2,3)")]
    public void Rejects_unsafe_or_malformed_types(string input)
        => Assert.Throws<GridletValidationException>(() => SqliteDdlBuilder.NormalizeDataType(input));

    /// <summary>
    /// A type name may contain spaces, so shape alone cannot separate DOUBLE PRECISION from a type
    /// with a constraint stuck on the end. Left alone, that text goes into the column definition
    /// whole: "TEXT NOT NULL" on a nullable column would silently make it required, and "TEXT
    /// REFERENCES Other" would add a foreign key the designer never showed anybody.
    /// </summary>
    [Theory]
    [InlineData("TEXT NOT NULL")]
    [InlineData("TEXT PRIMARY KEY")]
    [InlineData("INTEGER PRIMARY KEY AUTOINCREMENT")]
    [InlineData("TEXT UNIQUE")]
    [InlineData("TEXT COLLATE NOCASE")]
    [InlineData("TEXT REFERENCES Other")]
    [InlineData("INTEGER GENERATED ALWAYS AS")]
    public void Rejects_a_constraint_dressed_up_as_a_type(string input)
    {
        var exception = Assert.Throws<GridletValidationException>(
            () => SqliteDdlBuilder.NormalizeDataType(input));

        Assert.Contains("column constraint", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal is about constraint words, not about spaces: real type names still pass, and the
    /// extra spaces a paste leaves behind are written out as one rather than rejected.
    /// </summary>
    [Theory]
    [InlineData("double precision", "DOUBLE PRECISION")]
    [InlineData("double  precision", "DOUBLE PRECISION")]
    [InlineData("unsigned big int", "UNSIGNED BIG INT")]
    [InlineData("varying\tcharacter(20)", "VARYING CHARACTER(20)")]
    public void Still_accepts_a_type_name_written_as_several_words(string input, string expected)
        => Assert.Equal(expected, SqliteDdlBuilder.NormalizeDataType(input));

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
    public void Accepts_attached_database_names_and_rejects_nonstandard_identity_sequences()
    {
        Assert.Contains("CREATE TABLE \"archive\".\"T\"",
            SqliteDdlBuilder.BuildCreateTable(new TableDesign("archive", "T",
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
