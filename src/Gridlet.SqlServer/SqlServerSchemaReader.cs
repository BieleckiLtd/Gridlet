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
            SELECT s.name AS [schema], o.name, o.type,
                   CONVERT(nvarchar(4000), ep.value) AS [description],
                   CONVERT(nvarchar(20), NULL) AS sub_kind
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
            WHERE o.type IN ('U', 'V', 'P', 'FN', 'IF', 'TF', 'TR', 'SO')
              AND o.is_ms_shipped = 0
            UNION ALL
            SELECT s.name, t.name, N'UDT', CONVERT(nvarchar(4000), ep.value),
                   CASE WHEN t.is_table_type = 1 THEN N'table'
                        WHEN t.is_assembly_type = 1 THEN N'clr'
                        ELSE N'alias' END
            FROM sys.types t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 6 AND ep.major_id = t.user_type_id
             AND ep.minor_id = 0 AND ep.name = N'MS_Description'
            WHERE t.is_user_defined = 1
            ORDER BY [schema], name;
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
                objects.Add(new DbObjectInfo(reader.GetString(0), reader.GetString(1), type.Value,
                    SubKind: reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description: reader.IsDBNull(3) ? null : reader.GetString(3)));
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
            SELECT o.type, s.name, o.name, CONVERT(nvarchar(4000), ep.value) AS [description]
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 1 AND ep.major_id = o.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
            WHERE o.object_id = OBJECT_ID(@name);

            SELECT c.name, t.name AS type_name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity, c.is_computed, dc.definition AS default_definition,
                   cc.definition AS computed_definition, cc.is_persisted,
                   CONVERT(bigint, ic.seed_value), CONVERT(bigint, ic.increment_value),
                   c.collation_name, CONVERT(nvarchar(4000), ep.value) AS [description],
                   CONVERT(int, COLUMNPROPERTY(c.object_id, c.name, 'IsHidden')) AS is_hidden
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc
              ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            LEFT JOIN sys.computed_columns cc
              ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.identity_columns ic
              ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 1 AND ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = N'MS_Description'
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

            DECLARE @temporal_sql nvarchar(max);
            DECLARE @retention_columns nvarchar(500) =
                CASE WHEN EXISTS (
                    SELECT 1 FROM sys.all_columns
                    WHERE object_id = OBJECT_ID(N'sys.tables') AND name = N'history_retention_period')
                THEN N',
                    CONVERT(bigint, CASE WHEN t.temporal_type = 2
                        THEN t.history_retention_period ELSE owner_table.history_retention_period END)
                        AS history_retention_period,
                    CONVERT(nvarchar(60), CASE WHEN t.temporal_type = 2
                        THEN t.history_retention_period_unit_desc ELSE owner_table.history_retention_period_unit_desc END)
                        AS history_retention_unit'
                ELSE N',
                    CONVERT(bigint, NULL) AS history_retention_period,
                    CONVERT(nvarchar(60), NULL) AS history_retention_unit'
                END;
            IF OBJECT_ID(N'sys.periods') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.all_columns
                   WHERE object_id = OBJECT_ID(N'sys.tables') AND name = N'temporal_type')
            BEGIN
                SET @temporal_sql = N'
                    SELECT CONVERT(int, t.temporal_type) AS temporal_type,
                           CASE WHEN t.temporal_type = 2 THEN hs.name ELSE owner_schema.name END AS related_schema,
                           CASE WHEN t.temporal_type = 2 THEN history_table.name ELSE owner_table.name END AS related_table,
                           period_start.name AS period_start_column,
                           period_end.name AS period_end_column' + @retention_columns + N'
                    FROM sys.tables t
                    LEFT JOIN sys.tables history_table ON history_table.object_id = t.history_table_id
                    LEFT JOIN sys.schemas hs ON hs.schema_id = history_table.schema_id
                    LEFT JOIN sys.tables owner_table ON owner_table.history_table_id = t.object_id
                    LEFT JOIN sys.schemas owner_schema ON owner_schema.schema_id = owner_table.schema_id
                    LEFT JOIN sys.periods period
                      ON period.object_id = CASE WHEN t.temporal_type = 1 THEN owner_table.object_id ELSE t.object_id END
                    LEFT JOIN sys.columns period_start
                      ON period_start.object_id = period.object_id AND period_start.column_id = period.start_column_id
                    LEFT JOIN sys.columns period_end
                      ON period_end.object_id = period.object_id AND period_end.column_id = period.end_column_id
                    WHERE t.object_id = OBJECT_ID(@object_name);';
            END
            ELSE
            BEGIN
                SET @temporal_sql = N'
                    SELECT CONVERT(int, 0) AS temporal_type,
                           CONVERT(sysname, NULL) AS related_schema,
                           CONVERT(sysname, NULL) AS related_table,
                           CONVERT(sysname, NULL) AS period_start_column,
                           CONVERT(sysname, NULL) AS period_end_column,
                           CONVERT(bigint, NULL) AS history_retention_period,
                           CONVERT(nvarchar(60), NULL) AS history_retention_unit
                    WHERE 1 = 0;';
            END;
            EXEC sys.sp_executesql @temporal_sql, N'@object_name nvarchar(776)', @object_name = @name;
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
        var dbObject = new DbObjectInfo(reader.GetString(1), reader.GetString(2), objectType,
            Description: reader.IsDBNull(3) ? null : reader.GetString(3));

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
                Collation: reader.IsDBNull(13) ? null : reader.GetString(13),
                Description: reader.IsDBNull(14) ? null : reader.GetString(14),
                IsHidden: !reader.IsDBNull(15) && reader.GetInt32(15) != 0));
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

        // Result set 8: SQL Server system-versioning metadata. A normal table reports temporal_type
        // 0; a view has no sys.tables row. Both intentionally map to no temporal descriptor.
        await reader.NextResultAsync(cancellationToken);
        TemporalTableInfo? temporal = null;
        if (await reader.ReadAsync(cancellationToken))
        {
            temporal = CreateTemporalTableInfo(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6));
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
                : null,
            Temporal: temporal);
    }

    internal static TemporalTableInfo? CreateTemporalTableInfo(
        int temporalType,
        string? relatedSchema,
        string? relatedTable,
        string? periodStartColumn,
        string? periodEndColumn,
        long? historyRetentionPeriod = null,
        string? historyRetentionUnit = null)
    {
        // SQL Server exposes its default infinite retention as -1 / INFINITE. Keep the
        // provider-neutral model limited to finite durations; null means no finite policy.
        if (historyRetentionPeriod < 0 ||
            string.Equals(historyRetentionUnit, "INFINITE", StringComparison.OrdinalIgnoreCase))
        {
            historyRetentionPeriod = null;
            historyRetentionUnit = null;
        }

        return temporalType switch
        {
            2 => new TemporalTableInfo(TemporalTableKinds.SystemVersioned, relatedSchema, relatedTable,
                periodStartColumn, periodEndColumn, historyRetentionPeriod, historyRetentionUnit),
            1 => new TemporalTableInfo(TemporalTableKinds.HistoryTable, relatedSchema, relatedTable,
                periodStartColumn, periodEndColumn, historyRetentionPeriod, historyRetentionUnit),
            _ => null,
        };
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

        if (objectType == DbObjectType.Sequence)
        {
            return BuildSequenceDefinition(
                await GetSequenceAsync(context, schema, name, cancellationToken));
        }

        return null;
    }

    public async Task<IReadOnlyList<ObjectDependencyInfo>> GetObjectDependenciesAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            DECLARE @id int = OBJECT_ID(@name);
            IF @id IS NULL THROW 50000, 'Gridlet object not found.', 1;

            SELECT N'references', rs.name, ro.name, ro.type, d.is_schema_bound_reference
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects ro ON ro.object_id = d.referenced_id
            JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            WHERE d.referencing_id = @id
            UNION ALL
            SELECT N'references', rs.name, ro.name, ro.type, CONVERT(bit, 1)
            FROM sys.foreign_keys fk
            JOIN sys.objects ro ON ro.object_id = fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id = ro.schema_id
            WHERE fk.parent_object_id = @id
            UNION ALL
            SELECT N'referencedBy', ss.name, so.name, so.type, d.is_schema_bound_reference
            FROM sys.sql_expression_dependencies d
            JOIN sys.objects so ON so.object_id = d.referencing_id
            JOIN sys.schemas ss ON ss.schema_id = so.schema_id
            WHERE d.referenced_id = @id
            UNION ALL
            SELECT N'referencedBy', ss.name, so.name, so.type, CONVERT(bit, 1)
            FROM sys.foreign_keys fk
            JOIN sys.objects so ON so.object_id = fk.parent_object_id
            JOIN sys.schemas ss ON ss.schema_id = so.schema_id
            WHERE fk.referenced_object_id = @id
            ORDER BY 1, 2, 3;
            """;
        var qualified = SqlServerIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", qualified);
        try
        {
            var dependencies = new List<ObjectDependencyInfo>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = MapObjectType(reader.GetString(3));
                if (type is null) continue;
                dependencies.Add(new ObjectDependencyInfo(
                    reader.GetString(0),
                    new DbObjectInfo(reader.GetString(1), reader.GetString(2), type.Value),
                    reader.GetBoolean(4)));
            }
            return dependencies.Distinct().ToArray();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 50000)
        {
            throw new GridletObjectNotFoundException(qualified);
        }
    }

    public async Task<SequenceInfo> GetSequenceAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            SELECT ss.name, seq.name, t.name, seq.precision, seq.scale,
                   seq.start_value, seq.increment, seq.minimum_value, seq.maximum_value,
                   seq.current_value, seq.is_cycling, seq.is_cached, seq.cache_size,
                   CONVERT(nvarchar(4000), ep.value)
            FROM sys.sequences seq
            JOIN sys.schemas ss ON ss.schema_id = seq.schema_id
            JOIN sys.types t ON t.user_type_id = seq.user_type_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 1 AND ep.major_id = seq.object_id AND ep.minor_id = 0 AND ep.name = N'MS_Description'
            WHERE seq.object_id = OBJECT_ID(@name);
            """;
        var qualified = SqlServerIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@name", qualified);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new GridletObjectNotFoundException(qualified);
        var type = reader.GetString(2);
        if (type is "decimal" or "numeric") type += $"({reader.GetByte(3)},{reader.GetByte(4)})";
        string Value(int ordinal) => Convert.ToString(
            reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return new SequenceInfo(
            new DbObjectInfo(reader.GetString(0), reader.GetString(1), DbObjectType.Sequence,
                Description: reader.IsDBNull(13) ? null : reader.GetString(13)),
            type, Value(5), Value(6), Value(7), Value(8),
            reader.IsDBNull(9) ? null : Value(9),
            reader.GetBoolean(10), reader.GetBoolean(11),
            reader.IsDBNull(12) ? null : Convert.ToInt64(reader.GetValue(12)));
    }

    public async Task<string> GetUserDefinedTypeDefinitionAsync(
        GridletConnectionContext context, string schema, string name,
        CancellationToken cancellationToken = default)
    {
        const string sql =
            """
            DECLARE @type_id int = (
                SELECT t.user_type_id
                FROM sys.types t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = @schema AND t.name = @type_name
                  AND t.is_user_defined = 1
            );

            SELECT s.name, t.name, t.is_table_type, t.is_assembly_type,
                   bt.name AS base_type_name, t.max_length, t.precision, t.scale,
                   t.is_nullable, a.name AS assembly_name, at.assembly_class,
                   CONVERT(nvarchar(4000), ep.value) AS [description],
                   tt.type_table_object_id
            FROM sys.types t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            LEFT JOIN sys.types bt
              ON bt.user_type_id = t.system_type_id AND bt.is_user_defined = 0
            LEFT JOIN sys.assembly_types at ON at.user_type_id = t.user_type_id
            LEFT JOIN sys.assemblies a ON a.assembly_id = at.assembly_id
            LEFT JOIN sys.table_types tt ON tt.user_type_id = t.user_type_id
            LEFT JOIN sys.extended_properties ep
              ON ep.class = 6 AND ep.major_id = t.user_type_id
             AND ep.minor_id = 0 AND ep.name = N'MS_Description'
            WHERE t.user_type_id = @type_id AND t.is_user_defined = 1;

            SELECT c.name, ct.name, SCHEMA_NAME(ct.schema_id), ct.is_user_defined,
                   c.max_length, c.precision, c.scale, c.is_nullable, c.column_id
            FROM sys.table_types tt
            JOIN sys.columns c ON c.object_id = tt.type_table_object_id
            JOIN sys.types ct ON ct.user_type_id = c.user_type_id
            WHERE tt.user_type_id = @type_id
            ORDER BY c.column_id;
            """;
        var qualified = SqlServerIdentifier.QuoteQualified(schema, name);
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@type_name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new GridletObjectNotFoundException(qualified);

        var isTable = reader.GetBoolean(2);
        var isClr = reader.GetBoolean(3);
        var kind = isTable ? "table" : isClr ? "clr" : "alias";
        var baseType = reader.IsDBNull(4) ? null : SqlServerDataTypeFormatter.Format(
            reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7));
        var info = new SqlServerUserDefinedType(
            reader.GetString(0), reader.GetString(1), kind, baseType, reader.GetBoolean(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10), []);

        await reader.NextResultAsync(cancellationToken);
        var columns = new List<SqlServerUserDefinedTypeColumn>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var dataType = reader.GetBoolean(3)
                ? SqlServerIdentifier.QuoteQualified(reader.GetString(2), reader.GetString(1))
                : SqlServerDataTypeFormatter.Format(
                    reader.GetString(1), reader.GetInt16(4), reader.GetByte(5), reader.GetByte(6));
            columns.Add(new SqlServerUserDefinedTypeColumn(
                reader.GetString(0), dataType, reader.GetBoolean(7), reader.GetInt32(8)));
        }
        return SqlServerUserDefinedTypeFormatter.Format(info with { Columns = columns });
    }

    private static string BuildSequenceDefinition(SequenceInfo sequence)
    {
        var cache = sequence.IsCached
            ? sequence.CacheSize is null ? "CACHE" : $"CACHE {sequence.CacheSize.Value}"
            : "NO CACHE";
        var current = sequence.CurrentValue is null ? "" : $"\n-- Current value: {sequence.CurrentValue}";
        return $"CREATE SEQUENCE {SqlServerIdentifier.QuoteQualified(sequence.Object.Schema, sequence.Object.Name)} AS {sequence.DataType}\n" +
            $"    START WITH {sequence.StartValue}\n    INCREMENT BY {sequence.Increment}\n" +
            $"    MINVALUE {sequence.MinimumValue}\n    MAXVALUE {sequence.MaximumValue}\n" +
            $"    {(sequence.IsCycling ? "CYCLE" : "NO CYCLE")}\n    {cache};{current}";
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
            "SO" => DbObjectType.Sequence,
            "UDT" => DbObjectType.UserDefinedType,
            _ => null,
        };
}
