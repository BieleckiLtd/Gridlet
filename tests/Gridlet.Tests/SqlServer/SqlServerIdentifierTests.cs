using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerIdentifierTests
{
    [Fact]
    public void Quotes_plain_identifier()
    {
        Assert.Equal("[Customers]", SqlServerIdentifier.Quote("Customers"));
    }

    [Fact]
    public void Escapes_closing_brackets()
    {
        Assert.Equal("[a]]b]", SqlServerIdentifier.Quote("a]b"));
    }

    [Fact]
    public void Neutralises_breakout_attempts()
    {
        // "]; DROP TABLE x; --" must stay inside the brackets.
        Assert.Equal("[]]; DROP TABLE x; --]", SqlServerIdentifier.Quote("]; DROP TABLE x; --"));
    }

    [Fact]
    public void Quotes_qualified_name()
    {
        Assert.Equal("[dbo].[Customers]", SqlServerIdentifier.QuoteQualified("dbo", "Customers"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_identifiers(string name)
    {
        Assert.Throws<GridletValidationException>(() => SqlServerIdentifier.Quote(name));
    }

    [Fact]
    public void Rejects_overlong_identifiers()
    {
        Assert.Throws<GridletValidationException>(() => SqlServerIdentifier.Quote(new string('x', 129)));
    }
}
