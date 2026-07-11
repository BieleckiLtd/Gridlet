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

    /// <summary>Builds a readable, executable CREATE snapshot for a table's Definition tab.</summary>
    public static string BuildTableDefinition(TableDefinition definition)
    {
        var primaryKey = definition.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        var columns = definition.Columns.Select(c => new ColumnDesign(
            c.Name,
            c.DataType,
            c.IsNullable,
            c.IsIdentity,
            c.IsPrimaryKey,
            c.DefaultDefinition,
            c.ComputedDefinition,
            c.IsPersisted,
            c.IdentitySeed ?? 1,
            c.IdentityIncrement ?? 1)).ToArray();
        var lines = columns.Select(c => BuildColumnDefinition(c, includeDefault: true)).ToList();

        if (primaryKey is not null)
        {
            lines.Add($"CONSTRAINT {SqlServerIdentifier.Quote(primaryKey.Name)} PRIMARY KEY " +
                      $"{(primaryKey.Kind.Contains("CLUSTERED", StringComparison.OrdinalIgnoreCase) && !primaryKey.Kind.Contains("NONCLUSTERED", StringComparison.OrdinalIgnoreCase) ? "CLUSTERED " : "NONCLUSTERED ")}" +
                      $"({string.Join(", ", primaryKey.Columns.Select(SqlServerIdentifier.Quote))})");
        }

        lines.AddRange(definition.ForeignKeys.Select(fk =>
            $"CONSTRAINT {SqlServerIdentifier.Quote(fk.Name)} FOREIGN KEY " +
            $"({string.Join(", ", fk.Columns.Select(p => SqlServerIdentifier.Quote(p.Column)))}) REFERENCES " +
            $"{SqlServerIdentifier.QuoteQualified(fk.ReferencedSchema, fk.ReferencedTable)} " +
            $"({string.Join(", ", fk.Columns.Select(p => SqlServerIdentifier.Quote(p.ReferencedColumn)))}) " +
            $"ON DELETE {fk.OnDelete.Replace('_', ' ')} ON UPDATE {fk.OnUpdate.Replace('_', ' ')}"));

        return $"CREATE TABLE {SqlServerIdentifier.QuoteQualified(definition.Object.Schema, definition.Object.Name)} (\n" +
               $"    {string.Join(",\n    ", lines)}\n);";
    }

    /// <summary>Creates a schema only when it is not already present.</summary>
    public static string BuildCreateSchemaIfMissing(string schema)
    {
        var quoted = SqlServerIdentifier.Quote(schema).Replace("'", "''", StringComparison.Ordinal);
        // CREATE SCHEMA does not accept a parameter for its identifier. The identifier is
        // validated and bracket-quoted before it is placed in the dynamic statement.
        return $"IF SCHEMA_ID(@schema) IS NULL EXEC(N'CREATE SCHEMA {quoted}');";
    }

    public static string BuildCreateSchema(SchemaDesign design)
    {
        var sql = $"CREATE SCHEMA {SqlServerIdentifier.Quote(design.Name)}";
        if (!string.IsNullOrWhiteSpace(design.Owner))
        {
            sql += $" AUTHORIZATION {SqlServerIdentifier.Quote(design.Owner)}";
        }
        return sql + ";";
    }

    public static string BuildAlterSchemaOwner(string schema, string owner)
        => $"ALTER AUTHORIZATION ON SCHEMA::{SqlServerIdentifier.Quote(schema)} TO {SqlServerIdentifier.Quote(owner)};";

    public static string BuildDropSchema(string schema)
        => $"DROP SCHEMA {SqlServerIdentifier.Quote(schema)};";

    public static string BuildAddColumn(string schema, string table, ColumnDesign column)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ADD {BuildColumnDefinition(column, includeDefault: true)};";

    /// <summary>Retypes a column. Identity and defaults are deliberately out of scope for ALTER.</summary>
    public static string BuildAlterColumn(string schema, string table, ColumnDesign column)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ALTER COLUMN " +
           $"{SqlServerIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)} {(column.IsNullable ? "NULL" : "NOT NULL")};";

    public static string BuildDropColumn(string schema, string table, string columnName)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} DROP COLUMN {SqlServerIdentifier.Quote(columnName)};";

    public static string BuildAddDefault(string schema, string table, string columnName, string expression)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ADD CONSTRAINT " +
           $"{SqlServerIdentifier.Quote($"DF_{table}_{columnName}")} DEFAULT ({RequireExpression(expression, "default")}) FOR {SqlServerIdentifier.Quote(columnName)};";

    public static string BuildAddPrimaryKey(string schema, string table, PrimaryKeyDesign primaryKey)
    {
        if (primaryKey.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A primary key needs at least one column.");
        }

        var columns = string.Join(", ", primaryKey.Columns.Select(SqlServerIdentifier.Quote));
        return $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ADD CONSTRAINT " +
               $"{SqlServerIdentifier.Quote(primaryKey.Name)} PRIMARY KEY {(primaryKey.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} ({columns});";
    }

    public static string BuildAddForeignKey(string schema, string table, ForeignKeyDesign foreignKey)
    {
        if (foreignKey.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A foreign key needs at least one column pair.");
        }

        var local = string.Join(", ", foreignKey.Columns.Select(c => SqlServerIdentifier.Quote(c.Column)));
        var referenced = string.Join(", ", foreignKey.Columns.Select(c => SqlServerIdentifier.Quote(c.ReferencedColumn)));
        return $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} ADD CONSTRAINT " +
               $"{SqlServerIdentifier.Quote(foreignKey.Name)} FOREIGN KEY ({local}) REFERENCES " +
               $"{SqlServerIdentifier.QuoteQualified(foreignKey.ReferencedSchema, foreignKey.ReferencedTable)} ({referenced})" +
               $" ON DELETE {NormalizeReferentialAction(foreignKey.OnDelete)} ON UPDATE {NormalizeReferentialAction(foreignKey.OnUpdate)};";
    }

    public static string BuildDropConstraint(string schema, string table, string constraintName)
        => $"ALTER TABLE {SqlServerIdentifier.QuoteQualified(schema, table)} DROP CONSTRAINT {SqlServerIdentifier.Quote(constraintName)};";

    public static string BuildDropTable(string schema, string table)
        => $"DROP TABLE {SqlServerIdentifier.QuoteQualified(schema, table)};";

    public static string BuildDropObject(string schema, string name, DbObjectType type)
        => $"DROP {type switch
        {
            DbObjectType.Table => "TABLE",
            DbObjectType.View => "VIEW",
            DbObjectType.StoredProcedure => "PROCEDURE",
            DbObjectType.ScalarFunction or DbObjectType.TableValuedFunction => "FUNCTION",
            _ => throw new GridletValidationException($"Unsupported database object type '{type}'."),
        }} {SqlServerIdentifier.QuoteQualified(schema, name)};";

    private static string BuildColumnDefinition(ColumnDesign column, bool includeDefault)
    {
        if (!string.IsNullOrWhiteSpace(column.ComputedExpression))
        {
            if (column.IsIdentity || !string.IsNullOrWhiteSpace(column.DefaultExpression) || column.IsPrimaryKey)
            {
                throw new GridletValidationException("A computed column cannot also be an identity, default, or primary-key column.");
            }

            return $"{SqlServerIdentifier.Quote(column.Name)} AS ({RequireExpression(column.ComputedExpression, "computed")})" +
                   (column.IsPersisted ? " PERSISTED" : "");
        }

        var definition =
            $"{SqlServerIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)}" +
            $"{(column.IsIdentity ? $" IDENTITY({column.IdentitySeed},{column.IdentityIncrement})" : "")}" +
            $"{(column.IsNullable && !column.IsPrimaryKey ? " NULL" : " NOT NULL")}";

        if (includeDefault && !string.IsNullOrWhiteSpace(column.DefaultExpression))
        {
            definition += $" DEFAULT ({column.DefaultExpression})";
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
        return normalized is "NO ACTION" or "CASCADE" or "SET NULL" or "SET DEFAULT"
            ? normalized
            : throw new GridletValidationException($"'{action}' is not a supported referential action.");
    }
}
