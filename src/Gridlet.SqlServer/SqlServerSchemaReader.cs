using Gridlet.Abstractions;
using Gridlet.Models;

namespace Gridlet.SqlServer;

public sealed class SqlServerSchemaReader : ISchemaReader
{
    public async Task<IReadOnlyList<SchemaInfo>> GetSchemasAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT s.name, USER_NAME(s.principal_id) AS owner_name
            FROM sys.schemas s
            ORDER BY s.name;
            """;

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var schemas = new List<SchemaInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            schemas.Add(new SchemaInfo(reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
        }

        return schemas;
    }

    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT name, CAST(CASE WHEN database_id <= 4 THEN 1 ELSE 0 END AS bit) AS is_system
            FROM sys.databases
            WHERE HAS_DBACCESS(name) = 1
            ORDER BY is_system, name;
            """;

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var databases = new List<DatabaseInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            databases.Add(new DatabaseInfo(reader.GetString(0), reader.GetBoolean(1)));
        }

        return databases;
    }

    public async Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT s.name AS [schema], o.name, o.type
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'TR')
              AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name;
            """;

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var objects = new List<DbObjectInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = MapObjectType(reader.GetString(2));
            if (type is not null)
            {
                objects.Add(new DbObjectInfo(reader.GetString(0), reader.GetString(1), type.Value));
            }
        }

        return objects;
    }

    public async Task<TableDefinition> GetTableDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT o.type, s.name, o.name
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.object_id = OBJECT_ID(@name);

            SELECT c.name, t.name AS type_name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.is_computed, dc.definition AS default_definition,
                   cc.definition AS computed_definition, cc.is_persisted,
                   CONVERT(bigint, ic.seed_value), CONVERT(bigint, ic.increment_value),
                   c.collation_name
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc
              ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            LEFT JOIN sys.computed_columns cc
              ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.identity_columns ic
              ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@name)
            ORDER BY c.column_id;

            SELECT col.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE i.object_id = OBJECT_ID(@name) AND i.is_primary_key = 1;

            DECLARE @target_object_id int = OBJECT_ID(@name);
            DECLARE @columnstore_order_expression nvarchar(100) =
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.all_columns
                    WHERE object_id = OBJECT_ID(N'sys.index_columns')
                      AND name = N'column_store_order_ordinal')
                THEN N'ic.column_store_order_ordinal'
                ELSE N'CONVERT(tinyint, 0)'
                END;
            DECLARE @index_sql nvarchar(max) = N'
                SELECT i.name, i.type_desc, i.is_unique, i.is_primary_key,
                       i.type, i.filter_definition, i.fill_factor, i.is_disabled,
                       ic.key_ordinal, ic.index_column_id, ic.is_descending_key,
                       ic.is_included_column, col.name AS column_name,
                       ' + @columnstore_order_expression + N' AS column_store_order_ordinal,
                       MAX(CONVERT(int, ' + @columnstore_order_expression + N')) OVER (
                           PARTITION BY i.object_id, i.index_id) AS max_column_store_order_ordinal
                FROM sys.indexes i
                LEFT JOIN sys.index_columns ic
                  ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                LEFT JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                WHERE i.object_id = @object_id
                  AND i.type > 0
                  AND i.is_unique_constraint = 0
                ORDER BY i.index_id,
                         CASE
                             WHEN i.type IN (5, 6) AND ' + @columnstore_order_expression + N' > 0 THEN 0
                             WHEN i.type IN (5, 6) THEN 1
                             WHEN ic.is_included_column = 1 THEN 1
                             ELSE 0
                         END,
                         CASE
                             WHEN i.type IN (5, 6) AND ' + @columnstore_order_expression + N' > 0
                                 THEN ' + @columnstore_order_expression + N'
                             WHEN ic.key_ordinal > 0 THEN ic.key_ordinal
                             ELSE ic.index_column_id
                         END;';
            EXEC sys.sp_executesql @index_sql, N'@object_id int', @object_id = @target_object_id;

            SELECT fk.name, rs.name AS referenced_schema, rt.name AS referenced_table,
                   fk.delete_referential_action_desc, fk.update_referential_action_desc,
                   pc.name AS column_name, rc.name AS referenced_column
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
            WHERE fk.parent_object_id = OBJECT_ID(@name)
            ORDER BY fk.name, fkc.constraint_column_id;

            SELECT cc.name, cc.definition, col.name AS column_name,
                   cc.is_disabled, cc.is_not_trusted, cc.is_not_for_replication
            FROM sys.check_constraints cc
            LEFT JOIN sys.columns col
              ON col.object_id = cc.parent_object_id AND col.column_id = cc.parent_column_id
            WHERE cc.parent_object_id = OBJECT_ID(@name)
            ORDER BY cc.object_id;

            SELECT kc.name, i.type, i.fill_factor, i.is_disabled,
                   ic.key_ordinal, ic.is_descending_key, col.name AS column_name
            FROM sys.key_constraints kc
            JOIN sys.indexes i
              ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
            JOIN sys.index_columns ic
              ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE kc.parent_object_id = OBJECT_ID(@name) AND kc.type = 'UQ'
            ORDER BY kc.object_id, ic.key_ordinal;
            """;

        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, name);

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", qualifiedName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Result set 1: the object itself.
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

        var objectType = MapObjectType(reader.GetString(0)) ?? DbObjectType.Table;
        var dbObject = new DbObjectInfo(reader.GetString(1), reader.GetString(2), objectType);

        // Result set 2: columns (primary-key flag filled in after result set 3).
        await reader.NextResultAsync(cancellationToken);
        var columns = new List<ColumnInfo>();
        var ordinal = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(0),
                DataType: SqlServerDataTypeFormatter.Format(
                    reader.GetString(1), reader.GetInt16(2), reader.GetByte(3), reader.GetByte(4)),
                IsNullable: reader.GetBoolean(5),
                IsIdentity: reader.GetBoolean(6),
                IsComputed: reader.GetBoolean(7),
                IsPrimaryKey: false,
                DefaultDefinition: reader.IsDBNull(8) ? null : reader.GetString(8),
                Ordinal: ordinal++,
                ComputedDefinition: reader.IsDBNull(9) ? null : reader.GetString(9),
                IsPersisted: !reader.IsDBNull(10) && reader.GetBoolean(10),
                IdentitySeed: reader.IsDBNull(11) ? null : reader.GetInt64(11),
                IdentityIncrement: reader.IsDBNull(12) ? null : reader.GetInt64(12),
                Collation: reader.IsDBNull(13) ? null : reader.GetString(13)));
        }

        // Result set 3: primary key columns.
        await reader.NextResultAsync(cancellationToken);
        var primaryKeyColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            primaryKeyColumns.Add(reader.GetString(0));
        }

        for (var i = 0; i < columns.Count; i++)
        {
            if (primaryKeyColumns.Contains(columns[i].Name))
            {
                columns[i] = columns[i] with { IsPrimaryKey = true };
            }
        }

        // Result set 4: indexes (one row per key or included column). UNIQUE constraints are
        // deliberately excluded and loaded separately below; primary-key indexes remain here for
        // compatibility with callers that discover the PK through Indexes.
        await reader.NextResultAsync(cancellationToken);
        var indexes = new Dictionary<string, (
            string Kind,
            bool IsUnique,
            bool IsPrimaryKey,
            bool IsClustered,
            bool IsColumnstore,
            bool IsOrderedColumnstore,
            string? FilterDefinition,
            int FillFactor,
            bool IsDisabled,
            List<IndexKeyInfo> KeyColumns,
            List<string> IncludedColumns)>();
        var indexOrder = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var indexName = reader.GetString(0);
            if (!indexes.TryGetValue(indexName, out var entry))
            {
                var indexType = reader.GetByte(4);
                var kind = reader.GetString(1);
                entry = (
                    kind,
                    reader.GetBoolean(2),
                    reader.GetBoolean(3),
                    indexType is 1 or 5,
                    indexType is 5 or 6,
                    indexType is 5 or 6
                        && !reader.IsDBNull(14)
                        && Convert.ToInt32(reader.GetValue(14)) > 0,
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetByte(6),
                    reader.GetBoolean(7),
                    [],
                    []);
                indexes[indexName] = entry;
                indexOrder.Add(indexName);
            }

            if (reader.IsDBNull(12))
            {
                continue;
            }

            var columnName = reader.GetString(12);
            if (entry.IsColumnstore)
            {
                var columnstoreOrderOrdinal = reader.IsDBNull(13)
                    ? 0
                    : Convert.ToInt32(reader.GetValue(13));
                var keyOrdinal = columnstoreOrderOrdinal > 0
                    ? columnstoreOrderOrdinal
                    : Convert.ToInt32(reader.GetValue(9));
                entry.KeyColumns.Add(new IndexKeyInfo(columnName, keyOrdinal));
            }
            else if (reader.GetBoolean(11))
            {
                entry.IncludedColumns.Add(columnName);
            }
            else if (Convert.ToInt32(reader.GetValue(8)) > 0)
            {
                var keyOrdinal = Convert.ToInt32(reader.GetValue(8)) > 0
                    ? Convert.ToInt32(reader.GetValue(8))
                    : Convert.ToInt32(reader.GetValue(9));
                entry.KeyColumns.Add(new IndexKeyInfo(
                    columnName,
                    keyOrdinal,
                    reader.GetBoolean(10)));
            }
        }

        // Result set 5: foreign keys (one row per column pairing).
        await reader.NextResultAsync(cancellationToken);
        var foreignKeys = new Dictionary<string, (string ReferencedSchema, string ReferencedTable, string OnDelete, string OnUpdate, List<ForeignKeyColumnPair> Columns)>();
        var foreignKeyOrder = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var fkName = reader.GetString(0);
            if (!foreignKeys.TryGetValue(fkName, out var entry))
            {
                entry = (reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), []);
                foreignKeys[fkName] = entry;
                foreignKeyOrder.Add(fkName);
            }

            entry.Columns.Add(new ForeignKeyColumnPair(reader.GetString(5), reader.GetString(6)));
        }

        // Result set 6: CHECK constraints.
        await reader.NextResultAsync(cancellationToken);
        var checkConstraints = new List<CheckConstraintInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            checkConstraints.Add(new CheckConstraintInfo(
                Name: reader.GetString(0),
                Definition: reader.GetString(1),
                Ordinal: checkConstraints.Count,
                Column: reader.IsDBNull(2) ? null : reader.GetString(2),
                IsDisabled: reader.GetBoolean(3),
                IsTrusted: !reader.GetBoolean(4),
                IsNotForReplication: reader.GetBoolean(5)));
        }

        // Result set 7: UNIQUE constraints (one row per ordered key column).
        await reader.NextResultAsync(cancellationToken);
        var uniqueConstraints = new Dictionary<string, (
            bool IsClustered,
            int FillFactor,
            bool IsDisabled,
            int Ordinal,
            List<IndexKeyInfo> Columns)>();
        var uniqueConstraintOrder = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var constraintName = reader.GetString(0);
            if (!uniqueConstraints.TryGetValue(constraintName, out var entry))
            {
                entry = (
                    reader.GetByte(1) == 1,
                    reader.GetByte(2),
                    reader.GetBoolean(3),
                    uniqueConstraintOrder.Count,
                    []);
                uniqueConstraints[constraintName] = entry;
                uniqueConstraintOrder.Add(constraintName);
            }

            entry.Columns.Add(new IndexKeyInfo(
                reader.GetString(6),
                Convert.ToInt32(reader.GetValue(4)),
                reader.GetBoolean(5)));
        }

        var indexInfos = indexOrder
            .Select(n => new IndexInfo(
                    n,
                    indexes[n].Kind,
                    indexes[n].IsUnique,
                    indexes[n].IsPrimaryKey,
                    indexes[n].KeyColumns.Where(k => k.Column is not null).Select(k => k.Column!).ToArray(),
                    indexes[n].KeyColumns,
                    indexes[n].IncludedColumns,
                    indexes[n].FilterDefinition,
                    indexes[n].IsClustered,
                    indexes[n].IsColumnstore,
                    indexes[n].FillFactor,
                    indexes[n].IsDisabled,
                    indexes[n].IsOrderedColumnstore))
            .ToArray();
        var uniqueConstraintInfos = uniqueConstraintOrder
            .Select(n => new UniqueConstraintInfo(
                n,
                uniqueConstraints[n].Columns,
                uniqueConstraints[n].Ordinal,
                uniqueConstraints[n].IsClustered,
                uniqueConstraints[n].FillFactor,
                uniqueConstraints[n].IsDisabled))
            .ToArray();

        return new TableDefinition(
            dbObject,
            columns,
            indexInfos,
            foreignKeyOrder
                .Select(n => new ForeignKeyInfo(n, foreignKeys[n].ReferencedSchema, foreignKeys[n].ReferencedTable,
                    foreignKeys[n].Columns, foreignKeys[n].OnDelete, foreignKeys[n].OnUpdate))
                .ToArray(),
            checkConstraints,
            uniqueConstraintInfos,
            objectType == DbObjectType.Table
                ? SqlServerRowIdentity.Resolve(
                    PrimaryKeyColumnsInKeyOrder(indexInfos, columns),
                    UniqueKeyCandidates(indexInfos, uniqueConstraintInfos),
                    columns.ToDictionary(c => c.Name, c => c.IsNullable, StringComparer.OrdinalIgnoreCase))
                : null);
    }

    /// <summary>Returns the primary-key columns in key order, preferring the index's own ordering.</summary>
    private static IReadOnlyList<string> PrimaryKeyColumnsInKeyOrder(
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ColumnInfo> columns)
    {
        var primaryKey = indexes.FirstOrDefault(index => index.IsPrimaryKey);
        return primaryKey is not null
            ? primaryKey.Columns
            : columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToArray();
    }

    /// <summary>Returns the unique constraints and unique indexes that could identify a row.</summary>
    private static IEnumerable<SqlServerRowIdentity.UniqueKey> UniqueKeyCandidates(
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints)
    {
        foreach (var constraint in uniqueConstraints.Where(c => c.Name is not null))
        {
            yield return new SqlServerRowIdentity.UniqueKey(
                constraint.Name!,
                constraint.Columns
                    .OrderBy(column => column.Ordinal)
                    .Select(column => column.Column)
                    .Where(column => column is not null)
                    .Select(column => column!)
                    .ToArray(),
                constraint.IsDisabled);
        }

        foreach (var index in indexes.Where(i => i.IsUnique && !i.IsPrimaryKey && !i.IsColumnstore))
        {
            yield return new SqlServerRowIdentity.UniqueKey(
                index.Name,
                index.Columns,
                index.IsDisabled,
                !string.IsNullOrWhiteSpace(index.FilterDefinition));
        }
    }

    public async Task<string?> GetObjectDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT OBJECT_ID(@name), OBJECT_DEFINITION(OBJECT_ID(@name)), o.type FROM sys.objects o WHERE o.object_id = OBJECT_ID(@name);";
        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, name);

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", qualifiedName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

        var objectType = MapObjectType(reader.GetString(2));

        if (!reader.IsDBNull(1))
        {
            return reader.GetString(1);
        }

        if (objectType == DbObjectType.Table)
        {
            return SqlServerDdlBuilder.BuildTableDefinition(
                await GetTableDefinitionAsync(context, schema, name, cancellationToken));
        }

        return null;
    }

    /// <summary>
    /// Reads a routine's parameters. For a procedure the return value is synthesised, because every
    /// procedure returns an int and SQL Server does not list it; for a scalar function the engine
    /// reports it as parameter 0.
    /// </summary>
    public async Task<RoutineDefinition> GetRoutineDefinitionAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT o.type, s.name, o.name
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.object_id = OBJECT_ID(@name);

            SELECT p.name, t.name AS type_name, p.max_length, p.precision, p.scale,
                   p.parameter_id, p.is_output, p.has_default_value,
                   CONVERT(nvarchar(4000), p.default_value) AS default_text,
                   p.is_readonly, t.is_table_type, SCHEMA_NAME(t.schema_id) AS type_schema
            FROM sys.parameters p
            JOIN sys.types t ON t.user_type_id = p.user_type_id
            WHERE p.object_id = OBJECT_ID(@name)
            ORDER BY p.parameter_id;
            """;

        var qualifiedName = SqlServerIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", qualifiedName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new GridletObjectNotFoundException(qualifiedName);
        }

        var objectType = MapObjectType(reader.GetString(0));
        if (objectType is not (DbObjectType.StoredProcedure or DbObjectType.ScalarFunction
            or DbObjectType.TableValuedFunction))
        {
            throw new GridletValidationException(
                $"{qualifiedName} is not a stored procedure or function.");
        }

        var routine = new DbObjectInfo(reader.GetString(1), reader.GetString(2), objectType.Value);

        await reader.NextResultAsync(cancellationToken);
        var parameters = new List<RoutineParameterInfo>();
        if (objectType == DbObjectType.StoredProcedure)
        {
            parameters.Add(new RoutineParameterInfo(
                "@ReturnValue", "int", 0, IsOutput: true, IsReturnValue: true));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var parameterId = reader.GetInt32(5);
            var isTableType = !reader.IsDBNull(10) && reader.GetBoolean(10);
            var typeName = ParameterTypeName(
                isTableType,
                reader.IsDBNull(11) ? "dbo" : reader.GetString(11),
                reader.GetString(1),
                reader.GetInt16(2),
                reader.GetByte(3),
                reader.GetByte(4));
            parameters.Add(new RoutineParameterInfo(
                Name: ParameterName(reader.IsDBNull(0) ? null : reader.GetString(0)),
                DataType: typeName,
                Ordinal: parameterId,
                IsOutput: reader.GetBoolean(6) && parameterId > 0,
                IsReturnValue: parameterId == 0,
                HasDefault: reader.GetBoolean(7),
                DefaultDefinition: reader.IsDBNull(8) ? null : reader.GetString(8),
                IsReadOnly: reader.GetBoolean(9),
                IsTableType: isTableType));
        }

        return new RoutineDefinition(routine, parameters);
    }

    /// <summary>
    /// Names the return-value row of a function, which SQL Server reports with no name of its own.
    /// Both an empty name and a missing one are treated the same way, so the caller does not depend
    /// on which the engine returns.
    /// </summary>
    internal static string ParameterName(string? name)
        => string.IsNullOrEmpty(name) ? "@ReturnValue" : name;

    /// <summary>
    /// Names a parameter's type for the generated script. Only the built-in types are written bare
    /// with their length; a type the database owns - a table type, an alias type, a CLR type - is
    /// named in full, because the script can be run by somebody whose default schema is not the one
    /// the type lives in, where an unqualified name would not resolve.
    /// </summary>
    internal static string ParameterTypeName(
        bool isTableType, string typeSchema, string typeName, int maxLength, byte precision, byte scale)
        => isTableType || !string.Equals(typeSchema, "sys", StringComparison.OrdinalIgnoreCase)
            ? SqlServerIdentifier.QuoteQualified(typeSchema, typeName)
            : SqlServerDataTypeFormatter.Format(typeName, maxLength, precision, scale);

    /// <inheritdoc />
    public string BuildRoutineExecuteScript(
        RoutineDefinition routine,
        IReadOnlyDictionary<string, RoutineArgument> arguments)
        => SqlServerRoutineScriptBuilder.Build(routine, arguments);

    private static DbObjectType? MapObjectType(string type)
        => type.Trim() switch
        {
            "U" => DbObjectType.Table,
            "V" => DbObjectType.View,
            "P" => DbObjectType.StoredProcedure,
            "FN" => DbObjectType.ScalarFunction,
            "IF" or "TF" => DbObjectType.TableValuedFunction,
            "TR" => DbObjectType.Trigger,
            _ => null,
        };
}
