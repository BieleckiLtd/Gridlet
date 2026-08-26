using System.Diagnostics;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

public sealed class SqliteQueryRunner : IQueryRunner, IQuerySessionRunner, IQueryPlanRunner
{
    private const int BatchSize = 100;

    /// <summary>
    /// Returns SQLite's query plan. SQLite has no actual plan: EXPLAIN QUERY PLAN describes what the
    /// planner chose without running the statement, and asking for an actual plan returns the same
    /// thing rather than running a statement to learn nothing more.
    /// </summary>
    public async Task<QueryPlan> GetPlanAsync(
        GridletConnectionContext context,
        string sql,
        QueryPlanMode mode,
        QueryRequestOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateSql(sql);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        var rows = new List<(long Id, long Parent, string Detail)>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(3)));
            }
        }
        catch (SqliteException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }

        var messages = mode == QueryPlanMode.Actual
            ? new[] { "SQLite has no actual execution plan; this is the plan the query planner chose." }
            : Array.Empty<string>();
        return new QueryPlan(
            QueryPlanMode.Estimated,
            "sqlite-query-plan",
            BuildPlanTree(rows),
            rows.Count == 0 ? null : string.Join("\n", rows.Select(row => $"{row.Id}|{row.Parent}|{row.Detail}")),
            messages);
    }

    /// <summary>
    /// EXPLAIN QUERY PLAN returns a flat list whose parent column describes the tree; only the
    /// detail text carries meaning, so it becomes both the operation and its description.
    /// </summary>
    private static IReadOnlyList<QueryPlanNode> BuildPlanTree(
        IReadOnlyList<(long Id, long Parent, string Detail)> rows)
    {
        var childrenByParent = rows
            .GroupBy(row => row.Parent)
            .ToDictionary(group => group.Key, group => group.ToArray());

        QueryPlanNode Build((long Id, long Parent, string Detail) row)
        {
            var children = childrenByParent.TryGetValue(row.Id, out var found)
                ? found.Select(Build).ToArray()
                : [];
            var separator = row.Detail.IndexOf(' ');
            return new QueryPlanNode(
                Operation: separator < 0 ? row.Detail : row.Detail[..separator],
                Detail: separator < 0 ? null : row.Detail[(separator + 1)..],
                Children: children);
        }

        return childrenByParent.TryGetValue(0, out var roots)
            ? roots.Select(Build).ToArray()
            : [];
    }

    async Task<System.Data.Common.DbConnection> IQuerySessionRunner.OpenSessionAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken)
        => await SqliteConnectionFactory.OpenAsync(context, cancellationToken);

    IAsyncEnumerable<QueryStreamEvent> IQuerySessionRunner.StreamAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        QueryRequestOptions options,
        CancellationToken cancellationToken)
        => StreamOnAsync(
            RequireSqliteConnection(connection), sql, options, parameters: null,
            rollbackOnError: false, cancellationToken);

    Task<TransactionStatus> IQuerySessionRunner.GetTransactionStatusAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
        => Task.FromResult(ReadTransactionStatus(RequireSqliteConnection(connection)));

    async Task<TransactionStatus> IQuerySessionRunner.RunTransactionCommandAsync(
        System.Data.Common.DbConnection connection,
        TransactionCommand command,
        CancellationToken cancellationToken)
    {
        var sqliteConnection = RequireSqliteConnection(connection);
        var statement = command switch
        {
            TransactionCommand.Begin => "BEGIN;",
            TransactionCommand.Commit => "COMMIT;",
            TransactionCommand.Rollback => "ROLLBACK;",
            _ => throw new GridletValidationException($"Unsupported transaction command '{command}'."),
        };

        try
        {
            await using var statementCommand = sqliteConnection.CreateCommand();
            statementCommand.CommandText = statement;
            await statementCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }

        return ReadTransactionStatus(sqliteConnection);
    }

    private static SqliteConnection RequireSqliteConnection(System.Data.Common.DbConnection connection)
        => connection as SqliteConnection
            ?? throw new GridletValidationException("This session was not opened by the SQLite provider.");

    /// <summary>
    /// Reads the transaction state straight from SQLite. The autocommit flag is authoritative: it is
    /// set whether the transaction was started by a typed <c>BEGIN</c> or by the session's own
    /// command, and SQLite clears it when it rolls a statement back on its own.
    /// </summary>
    private static TransactionStatus ReadTransactionStatus(SqliteConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            return TransactionStatus.None;
        }

        var inTransaction = SQLitePCL.raw.sqlite3_get_autocommit(connection.Handle) == 0;
        return inTransaction ? new TransactionStatus(true, 1) : TransactionStatus.None;
    }

    public async Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ValidateSql(sql);
        var stopwatch = Stopwatch.StartNew();
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = CreateCommand(connection, sql, options, parameters);
        var resultSets = new List<QueryResultSet>();
        int recordsAffected;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                if (reader.FieldCount == 0) continue;
                var columns = ReadColumns(reader);
                var rows = new List<object?[]>();
                var truncated = false;
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (options.MaxRowsPerResultSet > 0 && rows.Count >= options.MaxRowsPerResultSet)
                    {
                        truncated = true;
                        break;
                    }

                    rows.Add(ReadRow(reader));
                }

                resultSets.Add(new QueryResultSet(columns, rows, truncated));
            }
            while (await reader.NextResultAsync(cancellationToken));

            recordsAffected = reader.RecordsAffected;
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }

        stopwatch.Stop();
        return new QueryResult(resultSets, recordsAffected, [], stopwatch.ElapsedMilliseconds);
    }

    public async IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSql(sql);
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await foreach (var streamEvent in StreamOnAsync(
            connection, sql, options, parameters, rollbackOnError: true, cancellationToken))
        {
            yield return streamEvent;
        }
    }

    /// <summary>
    /// Streams a query on a connection somebody else owns.
    /// </summary>
    /// <param name="rollbackOnError">
    /// Whether a failed statement should roll back whatever transaction is open. That is right for a
    /// connection about to be closed, where an abandoned transaction would be rolled back anyway,
    /// but wrong for a pinned session: there the transaction is the person's, and only they decide
    /// whether a failed statement ends it.
    /// </param>
    private static async IAsyncEnumerable<QueryStreamEvent> StreamOnAsync(
        SqliteConnection connection,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters,
        bool rollbackOnError,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateSql(sql);
        var stopwatch = Stopwatch.StartNew();
        var recovery = rollbackOnError ? connection : null;
        await using var command = CreateCommand(connection, sql, options, parameters);

        yield return new QueryStreamEvent("started");
        var reader = await ExecuteReaderAsync(command, recovery, cancellationToken);

        await using (reader)
        {
            var resultSetIndex = 0;
            do
            {
                if (reader.FieldCount == 0) continue;
                yield return new QueryStreamEvent(
                    "resultSet", resultSetIndex, await ReadColumnsAsync(reader, recovery));
                var batch = new List<object?[]>(BatchSize);
                var rowCount = 0;
                var truncated = false;
                while (await ReadAsync(reader, recovery, cancellationToken))
                {
                    if (options.MaxRowsPerResultSet > 0 && rowCount >= options.MaxRowsPerResultSet)
                    {
                        truncated = true;
                        break;
                    }

                    batch.Add(await ReadRowAsync(reader, recovery));
                    rowCount++;
                    if (batch.Count == BatchSize)
                    {
                        yield return new QueryStreamEvent("rows", resultSetIndex, Rows: batch.ToArray());
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                {
                    yield return new QueryStreamEvent("rows", resultSetIndex, Rows: batch.ToArray());
                }

                yield return new QueryStreamEvent("resultSetCompleted", resultSetIndex, Truncated: truncated);
                resultSetIndex++;
            }
            while (await NextResultAsync(reader, recovery, cancellationToken));

            stopwatch.Stop();
            yield return new QueryStreamEvent(
                "completed", RecordsAffected: reader.RecordsAffected, DurationMs: stopwatch.ElapsedMilliseconds);
        }
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name.StartsWith('@') ? name : "@" + name, value ?? DBNull.Value);
            }
        }

        return command;
    }

    private static ResultColumn[] ReadColumns(System.Data.Common.DbDataReader reader)
        => Enumerable.Range(0, reader.FieldCount)
            .Select(i => new ResultColumn(
                reader.GetName(i), reader.GetDataTypeName(i), reader.GetFieldType(i) == typeof(byte[])))
            .ToArray();

    private static object?[] ReadRow(System.Data.Common.DbDataReader reader)
    {
        var row = new object?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[i] = SqliteValues.Materialize(reader.GetValue(i));
        }

        return row;
    }

    private static async Task<System.Data.Common.DbDataReader> ExecuteReaderAsync(
        SqliteCommand command,
        SqliteConnection? connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<bool> ReadAsync(
        System.Data.Common.DbDataReader reader,
        SqliteConnection? connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<bool> NextResultAsync(
        System.Data.Common.DbDataReader reader,
        SqliteConnection? connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.NextResultAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<ResultColumn[]> ReadColumnsAsync(
        System.Data.Common.DbDataReader reader,
        SqliteConnection? connection)
    {
        try
        {
            return ReadColumns(reader);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<object?[]> ReadRowAsync(
        System.Data.Common.DbDataReader reader,
        SqliteConnection? connection)
    {
        try
        {
            return ReadRow(reader);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static void ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new GridletValidationException("Query text must not be empty.");
        }
    }

    /// <summary>
    /// Rolls back whatever transaction is open after a failed statement. A <see langword="null"/>
    /// connection means the caller owns the transaction and does not want it touched.
    /// </summary>
    private static async Task RollbackActiveTransactionAsync(SqliteConnection? connection)
    {
        if (connection is null) return;
        try
        {
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            await rollback.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqliteException)
        {
            // No transaction was active, or SQLite had already rolled it back.
        }
    }
}
