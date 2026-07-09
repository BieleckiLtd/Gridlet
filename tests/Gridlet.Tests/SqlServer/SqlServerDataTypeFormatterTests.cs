using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerDataTypeFormatterTests
{
    [Theory]
    [InlineData("int", 4, 10, 0, "int")]
    [InlineData("nvarchar", 100, 0, 0, "nvarchar(50)")] // max_length is bytes; nvarchar is 2 bytes/char
    [InlineData("nvarchar", -1, 0, 0, "nvarchar(max)")]
    [InlineData("varchar", 80, 0, 0, "varchar(80)")]
    [InlineData("varbinary", -1, 0, 0, "varbinary(max)")]
    [InlineData("decimal", 9, 10, 2, "decimal(10,2)")]
    [InlineData("datetime2", 8, 27, 7, "datetime2(7)")]
    [InlineData("bit", 1, 1, 0, "bit")]
    public void Formats_display_types(string typeName, int maxLength, int precision, int scale, string expected)
    {
        Assert.Equal(expected, SqlServerDataTypeFormatter.Format(typeName, maxLength, precision, scale));
    }
}
