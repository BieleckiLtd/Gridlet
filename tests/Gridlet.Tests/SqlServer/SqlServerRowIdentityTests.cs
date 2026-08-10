using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerRowIdentityTests
{
    private static readonly Dictionary<string, bool> Columns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Id"] = false,
        ["Code"] = false,
        ["Region"] = false,
        ["Email"] = true,
    };

    [Fact]
    public void Declared_primary_key_wins()
    {
        var identity = SqlServerRowIdentity.Resolve(
            ["Id"],
            [new SqlServerRowIdentity.UniqueKey("UX_Code", ["Code"])],
            Columns);

        Assert.NotNull(identity);
        Assert.Equal(RowIdentityKinds.PrimaryKey, identity.Kind);
        Assert.Equal(["Id"], identity.Columns);
        Assert.Null(identity.Source);
    }

    [Fact]
    public void Heap_falls_back_to_a_unique_key_over_non_nullable_columns()
    {
        var identity = SqlServerRowIdentity.Resolve(
            [],
            [new SqlServerRowIdentity.UniqueKey("UX_Code", ["Code"])],
            Columns);

        Assert.NotNull(identity);
        Assert.Equal(RowIdentityKinds.UniqueKey, identity.Kind);
        Assert.Equal(["Code"], identity.Columns);
        Assert.Equal("UX_Code", identity.Source);
    }

    [Fact]
    public void Nullable_disabled_and_filtered_unique_keys_are_rejected()
    {
        var identity = SqlServerRowIdentity.Resolve(
            [],
            [
                new SqlServerRowIdentity.UniqueKey("UX_Email", ["Email"]),
                new SqlServerRowIdentity.UniqueKey("UX_Disabled", ["Code"], IsDisabled: true),
                new SqlServerRowIdentity.UniqueKey("UX_Filtered", ["Region"], IsFiltered: true),
                new SqlServerRowIdentity.UniqueKey("UX_Unknown", ["Missing"]),
                new SqlServerRowIdentity.UniqueKey("UX_Empty", []),
            ],
            Columns);

        Assert.Null(identity);
    }

    [Fact]
    public void The_narrowest_usable_unique_key_is_chosen()
    {
        var identity = SqlServerRowIdentity.Resolve(
            [],
            [
                new SqlServerRowIdentity.UniqueKey("UX_Composite", ["Code", "Region"]),
                new SqlServerRowIdentity.UniqueKey("UX_Region", ["Region"]),
            ],
            Columns);

        Assert.NotNull(identity);
        Assert.Equal(["Region"], identity.Columns);
        Assert.Equal("UX_Region", identity.Source);
    }

    [Fact]
    public void Equally_wide_unique_keys_are_chosen_by_name_so_paging_stays_stable()
    {
        var identity = SqlServerRowIdentity.Resolve(
            [],
            [
                new SqlServerRowIdentity.UniqueKey("UX_Region", ["Region"]),
                new SqlServerRowIdentity.UniqueKey("UX_Code", ["Code"]),
            ],
            Columns);

        Assert.NotNull(identity);
        Assert.Equal("UX_Code", identity.Source);
    }

    [Fact]
    public void Composite_key_order_is_preserved()
    {
        var identity = SqlServerRowIdentity.Resolve(
            [],
            [new SqlServerRowIdentity.UniqueKey("UX_Composite", ["Region", "Code"])],
            Columns);

        Assert.NotNull(identity);
        Assert.Equal(["Region", "Code"], identity.Columns);
    }

    [Fact]
    public void No_key_at_all_leaves_the_table_read_only()
        => Assert.Null(SqlServerRowIdentity.Resolve([], [], Columns));
}
