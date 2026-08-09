using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>
/// The table-level options SQLite accepts after the column list. Both change what the table is
/// rather than decorating it: WITHOUT ROWID removes the implicit rowid, and STRICT makes the engine
/// enforce declared types instead of applying affinity.
/// </summary>
public static class SqliteTableOptions
{
    public const string WithoutRowId = "WITHOUT ROWID";

    public const string Strict = "STRICT";

    /// <summary>The types a STRICT table may declare. SQLite rejects anything else outright.</summary>
    private static readonly HashSet<string> StrictTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "INT", "INTEGER", "REAL", "TEXT", "BLOB", "ANY",
    };

    /// <summary>
    /// Validates the requested options and returns them in the order SQLite writes them, or an
    /// empty string when there are none.
    /// </summary>
    /// <returns>The clause to append after the closing parenthesis, including its leading space.</returns>
    public static string BuildClause(IReadOnlyList<string>? options, IReadOnlyList<ColumnDesign> columns)
    {
        if (options is not { Count: > 0 })
        {
            return "";
        }

        var withoutRowId = false;
        var strict = false;
        foreach (var option in options)
        {
            var normalized = string.Join(' ',
                (option ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
            if (normalized == WithoutRowId) withoutRowId = true;
            else if (normalized == Strict) strict = true;
            else
            {
                throw new GridletValidationException(
                    $"'{option}' is not a SQLite table option. Use WITHOUT ROWID or STRICT.");
            }
        }

        if (withoutRowId && !columns.Any(column => column.IsPrimaryKey))
        {
            throw new GridletValidationException(
                "A WITHOUT ROWID table must declare a primary key: it has no rowid to fall back on.");
        }

        if (strict)
        {
            // Failing here names the column; letting SQLite fail says only that the type is unknown.
            var offender = columns.FirstOrDefault(column =>
                !StrictTypeNames.Contains(column.DataType.Split('(')[0].Trim()));
            if (offender is not null)
            {
                throw new GridletValidationException(
                    $"A STRICT table cannot declare column '{offender.Name}' as {offender.DataType}. " +
                    "STRICT allows INT, INTEGER, REAL, TEXT, BLOB and ANY.");
            }
        }

        return (withoutRowId ? " WITHOUT ROWID" : "") + (strict ? (withoutRowId ? ", STRICT" : " STRICT") : "");
    }
}
