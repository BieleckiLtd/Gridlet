using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerTableDdlService : ITableDdlService
{
    public Task CreateSchemaAsync(
        GridletConnectionContext context, SchemaDesign design, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildCreateSchema(design), cancellationToken);

    public Task AlterSchemaOwnerAsync(
        GridletConnectionContext context, string schema, string owner, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildAlterSchemaOwner(schema, owner), cancellationToken);

    public Task DropSchemaAsync(
        GridletConnectionContext context, string schema, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropSchema(schema), cancellationToken);

    public async Task CreateTableAsync(
        GridletConnectionContext context, TableDesign design, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using (var createSchema = connection.CreateCommand())
            {
                createSchema.Transaction = (SqlTransaction)transaction;
                createSchema.CommandText = SqlServerDdlBuilder.BuildCreateSchemaIfMissing(design.Schema);
                createSchema.Parameters.AddWithValue("@schema", design.Schema);
                await createSchema.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var createTable = connection.CreateCommand())
            {
                createTable.Transaction = (SqlTransaction)transaction;
                createTable.CommandText = SqlServerDdlBuilder.BuildCreateTable(design);
                await createTable.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new GridletQueryException(
                ex.Number == 2760
                    ? $"Schema '{design.Schema}' could not be created. Grant the configured SQL principal CREATE SCHEMA (or ALTER ANY SCHEMA) permission, then try again. SQL Server: {ex.Message}"
                    : ex.Message,
                ex);
        }
    }

    public Task AddColumnAsync(
        GridletConnectionContext context, string schema, string table, ColumnDesign column,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildAddColumn(schema, table, column), cancellationToken);

    public async Task AlterColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName, ColumnDesign column,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            bool isIdentity;
            bool isComputed;
            bool isPersisted;
            string? computedDefinition;
            string? defaultConstraint;
            long? identitySeed;
            long? identityIncrement;
            string currentDataType;
            bool currentIsNullable;
            await using (var inspect = connection.CreateCommand())
            {
                inspect.Transaction = transaction;
                inspect.CommandText =
                    """
                    SELECT c.is_identity, c.is_computed, cc.definition, dc.name,
                           CONVERT(bigint, ic.seed_value), CONVERT(bigint, ic.increment_value)
                           , ISNULL(cc.is_persisted, 0), t.name, c.max_length, c.precision, c.scale, c.is_nullable
                    FROM sys.columns c
                    JOIN sys.types t ON t.user_type_id = c.user_type_id
                    LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
                    LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                    LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    WHERE c.object_id = OBJECT_ID(@table) AND c.name = @column;
                    """;
                inspect.Parameters.AddWithValue("@table", SqlServerIdentifier.QuoteQualified(schema, table));
                inspect.Parameters.AddWithValue("@column", columnName);
                await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new GridletObjectNotFoundException($"{schema}.{table}.{columnName}");
                }

                isIdentity = reader.GetBoolean(0);
                isComputed = reader.GetBoolean(1);
                computedDefinition = reader.IsDBNull(2) ? null : reader.GetString(2);
                defaultConstraint = reader.IsDBNull(3) ? null : reader.GetString(3);
                identitySeed = reader.IsDBNull(4) ? null : reader.GetInt64(4);
                identityIncrement = reader.IsDBNull(5) ? null : reader.GetInt64(5);
                isPersisted = reader.GetBoolean(6);
                currentDataType = SqlServerDataTypeFormatter.Format(
                    reader.GetString(7), reader.GetInt16(8), reader.GetByte(9), reader.GetByte(10));
                currentIsNullable = reader.GetBoolean(11);
            }

            if (isIdentity != column.IsIdentity ||
                (isIdentity && (identitySeed != column.IdentitySeed || identityIncrement != column.IdentityIncrement)))
            {
                throw new GridletValidationException(
                    "SQL Server cannot change the identity property, seed, or increment on an existing column. Create a replacement column or rebuild the table instead.");
            }

            var wantsComputed = !string.IsNullOrWhiteSpace(column.ComputedExpression);
            if (isComputed || wantsComputed)
            {
                var sameExpression = isComputed && wantsComputed &&
                    NormalizeExpression(computedDefinition) == NormalizeExpression(column.ComputedExpression) &&
                    isPersisted == column.IsPersisted;
                if (!sameExpression)
                {
                    // SQL Server has no ALTER COLUMN syntax for computed expressions. Recreating the
                    // column is transactional; dependencies cause the operation to fail and roll back.
                    await ExecuteAsync(connection, transaction,
                        SqlServerDdlBuilder.BuildDropColumn(schema, table, columnName), cancellationToken);
                    await ExecuteAsync(connection, transaction,
                        SqlServerDdlBuilder.BuildAddColumn(schema, table, column), cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }
            }

            if (!string.Equals(columnName, column.Name, StringComparison.Ordinal))
            {
                // sp_rename takes the names as string parameters; no dynamic SQL involved.
                await using var rename = connection.CreateCommand();
                rename.Transaction = transaction;
                rename.CommandText = "EXEC sp_rename @objname, @newname, 'COLUMN';";
                rename.Parameters.AddWithValue("@objname",
                    $"{SqlServerIdentifier.QuoteQualified(schema, table)}.{SqlServerIdentifier.Quote(columnName)}");
                rename.Parameters.AddWithValue("@newname", column.Name);
                await rename.ExecuteNonQueryAsync(cancellationToken);
            }

            // An empty data type means rename-only. Otherwise defaults are managed
            // independently so changing one does not needlessly ALTER the column and
            // collide with computed columns or other dependencies.
            if (!string.IsNullOrWhiteSpace(column.DataType))
            {
                if (defaultConstraint is not null)
                {
                    await ExecuteAsync(connection, transaction,
                        SqlServerDdlBuilder.BuildDropConstraint(schema, table, defaultConstraint), cancellationToken);
                }

                var normalizedType = SqlServerDdlBuilder.NormalizeDataType(column.DataType);
                if (!string.Equals(normalizedType, currentDataType, StringComparison.OrdinalIgnoreCase) ||
                    column.IsNullable != currentIsNullable)
                {
                    await using var alter = connection.CreateCommand();
                    alter.Transaction = transaction;
                    alter.CommandText = SqlServerDdlBuilder.BuildAlterColumn(schema, table, column);
                    await alter.ExecuteNonQueryAsync(cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(column.DefaultExpression))
                {
                    await ExecuteAsync(connection, transaction,
                        SqlServerDdlBuilder.BuildAddDefault(schema, table, column.Name, column.DefaultExpression), cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (GridletException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqlException ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    public Task DropColumnAsync(
        GridletConnectionContext context, string schema, string table, string columnName,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropColumn(schema, table, columnName), cancellationToken);

    public Task AddPrimaryKeyAsync(
        GridletConnectionContext context, string schema, string table, PrimaryKeyDesign primaryKey,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildAddPrimaryKey(schema, table, primaryKey), cancellationToken);

    public Task AddCheckConstraintAsync(
        GridletConnectionContext context, string schema, string table, CheckConstraintDesign checkConstraint,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context,
            SqlServerDdlBuilder.BuildAddCheckConstraint(schema, table, checkConstraint), cancellationToken);

    public Task DropCheckConstraintAsync(
        GridletConnectionContext context, string schema, string table, ConstraintReference constraint,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context,
            SqlServerDdlBuilder.BuildDropCheckConstraint(schema, table, constraint), cancellationToken);

    public Task AddUniqueConstraintAsync(
        GridletConnectionContext context, string schema, string table, UniqueConstraintDesign uniqueConstraint,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context,
            SqlServerDdlBuilder.BuildAddUniqueConstraint(schema, table, uniqueConstraint), cancellationToken);

    public Task DropUniqueConstraintAsync(
        GridletConnectionContext context, string schema, string table, ConstraintReference constraint,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context,
            SqlServerDdlBuilder.BuildDropUniqueConstraint(schema, table, constraint), cancellationToken);

    public Task CreateIndexAsync(
        GridletConnectionContext context, string schema, string table, IndexDesign index,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildCreateIndex(schema, table, index), cancellationToken);

    public Task DropIndexAsync(
        GridletConnectionContext context, string schema, string table, string indexName,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropIndex(schema, table, indexName), cancellationToken);

    public Task AddForeignKeyAsync(
        GridletConnectionContext context, string schema, string table, ForeignKeyDesign foreignKey,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildAddForeignKey(schema, table, foreignKey), cancellationToken);

    public Task DropConstraintAsync(
        GridletConnectionContext context, string schema, string table, string constraintName,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropConstraint(schema, table, constraintName), cancellationToken);

    public Task DropTableAsync(
        GridletConnectionContext context, string schema, string table, CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropTable(schema, table), cancellationToken);

    public Task DropObjectAsync(
        GridletConnectionContext context, string schema, string name, DbObjectType type,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(context, SqlServerDdlBuilder.BuildDropObject(schema, name, type), cancellationToken);

    public string BuildDropScript(DbObjectInfo @object)
        => SqlServerDdlBuilder.BuildDropObject(@object.Schema, @object.Name, @object.Type);

    public string BuildInsertScript(
        TableDefinition table,
        IReadOnlyList<ResultColumn> columns,
        IReadOnlyList<object?[]> rows)
        => SqlServerInsertScriptBuilder.Build(table, columns, rows);

    public Task RenameObjectAsync(
        GridletConnectionContext context, string schema, string name, DbObjectType type, string newName,
        CancellationToken cancellationToken = default)
        // sp_rename takes both names as parameters, so neither reaches dynamic SQL. The new name is
        // unqualified by definition: sp_rename cannot move an object between schemas.
        => RenameAsync(
            context,
            SqlServerIdentifier.QuoteQualified(schema, name),
            RequireUnqualified(newName, "object"),
            "OBJECT",
            cancellationToken);

    public Task RenameIndexAsync(
        GridletConnectionContext context, string schema, string table, string indexName, string newName,
        CancellationToken cancellationToken = default)
        => RenameAsync(
            context,
            $"{SqlServerIdentifier.QuoteQualified(schema, table)}.{SqlServerIdentifier.Quote(indexName)}",
            RequireUnqualified(newName, "index"),
            "INDEX",
            cancellationToken);

    /// <summary>
    /// Empties a table with TRUNCATE, which SQL Server refuses when the table is referenced by a
    /// foreign key. That refusal is reported rather than worked around: a DELETE in its place would
    /// quietly do something slower, log more, and fire triggers the caller did not ask for.
    /// </summary>
    public Task TruncateTableAsync(
        GridletConnectionContext context, string schema, string table,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            context,
            $"TRUNCATE TABLE {SqlServerIdentifier.QuoteQualified(schema, table)};",
            cancellationToken);

    private static string RequireUnqualified(string newName, string kind)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new GridletValidationException($"The new {kind} name must not be empty.");
        }

        if (newName.Contains('.', StringComparison.Ordinal))
        {
            throw new GridletValidationException(
                $"A rename changes the name only, so give the new {kind} name without a schema. " +
                "Moving between schemas is a different operation.");
        }

        return newName;
    }

    private static async Task RenameAsync(
        GridletConnectionContext context,
        string currentName,
        string newName,
        string objectType,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_rename @objname, @newname, @objtype;";
        command.Parameters.AddWithValue("@objname", currentName);
        command.Parameters.AddWithValue("@newname", newName);
        command.Parameters.AddWithValue("@objtype", objectType);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task ExecuteAsync(
        GridletConnectionContext context, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task ExecuteAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeExpression(string? expression)
    {
        var value = expression?.Trim() ?? "";
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')')
        {
            value = value[1..^1].Trim();
        }
        return value;
    }
}
