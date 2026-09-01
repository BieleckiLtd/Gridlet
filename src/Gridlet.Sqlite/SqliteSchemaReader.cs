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
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA database_list;";
        var databases = new List<DatabaseInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(1);
            if (!name.Equals("temp", StringComparison.OrdinalIgnoreCase))
                databases.Add(new DatabaseInfo(name, IsSystem: false));
        }
        return databases;
    }

    public async Task<IReadOnlyList<SchemaInfo>> GetSchemasAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return [new SchemaInfo(SqliteIdentifier.SelectedSchema(context), "")];
    }

    public async Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var schema = SqliteIdentifier.SelectedSchema(context);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT name, type, CASE WHEN type = 'shadow' OR name GLOB 'sqlite_*' THEN 1 ELSE 0 END AS is_internal
            FROM pragma_table_list
            WHERE schema = @schema AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            UNION ALL
            SELECT name, 'trigger', 0
            FROM {{SqliteIdentifier.Quote(schema)}}.sqlite_schema
            WHERE type = 'trigger' AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
            ORDER BY name;
            """;
        command.Parameters.AddWithValue("@schema", schema);

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
            objects.Add(new DbObjectInfo(schema, reader.GetString(0), type,
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
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        return await LoadTableDefinitionAsync(connection, schema, name, cancellationToken);
    }

    internal static async Task<TableDefinition> LoadTableDefinitionAsync(
        SqliteConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        string objectType;
        bool isInternal;
        bool withoutRowId;
        bool strict;
        string? createSql;
        await using (var objectCommand = connection.CreateCommand())
        {
            objectCommand.CommandText =
                $$"""
                SELECT tl.type, s.sql,
                       CASE WHEN tl.type = 'shadow' OR tl.name GLOB 'sqlite_*' THEN 1 ELSE 0 END,
                       tl.wr, tl.strict
                FROM pragma_table_list AS tl
                LEFT JOIN {{SqliteIdentifier.Quote(schema)}}.sqlite_schema AS s ON s.name = tl.name AND s.type IN ('table', 'view')
                WHERE tl.schema = @schema AND tl.name = @name;
                """;
            objectCommand.Parameters.AddWithValue("@name", name);
            objectCommand.Parameters.AddWithValue("@schema", schema);
            await using var objectReader = await objectCommand.ExecuteReaderAsync(cancellationToken);
            if (!await objectReader.ReadAsync(cancellationToken))
            {
                throw new GridletObjectNotFoundException($"{schema}.{name}");
            }

            objectType = objectReader.GetString(0);
            createSql = objectReader.IsDBNull(1) ? null : objectReader.GetString(1);
            isInternal = objectReader.GetInt64(2) != 0;
            withoutRowId = objectReader.GetInt64(3) != 0;
            strict = objectReader.GetInt64(4) != 0;
        }

        var rawColumns = new List<(string Name, string Type, bool Nullable, string? Default, int PkOrdinal, int Hidden)>();
        await using (var columnsCommand = connection.CreateCommand())
        {
            columnsCommand.CommandText =
                "SELECT name, type, [notnull], dflt_value, pk, hidden " +
                "FROM pragma_table_xinfo(@table, @schema) ORDER BY cid;";
            columnsCommand.Parameters.AddWithValue("@table", name);
            columnsCommand.Parameters.AddWithValue("@schema", schema);
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

        var parsedTable = SqliteCreateSqlParser.ParseTable(createSql);
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
                IsHidden: column.Hidden == 1,
                Collation: parsedTable.ColumnCollations.GetValueOrDefault(column.Name));
        }).ToArray();

        var indexes = await LoadIndexesAsync(connection, schema, name, columns, cancellationToken);
        var foreignKeys = await LoadForeignKeysAsync(
            connection, schema, name, parsedTable.ForeignKeys,
            indexes.FirstOrDefault(index => index.IsPrimaryKey)?.Name, cancellationToken);
        var rowIdentity = SqliteRowIdentity.Resolve(
            objectType,
            isInternal,
            withoutRowId,
            rawColumns
                .Select(column => new SqliteRowIdentity.Column(
                    column.Name, column.Type, !column.Nullable, column.PkOrdinal))
                .ToArray());

        return new TableDefinition(
            new DbObjectInfo(schema, name,
                objectType == "view" ? DbObjectType.View : DbObjectType.Table,
                objectType is "virtual" or "shadow" ? objectType : null,
                isInternal),
            columns,
            indexes,
            foreignKeys,
            parsedTable.Checks,
            parsedTable.Uniques,
            rowIdentity,
            // The options are read from pragma_table_list rather than the CREATE text, so they are
            // what SQLite applied rather than what the statement appeared to ask for.
            [.. withoutRowId ? new[] { SqliteTableOptions.WithoutRowId } : [],
             .. strict ? new[] { SqliteTableOptions.Strict } : []]);
    }

    internal static Task<TableDefinition> LoadTableDefinitionAsync(
        SqliteConnection connection,
        string name,
        CancellationToken cancellationToken)
        => LoadTableDefinitionAsync(connection, SqliteIdentifier.MainSchema, name, cancellationToken);

    public async Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT sql FROM {SqliteIdentifier.Quote(schema)}.sqlite_schema WHERE name = @name AND type IN ('table', 'view', 'trigger');";
        command.Parameters.AddWithValue("@name", name);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            null => throw new GridletObjectNotFoundException($"{schema}.{name}"),
            DBNull => null,
            _ => Convert.ToString(result),
        };
    }

    public async Task<IReadOnlyList<ObjectDependencyInfo>> GetObjectDependenciesAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireSelectedSchema(context, schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name, type, sql FROM {SqliteIdentifier.Quote(schema)}.sqlite_schema " +
            "WHERE type IN ('table', 'view', 'trigger') AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\';";
        var definitions = new List<(string Name, DbObjectType Type, string? Sql)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                definitions.Add((reader.GetString(0), reader.GetString(1) switch
                {
                    "view" => DbObjectType.View,
                    "trigger" => DbObjectType.Trigger,
                    _ => DbObjectType.Table,
                }, reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }
        var target = definitions.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target.Name is null) throw new GridletObjectNotFoundException($"{schema}.{name}");

        var result = new List<ObjectDependencyInfo>();
        foreach (var candidate in definitions.Where(item =>
                     !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            if (ContainsIdentifier(target.Sql, candidate.Name))
                result.Add(new ObjectDependencyInfo("references",
                    new DbObjectInfo(schema, candidate.Name, candidate.Type), IsInferred: true));
            if (ContainsIdentifier(candidate.Sql, name))
                result.Add(new ObjectDependencyInfo("referencedBy",
                    new DbObjectInfo(schema, candidate.Name, candidate.Type), IsInferred: true));
        }
        return result.Distinct().OrderBy(item => item.Direction).ThenBy(item => item.Object.Name).ToArray();
    }

    private static bool ContainsIdentifier(string? sql, string identifier)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var escaped = Regex.Escape(identifier);
        return Regex.IsMatch(sql,
            $@"(?<![\p{{L}}\p{{N}}_])(?:{escaped}|\[{escaped}\]|\""{escaped}\""|`{escaped}`)(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
    }

    private static async Task<IReadOnlyList<IndexInfo>> LoadIndexesAsync(
        SqliteConnection connection,
        string schema,
        string table,
        IReadOnlyList<ColumnInfo> columns,
        CancellationToken cancellationToken)
    {
        var indexes = new List<IndexInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $$"""
            SELECT il.name, il.[unique], il.origin, s.sql
            FROM pragma_index_list(@table, @schema) AS il
            LEFT JOIN {{SqliteIdentifier.Quote(schema)}}.sqlite_schema AS s ON s.type = 'index' AND s.name = il.name
            ORDER BY il.seq;
            """;
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@schema", schema);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var indexName = reader.GetString(0);
            var unique = reader.GetInt64(1) != 0;
            var origin = reader.GetString(2);
            if (origin != "c") continue; // UNIQUE and PRIMARY KEY constraints are modeled separately.

            var createSql = reader.IsDBNull(3) ? null : reader.GetString(3);
            var parsed = SqliteCreateSqlParser.ParseIndex(createSql);
            var keyColumns = await LoadIndexKeysAsync(connection, schema, indexName, parsed.Keys, cancellationToken);
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
        string schema,
        string index,
        IReadOnlyList<IndexKeyInfo> parsedKeys,
        CancellationToken cancellationToken)
    {
        var keys = new List<IndexKeyInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT seqno, cid, name, [desc], coll FROM pragma_index_xinfo(@index, @schema) " +
            "WHERE [key] <> 0 ORDER BY seqno;";
        command.Parameters.AddWithValue("@index", index);
        command.Parameters.AddWithValue("@schema", schema);
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
        string schema,
        string table,
        IReadOnlyList<SqliteCreateSqlParser.ParsedForeignKey> declarations,
        string? primaryKeyName,
        CancellationToken cancellationToken)
    {
        var entries = new Dictionary<long, (string Table, string OnDelete, string OnUpdate, List<ForeignKeyColumnPair> Columns)>();
        var order = new List<long>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, seq, [table], [from], [to], on_update, on_delete " +
            "FROM pragma_foreign_key_list(@table, @schema) ORDER BY id, seq;";
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@schema", schema);
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

        // A declared name is reported as it was written, even when it repeats or matches another
        // constraint: it is what the database holds, and a rebuild has to write it back. SQLite does
        // not require these names to be unique, so a name is not a reliable way to single out one
        // key; DropConstraintAsync refuses an ambiguous name rather than acting on the wrong
        // constraint.
        var declaredNames = AlignDeclaredNames(declarations, order, entries);

        // The label for an unnamed key is Gridlet's own choice, so it is chosen not to collide with
        // a name the table already carries - a declared foreign-key name, or the primary key, which
        // DropConstraintAsync resolves through the same route. A collision between two declared
        // names is the database's, and stands.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (primaryKeyName is not null) taken.Add(primaryKeyName);
        for (var position = 0; position < order.Count; position++)
        {
            if (declaredNames?[position] is { } declared && !string.IsNullOrWhiteSpace(declared))
            {
                taken.Add(declared);
            }
        }

        return order.Select((id, position) =>
        {
            var declaredName = declaredNames?[position];
            var synthesized = string.IsNullOrWhiteSpace(declaredName);
            var name = declaredName!;
            if (synthesized)
            {
                name = $"FK_{table}_{id}";
                for (var attempt = 2; !taken.Add(name); attempt++) name = $"FK_{table}_{id}_{attempt}";
            }

            return new ForeignKeyInfo(
                name,
                schema,
                entries[id].Table,
                entries[id].Columns,
                entries[id].OnDelete.Replace(' ', '_'),
                entries[id].OnUpdate.Replace(' ', '_'),
                synthesized);
        }).ToArray();
    }

    /// <summary>
    /// Pairs the foreign keys written in the CREATE statement with the rows
    /// <c>pragma_foreign_key_list</c> returned, so a declared CONSTRAINT name can be recovered.
    /// The pragma numbers foreign keys in reverse declaration order, which is why the declarations
    /// are reversed before pairing. The pairing is accepted only when every pair agrees on the
    /// referenced table and the local columns; on any disagreement the result is null and every key
    /// keeps a synthesized name, because naming the wrong constraint is worse than naming none.
    /// </summary>
    /// <returns>One entry per pragma row, in pragma order, or null when the two do not line up.</returns>
    private static IReadOnlyList<string?>? AlignDeclaredNames(
        IReadOnlyList<SqliteCreateSqlParser.ParsedForeignKey> declarations,
        IReadOnlyList<long> order,
        IReadOnlyDictionary<long, (string Table, string OnDelete, string OnUpdate, List<ForeignKeyColumnPair> Columns)> entries)
    {
        if (declarations.Count != order.Count) return null;

        var names = new string?[order.Count];
        for (var position = 0; position < order.Count; position++)
        {
            var declaration = declarations[declarations.Count - 1 - position];
            var entry = entries[order[position]];
            if (!string.Equals(declaration.ReferencedTable, entry.Table, StringComparison.OrdinalIgnoreCase) ||
                declaration.Columns.Count != entry.Columns.Count)
            {
                return null;
            }

            for (var column = 0; column < entry.Columns.Count; column++)
            {
                if (!string.Equals(
                        declaration.Columns[column],
                        entry.Columns[column].Column,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            names[position] = declaration.Name;
        }

        return names;
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
