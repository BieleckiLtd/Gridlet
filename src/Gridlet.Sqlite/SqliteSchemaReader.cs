using System.Text.RegularExpressions;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

public sealed class SqliteSchemaReader : ISchemaReader
{
    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return [new DatabaseInfo(SqliteIdentifier.MainSchema, IsSystem: false)];
    }

    public async Task<IReadOnlyList<SchemaInfo>> GetSchemasAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return [new SchemaInfo(SqliteIdentifier.MainSchema, "")];
    }

    public async Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name, type, CASE WHEN type = 'shadow' OR name GLOB 'sqlite_*' THEN 1 ELSE 0 END AS is_internal
            FROM pragma_table_list
            WHERE schema = 'main' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            UNION ALL
            SELECT name, 'trigger', 0
            FROM main.sqlite_schema
            WHERE type = 'trigger' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            ORDER BY name;
            """;

        var objects = new List<DbObjectInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var subKind = reader.GetString(1);
            var type = subKind switch
            {
                "view" => DbObjectType.View,
                "trigger" => DbObjectType.Trigger,
                _ => DbObjectType.Table,
            };
            objects.Add(new DbObjectInfo(SqliteIdentifier.MainSchema, reader.GetString(0), type,
                subKind is "virtual" or "shadow" ? subKind : null,
                reader.GetInt64(2) != 0));
        }

        return objects;
    }

    public async Task<TableDefinition> GetTableDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return await LoadTableDefinitionAsync(connection, name, cancellationToken);
    }

    internal static async Task<TableDefinition> LoadTableDefinitionAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
    {
        string objectType;
        bool isInternal;
        bool withoutRowId;
        string? createSql;
        await using (var objectCommand = connection.CreateCommand())
        {
            objectCommand.CommandText =
                """
                SELECT tl.type, s.sql,
                       CASE WHEN tl.type = 'shadow' OR tl.name GLOB 'sqlite_*' THEN 1 ELSE 0 END,
                       tl.wr
                FROM pragma_table_list AS tl
                LEFT JOIN main.sqlite_schema AS s ON s.name = tl.name AND s.type IN ('table', 'view')
                WHERE tl.schema = 'main' AND tl.name = @name;
                """;
            objectCommand.Parameters.AddWithValue("@name", name);
            await using var objectReader = await objectCommand.ExecuteReaderAsync(cancellationToken);
            if (!await objectReader.ReadAsync(cancellationToken))
            {
                throw new GridletObjectNotFoundException($"{SqliteIdentifier.MainSchema}.{name}");
            }

            objectType = objectReader.GetString(0);
            createSql = objectReader.IsDBNull(1) ? null : objectReader.GetString(1);
            isInternal = objectReader.GetInt64(2) != 0;
            withoutRowId = objectReader.GetInt64(3) != 0;
        }

        var rawColumns = new List<(string Name, string Type, bool Nullable, string? Default, int PkOrdinal, int Hidden)>();
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                "SELECT name, type, [notnull], dflt_value, pk, hidden " +
                "FROM pragma_table_xinfo(@table, 'main') ORDER BY cid;";
            columnsCommand.Parameters.AddWithValue("@table", name);
            await using var reader = await columnsCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rawColumns.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)) ? "BLOB" : reader.GetString(1),
                    reader.GetInt64(2) == 0,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }
        }

        var singlePrimaryKey = rawColumns.Count(c => c.PkOrdinal > 0) == 1;
        var columns = rawColumns.Select((column, ordinal) =>
        {
            var isComputed = column.Hidden is 2 or 3;
            var isIdentity = singlePrimaryKey && column.PkOrdinal > 0 &&
                             SqliteSqlInspection.HasAutoincrementColumn(createSql, column.Name) &&
                             string.Equals(column.Type.Trim(), "INTEGER", StringComparison.OrdinalIgnoreCase);
            return new ColumnInfo(
                column.Name,
                column.Type,
                IsNullable: column.PkOrdinal == 0 && column.Nullable,
                IsIdentity: isIdentity,
                IsComputed: isComputed,
                IsPrimaryKey: column.PkOrdinal > 0,
                DefaultDefinition: column.Default,
                Ordinal: ordinal,
                ComputedDefinition: isComputed ? ExtractGeneratedExpression(createSql, column.Name) : null,
                IsPersisted: column.Hidden == 3,
                IdentitySeed: isIdentity ? 1 : null,
                IdentityIncrement: isIdentity ? 1 : null,
                IsHidden: column.Hidden == 1);
        }).ToArray();

        var parsedTable = SqliteCreateSqlParser.ParseTable(createSql);
        var indexes = await LoadIndexesAsync(connection, name, columns, cancellationToken);
        var foreignKeys = await LoadForeignKeysAsync(connection, name, cancellationToken);
        var rowIdentity = SqliteRowIdentity.Resolve(
            objectType,
            isInternal,
            withoutRowId,
            rawColumns
                .Select(column => new SqliteRowIdentity.Column(
                    column.Name, column.Type, !column.Nullable, column.PkOrdinal))
                .ToArray());

        return new TableDefinition(
            new DbObjectInfo(SqliteIdentifier.MainSchema, name,
                objectType == "view" ? DbObjectType.View : DbObjectType.Table,
                objectType is "virtual" or "shadow" ? objectType : null,
                isInternal),
            columns,
            indexes,
            foreignKeys,
            parsedTable.Checks,
            parsedTable.Uniques,
            rowIdentity);
    }

    public async Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM main.sqlite_schema WHERE name = @name AND type IN ('table', 'view', 'trigger');";
        command.Parameters.AddWithValue("@name", name);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            null => throw new GridletObjectNotFoundException($"{schema}.{name}"),
            DBNull => null,
            _ => Convert.ToString(result),
        };
    }

    private static async Task<IReadOnlyList<IndexInfo>> LoadIndexesAsync(
        SqliteConnection connection,
        string table,
        IReadOnlyList<ColumnInfo> columns,
        CancellationToken cancellationToken)
    {
        var indexes = new List<IndexInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT il.name, il.[unique], il.origin, s.sql
            FROM pragma_index_list(@table, 'main') AS il
            LEFT JOIN main.sqlite_schema AS s ON s.type = 'index' AND s.name = il.name
            ORDER BY il.seq;
            """;
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var indexName = reader.GetString(0);
            var unique = reader.GetInt64(1) != 0;
            var origin = reader.GetString(2);
            if (origin != "c") continue; // UNIQUE and PRIMARY KEY constraints are modeled separately.

            var createSql = reader.IsDBNull(3) ? null : reader.GetString(3);
            var parsed = SqliteCreateSqlParser.ParseIndex(createSql);
            var keyColumns = await LoadIndexKeysAsync(connection, indexName, parsed.Keys, cancellationToken);
            var indexColumns = keyColumns.Where(key => key.Column is not null).Select(key => key.Column!).ToArray();
            indexes.Add(new IndexInfo(indexName, unique ? "UNIQUE INDEX" : "INDEX", unique, false,
                indexColumns, keyColumns, FilterDefinition: parsed.Filter));
        }

        var primaryKey = columns.Where(c => c.IsPrimaryKey).OrderBy(c => c.Ordinal).Select(c => c.Name).ToArray();
        if (primaryKey.Length > 0)
        {
            indexes.Insert(0, new IndexInfo($"PK_{table}", "PRIMARY KEY", true, true, primaryKey));
        }

        return indexes;
    }

    private static async Task<IReadOnlyList<IndexKeyInfo>> LoadIndexKeysAsync(
        SqliteConnection connection,
        string index,
        IReadOnlyList<IndexKeyInfo> parsedKeys,
        CancellationToken cancellationToken)
    {
        var keys = new List<IndexKeyInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT seqno, cid, name, [desc], coll FROM pragma_index_xinfo(@index, 'main') " +
            "WHERE [key] <> 0 ORDER BY seqno;";
        command.Parameters.AddWithValue("@index", index);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ordinal = reader.GetInt32(0) + 1;
            var cid = reader.GetInt32(1);
            var parsed = parsedKeys.ElementAtOrDefault(ordinal - 1);
            keys.Add(new IndexKeyInfo(
                cid >= 0 && !reader.IsDBNull(2) ? reader.GetString(2) : null,
                ordinal,
                reader.GetInt64(3) != 0,
                cid < 0 ? parsed?.Expression : null,
                reader.IsDBNull(4) ? parsed?.Collation : reader.GetString(4)));
        }

        return keys;
    }

    private static async Task<IReadOnlyList<ForeignKeyInfo>> LoadForeignKeysAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var entries = new Dictionary<long, (string Table, string OnDelete, string OnUpdate, List<ForeignKeyColumnPair> Columns)>();
        var order = new List<long>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, seq, [table], [from], [to], on_update, on_delete " +
            "FROM pragma_foreign_key_list(@table, 'main') ORDER BY id, seq;";
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            if (!entries.TryGetValue(id, out var entry))
            {
                entry = (reader.GetString(2), reader.GetString(6), reader.GetString(5), []);
                entries[id] = entry;
                order.Add(id);
            }

            entry.Columns.Add(new ForeignKeyColumnPair(
                reader.GetString(3),
                reader.IsDBNull(4) ? "rowid" : reader.GetString(4)));
        }

        return order.Select(id => new ForeignKeyInfo(
            $"FK_{table}_{id}",
            SqliteIdentifier.MainSchema,
            entries[id].Table,
            entries[id].Columns,
            entries[id].OnDelete.Replace(' ', '_'),
            entries[id].OnUpdate.Replace(' ', '_'))).ToArray();
    }

    private static string? ExtractGeneratedExpression(string? createSql, string columnName)
    {
        if (string.IsNullOrWhiteSpace(createSql))
        {
            return null;
        }

        var quotedName = SqliteIdentifier.Quote(columnName);
        var columnStart = createSql.IndexOf(quotedName, StringComparison.OrdinalIgnoreCase);
        var identifierLength = quotedName.Length;
        if (columnStart < 0)
        {
            var bareName = Regex.Match(
                createSql,
                $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(columnName)}(?![\p{{L}}\p{{N}}_])",
                RegexOptions.IgnoreCase);
            if (!bareName.Success) return null;
            columnStart = bareName.Index;
            identifierLength = bareName.Length;
        }

        var asMatch = Regex.Match(createSql[(columnStart + identifierLength)..], @"\bAS\s*\(", RegexOptions.IgnoreCase);
        if (!asMatch.Success)
        {
            return null;
        }

        var open = columnStart + identifierLength + asMatch.Index + asMatch.Length - 1;
        var depth = 0;
        for (var i = open; i < createSql.Length; i++)
        {
            if (createSql[i] == '(') depth++;
            else if (createSql[i] == ')' && --depth == 0) return createSql[(open + 1)..i].Trim();
        }

        return null;
    }
}
