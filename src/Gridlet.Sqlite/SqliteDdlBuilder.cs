using System.Text.RegularExpressions;
using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>Builds SQLite DDL while validating every identifier and designer-supplied type.</summary>
public static partial class SqliteDdlBuilder
{
    private static readonly HashSet<string> AllowedTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "int", "tinyint", "smallint", "mediumint", "bigint", "unsignedbigint", "int2", "int8",
        "text", "character", "varchar", "varyingcharacter", "nchar", "nativecharacter", "nvarchar", "clob",
        "blob", "real", "double", "doubleprecision", "float", "numeric", "decimal", "boolean", "date", "datetime",
    };

    [GeneratedRegex(@"^(?<name>[a-zA-Z][a-zA-Z ]*)(?:\s*\(\s*(?<args>\d{1,4}(?:\s*,\s*\d{1,4})?)\s*\))?$")]
    private static partial Regex DataTypePattern();

    public static string NormalizeDataType(string dataType)
    {
        var match = DataTypePattern().Match(dataType?.Trim() ?? "");
        var compactName = match.Success
            ? Regex.Replace(match.Groups["name"].Value, @"\s+", "").ToLowerInvariant()
            : "";
        if (!match.Success || !AllowedTypeNames.Contains(compactName))
        {
            throw new GridletValidationException(
                $"'{dataType}' is not a supported SQLite data type. Use a type such as INTEGER, TEXT, REAL, BLOB, NUMERIC, or VARCHAR(100).");
        }

        var displayName = match.Groups["name"].Value.Trim().ToUpperInvariant();
        return match.Groups["args"].Success
            ? $"{displayName}({Regex.Replace(match.Groups["args"].Value, @"\s+", "")})"
            : displayName;
    }

    public static string BuildCreateTable(
        TableDesign design,
        string? primaryKeyName = null,
        IReadOnlyList<ForeignKeyDesign>? foreignKeys = null)
    {
        SqliteIdentifier.RequireMainSchema(design.Schema);
        if (design.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A table needs at least one column.");
        }

        var identityColumns = design.Columns.Where(c => c.IsIdentity).ToArray();
        if (identityColumns.Length > 1)
        {
            throw new GridletValidationException("SQLite tables can contain only one identity column.");
        }

        if (identityColumns.Length == 1 &&
            (!identityColumns[0].IsPrimaryKey ||
             NormalizeDataType(identityColumns[0].DataType) != "INTEGER" ||
             identityColumns[0].IdentitySeed != 1 || identityColumns[0].IdentityIncrement != 1))
        {
            throw new GridletValidationException(
                "A SQLite identity must be an INTEGER primary key with seed and increment set to 1.");
        }

        var lines = design.Columns.Select(BuildColumnDefinition).ToList();
        var primaryKeyColumns = design.Columns.Where(c => c.IsPrimaryKey).ToArray();
        if (primaryKeyColumns.Length > 0 && identityColumns.Length == 0)
        {
            var name = string.IsNullOrWhiteSpace(primaryKeyName) ? $"PK_{design.Name}" : primaryKeyName;
            lines.Add($"CONSTRAINT {SqliteIdentifier.Quote(name!)} PRIMARY KEY " +
                      $"({string.Join(", ", primaryKeyColumns.Select(c => SqliteIdentifier.Quote(c.Name)))})");
        }

        if (foreignKeys is not null)
        {
            lines.AddRange(foreignKeys.Select(BuildForeignKeyDefinition));
        }

        return $"CREATE TABLE {SqliteIdentifier.QuoteQualified(design.Schema, design.Name)} (\n" +
               $"    {string.Join(",\n    ", lines)}\n);";
    }

    public static string BuildAddColumn(string schema, string table, ColumnDesign column)
    {
        if (column.IsPrimaryKey || column.IsIdentity)
        {
            throw new GridletValidationException(
                "SQLite cannot add a primary-key or identity column with ALTER TABLE; add a regular column, then rebuild the key.");
        }

        return $"ALTER TABLE {SqliteIdentifier.QuoteQualified(schema, table)} ADD COLUMN {BuildColumnDefinition(column)};";
    }

    public static string BuildDropColumn(string schema, string table, string column)
        => $"ALTER TABLE {SqliteIdentifier.QuoteQualified(schema, table)} DROP COLUMN {SqliteIdentifier.Quote(column)};";

    public static string BuildDropTable(string schema, string table)
        => $"DROP TABLE {SqliteIdentifier.QuoteQualified(schema, table)};";

    public static string BuildDropObject(string schema, string name, DbObjectType type)
        => type switch
        {
            DbObjectType.Table => BuildDropTable(schema, name),
            DbObjectType.View => $"DROP VIEW {SqliteIdentifier.QuoteQualified(schema, name)};",
            DbObjectType.Trigger => $"DROP TRIGGER {SqliteIdentifier.QuoteQualified(schema, name)};",
            _ => throw new GridletValidationException($"SQLite does not support database object type '{type}'."),
        };

    public static string BuildCreateIndex(
        string schema,
        string table,
        string name,
        bool unique,
        IReadOnlyList<string> columns)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        if (columns.Count == 0)
        {
            throw new GridletValidationException("An index needs at least one column.");
        }

        return $"CREATE {(unique ? "UNIQUE " : "")}INDEX {SqliteIdentifier.Quote(name)} ON " +
               $"{SqliteIdentifier.Quote(table)} ({string.Join(", ", columns.Select(SqliteIdentifier.Quote))});";
    }

    public static string BuildForeignKeyDefinition(ForeignKeyDesign foreignKey)
    {
        SqliteIdentifier.RequireMainSchema(foreignKey.ReferencedSchema);
        if (foreignKey.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A foreign key needs at least one column pair.");
        }

        var local = string.Join(", ", foreignKey.Columns.Select(c => SqliteIdentifier.Quote(c.Column)));
        var referenced = string.Join(", ", foreignKey.Columns.Select(c => SqliteIdentifier.Quote(c.ReferencedColumn)));
        return $"CONSTRAINT {SqliteIdentifier.Quote(foreignKey.Name)} FOREIGN KEY ({local}) REFERENCES " +
               $"{SqliteIdentifier.Quote(foreignKey.ReferencedTable)} ({referenced}) " +
               $"ON DELETE {NormalizeReferentialAction(foreignKey.OnDelete)} " +
               $"ON UPDATE {NormalizeReferentialAction(foreignKey.OnUpdate)}";
    }

    private static string BuildColumnDefinition(ColumnDesign column)
    {
        if (!string.IsNullOrWhiteSpace(column.ComputedExpression))
        {
            if (column.IsIdentity || !string.IsNullOrWhiteSpace(column.DefaultExpression) || column.IsPrimaryKey)
            {
                throw new GridletValidationException(
                    "A computed column cannot also be an identity, default, or primary-key column.");
            }

            return $"{SqliteIdentifier.Quote(column.Name)} AS ({RequireExpression(column.ComputedExpression, "computed")}) " +
                   (column.IsPersisted ? "STORED" : "VIRTUAL");
        }

        var definition = $"{SqliteIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)}";
        if (column.IsIdentity)
        {
            definition += " PRIMARY KEY AUTOINCREMENT";
        }
        else
        {
            definition += column.IsNullable && !column.IsPrimaryKey ? " NULL" : " NOT NULL";
        }

        if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
        {
            definition += $" DEFAULT ({RequireExpression(column.DefaultExpression, "default")})";
        }

        return definition;
    }

    private static string RequireExpression(string? expression, string kind)
        => string.IsNullOrWhiteSpace(expression)
            ? throw new GridletValidationException($"A {kind} expression is required.")
            : expression.Trim();

    private static string NormalizeReferentialAction(string? action)
    {
        var normalized = Regex.Replace(action?.Trim().ToUpperInvariant() ?? "", @"\s+", " ");
        return normalized is "NO ACTION" or "RESTRICT" or "CASCADE" or "SET NULL" or "SET DEFAULT"
            ? normalized
            : throw new GridletValidationException($"'{action}' is not a supported referential action.");
    }
}
