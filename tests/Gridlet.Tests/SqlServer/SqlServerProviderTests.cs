using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerProviderTests
{
    [Fact]
    public void Advertises_schema_fidelity_capabilities()
    {
        var capabilities = new SqlServerGridletProvider().Capabilities;

        Assert.True(capabilities.SupportsCheckConstraints);
        Assert.True(capabilities.SupportsUniqueConstraints);
        Assert.True(capabilities.SupportsIndexes);
        Assert.True(capabilities.SupportsSequences);
        Assert.True(capabilities.SupportsImport);
    }

    [Fact]
    public void Sequence_sql_is_quoted_and_rejects_executable_values()
    {
        var sql = SqlServerSequenceService.BuildCreate(new Gridlet.Models.SequenceDesign(
            "sales", "Order Numbers", "decimal(20,0)", "1000", "5",
            MinimumValue: "10", MaximumValue: "999999", IsCycling: true,
            IsCached: true, CacheSize: 50));

        Assert.Equal(
            "CREATE SEQUENCE [sales].[Order Numbers] AS decimal(20,0) START WITH 1000 INCREMENT BY 5 MINVALUE 10 MAXVALUE 999999 CYCLE CACHE 50;",
            sql);
        Assert.Equal("ALTER SEQUENCE [sales].[Order Numbers] RESTART WITH -5;",
            SqlServerSequenceService.BuildRestart("sales", "Order Numbers", "-5"));
        Assert.Throws<GridletValidationException>(() =>
            SqlServerSequenceService.BuildRestart("dbo", "Unsafe", "1; DROP TABLE Users"));
        Assert.Throws<GridletValidationException>(() =>
            SqlServerSequenceService.BuildRestart("dbo", "Fractional", "1.5"));
        Assert.Throws<GridletValidationException>(() => SqlServerSequenceService.BuildCreate(
            new Gridlet.Models.SequenceDesign("dbo", "TooPrecise", "decimal(99,0)", "1", "1")));
    }
}
