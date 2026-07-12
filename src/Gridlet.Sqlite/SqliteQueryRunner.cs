using System.Diagnostics;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.Sqlite;

namespace Gridlet.Sqlite;

public sealed class SqliteQueryRunner : IQueryRunner
{
    private const int BatchSize = 100;

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
        var stopwatch = Stopwatch.StartNew();
        await using var connection = await SqliteConnectionFactory.OpenAsync(context, cancellationToken);
        await using var command = CreateCommand(connection, sql, options, parameters);

        yield return new QueryStreamEvent("started");
        System.Data.Common.DbDataReader reader;
        try
        {
            reader = await command.ExecuteReaderAsync(cancellationToken);
        }
        catch (SqliteException ex)
        {
            await RollbackActiveTransactionAsync(connection);
            throw new GridletQueryException(ex.Message, ex);
        }

        await using (reader)
        {
            var resultSetIndex = 0;
            do
            {
                if (reader.FieldCount == 0) continue;
                yield return new QueryStreamEvent("resultSet", resultSetIndex, ReadColumns(reader));
                var batch = new List<object?[]>(BatchSize);
                var rowCount = 0;
                var truncated = false;
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (options.MaxRowsPerResultSet > 0 && rowCount >= options.MaxRowsPerResultSet)
                    {
                        truncated = true;
                        break;
                    }

                    batch.Add(ReadRow(reader));
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
            while (await reader.NextResultAsync(cancellationToken));

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
            .Select(i => new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i)))
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

    private static void ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new GridletValidationException("Query text must not be empty.");
        }
    }

    private static async Task RollbackActiveTransactionAsync(SqliteConnection connection)
    {
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
