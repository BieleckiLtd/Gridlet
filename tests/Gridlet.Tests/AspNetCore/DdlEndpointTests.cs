using System.Net;
using System.Net.Http.Json;
using Gridlet.Abstractions;
using Gridlet.Tests.AspNetCore.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public class DdlEndpointTests
{
    private const string Db = "/gridlet/api/connections/Main/databases/FakeDb";

    [Fact]
    public async Task Schema_operations_reach_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var list = await client.GetAsync($"{Db}/schemas");
        var create = await client.PostAsJsonAsync($"{Db}/schemas", new { name = "sales", owner = "dbo" });
        var alter = await client.PutAsJsonAsync($"{Db}/schemas/sales", new { name = "sales", owner = "app_user" });
        var drop = await client.DeleteAsync($"{Db}/schemas/sales");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alter.StatusCode);
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);
        Assert.Contains("createSchema sales owner=dbo", fake.Calls);
        Assert.Contains("alterSchemaOwner sales owner=app_user", fake.Calls);
        Assert.Contains("dropSchema sales", fake.Calls);
    }

    [Fact]
    public async Task Table_and_column_operations_reach_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var create = await client.PostAsJsonAsync($"{Db}/tables", new
        {
            schema = "dbo",
            name = "Widgets",
            columns = new[] { new { name = "Id", dataType = "int", isPrimaryKey = true, isIdentity = true, isNullable = false } },
        });
        var addColumn = await client.PostAsJsonAsync($"{Db}/objects/dbo/Widgets/columns",
            new { name = "Age", dataType = "int", isNullable = true });
        var alterColumn = await client.PutAsJsonAsync($"{Db}/objects/dbo/Widgets/columns/Age",
            new { name = "Years", dataType = "bigint", isNullable = false });
        var dropColumn = await client.DeleteAsync($"{Db}/objects/dbo/Widgets/columns/Years");
        var addPrimaryKey = await client.PostAsJsonAsync($"{Db}/objects/dbo/Widgets/primary-key",
            new { name = "PK_Widgets", columns = new[] { "Id" }, isClustered = true });
        var addForeignKey = await client.PostAsJsonAsync($"{Db}/objects/dbo/Widgets/foreign-keys", new
        {
            name = "FK_Widgets_Owners", referencedSchema = "dbo", referencedTable = "Owners",
            columns = new[] { new { column = "OwnerId", referencedColumn = "Id" } },
            onDelete = "CASCADE", onUpdate = "NO ACTION",
        });
        var dropConstraint = await client.DeleteAsync($"{Db}/objects/dbo/Widgets/constraints/FK_Widgets_Owners");
        var dropTable = await client.DeleteAsync($"{Db}/objects/dbo/Widgets");
        var dropView = await client.DeleteAsync($"{Db}/objects/dbo/WidgetView?type=View");
        var dropTrigger = await client.DeleteAsync($"{Db}/objects/dbo/AuditWidgets?type=Trigger");

        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, alterColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropColumn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addPrimaryKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addForeignKey.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropConstraint.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropTable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropView.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dropTrigger.StatusCode);
        Assert.Contains("createTable dbo.Widgets (1 columns)", fake.Calls);
        Assert.Contains("addColumn dbo.Widgets.Age", fake.Calls);
        Assert.Contains("alterColumn dbo.Widgets.Age -> Years", fake.Calls);
        Assert.Contains("dropColumn dbo.Widgets.Years", fake.Calls);
        Assert.Contains("addPrimaryKey dbo.Widgets.PK_Widgets", fake.Calls);
        Assert.Contains("addForeignKey dbo.Widgets.FK_Widgets_Owners", fake.Calls);
        Assert.Contains("dropConstraint dbo.Widgets.FK_Widgets_Owners", fake.Calls);
        Assert.Contains("dropObject Table dbo.Widgets", fake.Calls);
        Assert.Contains("dropObject View dbo.WidgetView", fake.Calls);
        Assert.Contains("dropObject Trigger dbo.AuditWidgets", fake.Calls);
    }

    [Fact]
    public async Task Ddl_is_forbidden_when_disabled()
    {
        var (app, client) = await GridletTestHost.StartAsync(o =>
        {
            o.AddConnection("Main", "Server=x;", FakeGridletProvider.Name, c => c.AllowDdl = false);
            o.Security.AllowAnonymous = true;
        });
        await using var _ = app;

        var create = await client.PostAsJsonAsync($"{Db}/tables", new
        {
            schema = "dbo",
            name = "Widgets",
            columns = new[] { new { name = "Id", dataType = "int" } },
        });
        var dropTable = await client.DeleteAsync($"{Db}/objects/dbo/Widgets");
        var addCheck = await client.PostAsJsonAsync($"{Db}/objects/dbo/Widgets/check-constraints",
            new { name = "CK_Widgets", expression = "Age >= 0" });
        var dropIndex = await client.DeleteAsync($"{Db}/objects/dbo/Widgets/indexes/IX_Widgets");

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dropTable.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, addCheck.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, dropIndex.StatusCode);
    }

    [Fact]
    public async Task Portable_constraint_and_index_operations_reach_the_provider()
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();
        const string table = $"{Db}/objects/dbo/Widgets";

        var addCheck = await client.PostAsJsonAsync($"{table}/check-constraints",
            new { name = "CK_Widgets_Age", expression = "Age >= 0" });
        var dropCheck = await client.PostAsJsonAsync($"{table}/check-constraints/drop",
            new { ordinal = 2 });
        var addUnique = await client.PostAsJsonAsync($"{table}/unique-constraints", new
        {
            name = "UQ_Widgets_Code",
            columns = new[] { new { column = "Code", isDescending = true } },
        });
        var dropUnique = await client.PostAsJsonAsync($"{table}/unique-constraints/drop",
            new { name = "UQ_Widgets_Code" });
        var addDefault = await client.PostAsJsonAsync($"{table}/default-constraints",
            new { name = "DF_Widgets_Code", column = "Code", expression = "'N/A'" });
        var dropDefault = await client.PostAsJsonAsync($"{table}/default-constraints/drop",
            new { name = "DF_Widgets_Code" });
        var createIndex = await client.PostAsJsonAsync($"{table}/indexes", new
        {
            name = "IX_Widgets_Age",
            keyColumns = new[] { new { column = "Age", isDescending = true } },
            isUnique = true,
            filterExpression = "Age IS NOT NULL",
        });
        var dropIndex = await client.DeleteAsync($"{table}/indexes/IX_Widgets_Age");

        Assert.All([addCheck, dropCheck, addUnique, dropUnique, addDefault, dropDefault, createIndex, dropIndex],
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Contains("addCheckConstraint dbo.Widgets.CK_Widgets_Age expression=Age >= 0", fake.Calls);
        Assert.Contains("dropCheckConstraint dbo.Widgets.#2", fake.Calls);
        Assert.Contains("addUniqueConstraint dbo.Widgets.UQ_Widgets_Code (Code)", fake.Calls);
        Assert.Contains("dropUniqueConstraint dbo.Widgets.UQ_Widgets_Code", fake.Calls);
        Assert.Contains("addDefaultConstraint dbo.Widgets.Code.DF_Widgets_Code expression='N/A'", fake.Calls);
        Assert.Contains("dropDefaultConstraint dbo.Widgets.DF_Widgets_Code", fake.Calls);
        Assert.Contains("createIndex dbo.Widgets.IX_Widgets_Age (Age:DESC) unique=True filter=Age IS NOT NULL", fake.Calls);
        Assert.Contains("dropIndex dbo.Widgets.IX_Widgets_Age", fake.Calls);
    }

    [Theory]
    [InlineData("check-constraints")]
    [InlineData("unique-constraints")]
    [InlineData("default-constraints")]
    public async Task Empty_constraint_reference_is_rejected(string route)
    {
        var (app, client) = await GridletTestHost.StartDefaultAsync();
        await using var _ = app;
        var fake = (FakeGridletProvider)app.Services.GetRequiredService<IGridletProvider>();

        var response = await client.PostAsJsonAsync(
            $"{Db}/objects/dbo/Widgets/{route}/drop", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name or ordinal", await response.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("dropCheckConstraint") ||
            call.StartsWith("dropUniqueConstraint") || call.StartsWith("dropDefaultConstraint"));
    }
}
