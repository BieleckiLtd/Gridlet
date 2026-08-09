using Gridlet.Models;
using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerRoutineScriptBuilderTests
{
    private static readonly DbObjectInfo Procedure =
        new("dbo", "UpdateCustomer", DbObjectType.StoredProcedure);

    private static RoutineDefinition WithParameters(
        DbObjectInfo routine, params RoutineParameterInfo[] parameters)
        => new(routine, parameters);

    private static Dictionary<string, RoutineArgument> Arguments(
        params (string Name, RoutineArgument Argument)[] arguments)
        => arguments.ToDictionary(entry => entry.Name, entry => entry.Argument, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void A_procedure_call_captures_the_return_value_and_every_output_parameter()
    {
        var routine = WithParameters(
            Procedure,
            new RoutineParameterInfo("@ReturnValue", "int", 0, IsOutput: true, IsReturnValue: true),
            new RoutineParameterInfo("@Id", "int", 1),
            new RoutineParameterInfo("@Name", "nvarchar(50)", 2),
            new RoutineParameterInfo("@RowsChanged", "int", 3, IsOutput: true));

        var script = SqlServerRoutineScriptBuilder.Build(routine, Arguments(
            ("@Id", new RoutineArgument("7")),
            ("@Name", new RoutineArgument("O'Hara"))));

        Assert.Equal(
            """
            DECLARE @ReturnValue int;
            DECLARE @out_RowsChanged int;
            EXEC @ReturnValue = [dbo].[UpdateCustomer] @Id = 7, @Name = N'O''Hara', @RowsChanged = @out_RowsChanged OUTPUT;
            SELECT @ReturnValue AS [Return value], @out_RowsChanged AS [@RowsChanged];
            """,
            script);
    }

    [Fact]
    public void An_omitted_argument_is_left_out_so_the_routines_own_default_applies()
    {
        var routine = WithParameters(
            Procedure,
            new RoutineParameterInfo("@ReturnValue", "int", 0, IsOutput: true, IsReturnValue: true),
            new RoutineParameterInfo("@Id", "int", 1),
            new RoutineParameterInfo("@Name", "nvarchar(50)", 2, HasDefault: true));

        var script = SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Id", new RoutineArgument("7"))));

        Assert.Contains("EXEC @ReturnValue = [dbo].[UpdateCustomer] @Id = 7;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("@Name", script, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_null_is_passed_rather_than_omitted()
    {
        var routine = WithParameters(
            Procedure,
            new RoutineParameterInfo("@Name", "nvarchar(50)", 1));

        var script = SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Name", new RoutineArgument(null, IsNull: true))));

        Assert.Contains("@Name = NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void An_output_parameter_can_be_seeded_with_a_value()
    {
        var routine = WithParameters(
            Procedure,
            new RoutineParameterInfo("@Total", "int", 1, IsOutput: true));

        var script = SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Total", new RoutineArgument("3"))));

        Assert.Contains("DECLARE @out_Total int = 3;", script, StringComparison.Ordinal);
        Assert.Contains("@Total = @out_Total OUTPUT", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int", "42", "42")]
    [InlineData("decimal(10,2)", " 4.5 ", "4.5")]
    [InlineData("bit", "true", "1")]
    [InlineData("bit", "0", "0")]
    [InlineData("varchar(10)", "a'b", "'a''b'")]
    [InlineData("nvarchar(10)", "a'b", "N'a''b'")]
    [InlineData("datetime2(7)", "2026-01-31 10:00", "N'2026-01-31 10:00'")]
    [InlineData("uniqueidentifier", "0f8fad5b-d9cb-469f-a165-70867728950e", "'0f8fad5b-d9cb-469f-a165-70867728950e'")]
    [InlineData("varbinary(16)", "0xAB01", "0xAB01")]
    [InlineData("varbinary(16)", "ab01", "0xab01")]
    public void Values_are_quoted_for_the_parameters_declared_type(
        string dataType, string value, string expected)
    {
        var routine = WithParameters(Procedure, new RoutineParameterInfo("@Value", dataType, 1));

        var script = SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Value", new RoutineArgument(value))));

        Assert.Contains($"@Value = {expected}", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int", "7; DROP TABLE Customers")]
    [InlineData("bit", "maybe")]
    [InlineData("uniqueidentifier", "not-a-guid")]
    [InlineData("varbinary(16)", "zz")]
    public void A_value_that_is_not_of_the_declared_type_is_rejected(string dataType, string value)
    {
        var routine = WithParameters(Procedure, new RoutineParameterInfo("@Value", dataType, 1));

        Assert.Throws<GridletValidationException>(() => SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Value", new RoutineArgument(value)))));
    }

    [Fact]
    public void A_table_valued_parameter_is_written_as_the_expression_it_is_given()
    {
        var routine = WithParameters(
            Procedure,
            new RoutineParameterInfo("@Items", "[dbo].[ItemList]", 1, IsReadOnly: true, IsTableType: true));

        var script = SqlServerRoutineScriptBuilder.Build(routine, Arguments(
            ("@Items", new RoutineArgument("@MyItems", IsRawSql: true))));

        Assert.Contains("@Items = @MyItems", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Raw SQL is the escape hatch for a table-valued parameter, which has to be a variable. On any
    /// other parameter it would let the caller write the statement rather than the value, so it is
    /// refused even though the caller could write that statement themselves in the editor.
    /// </summary>
    [Theory]
    [InlineData("int", "1; DROP TABLE Customers")]
    [InlineData("nvarchar(50)", "@SomeVariable")]
    public void Raw_sql_is_refused_for_a_parameter_that_takes_a_value(string dataType, string value)
    {
        var routine = WithParameters(Procedure, new RoutineParameterInfo("@Value", dataType, 1));

        var exception = Assert.Throws<GridletValidationException>(() => SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Value", new RoutineArgument(value, IsRawSql: true)))));

        Assert.Contains("not a SQL expression", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scalar_function_is_selected_and_a_table_valued_function_is_queried()
    {
        var scalar = WithParameters(
            new DbObjectInfo("dbo", "TaxFor", DbObjectType.ScalarFunction),
            new RoutineParameterInfo("@ReturnValue", "money", 0, IsReturnValue: true),
            new RoutineParameterInfo("@Amount", "money", 1));
        var table = WithParameters(
            new DbObjectInfo("dbo", "OrdersFor", DbObjectType.TableValuedFunction),
            new RoutineParameterInfo("@CustomerId", "int", 1));

        Assert.Equal(
            "SELECT [dbo].[TaxFor](10.5) AS [Result];",
            SqlServerRoutineScriptBuilder.Build(scalar, Arguments(("@Amount", new RoutineArgument("10.5")))));
        Assert.Equal(
            "SELECT * FROM [dbo].[OrdersFor](7);",
            SqlServerRoutineScriptBuilder.Build(table, Arguments(("@CustomerId", new RoutineArgument("7")))));
    }

    [Fact]
    public void An_omitted_function_argument_becomes_DEFAULT_so_the_rest_stay_in_position()
    {
        var routine = WithParameters(
            new DbObjectInfo("dbo", "Rate", DbObjectType.ScalarFunction),
            new RoutineParameterInfo("@First", "int", 1, HasDefault: true),
            new RoutineParameterInfo("@Second", "int", 2));

        var script = SqlServerRoutineScriptBuilder.Build(
            routine, Arguments(("@Second", new RoutineArgument("2"))));

        Assert.Equal("SELECT [dbo].[Rate](DEFAULT, 2) AS [Result];", script);
    }

    [Fact]
    public void A_parameterless_procedure_is_just_the_call()
    {
        var routine = WithParameters(
            new DbObjectInfo("dbo", "RefreshOrders", DbObjectType.StoredProcedure),
            new RoutineParameterInfo("@ReturnValue", "int", 0, IsOutput: true, IsReturnValue: true));

        var script = SqlServerRoutineScriptBuilder.Build(routine, Arguments());

        Assert.Equal(
            """
            DECLARE @ReturnValue int;
            EXEC @ReturnValue = [dbo].[RefreshOrders];
            SELECT @ReturnValue AS [Return value];
            """,
            script);
    }

    [Fact]
    public void A_table_or_view_cannot_be_called()
    {
        var routine = WithParameters(new DbObjectInfo("dbo", "Customers", DbObjectType.Table));

        Assert.Throws<GridletValidationException>(
            () => SqlServerRoutineScriptBuilder.Build(routine, Arguments()));
    }
}
