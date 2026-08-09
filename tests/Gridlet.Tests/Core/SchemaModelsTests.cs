using Gridlet.Models;
using Xunit;

namespace Gridlet.Tests.Core;

public sealed class SchemaModelsTests
{
    [Fact]
    public void Legacy_index_shape_still_constructs_and_deconstructs()
    {
        var index = new IndexInfo("IX_T_Name", "NONCLUSTERED", false, false, ["Name"]);
        var legacyConstructor = typeof(IndexInfo).GetConstructor(
        [
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(bool),
            typeof(IReadOnlyList<string>),
        ]);

        var (name, kind, isUnique, isPrimaryKey, columns) = index;

        Assert.NotNull(legacyConstructor);
        var reflected = Assert.IsType<IndexInfo>(legacyConstructor.Invoke(
            ["IX_T_Name", "NONCLUSTERED", false, false, new[] { "Name" }]));
        Assert.Equal("IX_T_Name", name);
        Assert.Equal("NONCLUSTERED", kind);
        Assert.False(isUnique);
        Assert.False(isPrimaryKey);
        Assert.Equal(["Name"], columns);
        Assert.Null(index.KeyColumns);
        Assert.False(index.IsOrderedColumnstore);
        Assert.Equal(index.Name, reflected.Name);
        Assert.Equal(index.Kind, reflected.Kind);
        Assert.Equal(index.IsUnique, reflected.IsUnique);
        Assert.Equal(index.IsPrimaryKey, reflected.IsPrimaryKey);
        Assert.Equal(index.Columns, reflected.Columns);
        Assert.Null(reflected.KeyColumns);
        Assert.False(reflected.IsOrderedColumnstore);
    }

    [Fact]
    public void Rich_index_shape_carries_ordered_columnstore_marker()
    {
        var index = new IndexInfo(
            "CCI_T", "CLUSTERED COLUMNSTORE", false, false, ["Id"],
            [new IndexKeyInfo("Id", 1)], IsClustered: true, IsColumnstore: true,
            IsOrderedColumnstore: true);

        Assert.True(index.IsOrderedColumnstore);
    }

    [Fact]
    public void Legacy_table_definition_shape_uses_empty_new_collections()
    {
        var definition = new TableDefinition(
            new DbObjectInfo("dbo", "T", DbObjectType.Table), [], [], []);

        var (@object, columns, indexes, foreignKeys) = definition;

        Assert.Equal("T", @object.Name);
        Assert.Empty(columns);
        Assert.Empty(indexes);
        Assert.Empty(foreignKeys);
        Assert.Empty(definition.CheckConstraints);
        Assert.Empty(definition.UniqueConstraints);
    }
}
