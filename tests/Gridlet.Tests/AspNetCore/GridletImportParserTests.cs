using Gridlet.AspNetCore;
using Xunit;

namespace Gridlet.Tests.AspNetCore;

public sealed class GridletImportParserTests
{
    [Fact]
    public void Csv_preserves_quoted_content_and_distinguishes_null_from_empty_string()
    {
        var import = GridletImportParser.Parse(
            "Name,Note,Extra\r\n\"Lovelace, Ada\",\"line 1\r\nline \"\"2\"\"\",\r\nGrace,\"\",\r\n,,\r\n",
            "csv");

        Assert.Equal(["Name", "Note", "Extra"], import.Columns);
        Assert.Equal(3, import.Rows.Count);
        Assert.Equal(["Lovelace, Ada", "line 1\r\nline \"2\"", null], import.Rows[0]);
        Assert.Equal(["Grace", "", null], import.Rows[1]);
        Assert.Equal([null, null, null], import.Rows[2]);
    }

    [Fact]
    public void Mapping_is_case_insensitive_and_can_omit_columns()
    {
        var import = GridletImportParser.Parse("First,Last\nAda,Lovelace\n", "csv",
            new Dictionary<string, string> { ["first"] = "Name", ["LAST"] = "" });

        Assert.Equal(["Name"], import.Columns);
        Assert.Equal(["Ada"], Assert.Single(import.Rows));
    }

    [Theory]
    [InlineData("A,A\n1,2\n", "CSV headers must be unique")]
    [InlineData("A,B\n1\n", "CSV record 2")]
    [InlineData("A\n\"unterminated", "unterminated quoted field")]
    public void Csv_rejects_invalid_input(string content, string message)
    {
        var error = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(content, "csv"));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Csv_enforces_the_row_limit()
    {
        var content = "Value\n" + string.Concat(Enumerable.Repeat("1\n", GridletImportParser.MaxRows + 1));

        var error = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(content, "csv"));

        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void Json_enforces_the_row_limit_before_materializing_rows()
    {
        var content = "[" + string.Join(',', Enumerable.Repeat("{}", GridletImportParser.MaxRows + 1)) + "]";

        var error = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(content, "json"));

        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void Json_bounds_distinct_columns_and_dense_cell_allocation()
    {
        var tooManyColumns = "[{" + string.Join(',', Enumerable.Range(0, GridletImportParser.MaxColumns + 1)
            .Select(index => $"\"C{index}\":1")) + "}]";
        var columnsError = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(tooManyColumns, "json"));
        Assert.Contains("columns", columnsError.Message);

        const int columns = 100;
        var row = "{" + string.Join(',', Enumerable.Range(0, columns).Select(index => $"\"C{index}\":1")) + "}";
        var rows = (int)(GridletImportParser.MaxCells / columns) + 1;
        var tooManyCells = "[" + row + "," + string.Join(',', Enumerable.Repeat("{}", rows - 1)) + "]";
        var cellsError = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(tooManyCells, "json"));
        Assert.Contains("values", cellsError.Message);
    }

    [Fact]
    public void Json_rejects_case_insensitive_duplicate_properties_as_validation_error()
    {
        var error = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse("[{\"id\":1,\"ID\":2}]", "json"));

        Assert.Contains("duplicate column", error.Message);
    }

    [Fact]
    public void Csv_stops_a_single_record_at_the_column_limit()
    {
        var content = string.Join(',', Enumerable.Repeat("", GridletImportParser.MaxColumns + 1));

        var error = Assert.Throws<GridletValidationException>(() =>
            GridletImportParser.Parse(content, "csv"));

        Assert.Contains("columns", error.Message);
    }

    [Fact]
    public void Mapping_rejects_unknown_source_columns()
    {
        var error = Assert.Throws<GridletValidationException>(() => GridletImportParser.Parse(
            "Name\nAda\n", "csv", new Dictionary<string, string> { ["Missing"] = "Target" }));

        Assert.Contains("does not exist", error.Message);
    }
}
