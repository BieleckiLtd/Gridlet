namespace Gridlet.SqlServer;

/// <summary>
/// Builds row-level INSERT/UPDATE/DELETE statements. Column names are bracket-quoted;
/// values always travel as parameters (<c>@v0..</c> for SET/VALUES, <c>@k0..</c> for keys).
/// </summary>
public static class SqlServerDmlBuilder
{
    public static string BuildInsert(string schema, string table, IReadOnlyList<string> columns)
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        var columnList = string.Join(", ", columns.Select(SqlServerIdentifier.Quote));
        var valueList = string.Join(", ", columns.Select((_, i) => "@v" + i));
        return $"INSERT INTO {target} ({columnList}) VALUES ({valueList});";
    }

    public static string BuildUpdate(
        string schema, string table, IReadOnlyList<string> setColumns, IReadOnlyList<string> keyColumns)
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        var set = string.Join(", ", setColumns.Select((c, i) => $"{SqlServerIdentifier.Quote(c)} = @v{i}"));
        return $"UPDATE {target} SET {set} WHERE {BuildKeyPredicate(keyColumns)};";
    }

    public static string BuildDelete(string schema, string table, IReadOnlyList<string> keyColumns)
    {
        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        return $"DELETE FROM {target} WHERE {BuildKeyPredicate(keyColumns)};";
    }

    private static string BuildKeyPredicate(IReadOnlyList<string> keyColumns)
        => string.Join(" AND ", keyColumns.Select((c, i) => $"{SqlServerIdentifier.Quote(c)} = @k{i}"));
}
