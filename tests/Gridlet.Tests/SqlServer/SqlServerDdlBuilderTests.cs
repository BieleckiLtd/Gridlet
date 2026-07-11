using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerDdlBuilderTests
{
    [Theory]
    [InlineData("int", "int")]
    [InlineData("NVARCHAR(100)", "nvarchar(100)")]
    [InlineData("nvarchar ( max )", "nvarchar(max)")]
    [InlineData("decimal(10, 2)", "decimal(10,2)")]
    public void Normalises_valid_data_types(string input, string expected)
    {
        Assert.Equal(expected, SqlServerDdlBuilder.NormalizeDataType(input));
    }

    [Theory]
    [InlineData("int; DROP TABLE x")]
    [InlineData("nvarchar(100)) AS SELECT 1 --")]
    [InlineData("frobnicator")]
    [InlineData("")]
    public void Rejects_hostile_or_unknown_data_types(string input)
    {
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.NormalizeDataType(input));
    }

    [Fact]
    public void Builds_create_table_with_identity_pk_and_default()
    {
        var sql = SqlServerDdlBuilder.BuildCreateTable(new TableDesign("dbo", "Widgets",
        [
            new ColumnDesign("Id", "int", IsNullable: false, IsIdentity: true, IsPrimaryKey: true),
            new ColumnDesign("Name", "nvarchar(100)", IsNullable: false),
            new ColumnDesign("CreatedAt", "datetime2", IsNullable: false, DefaultExpression: "SYSUTCDATETIME()"),
        ]));

        Assert.Contains("CREATE TABLE [dbo].[Widgets]", sql);
        Assert.Contains("[Id] int IDENTITY(1,1) NOT NULL", sql);
        Assert.Contains("[Name] nvarchar(100) NOT NULL", sql);
        Assert.Contains("[CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME())", sql);
        Assert.Contains("CONSTRAINT [PK_Widgets] PRIMARY KEY ([Id])", sql);
    }

    [Fact]
    public void Create_table_requires_columns()
    {
        Assert.Throws<GridletValidationException>(
            () => SqlServerDdlBuilder.BuildCreateTable(new TableDesign("dbo", "Empty", [])));
    }

    [Fact]
    public void Primary_key_columns_are_never_nullable()
    {
        var sql = SqlServerDdlBuilder.BuildCreateTable(new TableDesign("dbo", "T",
            [new ColumnDesign("Id", "int", IsNullable: true, IsPrimaryKey: true)]));

        Assert.Contains("[Id] int NOT NULL", sql);
    }

    [Fact]
    public void Builds_column_operations()
    {
        Assert.Equal(
            "ALTER TABLE [dbo].[T] ADD [Age] int NULL;",
            SqlServerDdlBuilder.BuildAddColumn("dbo", "T", new ColumnDesign("Age", "int")));
        Assert.Equal(
            "ALTER TABLE [dbo].[T] ALTER COLUMN [Age] bigint NOT NULL;",
            SqlServerDdlBuilder.BuildAlterColumn("dbo", "T", new ColumnDesign("Age", "bigint", IsNullable: false)));
        Assert.Equal(
            "ALTER TABLE [dbo].[T] DROP COLUMN [Age];",
            SqlServerDdlBuilder.BuildDropColumn("dbo", "T", "Age"));
        Assert.Equal(
            "DROP TABLE [dbo].[T];",
            SqlServerDdlBuilder.BuildDropTable("dbo", "T"));
        Assert.Equal(
            "DROP VIEW [dbo].[V];",
            SqlServerDdlBuilder.BuildDropObject("dbo", "V", DbObjectType.View));
        Assert.Equal(
            "DROP PROCEDURE [dbo].[P];",
            SqlServerDdlBuilder.BuildDropObject("dbo", "P", DbObjectType.StoredProcedure));
    }

    [Fact]
    public void Builds_safe_create_schema_if_missing()
    {
        Assert.Equal(
            "IF SCHEMA_ID(@schema) IS NULL EXEC(N'CREATE SCHEMA [sales'']]archive]');",
            SqlServerDdlBuilder.BuildCreateSchemaIfMissing("sales']archive"));
    }

    [Fact]
    public void Builds_schema_operations()
    {
        Assert.Equal("CREATE SCHEMA [sales] AUTHORIZATION [reporting_user];",
            SqlServerDdlBuilder.BuildCreateSchema(new SchemaDesign("sales", "reporting_user")));
        Assert.Equal("ALTER AUTHORIZATION ON SCHEMA::[sales] TO [dbo];",
            SqlServerDdlBuilder.BuildAlterSchemaOwner("sales", "dbo"));
        Assert.Equal("DROP SCHEMA [sales];", SqlServerDdlBuilder.BuildDropSchema("sales"));
    }
}
