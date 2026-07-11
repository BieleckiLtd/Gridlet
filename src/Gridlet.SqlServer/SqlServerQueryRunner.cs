using System.Diagnostics;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerQueryRunner : IQueryRunner
{
    private const int BatchSize = 100;

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
        var messages = new List<string>();

        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        connection.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors)
            {
                messages.Add(error.Message);
            }
        };

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name.StartsWith('@') ? name : "@" + name, value ?? DBNull.Value);
            }
        }

        var resultSets = new List<QueryResultSet>();
        int recordsAffected;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            do
            {
                if (reader.FieldCount == 0)
                {
                    continue; // Non-query statement (INSERT/UPDATE/DDL) — no result set to read.
                }

                var columns = new ResultColumn[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    columns[i] = new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i));
                }

                var rows = new List<object?[]>();
                var truncated = false;
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (options.MaxRowsPerResultSet > 0 && rows.Count >= options.MaxRowsPerResultSet)
                    {
                        truncated = true;
                        break;
                    }

                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = SqlServerValues.Materialize(reader.GetValue(i));
                    }

                    rows.Add(row);
                }

                resultSets.Add(new QueryResultSet(columns, rows, truncated));
            }
            while (await reader.NextResultAsync(cancellationToken));

            recordsAffected = reader.RecordsAffected;
        }
        catch (SqlException ex)
        {
            throw new GridletQueryException(ex.Message, ex);
        }

        stopwatch.Stop();
        return new QueryResult(resultSets, recordsAffected, messages, stopwatch.ElapsedMilliseconds);
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

        var stopwatch = Stopwatch.StartNew();
        var messages = new Queue<string>();
        await using var connection = await SqlServerConnectionFactory.OpenAsync(context, cancellationToken);
        connection.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors) messages.Enqueue(error.Message);
        };

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        if (parameters is not null)
        {
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name.StartsWith('@') ? name : "@" + name, value ?? DBNull.Value);
        }

        yield return new QueryStreamEvent("started");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var resultSetIndex = 0;
        do
        {
            if (reader.FieldCount == 0) continue;
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new ResultColumn(reader.GetName(i), reader.GetDataTypeName(i))).ToArray();
            yield return new QueryStreamEvent("resultSet", resultSetIndex, columns);

            var batch = new List<object?[]>(BatchSize);
            var rowCount = 0;
            var truncated = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (options.MaxRowsPerResultSet > 0 && rowCount >= options.MaxRowsPerResultSet) { truncated = true; break; }
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) row[i] = SqlServerValues.Materialize(reader.GetValue(i));
                batch.Add(row);
                rowCount++;
                if (batch.Count == BatchSize)
                {
                    yield return new QueryStreamEvent("rows", resultSetIndex, Rows: batch.ToArray());
                    batch.Clear();
                    while (messages.TryDequeue(out var message)) yield return new QueryStreamEvent("message", Message: message);
                }
            }
            if (batch.Count > 0) yield return new QueryStreamEvent("rows", resultSetIndex, Rows: batch.ToArray());
            yield return new QueryStreamEvent("resultSetCompleted", resultSetIndex, Truncated: truncated);
            resultSetIndex++;
        }
        while (await reader.NextResultAsync(cancellationToken));

        while (messages.TryDequeue(out var message)) yield return new QueryStreamEvent("message", Message: message);
        stopwatch.Stop();
        yield return new QueryStreamEvent("completed", RecordsAffected: reader.RecordsAffected, DurationMs: stopwatch.ElapsedMilliseconds);
    }
}
