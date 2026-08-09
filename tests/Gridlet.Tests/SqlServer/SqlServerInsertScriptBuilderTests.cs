using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerInsertScriptBuilderTests
{
    /// <summary>
    /// Scripts always break lines with \n. The expected text below is normalised to it, so the
    /// assertion holds however git materialised this file - CRLF on a Windows checkout, LF on CI.
    /// </summary>
    private const string Newline = "\n";

    private static TableDefinition Table(params ColumnInfo[] columns)
        => new(new DbObjectInfo("dbo", "Customers", DbObjectType.Table), columns, [], []);

    private static readonly ResultColumn[] Result =
        [new ResultColumn("Id", "int"), new ResultColumn("Name", "nvarchar"), new ResultColumn("Notes", "varchar")];

    private static readonly ColumnInfo[] Columns =
    [
        new ColumnInfo("Id", "int", false, true, false, true, null, 0),
        new ColumnInfo("Name", "nvarchar(50)", false, false, false, false, null, 1),
        new ColumnInfo("Notes", "varchar(50)", true, false, false, false, null, 2),
    ];

    [Fact]
    public void Rows_become_inserts_wrapped_in_identity_insert()
    {
        var script = SqlServerInsertScriptBuilder.Build(
            Table(Columns), Result, [[1, "O'Hara", null], [2, "Ada", "note"]]);

        Assert.Equal(
            """
            SET IDENTITY_INSERT [dbo].[Customers] ON;
            INSERT INTO [dbo].[Customers] ([Id], [Name], [Notes]) VALUES (1, N'O''Hara', NULL);
            INSERT INTO [dbo].[Customers] ([Id], [Name], [Notes]) VALUES (2, N'Ada', 'note');
            SET IDENTITY_INSERT [dbo].[Customers] OFF;
            """.ReplaceLineEndings(Newline),
            script);
    }

    [Fact]
    public void Computed_and_hidden_columns_are_left_out_because_they_cannot_be_written()
    {
        var table = Table(
            new ColumnInfo("Name", "nvarchar(50)", false, false, false, false, null, 0),
            new ColumnInfo("Upper", "nvarchar(50)", true, false, true, false, null, 1, "upper([Name])"),
            new ColumnInfo("SysStart", "datetime2", false, false, false, false, null, 2, IsHidden: true));
        var columns = new[]
        {
            new ResultColumn("Name", "nvarchar"),
            new ResultColumn("Upper", "nvarchar"),
            new ResultColumn("SysStart", "datetime2"),
        };

        var script = SqlServerInsertScriptBuilder.Build(table, columns, [["ada", "ADA", "2026-01-01"]]);

        Assert.Equal("INSERT INTO [dbo].[Customers] ([Name]) VALUES (N'ada');", script);
        Assert.DoesNotContain("IDENTITY_INSERT", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "1")]
    [InlineData(42, "42")]
    [InlineData(1.5, "1.5")]
    public void Values_are_written_as_literals_of_their_own_type(object value, string expected)
    {
        var table = Table(new ColumnInfo("Value", "sql_variant", true, false, false, false, null, 0));

        var script = SqlServerInsertScriptBuilder.Build(
            table, [new ResultColumn("Value", "sql_variant")], [[value]]);

        Assert.Contains($"VALUES ({expected});", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Binary_dates_and_guids_keep_their_own_notation()
    {
        var table = Table(
            new ColumnInfo("Data", "varbinary(8)", true, false, false, false, null, 0),
            new ColumnInfo("At", "datetime2(7)", true, false, false, false, null, 1),
            new ColumnInfo("Ref", "uniqueidentifier", true, false, false, false, null, 2));
        var columns = new[]
        {
            new ResultColumn("Data", "varbinary"),
            new ResultColumn("At", "datetime2"),
            new ResultColumn("Ref", "uniqueidentifier"),
        };

        var script = SqlServerInsertScriptBuilder.Build(table, columns,
            [[new byte[] { 0xAB, 0x01 }, new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Unspecified),
                Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e")]]);

        Assert.Contains(
            "VALUES (0xAB01, '2026-01-31T10:00:00.0000000', '0f8fad5b-d9cb-469f-a165-70867728950e');",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void No_rows_says_so_instead_of_producing_nothing()
        => Assert.Equal(
            "-- No rows to script for [dbo].[Customers].",
            SqlServerInsertScriptBuilder.Build(Table(Columns), Result, []));
}
