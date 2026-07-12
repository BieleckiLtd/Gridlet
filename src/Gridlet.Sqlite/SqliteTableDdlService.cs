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

    public Task AddColumnAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        ColumnDesign column,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqliteDdlBuilder.BuildAddColumn(schema, table, column), cancellationToken);

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
            string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase)
                ? replacement
                : ToDesign(c)).ToArray();
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

    public Task DropTableAsync(
        GridletConnectionContext context,
        string schema,
        string table,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqliteDdlBuilder.BuildDropTable(schema, table), cancellationToken);

    public Task DropObjectAsync(
        GridletConnectionContext context,
        string schema,
        string name,
        DbObjectType type,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqliteDdlBuilder.BuildDropObject(schema, name, type), cancellationToken);

    private static async Task RebuildTableAsync(
        SqliteConnection connection,
        TableDefinition definition,
        IReadOnlyList<ColumnDesign> columns,
        IReadOnlyList<ForeignKeyDesign> foreignKeys,
        IReadOnlyDictionary<string, string>? renamedColumns,
        CancellationToken cancellationToken,
        string? primaryKeyName = null)
    {
        var table = definition.Object.Name;
        var schema = definition.Object.Schema;
        var tempTable = $"__gridlet_{table}_{Guid.NewGuid():N}";
        var keyName = primaryKeyName ?? definition.Indexes.FirstOrDefault(i => i.IsPrimaryKey)?.Name;
        var tempDesign = new TableDesign(schema, tempTable, columns);
        var createSql = SqliteDdlBuilder.BuildCreateTable(tempDesign, keyName, foreignKeys);

        var partialIndexes = await LoadPartialIndexNamesAsync(connection, table, cancellationToken);
        var indexes = new List<IndexInfo>();
        var generatedIndexNumber = 0;
        foreach (var index in definition.Indexes.Where(i => !i.IsPrimaryKey))
        {
            if (index.Columns.Count == 0 || partialIndexes.Contains(index.Name))
            {
                throw new GridletValidationException(
                    $"Table {schema}.{table} has an expression or partial index ('{index.Name}') that cannot be preserved by this designer operation. Drop or recreate that index explicitly in the query editor.");
            }

            var mappedColumns = index.Columns
                .Select(c => renamedColumns?.FirstOrDefault(pair =>
                    string.Equals(pair.Value, c, StringComparison.OrdinalIgnoreCase)).Key ?? c)
                .Where(c => columns.Any(column => string.Equals(column.Name, c, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (mappedColumns.Length == 0) continue;
            var indexName = index.Name.StartsWith("sqlite_autoindex_", StringComparison.OrdinalIgnoreCase)
                ? $"UX_{table}_Gridlet{++generatedIndexNumber}"
                : index.Name;
            indexes.Add(index with { Name = indexName, Columns = mappedColumns });
        }
        var triggers = await LoadTriggerSqlAsync(connection, table, cancellationToken);
        if (triggers.Count > 0 && renamedColumns is { Count: > 0 })
        {
            throw new GridletValidationException(
                $"Table {schema}.{table} has triggers. Rename the column with explicit SQLite DDL so trigger references can be reviewed.");
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, transaction, "PRAGMA defer_foreign_keys = ON;", cancellationToken);
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

            foreach (var index in indexes)
            {
                await ExecuteAsync(connection, transaction,
                    SqliteDdlBuilder.BuildCreateIndex(schema, table, index.Name, index.IsUnique, index.Columns),
                    cancellationToken);
            }

            foreach (var trigger in triggers)
            {
                await ExecuteAsync(connection, transaction, trigger, cancellationToken);
            }

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

    private static async Task<HashSet<string>> LoadPartialIndexNamesAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_index_list(@table, 'main') WHERE partial <> 0;";
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
        return names;
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
            column.IdentityIncrement ?? 1);

    private static ForeignKeyDesign[] ToForeignKeyDesigns(TableDefinition definition)
        => definition.ForeignKeys.Select(fk => new ForeignKeyDesign(
            fk.Name,
            fk.ReferencedSchema,
            fk.ReferencedTable,
            fk.Columns,
            fk.OnDelete.Replace('_', ' '),
            fk.OnUpdate.Replace('_', ' '))).ToArray();

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
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static GridletValidationException UnsupportedSchemas()
        => new("SQLite does not support creating, owning, or dropping schemas. Use the built-in 'main' schema.");
}
