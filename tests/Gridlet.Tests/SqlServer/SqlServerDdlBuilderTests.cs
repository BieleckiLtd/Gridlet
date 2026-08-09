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
    // Types the designer used to refuse outright, all of them ordinary SQL Server built-ins.
    [InlineData("SQL_VARIANT", "sql_variant")]
    [InlineData("text", "text")]
    [InlineData("image", "image")]
    [InlineData("geography", "geography")]
    [InlineData("hierarchyid", "hierarchyid")]
    [InlineData("json", "json")]
    [InlineData("vector(1536)", "vector(1536)")]
    public void Normalises_valid_data_types(string input, string expected)
    {
        Assert.Equal(expected, SqlServerDdlBuilder.NormalizeDataType(input));
    }

    /// <summary>
    /// Alias, CLR and table types are per-database, so there is no list to check them against. They
    /// are quoted instead, which keeps them harmless and leaves an unknown name for the engine to
    /// reject with its own message.
    /// </summary>
    [Theory]
    [InlineData("AccountNumber", "[AccountNumber]")]
    [InlineData("dbo.AccountNumber", "[dbo].[AccountNumber]")]
    [InlineData("[dbo].[Account Number]", "[dbo].[Account Number]")]
    [InlineData("frobnicator", "[frobnicator]")]
    public void Quotes_user_defined_types(string input, string expected)
    {
        Assert.Equal(expected, SqlServerDdlBuilder.NormalizeDataType(input));
    }

    [Theory]
    [InlineData("int; DROP TABLE x")]
    [InlineData("nvarchar(100)) AS SELECT 1 --")]
    [InlineData("nvarchar(100) NOT NULL, [x] int")]
    [InlineData("dbo.Type; DROP TABLE x")]
    [InlineData("frobnicator(1,2,3)")]
    [InlineData("")]
    public void Rejects_hostile_or_malformed_data_types(string input)
    {
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.NormalizeDataType(input));
    }

    /// <summary>
    /// A collation is an identifier the engine resolves, not a value, so it cannot be quoted - which
    /// makes validating its shape the only thing standing between it and the statement.
    /// </summary>
    [Fact]
    public void A_column_collation_is_emitted_and_validated()
    {
        var sql = SqlServerDdlBuilder.BuildCreateTable(new TableDesign("dbo", "Widgets",
            [new ColumnDesign("Name", "nvarchar(100)", IsNullable: false, Collation: "Latin1_General_CI_AS")]));

        Assert.Contains("[Name] nvarchar(100) COLLATE Latin1_General_CI_AS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateTable(
            new TableDesign("dbo", "Widgets",
                [new ColumnDesign("Name", "nvarchar(100)", Collation: "X NOT NULL, [y] int")])));
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
        Assert.Equal(
            "DROP TRIGGER [dbo].[AuditT];",
            SqlServerDdlBuilder.BuildDropObject("dbo", "AuditT", DbObjectType.Trigger));
    }

    [Fact]
    public void Builds_computed_and_custom_identity_columns()
    {
        Assert.Equal(
            "ALTER TABLE [dbo].[T] ADD [Total] AS ([Quantity] * [Price]) PERSISTED;",
            SqlServerDdlBuilder.BuildAddColumn("dbo", "T",
                new ColumnDesign("Total", "", ComputedExpression: "[Quantity] * [Price]", IsPersisted: true)));
        Assert.Equal(
            "ALTER TABLE [dbo].[T] ADD [Sequence] bigint IDENTITY(100,5) NOT NULL;",
            SqlServerDdlBuilder.BuildAddColumn("dbo", "T",
                new ColumnDesign("Sequence", "bigint", IsNullable: false, IsIdentity: true,
                    IdentitySeed: 100, IdentityIncrement: 5)));
    }

    [Fact]
    public void Builds_primary_and_foreign_key_operations()
    {
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] ADD CONSTRAINT [PK_Orders] PRIMARY KEY CLUSTERED ([TenantId], [Id]);",
            SqlServerDdlBuilder.BuildAddPrimaryKey("sales", "Orders",
                new PrimaryKeyDesign("PK_Orders", ["TenantId", "Id"])));
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] ADD CONSTRAINT [FK_Orders_Customers] FOREIGN KEY ([TenantId], [CustomerId]) REFERENCES [crm].[Customers] ([TenantId], [Id]) ON DELETE CASCADE ON UPDATE NO ACTION;",
            SqlServerDdlBuilder.BuildAddForeignKey("sales", "Orders",
                new ForeignKeyDesign("FK_Orders_Customers", "crm", "Customers",
                    [new("TenantId", "TenantId"), new("CustomerId", "Id")], "CASCADE")));
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] DROP CONSTRAINT [FK_Orders_Customers];",
            SqlServerDdlBuilder.BuildDropConstraint("sales", "Orders", "FK_Orders_Customers"));
    }

    [Fact]
    public void Builds_check_and_unique_constraint_operations()
    {
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] WITH NOCHECK ADD CONSTRAINT [CK_Orders_Total] CHECK NOT FOR REPLICATION ([Total] >= 0); ALTER TABLE [sales].[Orders] NOCHECK CONSTRAINT [CK_Orders_Total];",
            SqlServerDdlBuilder.BuildAddCheckConstraint("sales", "Orders",
                new CheckConstraintDesign(
                    "CK_Orders_Total", "[Total] >= 0", CheckExistingData: false,
                    IsDisabled: true, IsNotForReplication: true)));
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] ADD CONSTRAINT [UQ_Orders_Number] UNIQUE NONCLUSTERED ([TenantId] ASC, [Number] DESC) WITH (FILLFACTOR = 80);",
            SqlServerDdlBuilder.BuildAddUniqueConstraint("sales", "Orders",
                new UniqueConstraintDesign(
                    "UQ_Orders_Number",
                    [new("TenantId"), new("Number", IsDescending: true)],
                    FillFactor: 80)));
        Assert.Equal(
            "ALTER TABLE [sales].[Orders] DROP CONSTRAINT [CK_Orders_Total];",
            SqlServerDdlBuilder.BuildDropCheckConstraint(
                "sales", "Orders", new ConstraintReference("CK_Orders_Total")));
    }

    [Fact]
    public void Builds_rich_rowstore_and_columnstore_indexes()
    {
        Assert.Equal(
            "CREATE UNIQUE NONCLUSTERED INDEX [IX_Orders_Number] ON [sales].[Orders] ([TenantId] ASC, [Number] DESC) INCLUDE ([CreatedAt]) WHERE ([IsDeleted] = 0) WITH (FILLFACTOR = 90); ALTER INDEX [IX_Orders_Number] ON [sales].[Orders] DISABLE;",
            SqlServerDdlBuilder.BuildCreateIndex("sales", "Orders",
                new IndexDesign(
                    "IX_Orders_Number",
                    [new("TenantId"), new("Number", IsDescending: true)],
                    IsUnique: true,
                    IncludedColumns: ["CreatedAt"],
                    FilterExpression: "[IsDeleted] = 0",
                    FillFactor: 90,
                    IsDisabled: true)));
        Assert.Equal(
            "CREATE CLUSTERED COLUMNSTORE INDEX [CCI_Orders] ON [sales].[Orders];",
            SqlServerDdlBuilder.BuildCreateIndex("sales", "Orders",
                new IndexDesign("CCI_Orders", [], IsClustered: true, IsColumnstore: true)));
        Assert.Equal(
            "CREATE NONCLUSTERED COLUMNSTORE INDEX [NCCI_Orders] ON [sales].[Orders] ([Total], [CreatedAt]) WHERE ([Total] > 0);",
            SqlServerDdlBuilder.BuildCreateIndex("sales", "Orders",
                new IndexDesign(
                    "NCCI_Orders", [new("Total"), new("CreatedAt")],
                    FilterExpression: "[Total] > 0", IsColumnstore: true)));
        Assert.Equal(
            "DROP INDEX [IX_Orders_Number] ON [sales].[Orders];",
            SqlServerDdlBuilder.BuildDropIndex("sales", "Orders", "IX_Orders_Number"));
    }

    [Fact]
    public void Rejects_index_options_sql_server_cannot_represent()
    {
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T", new IndexDesign("IX", [new(null, Expression: "lower([Name])")])));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T", new IndexDesign("IX", [new("Name", Collation: "Latin1_General_CI_AS")])));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T", new IndexDesign("IX", [new("Name")], IncludedColumns: ["name"])));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T", new IndexDesign("IX", [], IsClustered: true, IsColumnstore: true, FilterExpression: "Id > 0")));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T", new IndexDesign("IX", [new("Id")], IsClustered: true, FilterExpression: "Id > 0")));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildAddUniqueConstraint(
            "dbo", "T", new UniqueConstraintDesign("UQ_T", [new("Name")], FillFactor: 101)));
        Assert.Throws<GridletValidationException>(() => SqlServerDdlBuilder.BuildDropUniqueConstraint(
            "dbo", "T", new ConstraintReference(Ordinal: 0)));
    }

    [Fact]
    public void Parenthesises_filter_so_index_options_cannot_be_injected()
    {
        var sql = SqlServerDdlBuilder.BuildCreateIndex(
            "dbo", "T",
            new IndexDesign(
                "IX_T_Id",
                [new("Id")],
                FilterExpression: "([Id] > 0) WITH (DROP_EXISTING = ON)"));

        Assert.Contains("WHERE (([Id] > 0) WITH (DROP_EXISTING = ON));", sql);
        Assert.DoesNotContain("WHERE ([Id] > 0) WITH (DROP_EXISTING = ON)", sql);
    }

    [Fact]
    public void Synthesised_create_renders_check_and_unique_constraints()
    {
        var definition = new TableDefinition(
            new DbObjectInfo("sales", "Orders", DbObjectType.Table),
            [new ColumnInfo("Id", "int", false, false, false, true, null, 0),
             new ColumnInfo("Number", "nvarchar(20)", false, false, false, false, null, 1)],
            [new IndexInfo(
                "PK_Orders", "CLUSTERED", true, true, ["Id"],
                [new IndexKeyInfo("Id", 1)], IsDisabled: true)],
            [],
            [new CheckConstraintInfo("CK_Orders_Id", "[Id] > 0", IsDisabled: true)],
            [new UniqueConstraintInfo(
                "UQ_Orders_Number", [new IndexKeyInfo("Number", 1, IsDescending: true)],
                FillFactor: 75)]);

        var sql = SqlServerDdlBuilder.BuildTableDefinition(definition);

        Assert.Contains("CONSTRAINT [CK_Orders_Id] CHECK ([Id] > 0)", sql);
        Assert.Contains(
            "CONSTRAINT [UQ_Orders_Number] UNIQUE NONCLUSTERED ([Number] DESC) WITH (FILLFACTOR = 75)", sql);
        Assert.Contains("ALTER TABLE [sales].[Orders] NOCHECK CONSTRAINT [CK_Orders_Id];", sql);
        Assert.Contains("ALTER INDEX [PK_Orders] ON [sales].[Orders] DISABLE;", sql);
    }

    [Fact]
    public void Synthesised_create_appends_rich_rowstore_indexes()
    {
        var definition = new TableDefinition(
            new DbObjectInfo("sales", "Orders", DbObjectType.Table),
            [new ColumnInfo("TenantId", "int", false, false, false, false, null, 0),
             new ColumnInfo("Number", "nvarchar(20)", false, false, false, false, null, 1),
             new ColumnInfo("CreatedAt", "datetime2", false, false, false, false, null, 2)],
            [new IndexInfo(
                "IX_Orders_Number", "NONCLUSTERED", true, false, ["TenantId", "Number"],
                [new IndexKeyInfo("TenantId", 1), new IndexKeyInfo("Number", 2, IsDescending: true)],
                ["CreatedAt"], "[Number] IS NOT NULL", FillFactor: 85, IsDisabled: true)],
            [], [], []);

        var sql = SqlServerDdlBuilder.BuildTableDefinition(definition);

        const string create =
            "CREATE UNIQUE NONCLUSTERED INDEX [IX_Orders_Number] ON [sales].[Orders] ([TenantId] ASC, [Number] DESC) INCLUDE ([CreatedAt]) WHERE ([Number] IS NOT NULL) WITH (FILLFACTOR = 85);";
        const string disable = "ALTER INDEX [IX_Orders_Number] ON [sales].[Orders] DISABLE;";
        Assert.Contains(create, sql);
        Assert.Contains(disable, sql);
        Assert.True(sql.IndexOf(create, StringComparison.Ordinal) < sql.IndexOf(disable, StringComparison.Ordinal));
    }

    [Fact]
    public void Synthesised_create_appends_unordered_columnstore_indexes_and_preserves_disabled_state()
    {
        var definition = new TableDefinition(
            new DbObjectInfo("dbo", "Facts", DbObjectType.Table),
            [new ColumnInfo("Id", "bigint", false, false, false, false, null, 0),
             new ColumnInfo("Amount", "decimal(18,2)", false, false, false, false, null, 1)],
            [new IndexInfo(
                "CCI_Facts", "CLUSTERED COLUMNSTORE", false, false, ["Id", "Amount"],
                [new IndexKeyInfo("Id", 1), new IndexKeyInfo("Amount", 2)],
                IsClustered: true, IsColumnstore: true),
             new IndexInfo(
                "NCCI_Facts_Amount", "NONCLUSTERED COLUMNSTORE", false, false, ["Amount"],
                [new IndexKeyInfo("Amount", 1)], IsColumnstore: true, IsDisabled: true)],
            [], [], []);

        var sql = SqlServerDdlBuilder.BuildTableDefinition(definition);

        Assert.Contains("CREATE CLUSTERED COLUMNSTORE INDEX [CCI_Facts] ON [dbo].[Facts];", sql);
        Assert.DoesNotContain("[CCI_Facts] ON [dbo].[Facts] (", sql);
        Assert.Contains(
            "CREATE NONCLUSTERED COLUMNSTORE INDEX [NCCI_Facts_Amount] ON [dbo].[Facts] ([Amount]);", sql);
        Assert.Contains("ALTER INDEX [NCCI_Facts_Amount] ON [dbo].[Facts] DISABLE;", sql);
    }

    [Theory]
    [InlineData("XML")]
    [InlineData("SPATIAL")]
    [InlineData("NONCLUSTERED HASH")]
    [InlineData("JSON")]
    public void Synthesised_create_comments_out_index_kinds_it_cannot_represent(string kind)
    {
        var definition = new TableDefinition(
            new DbObjectInfo("dbo", "T", DbObjectType.Table),
            [new ColumnInfo("Id", "int", false, false, false, false, null, 0)],
            [new IndexInfo("IX_Unsupported", kind, false, false, ["Id"],
                [new IndexKeyInfo("Id", 1)])],
            [], [], []);

        var sql = SqlServerDdlBuilder.BuildTableDefinition(definition);

        Assert.Contains("-- Gridlet omitted index [IX_Unsupported]:", sql);
        Assert.DoesNotContain("CREATE NONCLUSTERED INDEX [IX_Unsupported]", sql);
        Assert.DoesNotContain("CREATE CLUSTERED COLUMNSTORE INDEX [IX_Unsupported]", sql);
    }

    [Fact]
    public void Synthesised_create_comments_out_ordered_clustered_columnstore_from_reader_metadata()
    {
        var definition = new TableDefinition(
            new DbObjectInfo("dbo", "T", DbObjectType.Table),
            [new ColumnInfo("Id", "int", false, false, false, false, null, 0)],
            [new IndexInfo(
                "CCI_T", "CLUSTERED COLUMNSTORE", false, false, ["Id"],
                [new IndexKeyInfo("Id", 1)], IsClustered: true, IsColumnstore: true,
                IsOrderedColumnstore: true)],
            [], [], []);

        var sql = SqlServerDdlBuilder.BuildTableDefinition(definition);

        Assert.Contains("-- Gridlet omitted index [CCI_T]: ordered columnstore metadata", sql);
        Assert.DoesNotContain("CREATE CLUSTERED COLUMNSTORE INDEX [CCI_T]", sql);
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

    [Theory]
    [InlineData("0)); DROP TABLE dbo.Victim; /*")]
    [InlineData("1 -- comment")]
    [InlineData("1 /* comment */")]
    [InlineData("(1 + 2")]
    [InlineData("1 + 2)")]
    public void Rejects_non_expression_default_payloads(string expression)
        => Assert.Throws<GridletValidationException>(() =>
            SqlServerDdlBuilder.BuildAddColumn(
                "dbo", "Widgets", new ColumnDesign("Value", "int", DefaultExpression: expression)));

    [Fact]
    public void Allows_balanced_nested_and_quoted_default_expressions()
    {
        var sql = SqlServerDdlBuilder.BuildAddColumn(
            "dbo", "Widgets",
            new ColumnDesign("Value", "nvarchar(100)",
                DefaultExpression: "COALESCE(NULLIF('semi;--/*', ''), CONCAT([A]],B], 'x'))"));

        Assert.Contains("COALESCE(NULLIF('semi;--/*', ''), CONCAT([A]],B], 'x'))", sql);
    }
}
