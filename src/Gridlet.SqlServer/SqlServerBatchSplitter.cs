using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Gridlet.Tests")]

namespace Gridlet.SqlServer;

internal sealed record SqlServerBatch(string Sql, int RepeatCount);

internal static class SqlServerBatchSplitter
{
    private const int MaxBatchExecutionCount = 1_000;
    private const string InvalidRepeatCountMessage =
        "GO repeat count must be a positive 32-bit integer, optionally followed by a line comment.";
    private const string ExecutionBudgetExceededMessage =
        "A script may execute at most 1,000 SQL batches, including GO repeat counts.";

    public static IReadOnlyList<SqlServerBatch> Split(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var batches = new List<SqlServerBatch>();
        var state = LexicalState.Normal;
        var blockCommentDepth = 0;
        var batchStart = 0;
        var lineStart = 0;

        while (lineStart < sql.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < sql.Length && sql[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            var nextLineStart = lineEnd;
            if (nextLineStart < sql.Length && sql[nextLineStart] == '\r') nextLineStart++;
            if (nextLineStart < sql.Length && sql[nextLineStart] == '\n') nextLineStart++;

            var line = sql.AsSpan(lineStart, lineEnd - lineStart);
            var repeatCount = state == LexicalState.Normal && blockCommentDepth == 0
                ? ParseSeparator(line)
                : null;
            if (repeatCount is not null)
            {
                AddBatch(sql, batchStart, lineStart, repeatCount.Value, batches);
                batchStart = nextLineStart;
            }
            else
            {
                ScanLine(line, ref state, ref blockCommentDepth);
            }

            lineStart = nextLineStart;
        }

        AddBatch(sql, batchStart, sql.Length, 1, batches);
        ValidateExecutionBudget(batches);
        return batches;
    }

    private static void ValidateExecutionBudget(IEnumerable<SqlServerBatch> batches)
    {
        var total = 0;
        foreach (var batch in batches)
        {
            if (batch.RepeatCount > MaxBatchExecutionCount - total)
            {
                throw new GridletValidationException(ExecutionBudgetExceededMessage);
            }

            total += batch.RepeatCount;
        }
    }

    private static int? ParseSeparator(ReadOnlySpan<char> line)
    {
        var content = line.Trim();
        if (content.Length < 2 || !content[..2].Equals("GO", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (content.Length == 2)
        {
            return 1;
        }

        if (!char.IsWhiteSpace(content[2]))
        {
            return null;
        }

        var suffix = content[2..].TrimStart();
        if (suffix.IsEmpty || suffix.StartsWith("--", StringComparison.Ordinal))
        {
            return 1;
        }

        var digitCount = 0;
        while (digitCount < suffix.Length && suffix[digitCount] is >= '0' and <= '9')
        {
            digitCount++;
        }

        if (digitCount == 0
            || !int.TryParse(
                suffix[..digitCount],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var repeatCount)
            || repeatCount <= 0)
        {
            throw new GridletValidationException(InvalidRepeatCountMessage);
        }

        var remainder = suffix[digitCount..];
        if (remainder.IsEmpty)
        {
            return repeatCount;
        }

        if (!char.IsWhiteSpace(remainder[0]))
        {
            throw new GridletValidationException(InvalidRepeatCountMessage);
        }

        remainder = remainder.TrimStart();
        if (!remainder.IsEmpty && !remainder.StartsWith("--", StringComparison.Ordinal))
        {
            throw new GridletValidationException(InvalidRepeatCountMessage);
        }

        return repeatCount;
    }

    private static void AddBatch(
        string sql,
        int start,
        int end,
        int repeatCount,
        List<SqlServerBatch> batches)
    {
        var batch = sql[start..end];
        if (!string.IsNullOrWhiteSpace(batch)) batches.Add(new SqlServerBatch(batch, repeatCount));
    }

    private static void ScanLine(
        ReadOnlySpan<char> line,
        ref LexicalState state,
        ref int blockCommentDepth)
    {
        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            var next = index + 1 < line.Length ? line[index + 1] : '\0';

            if (blockCommentDepth > 0)
            {
                if (current == '/' && next == '*')
                {
                    blockCommentDepth++;
                    index++;
                }
                else if (current == '*' && next == '/')
                {
                    blockCommentDepth--;
                    index++;
                }

                continue;
            }

            switch (state)
            {
                case LexicalState.SingleQuoted:
                    if (current != '\'') continue;
                    if (next == '\'') index++;
                    else state = LexicalState.Normal;
                    break;

                case LexicalState.DoubleQuoted:
                    if (current != '"') continue;
                    if (next == '"') index++;
                    else state = LexicalState.Normal;
                    break;

                case LexicalState.Bracketed:
                    if (current != ']') continue;
                    if (next == ']') index++;
                    else state = LexicalState.Normal;
                    break;

                default:
                    if (current == '-' && next == '-') return;
                    if (current == '/' && next == '*')
                    {
                        blockCommentDepth = 1;
                        index++;
                    }
                    else if (current == '\'') state = LexicalState.SingleQuoted;
                    else if (current == '"') state = LexicalState.DoubleQuoted;
                    else if (current == '[') state = LexicalState.Bracketed;
                    break;
            }
        }
    }

    private enum LexicalState
    {
        Normal,
        SingleQuoted,
        DoubleQuoted,
        Bracketed,
    }
}
