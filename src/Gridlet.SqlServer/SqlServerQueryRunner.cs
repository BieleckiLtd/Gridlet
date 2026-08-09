using System.Collections.Concurrent;
using System.Diagnostics;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerQueryRunner : IQueryRunner, IQuerySessionRunner
{
    private const int BatchSize = 100;

    async Task<System.Data.Common.DbConnection> IQuerySessionRunner.OpenSessionAsync(
        GridletConnectionContext context,
        CancellationToken cancellationToken)
        => await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);

    IAsyncEnumerable<QueryStreamEvent> IQuerySessionRunner.StreamAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        QueryRequestOptions options,
        CancellationToken cancellationToken)
        => StreamOnAsync(RequireSqlConnection(connection), sql, options, parameters: null, cancellationToken);

    async Task<TransactionStatus> IQuerySessionRunner.GetTransactionStatusAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken)
        => await ReadTransactionStatusAsync(RequireSqlConnection(connection), cancellationToken);

    async Task<TransactionStatus> IQuerySessionRunner.RunTransactionCommandAsync(
        System.Data.Common.DbConnection connection,
        TransactionCommand command,
        CancellationToken cancellationToken)
    {
        var sqlConnection = RequireSqlConnection(connection);
        var statement = command switch
        {
            TransactionCommand.Begin => "BEGIN TRANSACTION;",
            TransactionCommand.Commit => "COMMIT TRANSACTION;",
            TransactionCommand.Rollback => "ROLLBACK TRANSACTION;",
            _ => throw new GridletValidationException($"Unsupported transaction command '{command}'."),
        };

        try
        {
            await using var statementCommand = sqlConnection.CreateCommand();
            statementCommand.CommandText = statement;
            await statementCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }

        return await ReadTransactionStatusAsync(sqlConnection, cancellationToken);
    }

    private static SqlConnection RequireSqlConnection(System.Data.Common.DbConnection connection)
        => connection as SqlConnection
            ?? throw new GridletValidationException(
                "This session was not opened by the SQL Server provider.");

    /// <summary>
    /// Reads the connection's transaction state. <c>@@TRANCOUNT</c> gives the nesting depth, and
    /// <c>XACT_STATE()</c> of -1 means the transaction is doomed: it can only be rolled back.
    /// </summary>
    private static async Task<TransactionStatus> ReadTransactionStatusAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT @@TRANCOUNT, CONVERT(int, XACT_STATE());";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return TransactionStatus.None;
            }

            var depth = reader.GetInt32(0);
            var xactState = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            return new TransactionStatus(depth > 0, depth, xactState == -1);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    public async Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new GridletValidationException("Query text must not be empty.");
        }

        var stopwatch = Stopwatch.StartNew();
        var messages = new ConcurrentQueue<string>();
        var batches = SqlServerBatchSplitter.Split(sql);

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        connection.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors)
            {
                messages.Enqueue(error.Message);
            }
        };

        var resultSets = new List<QueryResultSet>();
        var recordsAffected = -1;

        try
        {
            foreach (var batch in batches)
            {
                for (var repetition = 0; repetition < batch.RepeatCount; repetition++)
                {
                    await using var command = CreateCommand(connection, batch.Sql, options, parameters);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    do
                    {
                        if (reader.FieldCount == 0)
                        {
                            continue; // Non-query statement (INSERT/UPDATE/DDL); no result set to read.
                        }

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

                    recordsAffected = MergeRecordsAffected(recordsAffected, reader.RecordsAffected);
                }
            }
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }

        stopwatch.Stop();
        return new QueryResult(resultSets, recordsAffected, messages.ToArray(), stopwatch.ElapsedMilliseconds);
    }

    public async IAsyncEnumerable<QueryStreamEvent> StreamAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new GridletValidationException("Query text must not be empty.");
        }

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        await foreach (var streamEvent in StreamOnAsync(connection, sql, options, parameters, cancellationToken))
        {
            yield return streamEvent;
        }
    }

    /// <summary>
    /// Streams a query on a connection somebody else owns. A pinned session reuses one connection
    /// across executions, so this must not open or close it.
    /// </summary>
    private static async IAsyncEnumerable<QueryStreamEvent> StreamOnAsync(
        SqlConnection connection,
        string sql,
        QueryRequestOptions options,
        IReadOnlyDictionary<string, object?>? parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new GridletValidationException("Query text must not be empty.");
        }

        var stopwatch = Stopwatch.StartNew();
        var messages = new ConcurrentQueue<string>();
        var batches = SqlServerBatchSplitter.Split(sql);
        void CollectMessages(object sender, SqlInfoMessageEventArgs e)
        {
            foreach (SqlError error in e.Errors) messages.Enqueue(error.Message);
        }

        connection.InfoMessage += CollectMessages;
        try
        {
            yield return new QueryStreamEvent("started");
            var resultSetIndex = 0;
            var recordsAffected = -1;
            foreach (var batch in batches)
            {
                for (var repetition = 0; repetition < batch.RepeatCount; repetition++)
                {
                    await using var command = CreateCommand(connection, batch.Sql, options, parameters);
                    await using var reader = await ExecuteReaderAsync(command, cancellationToken);
                    do
                    {
                        if (reader.FieldCount == 0) continue;
                        var columns = ReadColumns(reader);
                        yield return new QueryStreamEvent("resultSet", resultSetIndex, columns);

                        var rows = new List<object?[]>(BatchSize);
                        var rowCount = 0;
                        var truncated = false;
                        while (await ReadAsync(reader, cancellationToken))
                        {
                            if (options.MaxRowsPerResultSet > 0 && rowCount >= options.MaxRowsPerResultSet)
                            {
                                truncated = true;
                                break;
                            }

                            rows.Add(ReadRow(reader));
                            rowCount++;
                            if (rows.Count == BatchSize)
                            {
                                yield return new QueryStreamEvent("rows", resultSetIndex, Rows: rows.ToArray());
                                rows.Clear();
                                while (messages.TryDequeue(out var message))
                                {
                                    yield return new QueryStreamEvent("message", Message: message);
                                }
                            }
                        }

                        if (rows.Count > 0)
                        {
                            yield return new QueryStreamEvent("rows", resultSetIndex, Rows: rows.ToArray());
                        }

                        yield return new QueryStreamEvent("resultSetCompleted", resultSetIndex, Truncated: truncated);
                        resultSetIndex++;
                    }
                    while (await NextResultAsync(reader, cancellationToken));

                    recordsAffected = MergeRecordsAffected(recordsAffected, reader.RecordsAffected);
                    while (messages.TryDequeue(out var message))
                    {
                        yield return new QueryStreamEvent("message", Message: message);
                    }
                }
            }

            while (messages.TryDequeue(out var message)) yield return new QueryStreamEvent("message", Message: message);
            stopwatch.Stop();
            yield return new QueryStreamEvent(
                "completed", RecordsAffected: recordsAffected, DurationMs: stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            connection.InfoMessage -= CollectMessages;
        }
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
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

    private static ResultColumn[] ReadColumns(SqlDataReader reader)
    {
        try
        {
            return Enumerable.Range(0, reader.FieldCount)
                .Select(i => new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)))
                .ToArray();
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static int MergeRecordsAffected(int accumulated, int current)
    {
        if (current < 0) return accumulated;
        return accumulated < 0 ? current : accumulated + current;
    }

    private static async Task<SqlDataReader> ExecuteReaderAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<bool> ReadAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static async Task<bool> NextResultAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.NextResultAsync(cancellationToken);
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }

    private static object?[] ReadRow(SqlDataReader reader)
    {
        try
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = SqlServerValues.Materialize(reader.GetValue(i));
            }

            return row;
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }
    }
}
