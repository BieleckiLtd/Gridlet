using Gridlet.SqlServer;
using Xunit;

namespace Gridlet.Tests.SqlServer;

public class SqlServerBatchSplitterTests
{
    [Fact]
    public void Splits_standalone_go_lines_case_insensitively()
    {
        const string sql = "SELECT 1;\r\n  go\t\r\nSELECT 2;\nGO\nSELECT 3;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 1;\r\n", 1),
                new SqlServerBatch("SELECT 2;\n", 1),
                new SqlServerBatch("SELECT 3;", 1),
            ],
            batches);
    }

    [Theory]
    [InlineData("GO -- comment", 1)]
    [InlineData("go 2", 2)]
    [InlineData("GO\t3\t-- repeat this batch", 3)]
    public void Parses_comments_and_positive_repeat_counts(string separator, int expectedRepeatCount)
    {
        var batches = SqlServerBatchSplitter.Split($"SELECT 1;\n{separator}\nSELECT 2;");

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 1;\n", expectedRepeatCount),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Accepts_exactly_one_thousand_total_batch_executions_without_expanding_them()
    {
        var batches = SqlServerBatchSplitter.Split("SELECT 1;\nGO 999\nSELECT 2;");

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 1;\n", 999),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Rejects_one_thousand_and_one_repetitions()
    {
        var exception = Assert.Throws<GridletValidationException>(
            () => SqlServerBatchSplitter.Split("SELECT 1;\nGO 1001"));

        Assert.Contains("1,000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_cumulative_counts_across_multiple_separators()
    {
        const string sql = "SELECT 1;\nGO 600\nSELECT 2;\nGO 401";

        var exception = Assert.Throws<GridletValidationException>(() => SqlServerBatchSplitter.Split(sql));

        Assert.Contains("1,000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Counts_ordinary_batches_towards_the_cumulative_limit()
    {
        const string sql = "SELECT 1;\nGO 600\nSELECT 2;\nGO 400\nSELECT 3;";

        var exception = Assert.Throws<GridletValidationException>(() => SqlServerBatchSplitter.Split(sql));

        Assert.Contains("1,000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Skips_empty_batches_and_does_not_apply_their_count_to_the_next_batch()
    {
        const string sql = "GO 5\n \t\nGO -- empty\r\nSELECT 1;\rGO 2";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal([new SqlServerBatch("SELECT 1;\r", 2)], batches);
    }

    [Theory]
    [InlineData("SELECT 'GO';")]
    [InlineData("SELECT [GO] FROM [Table];")]
    [InlineData("SELECT \"GO\";")]
    [InlineData("GOTO label;")]
    [InlineData("GO;")]
    [InlineData("SELECT 1; GO 2")]
    public void Does_not_split_go_that_is_not_a_client_command(string sql)
    {
        Assert.Equal([new SqlServerBatch(sql, 1)], SqlServerBatchSplitter.Split(sql));
    }

    [Theory]
    [InlineData("GO 0")]
    [InlineData("GO 000")]
    [InlineData("GO -1")]
    [InlineData("GO +1")]
    [InlineData("GO count")]
    [InlineData("GO 1.5")]
    [InlineData("GO 2147483648")]
    [InlineData("GO 999999999999999999999999999999999999")]
    [InlineData("GO 2 trailing text")]
    [InlineData("GO 2 /* block comment */")]
    [InlineData("GO 2-- missing whitespace")]
    public void Rejects_invalid_repeat_counts(string separator)
    {
        var exception = Assert.Throws<GridletValidationException>(
            () => SqlServerBatchSplitter.Split($"SELECT 1;\n{separator}\nSELECT 2;"));

        Assert.Contains("positive 32-bit integer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_invalid_go_commands_inside_multiline_string_literals()
    {
        const string sql = "SELECT 'first line\nGO 0\nthird ''line''';\nGO 2\nSELECT 2;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 'first line\nGO 0\nthird ''line''';\n", 2),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Ignores_invalid_go_commands_inside_multiline_bracketed_identifiers()
    {
        const string sql = "SELECT [first line\nGO 0\nthird ]]line]]];\nGO -- next\nSELECT 2;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT [first line\nGO 0\nthird ]]line]]];\n", 1),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Ignores_invalid_go_commands_inside_multiline_double_quoted_tokens()
    {
        const string sql = "SELECT \"first line\nGO 0\nthird \"\"line\"\"\";\nGO\nSELECT 2;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT \"first line\nGO 0\nthird \"\"line\"\"\";\n", 1),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Ignores_invalid_go_commands_inside_nested_block_comments()
    {
        const string sql = "SELECT 1; /* outer\nGO 0\n/* inner */\nGO overflow\n*/ SELECT 2;\nGO 3 -- valid\nSELECT 3;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 1; /* outer\nGO 0\n/* inner */\nGO overflow\n*/ SELECT 2;\n", 3),
                new SqlServerBatch("SELECT 3;", 1),
            ],
            batches);
    }

    [Fact]
    public void Line_comments_do_not_leak_lexical_state_to_the_next_line()
    {
        const string sql = "SELECT 1; -- unmatched ' [ /* and GO 0\nGO -- separator\nSELECT 2;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT 1; -- unmatched ' [ /* and GO 0\n", 1),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Comment_markers_inside_literals_and_identifiers_do_not_change_state()
    {
        const string sql = "SELECT '/*', '--', [/*], [--];\nGO 2 -- repeat\nSELECT 2;";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("SELECT '/*', '--', [/*], [--];\n", 2),
                new SqlServerBatch("SELECT 2;", 1),
            ],
            batches);
    }

    [Fact]
    public void Preserves_batch_text_and_mixed_line_endings()
    {
        const string sql = "\r\nSELECT 1;\rGO 2 -- twice\n\nSELECT 2;\r\n";

        var batches = SqlServerBatchSplitter.Split(sql);

        Assert.Equal(
            [
                new SqlServerBatch("\r\nSELECT 1;\r", 2),
                new SqlServerBatch("\nSELECT 2;\r\n", 1),
            ],
            batches);
    }

    [Fact]
    public void Rejects_null_input()
    {
        Assert.Throws<ArgumentNullException>(() => SqlServerBatchSplitter.Split(null!));
    }
}
