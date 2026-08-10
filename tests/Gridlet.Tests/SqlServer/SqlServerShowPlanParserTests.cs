using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public sealed class SqlServerShowPlanParserTests
{
    private const string Namespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    /// <summary>A seek joined to a scan, shaped the way SQL Server emits it.</summary>
    private static string Plan(string statementBody) =>
        $"""
        <ShowPlanXML xmlns="{Namespace}" Version="1.539" Build="16.0.4165.4">
          <BatchSequence><Batch><Statements>{statementBody}</Statements></Batch></BatchSequence>
        </ShowPlanXML>
        """;

    private const string NestedLoopsStatement =
        """
        <StmtSimple StatementText="SELECT * FROM Orders o&#xD;&#xA;JOIN Customers c ON c.Id = o.CustomerId"
                    StatementType="SELECT" StatementEstRows="120" StatementSubTreeCost="0.0345">
          <QueryPlan>
            <RelOp PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="120"
                   EstimatedTotalSubtreeCost="0.0345">
              <NestedLoops>
                <RelOp PhysicalOp="Index Seek" LogicalOp="Index Seek" EstimateRows="12"
                       EstimatedTotalSubtreeCost="0.0032">
                  <IndexScan>
                    <Object Database="[Shop]" Schema="[dbo]" Table="[Orders]" Index="[IX_Orders_CustomerId]" />
                  </IndexScan>
                </RelOp>
                <RelOp PhysicalOp="Clustered Index Scan" LogicalOp="Clustered Index Scan"
                       EstimateRows="900" EstimatedTotalSubtreeCost="0.0313">
                  <IndexScan>
                    <Object Database="[Shop]" Schema="[dbo]" Table="[Customers]" Index="[PK_Customers]" />
                  </IndexScan>
                </RelOp>
              </NestedLoops>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
        """;

    [Fact]
    public void A_plan_becomes_a_statement_with_its_operator_tree()
    {
        var roots = SqlServerShowPlanParser.Parse(Plan(NestedLoopsStatement));

        var statement = Assert.Single(roots);
        Assert.Equal("SELECT", statement.Operation);
        Assert.Equal("SELECT * FROM Orders o JOIN Customers c ON c.Id = o.CustomerId", statement.Detail);
        Assert.Equal(120, statement.EstimatedRows);
        Assert.Equal(0.0345, statement.EstimatedCost);

        var join = Assert.Single(statement.Children!);
        Assert.Equal("Nested Loops", join.Operation);
        Assert.Equal("Inner Join", join.Detail);
        Assert.Collection(join.Children!,
            seek =>
            {
                Assert.Equal("Index Seek", seek.Operation);
                Assert.Equal("Orders.IX_Orders_CustomerId", seek.Detail);
                Assert.Equal(12, seek.EstimatedRows);
            },
            scan =>
            {
                Assert.Equal("Clustered Index Scan", scan.Operation);
                Assert.Equal("Customers.PK_Customers", scan.Detail);
                Assert.Equal(0.0313, scan.EstimatedCost);
            });
    }

    [Fact]
    public void An_actual_plan_reports_the_rows_that_really_came_out()
    {
        var xml = Plan(
            """
            <StmtSimple StatementText="SELECT 1" StatementType="SELECT" StatementEstRows="1">
              <QueryPlan>
                <RelOp PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="10">
                  <TableScan><Object Table="[Orders]" /></TableScan>
                  <RunTimeInformation>
                    <RunTimeCountersPerThread Thread="1" ActualRows="7" />
                    <RunTimeCountersPerThread Thread="2" ActualRows="4" />
                  </RunTimeInformation>
                </RelOp>
              </QueryPlan>
            </StmtSimple>
            """);

        var scan = Assert.Single(Assert.Single(SqlServerShowPlanParser.Parse(xml)).Children!);

        Assert.Equal(10, scan.EstimatedRows);
        Assert.Equal(11, scan.ActualRows);
        Assert.Equal("Orders", scan.Detail);
    }

    [Fact]
    public void A_missing_index_is_surfaced_as_a_statement_warning()
    {
        var xml = Plan(
            """
            <StmtSimple StatementText="SELECT 1" StatementType="SELECT">
              <QueryPlan>
                <MissingIndexes>
                  <MissingIndexGroup Impact="98.4">
                    <MissingIndex Database="[Shop]" Schema="[dbo]" Table="[Orders]">
                      <ColumnGroup Usage="EQUALITY">
                        <Column Name="[CustomerId]" ColumnId="2" />
                        <Column Name="[Status]" ColumnId="5" />
                      </ColumnGroup>
                    </MissingIndex>
                  </MissingIndexGroup>
                </MissingIndexes>
                <RelOp PhysicalOp="Table Scan" LogicalOp="Table Scan" EstimateRows="900" />
              </QueryPlan>
            </StmtSimple>
            """);

        var statement = Assert.Single(SqlServerShowPlanParser.Parse(xml));

        Assert.Equal(
            "Missing index on Orders (CustomerId, Status)",
            Assert.Single(statement.Warnings!));
    }

    [Fact]
    public void An_operator_warning_is_kept_with_its_operator()
    {
        var xml = Plan(
            """
            <StmtSimple StatementText="SELECT 1" StatementType="SELECT">
              <QueryPlan>
                <RelOp PhysicalOp="Sort" LogicalOp="Sort" EstimateRows="900">
                  <Warnings><SpillToTempDb SpillLevel="1" /></Warnings>
                </RelOp>
              </QueryPlan>
            </StmtSimple>
            """);

        var sort = Assert.Single(Assert.Single(SqlServerShowPlanParser.Parse(xml)).Children!);

        Assert.Equal("Spilled to tempdb", Assert.Single(sort.Warnings!));
    }

    [Fact]
    public void A_batch_of_statements_produces_one_root_each_even_across_documents()
    {
        var single = SqlServerShowPlanParser.Parse(Plan(NestedLoopsStatement + NestedLoopsStatement));
        var concatenated = SqlServerShowPlanParser.Parse(
            Plan(NestedLoopsStatement) + Plan(NestedLoopsStatement));

        Assert.Equal(2, single.Count);
        Assert.Equal(2, concatenated.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    public void Anything_unparseable_yields_no_tree_rather_than_an_error(string? xml)
        => Assert.Empty(SqlServerShowPlanParser.Parse(xml));
}
