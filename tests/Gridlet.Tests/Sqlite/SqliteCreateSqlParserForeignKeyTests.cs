using Gridlet.Models;
using Gridlet.Sqlite;
using Xunit;

namespace Gridlet.Tests.Sqlite;

/// <summary>
/// The CREATE statement is the only place a SQLite foreign-key name survives, so the parser has to
/// read both constraint forms and leave an unnamed key unnamed.
/// </summary>
public sealed class SqliteCreateSqlParserForeignKeyTests
{
    [Fact]
    public void Reads_table_and_column_level_keys_in_declaration_order()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                A INTEGER CONSTRAINT fk_a REFERENCES P1 (Id),
                B INTEGER REFERENCES P2,
                C INTEGER,
                D INTEGER,
                CONSTRAINT fk_c FOREIGN KEY (C) REFERENCES "P 3" (Id),
                FOREIGN KEY (D) REFERENCES P4 (Id)
            )
            """);

        Assert.Collection(parsed.ForeignKeys,
            key =>
            {
                Assert.Equal("fk_a", key.Name);
                Assert.Equal(["A"], key.Columns);
                Assert.Equal("P1", key.ReferencedTable);
            },
            key =>
            {
                Assert.Null(key.Name);
                Assert.Equal(["B"], key.Columns);
                Assert.Equal("P2", key.ReferencedTable);
            },
            key =>
            {
                Assert.Equal("fk_c", key.Name);
                Assert.Equal(["C"], key.Columns);
                Assert.Equal("P 3", key.ReferencedTable);
            },
            key =>
            {
                Assert.Null(key.Name);
                Assert.Equal(["D"], key.Columns);
                Assert.Equal("P4", key.ReferencedTable);
            });
    }

    [Fact]
    public void Reads_a_composite_key_with_quoted_columns()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                [Parent A] INTEGER,
                "Parent B" INTEGER,
                CONSTRAINT `fk composite` FOREIGN KEY ([Parent A], "Parent B") REFERENCES Parent (A, B)
            )
            """);

        var key = Assert.Single(parsed.ForeignKeys);
        Assert.Equal("fk composite", key.Name);
        Assert.Equal(["Parent A", "Parent B"], key.Columns);
        Assert.Equal("Parent", key.ReferencedTable);
    }

    /// <summary>
    /// REFERENCES inside a CHECK expression is part of no constraint; only a top-level clause is a
    /// foreign key.
    /// </summary>
    [Fact]
    public void Ignores_text_that_only_looks_like_a_reference()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                Note TEXT CONSTRAINT ck_note CHECK (Note <> 'REFERENCES Parent (Id)'),
                Kind TEXT DEFAULT 'REFERENCES'
            )
            """);

        Assert.Empty(parsed.ForeignKeys);
        Assert.Equal("ck_note", Assert.Single(parsed.Checks).Name);
    }

    [Fact]
    public void Still_reads_checks_uniques_and_collations_alongside_keys()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                Code TEXT COLLATE NOCASE CONSTRAINT ux_code UNIQUE,
                ParentId INTEGER REFERENCES Parent (Id),
                CONSTRAINT ck_positive CHECK (ParentId > 0)
            )
            """);

        Assert.Equal("ux_code", Assert.Single(parsed.Uniques).Name);
        Assert.Equal("ck_positive", Assert.Single(parsed.Checks).Name);
        Assert.Equal("NOCASE", parsed.ColumnCollations["Code"]);
        Assert.Null(Assert.Single(parsed.ForeignKeys).Name);
    }

    [Fact]
    public void Reads_column_names_through_comments_and_whitespace()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                A INTEGER,
                B INTEGER,
                CONSTRAINT fk FOREIGN KEY (
                    A, -- first
                    "B" /* second */
                ) REFERENCES Parent (X, Y)
            )
            """);

        var key = Assert.Single(parsed.ForeignKeys);
        Assert.Equal("fk", key.Name);
        Assert.Equal(["A", "B"], key.Columns);
    }

    /// <summary>
    /// SQLite reads a single-quoted token as an identifier where a string literal cannot appear, and
    /// older tools wrote foreign keys that way.
    /// </summary>
    [Fact]
    public void Reads_single_quoted_identifiers()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                ParentId INTEGER,
                CONSTRAINT fk_quoted FOREIGN KEY ('ParentId') REFERENCES 'Parent' (Id)
            )
            """);

        var key = Assert.Single(parsed.ForeignKeys);
        Assert.Equal("fk_quoted", key.Name);
        Assert.Equal(["ParentId"], key.Columns);
        Assert.Equal("Parent", key.ReferencedTable);
    }

    /// <summary>
    /// A column list that is not a plain list of identifiers is not something to guess at; the key
    /// is left out so the caller falls back to a label rather than pairing the wrong name.
    /// </summary>
    [Fact]
    public void Skips_a_key_whose_column_list_is_not_plain_identifiers()
    {
        var parsed = SqliteCreateSqlParser.ParseTable("""
            CREATE TABLE Child (
                A INTEGER,
                CONSTRAINT fk FOREIGN KEY (A + 1) REFERENCES Parent (X)
            )
            """);

        Assert.Empty(parsed.ForeignKeys);
    }

    [Fact]
    public void Retains_the_previous_six_field_deconstruction()
    {
        var (name, referencedSchema, referencedTable, columns, onDelete, onUpdate) =
            new ForeignKeyInfo("fk", "main", "Parent",
                [new ForeignKeyColumnPair("ParentId", "Id")], "CASCADE", "NO_ACTION", true);

        Assert.Equal("fk", name);
        Assert.Equal("main", referencedSchema);
        Assert.Equal("Parent", referencedTable);
        Assert.Equal("ParentId", Assert.Single(columns).Column);
        Assert.Equal("CASCADE", onDelete);
        Assert.Equal("NO_ACTION", onUpdate);

        var (designName, _, designTable, _, _, _) =
            new ForeignKeyDesign("fk", "main", "Parent",
                [new ForeignKeyColumnPair("ParentId", "Id")], "NO ACTION", "NO ACTION", true);
        Assert.Equal("fk", designName);
        Assert.Equal("Parent", designTable);
    }

    /// <summary>
    /// SQLite accepts any character at or above U+0080 in an unquoted identifier, including a
    /// combining mark that .NET does not classify as a letter.
    /// </summary>
    [Fact]
    public void Reads_unquoted_identifiers_that_carry_combining_marks()
    {
        // The identifiers below are "Cafe" plus a combining acute accent, which SQLite
        // reads as one identifier and .NET classifies as a letter followed by a mark.
        var parsed = SqliteCreateSqlParser.ParseTable(
            "CREATE TABLE Child (\n" +
            "    Cafe\u0301Id INTEGER,\n" +
            "    CONSTRAINT fk_cafe\u0301 FOREIGN KEY (Cafe\u0301Id) REFERENCES Cafe\u0301 (Id)\n" +
            ")");

        var key = Assert.Single(parsed.ForeignKeys);
        Assert.Equal("fk_cafe\u0301", key.Name);
        Assert.Equal(["Cafe\u0301Id"], key.Columns);
        Assert.Equal("Cafe\u0301", key.ReferencedTable);
    }

    /// <summary>
    /// SQLite separates tokens on ASCII whitespace only. A no-break space belongs to the identifier
    /// even though .NET calls it whitespace.
    /// </summary>
    [Fact]
    public void Reads_unquoted_identifiers_that_carry_non_ascii_spaces()
    {
        // "Cafe", a no-break space, then "Id".
        const string name = "Cafe\u00A0Id";
        var parsed = SqliteCreateSqlParser.ParseTable(
            "CREATE TABLE Child (\n" +
            $"    {name} INTEGER,\n" +
            $"    CONSTRAINT fk_{name} FOREIGN KEY ({name}) REFERENCES Parent (Id)\n" +
            ")");

        var key = Assert.Single(parsed.ForeignKeys);
        Assert.Equal($"fk_{name}", key.Name);
        Assert.Equal([name], key.Columns);
    }

    /// <summary>
    /// Both records carry a compatibility constructor, which leaves System.Text.Json with two
    /// candidates unless the primary one is named.
    /// </summary>
    [Fact]
    public void Round_trips_through_System_Text_Json()
    {
        var key = new ForeignKeyInfo("fk", "main", "Parent",
            [new ForeignKeyColumnPair("ParentId", "Id")], "CASCADE", "NO_ACTION", true);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ForeignKeyInfo>(
            System.Text.Json.JsonSerializer.Serialize(key))!;
        Assert.Equal(key with { Columns = [] }, restored with { Columns = [] });
        Assert.Equal(key.Columns, restored.Columns);

        var design = new ForeignKeyDesign("fk", "main", "Parent",
            [new ForeignKeyColumnPair("ParentId", "Id")], "NO ACTION", "NO ACTION", true);
        var restoredDesign = System.Text.Json.JsonSerializer.Deserialize<ForeignKeyDesign>(
            System.Text.Json.JsonSerializer.Serialize(design))!;
        Assert.Equal(design with { Columns = [] }, restoredDesign with { Columns = [] });
        Assert.Equal(design.Columns, restoredDesign.Columns);
    }

    [Fact]
    public void Names_a_foreign_key_definition_only_when_the_name_was_declared()
    {
        var columns = new[] { new ForeignKeyColumnPair("ParentId", "Id") };

        Assert.StartsWith(
            "CONSTRAINT \"fk_child_parent\" FOREIGN KEY",
            SqliteDdlBuilder.BuildForeignKeyDefinition(
                new ForeignKeyDesign("fk_child_parent", "main", "Parent", columns)),
            StringComparison.Ordinal);

        Assert.StartsWith(
            "FOREIGN KEY",
            SqliteDdlBuilder.BuildForeignKeyDefinition(
                new ForeignKeyDesign("FK_Child_0", "main", "Parent", columns,
                    "NO ACTION", "NO ACTION", IsNameSynthesized: true)),
            StringComparison.Ordinal);

        // Leaving the name blank is a malformed request, not a request for an unnamed key.
        Assert.Throws<GridletValidationException>(() =>
            SqliteDdlBuilder.BuildForeignKeyDefinition(
                new ForeignKeyDesign("  ", "main", "Parent", columns)));
    }
}
