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
        Assert.True(capabilities.SupportsDefaultConstraints);
        Assert.True(capabilities.SupportsSecurityOverview);
        Assert.True(capabilities.SupportsTriggerManagement);
    }

    [Theory]
    [InlineData("object", true, "ENABLE TRIGGER [audit].[TrackOrders] ON [sales].[Orders];")]
    [InlineData("database", false, "DISABLE TRIGGER [TrackDdl] ON DATABASE;")]
    [InlineData("server", true, "ENABLE TRIGGER [TrackLogons] ON ALL SERVER;")]
    public void Builds_trigger_state_sql(string scope, bool enabled, string expected)
    {
        var design = new Gridlet.Models.TriggerStateDesign(
            scope == "object" ? "TrackOrders" : scope == "database" ? "TrackDdl" : "TrackLogons",
            scope, enabled,
            Schema: scope == "object" ? "audit" : null,
            ParentSchema: scope == "object" ? "sales" : null,
            ParentName: scope == "object" ? "Orders" : null);

        Assert.Equal(expected, SqlServerTriggerService.BuildSetEnabled(design));
    }

    [Fact]
    public void Trigger_state_sql_requires_a_known_complete_target()
    {
        Assert.Throws<GridletValidationException>(() => SqlServerTriggerService.BuildSetEnabled(
            new Gridlet.Models.TriggerStateDesign("T", "unknown", true)));
        Assert.Throws<GridletValidationException>(() => SqlServerTriggerService.BuildSetEnabled(
            new Gridlet.Models.TriggerStateDesign("T", Gridlet.Models.TriggerScopes.Object, true)));
    }

    [Fact]
    public void Altering_a_column_preserves_its_existing_default_constraint_name()
    {
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] ADD CONSTRAINT [DF_Custom_Created] DEFAULT (GETDATE()) FOR [Created];",
            SqlServerTableDdlService.BuildReplacementDefault(
                "sales", "Orders", "Created", "GETDATE()", "DF_Custom_Created"));
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] ADD CONSTRAINT [DF_Orders_Status] DEFAULT (0) FOR [Status];",
            SqlServerTableDdlService.BuildReplacementDefault(
                "sales", "Orders", "Status", "0", null));
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

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, Gridlet.Models.TemporalTableKinds.HistoryTable)]
    [InlineData(2, Gridlet.Models.TemporalTableKinds.SystemVersioned)]
    public void Maps_sql_server_temporal_types(int temporalType, string? expectedKind)
    {
        var temporal = SqlServerSchemaReader.CreateTemporalTableInfo(
            temporalType, "history", "OrdersHistory", "ValidFrom", "ValidTo");

        Assert.Equal(expectedKind, temporal?.Kind);
        if (temporal is not null)
        {
            Assert.Equal("history", temporal.RelatedSchema);
            Assert.Equal("OrdersHistory", temporal.RelatedTable);
            Assert.Equal("ValidFrom", temporal.PeriodStartColumn);
            Assert.Equal("ValidTo", temporal.PeriodEndColumn);
        }
    }

    [Fact]
    public void Maps_infinite_temporal_retention_to_no_finite_policy()
    {
        var temporal = SqlServerSchemaReader.CreateTemporalTableInfo(
            2, "history", "OrdersHistory", "ValidFrom", "ValidTo", -1, "INFINITE");

        Assert.NotNull(temporal);
        Assert.Null(temporal.HistoryRetentionPeriod);
        Assert.Null(temporal.HistoryRetentionUnit);
    }
}
