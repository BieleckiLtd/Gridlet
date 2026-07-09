using System.Diagnostics;
using Gridlet.Abstractions;
using Gridlet.Models;
using Microsoft.Data.SqlClient;

namespace Gridlet.SqlServer;

public sealed class SqlServerQueryRunner : IQueryRunner
{
    public async Task<QueryResult> ExecuteAsync(
        GridletConnectionContext context,
        string sql,
        QueryRequestOptions options,
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
                    if (rows.Count >= options.MaxRowsPerResultSet)
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
}
