using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerUserDefinedTypeFormatterTests
{
    [Fact]
    public void Formats_alias_and_clr_types_with_qualified_quoted_names()
    {
        Assert.Equal(
            "CREATE TYPE [sales].[Account Number] FROM nvarchar(24) NOT NULL;",
            SqlServerUserDefinedTypeFormatter.Format(new SqlServerUserDefinedType(
                "sales", "Account Number", "alias", "nvarchar(24)")));
        Assert.Equal(
            "CREATE TYPE [geo].[Point] EXTERNAL NAME [Spatial assembly].[Types.Point];",
            SqlServerUserDefinedTypeFormatter.Format(new SqlServerUserDefinedType(
                "geo", "Point", "clr", AssemblyName: "Spatial assembly", AssemblyClass: "Types.Point")));
    }

    [Fact]
    public void Formats_table_type_columns_and_discloses_metadata_limit()
    {
        var sql = SqlServerUserDefinedTypeFormatter.Format(new SqlServerUserDefinedType(
            "dbo", "OrderItems", "table", Columns:
            [
                new SqlServerUserDefinedTypeColumn("Order Id", "int", false, 1),
                new SqlServerUserDefinedTypeColumn("Note", "[dbo].[NoteText]", true, 2),
            ]));

        Assert.Contains("Constraints and indexes are not included", sql);
        Assert.Contains("[Order Id] int NOT NULL", sql);
        Assert.Contains("[Note] [dbo].[NoteText] NULL", sql);
    }
}
