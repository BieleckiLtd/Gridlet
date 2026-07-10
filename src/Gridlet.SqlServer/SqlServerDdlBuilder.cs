using System.Text.RegularExpressions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

/// <summary>
/// Builds CREATE/ALTER/DROP statements for the table designer. Identifiers are
/// bracket-quoted and data types are validated against a whitelist so a type string
/// can never smuggle arbitrary SQL.
/// </summary>
public static partial class SqlServerDdlBuilder
{
    private static readonly HashSet<string> AllowedTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint", "bit",
        "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "time", "datetime", "datetime2", "smalldatetime", "datetimeoffset",
        "char", "varchar", "nchar", "nvarchar",
        "binary", "varbinary", "uniqueidentifier", "xml", "rowversion",
    };

    [GeneratedRegex(@"^(?<name>[a-zA-Z][a-zA-Z0-9]*)(?:\s*\(\s*(?<args>max|\d{1,4}(?:\s*,\s*\d{1,3})?)\s*\))?$")]
    private static partial Regex DataTypePattern();

    /// <summary>Validates and canonicalises a designer-supplied data type like <c>nvarchar(100)</c> or <c>decimal(10,2)</c>.</summary>
    public static string NormalizeDataType(string dataType)
    {
        var match = DataTypePattern().Match(dataType?.Trim() ?? "");
        if (!match.Success || !AllowedTypeNames.Contains(match.Groups["name"].Value))
        {
            throw new GridletValidationException(
                $"'{dataType}' is not a supported data type. Use a SQL Server type such as int, nvarchar(100), decimal(10,2), or datetime2.");
        }

        var name = match.Groups["name"].Value.ToLowerInvariant();
        return match.Groups["args"].Success
            ? $"{name}({Regex.Replace(match.Groups["args"].Value.ToLowerInvariant(), @"\s+", "")})"
            : name;
    }

    public static string BuildCreateTable(TableDesign design)
    {
        if (design.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A table needs at least one column.");
        }

        var lines = design.Columns.Select(c => BuildColumnDefinition(c, includeDefault: true)).ToList();

        var primaryKey = design.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
        if (primaryKey.Count > 0)
        {
            var pkName = SqlServerIdentifier.Quote($"PK_{design.Name}");
            var pkColumns = string.Join(", ", primaryKey.Select(SqlServerIdentifier.Quote));
            lines.Add($"CONSTRAINT {pkName} PRIMARY KEY ({pkColumns})");
        }

        var target = SqlServerIdentifier.QuoteQualified(design.Schema, design.Name);
        return $"CREATE TABLE {target} (\n    {string.Join(",\n    ", lines)}\n);";
    }

    public static string BuildAddColumn(string schema, string table, ColumnDesign column)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ADD {BuildColumnDefinition(column, includeDefault: true)};";

    /// <summary>Retypes a column. Identity and defaults are deliberately out of scope for ALTER.</summary>
    public static string BuildAlterColumn(string schema, string table, ColumnDesign column)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ALTER COLUMN " +
           $"{SqlServerIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)} {(column.IsNullable ? "NULL" : "NOT NULL")};";

    public static string BuildDropColumn(string schema, string table, string columnName)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} DROP COLUMN {SqlServerIdentifier.Quote(columnName)};";

    public static string BuildDropTable(string schema, string table)
        => $"DROP TABLE {SqlServerIdentifier.QuoteQualified(schema, table)};";

    private static string BuildColumnDefinition(ColumnDesign column, bool includeDefault)
    {
        var definition =
            $"{SqlServerIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)}" +
            $"{(column.IsIdentity ? " IDENTITY(1,1)" : "")}" +
            $"{(column.IsNullable && !column.IsPrimaryKey ? " NULL" : " NOT NULL")}";

        if (includeDefault && !string.IsNullOrWhiteSpace(column.DefaultExpression))
        {
            definition += $" DEFAULT ({column.DefaultExpression})";
        }

        return definition;
    }
}
