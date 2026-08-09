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
    /// <summary>
    /// The built-in types, written bare and lower-cased. Anything else is treated as a user-defined
    /// or alias type and emitted as a quoted identifier, which is both how SQL Server writes it and
    /// what keeps a type string from smuggling SQL.
    /// </summary>
    private static readonly HashSet<string> BuiltInTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bigint", "int", "smallint", "tinyint", "bit",
        "decimal", "numeric", "money", "smallmoney", "float", "real",
        "date", "time", "datetime", "datetime2", "smalldatetime", "datetimeoffset",
        "char", "varchar", "nchar", "nvarchar", "text", "ntext", "image",
        "binary", "varbinary", "uniqueidentifier", "xml", "rowversion", "timestamp",
        "sql_variant", "sysname", "hierarchyid", "geography", "geometry", "json", "vector",
    };

    [GeneratedRegex(
        @"^(?<name>[a-zA-Z_][a-zA-Z0-9_]*)(?:\s*\(\s*(?<args>max|\d{1,4}(?:\s*,\s*\d{1,3})?)\s*\))?$")]
    private static partial Regex BuiltInDataTypePattern();

    /// <summary>A one- or two-part type name, bracketed or not: <c>MyType</c>, <c>[dbo].[My type]</c>.</summary>
    [GeneratedRegex(
        @"^(?:(?:\[(?<schema>[^\]]+)\]|(?<schema>[a-zA-Z_][a-zA-Z0-9_@$#]*))\s*\.\s*)?" +
        @"(?:\[(?<name>[^\]]+)\]|(?<name>[a-zA-Z_][a-zA-Z0-9_@$#]*))$")]
    private static partial Regex UserDefinedTypePattern();

    /// <summary>
    /// Validates and canonicalises a designer-supplied data type such as <c>nvarchar(100)</c>,
    /// <c>decimal(10,2)</c>, <c>geography</c> or <c>[dbo].[AccountNumber]</c>.
    /// </summary>
    public static string NormalizeDataType(string dataType)
    {
        var trimmed = dataType?.Trim() ?? "";
        var builtIn = BuiltInDataTypePattern().Match(trimmed);
        if (builtIn.Success && BuiltInTypeNames.Contains(builtIn.Groups["name"].Value))
        {
            var name = builtIn.Groups["name"].Value.ToLowerInvariant();
            return builtIn.Groups["args"].Success
                ? $"{name}({Regex.Replace(builtIn.Groups["args"].Value.ToLowerInvariant(), @"\s+", "")})"
                : name;
        }

        // Alias, CLR and other user-defined types are per-database, so there is no list to check
        // them against. Quoting each part keeps the string harmless; an unknown type is then the
        // engine's error to report, with its own message, rather than a guess made here.
        var userDefined = UserDefinedTypePattern().Match(trimmed);
        if (userDefined.Success)
        {
            var schema = userDefined.Groups["schema"];
            var name = userDefined.Groups["name"].Value;
            return schema.Success
                ? SqlServerIdentifier.QuoteQualified(schema.Value, name)
                : SqlServerIdentifier.Quote(name);
        }

        throw new GridletValidationException(
            $"'{dataType}' is not a usable data type. Use a SQL Server type such as int, nvarchar(100), " +
            "decimal(10,2) or datetime2, or a user-defined type such as [dbo].[AccountNumber].");
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
            var primaryKeyColumns = primaryKey.KeyColumns is { Count: > 0 }
                ? BuildMetadataKeyList(primaryKey.KeyColumns)
                : string.Join(", ", primaryKey.Columns.Select(SqlServerIdentifier.Quote));
            var isClustered = primaryKey.IsClustered ||
                primaryKey.Kind.Contains("CLUSTERED", StringComparison.OrdinalIgnoreCase) &&
                !primaryKey.Kind.Contains("NONCLUSTERED", StringComparison.OrdinalIgnoreCase);
            lines.Add($"CONSTRAINT {SqlServerIdentifier.Quote(primaryKey.Name)} PRIMARY KEY " +
                      $"{(isClustered ? "CLUSTERED " : "NONCLUSTERED ")}" +
                      $"({primaryKeyColumns})" +
                      BuildFillFactorClause(primaryKey.FillFactor));
        }

        lines.AddRange(definition.CheckConstraints.Select(check =>
            $"CONSTRAINT {SqlServerIdentifier.Quote(check.Name!)} CHECK " +
            $"{(check.IsNotForReplication ? "NOT FOR REPLICATION " : "")}" +
            $"({check.Definition})"));

        lines.AddRange(definition.UniqueConstraints.Select(unique =>
            $"CONSTRAINT {SqlServerIdentifier.Quote(unique.Name!)} UNIQUE " +
            $"{(unique.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} " +
            $"({BuildMetadataKeyList(unique.Columns)})" +
            BuildFillFactorClause(unique.FillFactor)));

        lines.AddRange(definition.ForeignKeys.Select(fk =>
            $"CONSTRAINT {SqlServerIdentifier.Quote(fk.Name)} FOREIGN KEY " +
            $"({string.Join(", ", fk.Columns.Select(p => SqlServerIdentifier.Quote(p.Column)))}) REFERENCES " +
            $"{SqlServerIdentifier.QuoteQualified(fk.ReferencedSchema, fk.ReferencedTable)} " +
            $"({string.Join(", ", fk.Columns.Select(p => SqlServerIdentifier.Quote(p.ReferencedColumn)))}) " +
            $"ON DELETE {fk.OnDelete.Replace('_', ' ')} ON UPDATE {fk.OnUpdate.Replace('_', ' ')}"));

        var target = SqlServerIdentifier.QuoteQualified(definition.Object.Schema, definition.Object.Name);
        var sql = $"CREATE TABLE {target} (\n" +
                  $"    {string.Join(",\n    ", lines)}\n);";
        var scriptedIndexes = new List<(IndexInfo Info, IndexDesign Design)>();

        foreach (var index in definition.Indexes.Where(i => !i.IsPrimaryKey))
        {
            if (TryCreateIndexDesign(index, out var design, out var unsupportedReason))
            {
                sql += "\n" + BuildCreateIndex(design! with { IsDisabled = false }, definition.Object.Schema,
                    definition.Object.Name);
                scriptedIndexes.Add((index, design!));
            }
            else
            {
                sql += "\n" + BuildUnsupportedIndexComment(index, unsupportedReason!);
            }
        }

        foreach (var check in definition.CheckConstraints.Where(c => c.IsDisabled))
        {
            sql += $"\nALTER TABLE {target} NOCHECK CONSTRAINT {SqlServerIdentifier.Quote(check.Name!)};";
        }

        foreach (var check in definition.CheckConstraints.Where(c => !c.IsDisabled && !c.IsTrusted))
        {
            var name = SqlServerIdentifier.Quote(check.Name!);
            sql += $"\nALTER TABLE {target} NOCHECK CONSTRAINT {name};" +
                   $"\nALTER TABLE {target} WITH NOCHECK CHECK CONSTRAINT {name};";
        }

        foreach (var unique in definition.UniqueConstraints.Where(c => c.IsDisabled))
        {
            sql += $"\nALTER INDEX {SqlServerIdentifier.Quote(unique.Name!)} ON {target} DISABLE;";
        }

        if (primaryKey is { IsDisabled: true })
        {
            sql += $"\nALTER INDEX {SqlServerIdentifier.Quote(primaryKey.Name)} ON {target} DISABLE;";
        }

        foreach (var index in scriptedIndexes.Where(i => i.Info.IsDisabled))
        {
            sql += "\n" + BuildDisableIndex(
                definition.Object.Schema, definition.Object.Name, index.Info.Name);
        }

        return sql;
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
           $"{SqlServerIdentifier.Quote($"DF_{table}_{columnName}")} DEFAULT ({SqlServerExpressionSafety.RequireSingleExpression(expression, "default")}) FOR {SqlServerIdentifier.Quote(columnName)};";

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

    public static string BuildAddCheckConstraint(
        string schema,
        string table,
        CheckConstraintDesign checkConstraint)
    {
        if (checkConstraint.IsDisabled && string.IsNullOrWhiteSpace(checkConstraint.Name))
        {
            throw new GridletValidationException("A disabled CHECK constraint needs a name.");
        }

        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        var name = string.IsNullOrWhiteSpace(checkConstraint.Name)
            ? ""
            : $"CONSTRAINT {SqlServerIdentifier.Quote(checkConstraint.Name)} ";
        var expression = SqlServerExpressionSafety.RequireSingleExpression(
            checkConstraint.Expression,
            "CHECK constraint");
        var sql = $"ALTER TABLE {target} WITH {(checkConstraint.CheckExistingData ? "CHECK" : "NOCHECK")} " +
                  $"ADD {name}CHECK " +
                  $"{(checkConstraint.IsNotForReplication ? "NOT FOR REPLICATION " : "")}" +
                  $"({expression});";

        if (checkConstraint.IsDisabled)
        {
            sql += $" ALTER TABLE {target} NOCHECK CONSTRAINT {SqlServerIdentifier.Quote(checkConstraint.Name!)};";
        }

        return sql;
    }

    public static string BuildAddUniqueConstraint(
        string schema,
        string table,
        UniqueConstraintDesign uniqueConstraint)
    {
        ValidateFillFactor(uniqueConstraint.FillFactor);
        if (uniqueConstraint.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A UNIQUE constraint needs at least one column.");
        }
        if (uniqueConstraint.IsDisabled && string.IsNullOrWhiteSpace(uniqueConstraint.Name))
        {
            throw new GridletValidationException("A disabled UNIQUE constraint needs a name.");
        }

        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        var name = string.IsNullOrWhiteSpace(uniqueConstraint.Name)
            ? ""
            : $"CONSTRAINT {SqlServerIdentifier.Quote(uniqueConstraint.Name)} ";
        var sql = $"ALTER TABLE {target} ADD {name}UNIQUE " +
                  $"{(uniqueConstraint.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} " +
                  $"({BuildKeyList(uniqueConstraint.Columns)})" +
                  $"{BuildFillFactorClause(uniqueConstraint.FillFactor)};";

        if (uniqueConstraint.IsDisabled)
        {
            sql += $" ALTER INDEX {SqlServerIdentifier.Quote(uniqueConstraint.Name!)} ON {target} DISABLE;";
        }

        return sql;
    }

    public static string BuildCreateIndex(string schema, string table, IndexDesign index)
        => BuildCreateIndex(index, schema, table);

    private static string BuildCreateIndex(IndexDesign index, string schema, string table)
    {
        SqlServerIdentifier.Quote(index.Name);
        ValidateFillFactor(index.FillFactor);
        var keyColumns = index.KeyColumns ?? throw new GridletValidationException("Index keys are required.");
        var includedColumns = index.IncludedColumns ?? [];

        ValidateNoDuplicateColumns(keyColumns, includedColumns);
        if (index.IsClustered && !string.IsNullOrWhiteSpace(index.FilterExpression))
        {
            throw new GridletValidationException("A clustered index cannot be filtered.");
        }

        if (index.IsColumnstore)
        {
            if (index.IsUnique)
            {
                throw new GridletValidationException("SQL Server columnstore indexes cannot be unique.");
            }
            if (index.FillFactor != 0)
            {
                throw new GridletValidationException("SQL Server columnstore indexes do not support fill factor.");
            }
            if (includedColumns.Count > 0)
            {
                throw new GridletValidationException("SQL Server columnstore indexes do not support included columns.");
            }
            if (index.IsClustered && keyColumns.Count > 0)
            {
                throw new GridletValidationException("A clustered columnstore index does not take a column list.");
            }
            if (!index.IsClustered && keyColumns.Count == 0)
            {
                throw new GridletValidationException("A nonclustered columnstore index needs at least one column.");
            }

            ValidateColumnstoreKeys(keyColumns);
        }
        else
        {
            if (keyColumns.Count == 0)
            {
                throw new GridletValidationException("An index needs at least one key column.");
            }
            if (index.IsClustered && includedColumns.Count > 0)
            {
                throw new GridletValidationException("A clustered index does not support included columns.");
            }
        }

        var target = SqlServerIdentifier.QuoteQualified(schema, table);
        var sql = "CREATE " +
                  (index.IsUnique ? "UNIQUE " : "") +
                  (index.IsClustered ? "CLUSTERED " : "NONCLUSTERED ") +
                  (index.IsColumnstore ? "COLUMNSTORE " : "") +
                  $"INDEX {SqlServerIdentifier.Quote(index.Name)} ON {target}";

        if (!index.IsColumnstore || !index.IsClustered)
        {
            sql += $" ({BuildKeyList(keyColumns, allowDirection: !index.IsColumnstore)})";
        }
        if (includedColumns.Count > 0)
        {
            sql += $" INCLUDE ({string.Join(", ", includedColumns.Select(SqlServerIdentifier.Quote))})";
        }
        if (!string.IsNullOrWhiteSpace(index.FilterExpression))
        {
            sql += $" WHERE ({SqlServerExpressionSafety.RequireSingleExpression(index.FilterExpression, "index filter")})";
        }
        sql += BuildFillFactorClause(index.FillFactor) + ";";

        if (index.IsDisabled)
        {
            sql += " " + BuildDisableIndex(schema, table, index.Name);
        }

        return sql;
    }

    public static string BuildDropIndex(string schema, string table, string indexName)
        => $"DROP INDEX {SqlServerIdentifier.Quote(indexName)} ON {SqlServerIdentifier.QuoteQualified(schema, table)};";

    public static string BuildDropCheckConstraint(
        string schema,
        string table,
        ConstraintReference constraint)
        => BuildDropNamedConstraint(schema, table, constraint, "CHECK");

    public static string BuildDropUniqueConstraint(
        string schema,
        string table,
        ConstraintReference constraint)
        => BuildDropNamedConstraint(schema, table, constraint, "UNIQUE");

    private static string BuildDropNamedConstraint(
        string schema,
        string table,
        ConstraintReference constraint,
        string kind)
    {
        if (string.IsNullOrWhiteSpace(constraint.Name))
        {
            throw new GridletValidationException($"SQL Server needs the {kind} constraint name to drop it.");
        }

        return BuildDropConstraint(schema, table, constraint.Name);
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
            DbObjectType.Trigger => "TRIGGER",
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

            return $"{SqlServerIdentifier.Quote(column.Name)} AS ({SqlServerExpressionSafety.RequireSingleExpression(column.ComputedExpression, "computed")})" +
                   (column.IsPersisted ? " PERSISTED" : "");
        }

        var definition =
            $"{SqlServerIdentifier.Quote(column.Name)} {NormalizeDataType(column.DataType)}" +
            $"{(column.IsIdentity ? $" IDENTITY({column.IdentitySeed},{column.IdentityIncrement})" : "")}" +
            $"{(column.IsNullable && !column.IsPrimaryKey ? " NULL" : " NOT NULL")}";

        if (includeDefault && !string.IsNullOrWhiteSpace(column.DefaultExpression))
        {
            definition += $" DEFAULT ({SqlServerExpressionSafety.RequireSingleExpression(column.DefaultExpression, "default")})";
        }

        return definition;
    }

    private static string NormalizeReferentialAction(string? action)
    {
        var normalized = Regex.Replace(action?.Trim().ToUpperInvariant() ?? "", @"\s+", " ");
        return normalized is "NO ACTION" or "CASCADE" or "SET NULL" or "SET DEFAULT"
            ? normalized
            : throw new GridletValidationException($"'{action}' is not a supported referential action.");
    }

    private static string BuildKeyList(
        IReadOnlyList<IndexKeyDesign> keys,
        bool allowDirection = true)
        => string.Join(", ", keys.Select(key =>
        {
            if (string.IsNullOrWhiteSpace(key.Column))
            {
                throw new GridletValidationException(
                    "SQL Server index and UNIQUE constraint keys must name a column; expression keys are not supported.");
            }
            if (!string.IsNullOrWhiteSpace(key.Expression))
            {
                throw new GridletValidationException(
                    "SQL Server index and UNIQUE constraint keys do not support expressions. Use a computed column instead.");
            }
            if (!string.IsNullOrWhiteSpace(key.Collation))
            {
                throw new GridletValidationException(
                    "SQL Server index and UNIQUE constraint keys do not support a per-key collation.");
            }
            if (!allowDirection && key.IsDescending)
            {
                throw new GridletValidationException("SQL Server columnstore index columns do not support sort direction.");
            }

            return SqlServerIdentifier.Quote(key.Column) +
                   (allowDirection ? (key.IsDescending ? " DESC" : " ASC") : "");
        }));

    private static string BuildMetadataKeyList(IReadOnlyList<IndexKeyInfo> keys)
        => string.Join(", ", keys.OrderBy(k => k.Ordinal).Select(key =>
        {
            if (string.IsNullOrWhiteSpace(key.Column))
            {
                throw new GridletValidationException("SQL Server metadata index keys must name a column.");
            }

            return SqlServerIdentifier.Quote(key.Column) + (key.IsDescending ? " DESC" : " ASC");
        }));

    private static string BuildFillFactorClause(int fillFactor)
    {
        ValidateFillFactor(fillFactor);
        return fillFactor == 0 ? "" : $" WITH (FILLFACTOR = {fillFactor})";
    }

    private static void ValidateFillFactor(int fillFactor)
    {
        if (fillFactor is < 0 or > 100)
        {
            throw new GridletValidationException("Fill factor must be between 0 and 100.");
        }
    }

    private static void ValidateColumnstoreKeys(IReadOnlyList<IndexKeyDesign> keys)
    {
        _ = BuildKeyList(keys, allowDirection: false);
    }

    private static void ValidateNoDuplicateColumns(
        IReadOnlyList<IndexKeyDesign> keys,
        IReadOnlyList<string> includedColumns)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key.Column) && !columns.Add(key.Column))
            {
                throw new GridletValidationException($"Column '{key.Column}' appears more than once in the index.");
            }
        }
        foreach (var column in includedColumns)
        {
            SqlServerIdentifier.Quote(column);
            if (!columns.Add(column))
            {
                throw new GridletValidationException($"Column '{column}' appears more than once in the index.");
            }
        }
    }

    private static bool TryCreateIndexDesign(
        IndexInfo index,
        out IndexDesign? design,
        out string? unsupportedReason)
    {
        var kind = Regex.Replace(index.Kind.Trim().ToUpperInvariant().Replace('_', ' '), @"\s+", " ");
        var isClustered = kind is "CLUSTERED" or "CLUSTERED COLUMNSTORE";
        var isColumnstore = kind is "CLUSTERED COLUMNSTORE" or "NONCLUSTERED COLUMNSTORE";

        if (index.IsOrderedColumnstore)
        {
            design = null;
            unsupportedReason = "ordered columnstore metadata cannot be represented safely";
            return false;
        }

        if (kind is not ("CLUSTERED" or "NONCLUSTERED" or
            "CLUSTERED COLUMNSTORE" or "NONCLUSTERED COLUMNSTORE"))
        {
            design = null;
            unsupportedReason = $"SQL Server index kind '{SanitizeCommentText(index.Kind)}' is not supported";
            return false;
        }

        var keys = isColumnstore && isClustered
            ? []
            : (index.KeyColumns is { Count: > 0 }
                ? index.KeyColumns.OrderBy(k => k.Ordinal)
                    .Select(k => new IndexKeyDesign(k.Column, k.IsDescending, k.Expression, k.Collation))
                    .ToArray()
                : index.Columns.Select(c => new IndexKeyDesign(c)).ToArray());
        design = new IndexDesign(
            index.Name,
            keys,
            index.IsUnique,
            index.IncludedColumns,
            index.FilterDefinition,
            isClustered,
            isColumnstore,
            index.FillFactor,
            index.IsDisabled);
        unsupportedReason = null;
        return true;
    }

    private static string BuildDisableIndex(string schema, string table, string indexName)
        => $"ALTER INDEX {SqlServerIdentifier.Quote(indexName)} ON " +
           $"{SqlServerIdentifier.QuoteQualified(schema, table)} DISABLE;";

    private static string BuildUnsupportedIndexComment(IndexInfo index, string reason)
        => $"-- Gridlet omitted index {SqlServerIdentifier.Quote(SanitizeCommentText(index.Name))}: " +
           $"{SanitizeCommentText(reason)}.";

    private static string SanitizeCommentText(string value)
    {
        var sanitized = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return sanitized.Length <= 256 ? sanitized : sanitized[..256];
    }
}
