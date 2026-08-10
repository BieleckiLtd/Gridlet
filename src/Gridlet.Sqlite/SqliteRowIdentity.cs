using Gridlet.Models;

namespace Gridlet.Sqlite;

/// <summary>
/// Chooses how one row of a SQLite table can be addressed for editing. A declared primary key is
/// preferred; a rowid table without one (or with one SQLite does not enforce as NOT NULL) falls back
/// to the <c>rowid</c> pseudo-column.
/// </summary>
internal static class SqliteRowIdentity
{
    /// <summary>The rowid aliases SQLite accepts, in the order Gridlet prefers them.</summary>
    internal static readonly string[] RowIdAliases = ["rowid", "_rowid_", "oid"];

    /// <summary>One column as reported by <c>pragma_table_xinfo</c>.</summary>
    /// <param name="Name">The column name.</param>
    /// <param name="DeclaredType">The declared type, empty when the column has none.</param>
    /// <param name="IsNotNull">Whether the column carries an explicit NOT NULL.</param>
    /// <param name="PrimaryKeyOrdinal">The one-based primary-key position, or 0.</param>
    internal readonly record struct Column(
        string Name,
        string DeclaredType,
        bool IsNotNull,
        int PrimaryKeyOrdinal);

    /// <summary>
    /// Returns the row identity for a SQLite object, or <see langword="null"/> when no single row can
    /// be addressed - views, virtual and shadow tables, and WITHOUT ROWID tables with no usable
    /// primary key.
    /// </summary>
    /// <param name="objectType">The <c>pragma_table_list</c> type: table, view, virtual or shadow.</param>
    /// <param name="isInternal">Whether the object is internal to SQLite or to a virtual table.</param>
    /// <param name="withoutRowId">The <c>pragma_table_list.wr</c> flag.</param>
    /// <param name="columns">The object's columns, in declared order.</param>
    internal static RowIdentityInfo? Resolve(
        string objectType,
        bool isInternal,
        bool withoutRowId,
        IReadOnlyList<Column> columns)
    {
        if (objectType != "table" || isInternal)
        {
            return null;
        }

        var primaryKey = columns
            .Where(column => column.PrimaryKeyOrdinal > 0)
            .OrderBy(column => column.PrimaryKeyOrdinal)
            .ToArray();
        if (primaryKey.Length > 0 && IsPrimaryKeyEnforcedNotNull(primaryKey, withoutRowId))
        {
            return new RowIdentityInfo(
                RowIdentityKinds.PrimaryKey,
                primaryKey.Select(column => column.Name).ToArray());
        }

        // A rowid table always has a stable, unique rowid, so it beats a primary key SQLite lets be
        // NULL. WITHOUT ROWID tables have no such fallback.
        if (!withoutRowId)
        {
            var alias = RowIdAliases.FirstOrDefault(candidate => columns.All(
                column => !string.Equals(column.Name, candidate, StringComparison.OrdinalIgnoreCase)));
            if (alias is not null)
            {
                return new RowIdentityInfo(RowIdentityKinds.RowId, [alias]);
            }
        }

        // Nothing left: the primary key is one SQLite lets hold NULLs, and every rowid alias is
        // taken by a real column. Returning that key anyway would offer editing on a table where
        // two rows can share a NULL key, and the update meant for one of them would change both.
        return null;
    }

    /// <summary>
    /// SQLite only enforces NOT NULL on primary-key columns for INTEGER PRIMARY KEY (which is the
    /// rowid), for WITHOUT ROWID tables, and where the column declares NOT NULL itself. Anything else
    /// is a long-standing quirk that lets a "primary key" hold NULLs in more than one row.
    /// </summary>
    private static bool IsPrimaryKeyEnforcedNotNull(IReadOnlyList<Column> primaryKey, bool withoutRowId)
    {
        if (withoutRowId || primaryKey.All(column => column.IsNotNull))
        {
            return true;
        }

        return primaryKey.Count == 1
            && string.Equals(primaryKey[0].DeclaredType.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);
    }
}
