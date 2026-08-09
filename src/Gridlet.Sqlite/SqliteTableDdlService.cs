using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

public sealed class SqliteTableDdlService : ITableDdlService
{
    public Task CreateSchemaAsync(
        GridletConnectionContext context,
        SchemaDesign design,
        CancellationToken cancellationToken = default)
        => throw UnsupportedSchemas();

    public Task AlterSchemaOwnerAsync(
        GridletConnectionContext context,
        string schema,
        string owner,
        CancellationToken cancellationToken = default)
        => throw UnsupportedSchemas();

    public Task DropSchemaAsync(
        GridletConnectionContext context,
        string schema,
        CancellationToken cancellationToken = default)
        => throw UnsupportedSchemas();

    public Task CreateTableAsync(
        GridletConnectionContext context,
        TableDesign design,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqliteDdlBuilder.BuildCreateTable(design), cancellationToken);

    public async Task AddColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        _ = await RequireOrdinaryTableAsync(connection, table, cancellationToken);
        await ExecuteAsync(connection, transaction: null,
            SqliteDdlBuilder.BuildAddColumn(schema, table, column), cancellationToken);
    }

    public async Task AlterColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string columnName,
        ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        var existing = FindColumn(definition, columnName);
        var replacement = string.IsNullOrWhiteSpace(column.DataType)
            ? ToDesign(existing) with { Name = column.Name }
            : column with { IsPrimaryKey = existing.IsPrimaryKey };

        var columns = definition.Columns.Select(c =>
        {
            if (string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)) return replacement;
            var design = ToDesign(c);
            return design.ComputedExpression is null ||
                   string.Equals(existing.Name, replacement.Name, StringComparison.OrdinalIgnoreCase)
                ? design
                : design with
                {
                    ComputedExpression = SqliteCreateSqlParser.RenameIdentifier(
                        design.ComputedExpression, existing.Name, replacement.Name),
                };
        }).ToArray();
        var renamedColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [replacement.Name] = existing.Name,
        };
        var foreignKeys = ToForeignKeyDesigns(definition).Select(fk => fk with
        {
            Columns = fk.Columns.Select(pair => new ForeignKeyColumnPair(
                string.Equals(pair.Column, existing.Name, StringComparison.OrdinalIgnoreCase)
                    ? replacement.Name
                    : pair.Column,
                string.Equals(fk.ReferencedTable, table, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.ReferencedColumn, existing.Name, StringComparison.OrdinalIgnoreCase)
                    ? replacement.Name
                    : pair.ReferencedColumn)).ToArray(),
        }).ToArray();
        await RebuildTableAsync(connection, definition, columns,
            foreignKeys, renamedColumns, cancellationToken);
    }

    public async Task DropColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string columnName,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        _ = FindColumn(definition, columnName);
        if (definition.Columns.Count == 1)
        {
            throw new GridletValidationException("The only column in a table cannot be dropped.");
        }

        var columns = definition.Columns
            .Where(c => !string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase))
            .Select(ToDesign)
            .ToArray();
        var foreignKeys = ToForeignKeyDesigns(definition)
            .Where(fk => fk.Columns.All(pair =>
                !string.Equals(pair.Column, columnName, StringComparison.OrdinalIgnoreCase) &&
                !(string.Equals(fk.ReferencedTable, table, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(pair.ReferencedColumn, columnName, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        await RebuildTableAsync(connection, definition, columns, foreignKeys, null, cancellationToken);
    }

    public async Task AddPrimaryKeyAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        PrimaryKeyDesign primaryKey,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        if (primaryKey.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A primary key needs at least one column.");
        }

        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        if (definition.Columns.Any(c => c.IsPrimaryKey))
        {
            throw new GridletValidationException($"Table {schema}.{table} already has a primary key.");
        }

        var names = primaryKey.Columns.Select(name => FindColumn(definition, name).Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columns = definition.Columns.Select(c =>
        {
            var selected = names.Contains(c.Name);
            if (selected && c.IsNullable)
            {
                throw new GridletValidationException($"Primary-key column '{c.Name}' must be NOT NULL.");
            }

            return ToDesign(c) with { IsPrimaryKey = selected };
        }).ToArray();

        await RebuildTableAsync(connection, definition, columns,
            ToForeignKeyDesigns(definition), null, cancellationToken, primaryKey.Name);
    }

    public async Task AddForeignKeyAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ForeignKeyDesign foreignKey,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        SqliteIdentifier.RequireMainSchema(foreignKey.ReferencedSchema);
        if (foreignKey.Columns is not { Count: > 0 })
        {
            throw new GridletValidationException("A foreign key needs at least one column pair.");
        }

        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        var referenced = await RequireTableAsync(connection, foreignKey.ReferencedTable, cancellationToken);
        foreach (var pair in foreignKey.Columns)
        {
            _ = FindColumn(definition, pair.Column);
            _ = FindColumn(referenced, pair.ReferencedColumn);
        }

        var foreignKeys = ToForeignKeyDesigns(definition).Append(foreignKey).ToArray();
        await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
            foreignKeys, null, cancellationToken);
    }

    public async Task AddCheckConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CheckConstraintDesign checkConstraint,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        if (!string.IsNullOrWhiteSpace(checkConstraint.Name) && definition.CheckConstraints.Any(check =>
                string.Equals(check.Name, checkConstraint.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GridletValidationException($"Constraint '{checkConstraint.Name}' already exists on {schema}.{table}.");
        }

        await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
            ToForeignKeyDesigns(definition), null, cancellationToken,
            checkConstraints: ToCheckDesigns(definition).Append(checkConstraint).ToArray());
    }

    public async Task DropCheckConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ConstraintReference constraint,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        var target = FindConstraint(definition.CheckConstraints, constraint, item => item.Name, item => item.Ordinal,
            "CHECK", schema, table);
        await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
            ToForeignKeyDesigns(definition), null, cancellationToken,
            checkConstraints: ToCheckDesigns(definition).Where((_, index) => index != target).ToArray());
    }

    public async Task AddUniqueConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        UniqueConstraintDesign uniqueConstraint,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        if (uniqueConstraint.Columns is not { Count: > 0 })
            throw new GridletValidationException("A unique constraint needs at least one key.");
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        if (!string.IsNullOrWhiteSpace(uniqueConstraint.Name) && definition.UniqueConstraints.Any(unique =>
                string.Equals(unique.Name, uniqueConstraint.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GridletValidationException($"Constraint '{uniqueConstraint.Name}' already exists on {schema}.{table}.");
        }
        ValidateIndexKeys(definition, uniqueConstraint.Columns);
        await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
            ToForeignKeyDesigns(definition), null, cancellationToken,
            uniqueConstraints: ToUniqueDesigns(definition).Append(uniqueConstraint).ToArray());
    }

    public async Task DropUniqueConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ConstraintReference constraint,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        var target = FindConstraint(definition.UniqueConstraints, constraint, item => item.Name, item => item.Ordinal,
            "UNIQUE", schema, table);
        await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
            ToForeignKeyDesigns(definition), null, cancellationToken,
            uniqueConstraints: ToUniqueDesigns(definition).Where((_, index) => index != target).ToArray());
    }

    public async Task CreateIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        IndexDesign index,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireOrdinaryTableAsync(connection, table, cancellationToken);
        ValidateIndexKeys(definition, index.KeyColumns);
        await ExecuteAsync(connection, transaction: null,
            SqliteDdlBuilder.BuildCreateIndex(schema, table, index), cancellationToken);
    }

    public async Task DropIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string indexName,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireOrdinaryTableAsync(connection, table, cancellationToken);
        if (!definition.Indexes.Any(index => string.Equals(index.Name, indexName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new GridletValidationException($"Ordinary index '{indexName}' does not exist on {schema}.{table}.");
        }
        await ExecuteAsync(connection, transaction: null,
            SqliteDdlBuilder.BuildDropIndex(schema, indexName), cancellationToken);
    }

    public async Task DropConstraintAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string constraintName,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        var primaryKey = definition.Indexes.FirstOrDefault(i => i.IsPrimaryKey);
        if (primaryKey is not null && string.Equals(primaryKey.Name, constraintName, StringComparison.OrdinalIgnoreCase))
        {
            var columns = definition.Columns.Select(c => ToDesign(c) with
            {
                IsPrimaryKey = false,
                IsIdentity = false,
            }).ToArray();
            await RebuildTableAsync(connection, definition, columns,
                ToForeignKeyDesigns(definition), null, cancellationToken);
            return;
        }

        var foreignKey = definition.ForeignKeys.FirstOrDefault(
            fk => string.Equals(fk.Name, constraintName, StringComparison.OrdinalIgnoreCase));
        if (foreignKey is not null)
        {
            await RebuildTableAsync(connection, definition, definition.Columns.Select(ToDesign).ToArray(),
                ToForeignKeyDesigns(definition)
                    .Where(fk => !string.Equals(fk.Name, constraintName, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                null, cancellationToken);
            return;
        }

        throw new GridletValidationException(
            $"Constraint '{constraintName}' does not exist on {schema}.{table}.");
    }

    public async Task DropTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        if (definition.Object.IsInternal || definition.Object.SubKind == "shadow")
        {
            throw new GridletValidationException(
                $"Internal SQLite table {schema}.{table} cannot be dropped directly.");
        }
        await ExecuteAsync(connection, transaction: null,
            SqliteDdlBuilder.BuildDropTable(schema, table), cancellationToken);
    }

    public Task DropObjectAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        DbObjectType type,
        CancellationToken cancellationToken = default)
        => type == DbObjectType.Table
            ? DropTableAsync(context, schema, name, cancellationToken)
            : ExecuteAsync(context, SqliteDdlBuilder.BuildDropObject(schema, name, type), cancellationToken);

    public string BuildDropScript(DbObjectInfo @object)
        => @object.Type == DbObjectType.Table
            ? SqliteDdlBuilder.BuildDropTable(SqliteIdentifier.MainSchema, @object.Name)
            : SqliteDdlBuilder.BuildDropObject(SqliteIdentifier.MainSchema, @object.Name, @object.Type);

    public string BuildInsertScript(
        TableDefinition table,
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<object?[]> rows)
        => SqliteInsertScriptBuilder.Build(table, columns, rows);

    /// <summary>
    /// Renames a table. SQLite has no rename for views, triggers or routines - they would have to be
    /// dropped and recreated from their source, which is the person's decision to make in the
    /// editor, not something to do to their definition behind their back.
    /// </summary>
    public async Task RenameObjectAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        DbObjectType type,
        string newName,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        if (type != DbObjectType.Table)
        {
            throw new GridletValidationException(
                $"SQLite cannot rename a {type.ToString().ToLowerInvariant()}. " +
                "Edit its definition instead: drop it and create it under the new name.");
        }

        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, name, cancellationToken);
        if (definition.Object.IsInternal || definition.Object.SubKind == "shadow")
        {
            throw new GridletValidationException(
                $"Internal SQLite table {schema}.{name} cannot be renamed.");
        }

        await ExecuteAsync(connection, transaction: null,
            $"ALTER TABLE {SqliteIdentifier.Quote(name)} RENAME TO {SqliteIdentifier.Quote(newName)};",
            cancellationToken);
    }

    /// <summary>
    /// Renames an index by recreating it: SQLite has no ALTER INDEX. The definition is read back from
    /// the database rather than rewritten as text, so the new index is the same index.
    /// </summary>
    public async Task RenameIndexAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        string indexName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, table, cancellationToken);
        var index = definition.Indexes.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new GridletObjectNotFoundException($"{schema}.{table}.{indexName}");
        if (index.IsPrimaryKey)
        {
            throw new GridletValidationException(
                "The primary-key index is part of the table definition and has no name to change.");
        }

        var keys = index.KeyColumns is { Count: > 0 }
            ? index.KeyColumns
                .OrderBy(key => key.Ordinal)
                .Select(key => new IndexKeyDesign(key.Column, key.IsDescending, key.Expression, key.Collation))
                .ToArray()
            : index.Columns.Select(column => new IndexKeyDesign(column)).ToArray();
        var design = new IndexDesign(
            newName,
            keys,
            index.IsUnique,
            FilterExpression: index.FilterDefinition);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction,
            SqliteDdlBuilder.BuildDropIndex(schema, indexName), cancellationToken);
        await ExecuteAsync(connection, transaction,
            SqliteDdlBuilder.BuildCreateIndex(schema, table, design), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Empties a table. SQLite has no TRUNCATE; an unqualified DELETE is its equivalent and the
    /// engine optimises it into the same wholesale drop of the table's pages.
    /// </summary>
    public async Task TruncateTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
    {
        SqliteIdentifier.RequireMainSchema(schema);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, table, cancellationToken);
        if (definition.Object.Type != DbObjectType.Table || definition.Object.IsInternal
            || definition.Object.SubKind == "shadow")
        {
            throw new GridletValidationException($"{schema}.{table} is not a table Gridlet can empty.");
        }

        await ExecuteAsync(connection, transaction: null,
            $"DELETE FROM {SqliteIdentifier.QuoteQualified(schema, table)};", cancellationToken);
    }

    internal static async Task RebuildTableAsync(
        SqliteConnection connection,
        TableDefinition definition,
        IReadOnlyList<ColumnDesign> columns,
        IReadOnlyList<ForeignKeyDesign> foreignKeys,
        IReadOnlyDictionary<string, string>? renamedColumns,
        CancellationToken cancellationToken,
        string? primaryKeyName = null,
        IReadOnlyList<CheckConstraintDesign>? checkConstraints = null,
        IReadOnlyList<UniqueConstraintDesign>? uniqueConstraints = null,
        IReadOnlyList<IndexDesign>? ordinaryIndexes = null)
    {
        var table = definition.Object.Name;
        var schema = definition.Object.Schema;
        await EnsureTableCanBeRebuiltAsync(connection, schema, table, cancellationToken);
        var tempTable = $"__gridlet_{table}_{Guid.NewGuid():N}";
        var keyName = primaryKeyName ?? definition.Indexes.FirstOrDefault(i => i.IsPrimaryKey)?.Name;
        // The rebuilt table has to be the same kind of table: dropping WITHOUT ROWID or STRICT would
        // silently change how the engine stores and checks every row.
        var tempDesign = new TableDesign(schema, tempTable, columns, definition.TableOptions);
        checkConstraints ??= ToCheckDesigns(definition, renamedColumns, columns);
        uniqueConstraints ??= ToUniqueDesigns(definition, renamedColumns, columns);
        ordinaryIndexes ??= ToIndexDesigns(definition, renamedColumns, columns);
        var createSql = SqliteDdlBuilder.BuildCreateTable(tempDesign, keyName, foreignKeys,
            checkConstraints, uniqueConstraints);
        var preserveAutoincrementSequence =
            definition.Columns.Any(column => column.IsIdentity) &&
            columns.Any(column => column.IsIdentity);

        var triggers = await LoadTriggerSqlAsync(connection, table, cancellationToken);
        if (triggers.Count > 0 && renamedColumns is { Count: > 0 })
        {
            throw new GridletValidationException(
                $"Table {schema}.{table} has triggers. Rename the column with explicit SQLite DDL so trigger references can be reviewed.");
        }

        // SQLite implements DROP TABLE as an implicit DELETE when foreign-key enforcement is on.
        // Deferring constraints does not defer ON DELETE actions, so rebuilding a parent table can
        // otherwise cascade-delete child rows. Enforcement must be disabled before the transaction;
        // foreign_key_check below prevents an invalid replacement schema from committing.
        var baselineForeignKeyViolations = await LoadForeignKeyViolationsAsync(
            connection, transaction: null, cancellationToken);
        var foreignKeyEnforcementWasEnabled = await GetForeignKeyEnforcementAsync(connection, cancellationToken);
        await SetForeignKeyEnforcementAsync(connection, enabled: false, cancellationToken);
        try
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                var originalSequence = preserveAutoincrementSequence
                    ? await LoadSequenceAsync(connection, transaction, table, cancellationToken)
                    : null;
                await ExecuteAsync(connection, transaction, createSql, cancellationToken);

                var copiedColumns = columns.Where(c => string.IsNullOrWhiteSpace(c.ComputedExpression))
                    .Select(c => (NewName: c.Name, OldName: renamedColumns is not null && renamedColumns.TryGetValue(c.Name, out var old)
                        ? old
                        : c.Name))
                    .Where(pair => definition.Columns.Any(c =>
                        string.Equals(c.Name, pair.OldName, StringComparison.OrdinalIgnoreCase) && !c.IsComputed))
                    .ToArray();
                if (copiedColumns.Length > 0)
                {
                    var insert = $"INSERT INTO {SqliteIdentifier.QuoteQualified(schema, tempTable)} " +
                                 $"({string.Join(", ", copiedColumns.Select(c => SqliteIdentifier.Quote(c.NewName)))}) " +
                                 $"SELECT {string.Join(", ", copiedColumns.Select(c => SqliteIdentifier.Quote(c.OldName)))} " +
                                 $"FROM {SqliteIdentifier.QuoteQualified(schema, table)};";
                    await ExecuteAsync(connection, transaction, insert, cancellationToken);
                }

                await ExecuteAsync(connection, transaction, SqliteDdlBuilder.BuildDropTable(schema, table), cancellationToken);
                await ExecuteAsync(connection, transaction,
                    $"ALTER TABLE {SqliteIdentifier.QuoteQualified(schema, tempTable)} RENAME TO {SqliteIdentifier.Quote(table)};",
                    cancellationToken);

                if (originalSequence is not null)
                {
                    await RestoreSequenceAsync(
                        connection, transaction, table, originalSequence.Value, cancellationToken);
                }

                foreach (var index in ordinaryIndexes)
                {
                    await ExecuteAsync(connection, transaction,
                        SqliteDdlBuilder.BuildCreateIndex(schema, table, index),
                        cancellationToken);
                }

                foreach (var trigger in triggers)
                {
                    await ExecuteAsync(connection, transaction, trigger, cancellationToken);
                }

                await EnsureNoNewForeignKeyViolationsAsync(
                    connection, transaction, baselineForeignKeyViolations, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (GridletException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
            catch (SqliteException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new GridletQueryException(ex.Message, ex);
            }
        }
        finally
        {
            await SetForeignKeyEnforcementAsync(
                connection, enabled: foreignKeyEnforcementWasEnabled, CancellationToken.None);
        }
    }

    private static async Task<IReadOnlyList<string>> LoadTriggerSqlAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var sql = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM main.sqlite_schema WHERE type = 'trigger' AND tbl_name = @table AND sql IS NOT NULL;";
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) sql.Add(reader.GetString(0));
        return sql;
    }

    private static async Task EnsureTableCanBeRebuiltAsync(
        SqliteConnection connection,
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        string tableType;
        await using (var classification = connection.CreateCommand())
        {
            classification.CommandText =
                "SELECT type FROM pragma_table_list WHERE schema = 'main' AND name = @table;";
            classification.Parameters.AddWithValue("@table", table);
            await using var reader = await classification.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new GridletObjectNotFoundException($"{schema}.{table}");
            tableType = reader.GetString(0);
        }

        if (tableType is "virtual" or "shadow")
        {
            throw new GridletValidationException(
                $"Table {schema}.{table} is a SQLite {tableType} table and cannot be rebuilt by the designer.");
        }
        if (tableType != "table")
            throw new GridletValidationException($"{schema}.{table} is not an ordinary table.");

        string source;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sql FROM main.sqlite_schema WHERE type = 'table' AND name = @table;";
            command.Parameters.AddWithValue("@table", table);
            source = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? "";
        }

        var unsupported = new List<string>();
        if (SqliteSqlInspection.ContainsKeywordSequence(source, "ON", "CONFLICT")) unsupported.Add("ON CONFLICT policies");

        await using (var primaryKey = connection.CreateCommand())
        {
            primaryKey.CommandText =
                """
                SELECT 1
                FROM pragma_index_list(@table, 'main') AS il
                JOIN pragma_index_xinfo(il.name, 'main') AS ix
                WHERE il.origin = 'pk' AND ix.[key] <> 0
                  AND (ix.[desc] <> 0 OR UPPER(COALESCE(ix.coll, '')) <> 'BINARY')
                LIMIT 1;
                """;
            primaryKey.Parameters.AddWithValue("@table", table);
            if (await primaryKey.ExecuteScalarAsync(cancellationToken) is not null)
                unsupported.Add("primary-key direction or collation");
        }

        if (unsupported.Count > 0)
        {
            throw new GridletValidationException(
                $"Table {schema}.{table} uses {string.Join(", ", unsupported)} that cannot be preserved by this designer operation. Apply the change with explicit SQLite DDL instead.");
        }
    }

    private static async Task EnsureNoNewForeignKeyViolationsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<ForeignKeyViolation> baseline,
        CancellationToken cancellationToken)
    {
        var baselineCounts = baseline
            .GroupBy(violation => violation)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var violation in await LoadForeignKeyViolationsAsync(
                     connection, transaction, cancellationToken))
        {
            if (baselineCounts.TryGetValue(violation, out var count) && count > 0)
            {
                baselineCounts[violation] = count - 1;
                continue;
            }

            throw new GridletValidationException(
                $"The designer operation would leave an invalid foreign key from '{violation.ChildTable}' row {violation.RowId ?? "unknown"} to '{violation.ParentTable}'. No changes were applied.");
        }
    }

    private static async Task<IReadOnlyList<ForeignKeyViolation>> LoadForeignKeyViolationsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var violations = new List<ForeignKeyViolation>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            violations.Add(new ForeignKeyViolation(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1)),
                reader.GetString(2)));
        }

        return violations;
    }

    private sealed record ForeignKeyViolation(
        string ChildTable,
        string? RowId,
        string ParentTable);

    private static async Task SetForeignKeyEnforcementAsync(
        SqliteConnection connection,
        bool enabled,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = enabled ? "PRAGMA foreign_keys = ON;" : "PRAGMA foreign_keys = OFF;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> GetForeignKeyEnforcementAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    private static async Task<long?> LoadSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT seq FROM main.sqlite_sequence WHERE name = @table;";
        command.Parameters.AddWithValue("@table", table);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private static async Task RestoreSequenceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        long originalSequence,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE main.sqlite_sequence
            SET seq = CASE WHEN seq IS NULL OR seq < @sequence THEN @sequence ELSE seq END
            WHERE name = @table;
            """;
        update.Parameters.AddWithValue("@table", table);
        update.Parameters.AddWithValue("@sequence", originalSequence);
        if (await update.ExecuteNonQueryAsync(cancellationToken) > 0)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO main.sqlite_sequence (name, seq) VALUES (@table, @sequence);";
        insert.Parameters.AddWithValue("@table", table);
        insert.Parameters.AddWithValue("@sequence", originalSequence);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TableDefinition> RequireTableAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var definition = await SqliteSchemaReader.LoadTableDefinitionAsync(connection, table, cancellationToken);
        if (definition.Object.Type != DbObjectType.Table)
        {
            throw new GridletValidationException($"{definition.Object.Schema}.{table} is not a table.");
        }

        return definition;
    }

    private static async Task<TableDefinition> RequireOrdinaryTableAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var definition = await RequireTableAsync(connection, table, cancellationToken);
        if (definition.Object.IsInternal || definition.Object.SubKind is "virtual" or "shadow")
        {
            throw new GridletValidationException(
                $"{definition.Object.Schema}.{table} is a SQLite {definition.Object.SubKind ?? "internal"} table and cannot be changed by the designer.");
        }
        return definition;
    }

    private static ColumnInfo FindColumn(TableDefinition definition, string name)
        => definition.Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
           ?? throw new GridletValidationException(
               $"Column '{name}' does not exist on {definition.Object.Schema}.{definition.Object.Name}.");

    private static ColumnDesign ToDesign(ColumnInfo column)
        => new(
            column.Name,
            column.DataType,
            column.IsNullable,
            column.IsIdentity,
            column.IsPrimaryKey,
            column.DefaultDefinition,
            column.ComputedDefinition,
            column.IsPersisted,
            column.IdentitySeed ?? 1,
            column.IdentityIncrement ?? 1,
            column.Collation);

    private static ForeignKeyDesign[] ToForeignKeyDesigns(TableDefinition definition)
        => definition.ForeignKeys.Select(fk => new ForeignKeyDesign(
            fk.Name,
            fk.ReferencedSchema,
            fk.ReferencedTable,
            fk.Columns,
            fk.OnDelete.Replace('_', ' '),
            fk.OnUpdate.Replace('_', ' '))).ToArray();

    private static CheckConstraintDesign[] ToCheckDesigns(
        TableDefinition definition,
        IReadOnlyDictionary<string, string>? renamedColumns = null,
        IReadOnlyList<ColumnDesign>? availableColumns = null)
        => definition.CheckConstraints
            .Where(check => check.Column is null || availableColumns is null || availableColumns.Any(column =>
                string.Equals(column.Name, MapColumn(check.Column, renamedColumns), StringComparison.OrdinalIgnoreCase)))
            .Select(check => new CheckConstraintDesign(check.Name,
                RenameExpression(check.Definition, renamedColumns),
                IsDisabled: check.IsDisabled,
                IsNotForReplication: check.IsNotForReplication))
            .ToArray();

    private static UniqueConstraintDesign[] ToUniqueDesigns(
        TableDefinition definition,
        IReadOnlyDictionary<string, string>? renamedColumns = null,
        IReadOnlyList<ColumnDesign>? availableColumns = null)
        => definition.UniqueConstraints
            .Select(unique => new UniqueConstraintDesign(unique.Name,
                unique.Columns.OrderBy(key => key.Ordinal).Select(key => new IndexKeyDesign(
                    key.Column is null ? null : MapColumn(key.Column, renamedColumns),
                    key.IsDescending,
                    key.Expression is null ? null : RenameExpression(key.Expression, renamedColumns),
                    key.Collation)).ToArray(),
                unique.IsClustered, unique.FillFactor, unique.IsDisabled))
            .Where(unique => availableColumns is null || unique.Columns.All(key => key.Column is null ||
                availableColumns.Any(column => string.Equals(column.Name, key.Column, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

    private static IndexDesign[] ToIndexDesigns(
        TableDefinition definition,
        IReadOnlyDictionary<string, string>? renamedColumns,
        IReadOnlyList<ColumnDesign> availableColumns)
        => definition.Indexes.Where(index => !index.IsPrimaryKey)
            .Select(index => new IndexDesign(index.Name,
                (index.KeyColumns ?? index.Columns.Select((column, ordinal) =>
                    new IndexKeyInfo(column, ordinal + 1))).OrderBy(key => key.Ordinal).Select(key => new IndexKeyDesign(
                        key.Column is null ? null : MapColumn(key.Column, renamedColumns),
                        key.IsDescending,
                        key.Expression is null ? null : RenameExpression(key.Expression, renamedColumns),
                        key.Collation)).ToArray(),
                index.IsUnique,
                index.IncludedColumns,
                index.FilterDefinition is null ? null : RenameExpression(index.FilterDefinition, renamedColumns),
                index.IsClustered, index.IsColumnstore, index.FillFactor, index.IsDisabled))
            .Where(index => index.KeyColumns.All(key => key.Column is null || availableColumns.Any(column =>
                string.Equals(column.Name, key.Column, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

    private static string MapColumn(string name, IReadOnlyDictionary<string, string>? renamedColumns)
        => renamedColumns?.FirstOrDefault(pair =>
            string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)).Key ?? name;

    private static string RenameExpression(string expression, IReadOnlyDictionary<string, string>? renamedColumns)
    {
        // Schema SQL is trusted, but designer builders intentionally reject comments. Removing
        // comments as whitespace preserves SQLite expression semantics before normal validation.
        expression = SqliteCreateSqlParser.RemoveComments(expression);
        if (renamedColumns is null) return expression;
        foreach (var pair in renamedColumns)
            expression = SqliteCreateSqlParser.RenameIdentifier(expression, pair.Value, pair.Key);
        return expression;
    }

    private static void ValidateIndexKeys(TableDefinition definition, IReadOnlyList<IndexKeyDesign> keys)
    {
        if (keys is not { Count: > 0 }) throw new GridletValidationException("An index needs at least one key.");
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key.Column)) _ = FindColumn(definition, key.Column);
        }
    }

    private static int FindConstraint<T>(
        IReadOnlyList<T> constraints,
        ConstraintReference reference,
        Func<T, string?> name,
        Func<T, int> ordinal,
        string kind,
        string schema,
        string table)
    {
        if (string.IsNullOrWhiteSpace(reference.Name) && reference.Ordinal is null)
            throw new GridletValidationException($"A {kind} constraint name or ordinal is required.");
        for (var i = 0; i < constraints.Count; i++)
        {
            if ((!string.IsNullOrWhiteSpace(reference.Name) &&
                 string.Equals(name(constraints[i]), reference.Name, StringComparison.OrdinalIgnoreCase)) ||
                (reference.Name is null && reference.Ordinal == ordinal(constraints[i])))
                return i;
        }
        throw new GridletValidationException($"{kind} constraint '{reference.Name ?? $"#{reference.Ordinal}"}' does not exist on {schema}.{table}.");
    }

    private static async Task ExecuteAsync(
        GridletConnectionContext context,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static GridletValidationException UnsupportedSchemas()
        => new("SQLite does not support creating, owning, or dropping schemas. Use the built-in 'main' schema.");
}
