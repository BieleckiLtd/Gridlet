namespace Gridlet.SqlServer;

internal sealed record SqlServerUserDefinedTypeColumn(
    string Name, string DataType, bool IsNullable, int Ordinal);

internal sealed record SqlServerUserDefinedType(
    string Schema, string Name, string Kind, string? BaseType = null,
    bool IsNullable = false, string? AssemblyName = null, string? AssemblyClass = null,
    IReadOnlyList<SqlServerUserDefinedTypeColumn>? Columns = null);

internal static class SqlServerUserDefinedTypeFormatter
{
    public static string Format(SqlServerUserDefinedType type)
    {
        var target = SqlServerIdentifier.QuoteQualified(type.Schema, type.Name);
        return type.Kind switch
        {
            "alias" => $"CREATE TYPE {target} FROM {type.BaseType}{(type.IsNullable ? " NULL" : " NOT NULL")};",
            "clr" => $"CREATE TYPE {target} EXTERNAL NAME " +
                $"{SqlServerIdentifier.Quote(type.AssemblyName ?? "unknown")}." +
                $"{SqlServerIdentifier.Quote(type.AssemblyClass ?? "unknown")};",
            "table" => FormatTable(type, target),
            _ => throw new GridletValidationException($"Unsupported user-defined type kind '{type.Kind}'."),
        };
    }

    private static string FormatTable(SqlServerUserDefinedType type, string target)
    {
        var lines = (type.Columns ?? []).Select(column =>
            $"    {SqlServerIdentifier.Quote(column.Name)} {column.DataType}" +
            (column.IsNullable ? " NULL" : " NOT NULL"));
        return "-- Table type metadata. Constraints and indexes are not included because SQL Server " +
            "does not retain the original CREATE TYPE text.\n" +
            $"CREATE TYPE {target} AS TABLE\n(\n{string.Join(",\n", lines)}\n);";
    }
}
