using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Gridlet.Abstractions;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.Extensions.AI;

namespace Gridlet.AgentFramework;

internal sealed class GridletDatabaseAgentTools(
    ResolvedConnection resolved,
    string? userName,
    GridletAgentFrameworkSettings settings,
    IGridletAuditSink auditSink,
    Action<string, string>? toolResultObserver = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IList<AITool> Create(GridletAgentMode mode)
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(ListSchemasAsync, name: "list_schemas"),
            AIFunctionFactory.Create(ListObjectsAsync, name: "list_database_objects"),
            AIFunctionFactory.Create(DescribeTableAsync, name: "describe_table"),
            AIFunctionFactory.Create(GetObjectDefinitionAsync, name: "get_object_definition"),
        ];
        if (mode == GridletAgentMode.Data)
        {
            tools.Add(AIFunctionFactory.Create(ExecuteReadOnlyQueryAsync, name: "execute_read_only_query"));
        }
        return tools;
    }

    [Description("List the schemas in the selected database. Returns schema names and owners.")]
    private Task<string> ListSchemasAsync(CancellationToken cancellationToken)
        => ExecuteAuditedAsync(
            "agent.tool.list_schemas",
            "list_schemas",
            objectName: null,
            token => resolved.Provider.Schema.GetSchemasAsync(resolved.Context, token),
            cancellationToken);

    [Description("List tables, views, stored procedures, functions, and triggers in the selected database.")]
    private Task<string> ListObjectsAsync(CancellationToken cancellationToken)
        => ExecuteAuditedAsync(
            "agent.tool.list_objects",
            "list_database_objects",
            objectName: null,
            token => resolved.Provider.Schema.GetObjectsAsync(resolved.Context, token),
            cancellationToken);

    [Description("Describe the columns, indexes, primary key, and foreign keys of one table or view.")]
    private Task<string> DescribeTableAsync(
        [Description("The object's database schema.")] string schema,
        [Description("The table or view name.")] string name,
        CancellationToken cancellationToken)
    {
        ValidateObjectName(schema, name);
        return ExecuteAuditedAsync(
            "agent.tool.describe_table",
            "describe_table",
            AuditObjectName(schema, name),
            token => resolved.Provider.Schema.GetTableDefinitionAsync(
                resolved.Context, schema, name, token),
            cancellationToken);
    }

    [Description("Get the source definition of a view, stored procedure, function, or trigger when available.")]
    private Task<string> GetObjectDefinitionAsync(
        [Description("The object's database schema.")] string schema,
        [Description("The database object name.")] string name,
        CancellationToken cancellationToken)
    {
        ValidateObjectName(schema, name);
        return ExecuteAuditedAsync(
            "agent.tool.object_definition",
            "get_object_definition",
            AuditObjectName(schema, name),
            async token =>
            {
                var definition = await resolved.Provider.Schema.GetObjectDefinitionAsync(
                    resolved.Context, schema, name, token);
                return new
                {
                    schema,
                    name,
                    definition = LimitCellString(definition),
                };
            },
            cancellationToken);
    }

    [Description("Execute exactly one bounded read-only SELECT or WITH ... SELECT query against the selected database. Mutation, DDL, SELECT INTO, and multiple statements are rejected.")]
    private async Task<string> ExecuteReadOnlyQueryAsync(
        [Description("One read-only SELECT or WITH ... SELECT SQL statement.")] string sql,
        CancellationToken cancellationToken)
    {
        if (sql is null || sql.Length > settings.MaxQueryCharacters)
        {
            return await CreateAuditedRecoverableToolErrorAsync(
                "invalid_sql",
                $"Agent SQL must contain at most {settings.MaxQueryCharacters:N0} characters.",
                cancellationToken);
        }

        if (!GridletReadOnlySqlGuard.TryValidate(sql, out var guardError))
        {
            return await CreateAuditedRecoverableToolErrorAsync(
                "invalid_sql", guardError, cancellationToken);
        }

        return await ExecuteAuditedAsync(
            "agent.tool.read_query",
            "execute_read_only_query",
            objectName: null,
            async token =>
            {
                var result = await resolved.Provider.Query.ExecuteAsync(
                    CreateDataQueryContext(resolved.Context),
                    sql,
                    new QueryRequestOptions(settings.MaxQueryRows, settings.QueryTimeoutSeconds),
                    parameters: null,
                    token);

                return new
                {
                    resultSets = result.ResultSets.Select(set => new
                    {
                        columns = set.Columns,
                        rows = set.Rows.Select(row => row.Select(NormalizeCellValue).ToArray()).ToArray(),
                        set.Truncated,
                    }).ToArray(),
                    result.DurationMs,
                };
            },
            cancellationToken);
    }

    private async Task<string> CreateAuditedRecoverableToolErrorAsync(
        string code,
        string? message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serialized = SerializeBounded(new
        {
            error = new
            {
                code,
                message = message ?? "The query could not be executed.",
                recoverable = true,
            },
        });
        toolResultObserver?.Invoke("execute_read_only_query", serialized);
        await WriteAuditAsync(
            "agent.tool.read_query",
            objectName: null,
            succeeded: false,
            durationMs: 0,
            code);
        return serialized;
    }

    private async Task<string> ExecuteAuditedAsync<T>(
        string actionName,
        string toolName,
        string? objectName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await operation(cancellationToken);
            var serialized = SerializeBounded(result);
            toolResultObserver?.Invoke(toolName, serialized);
            await WriteAuditAsync(actionName, objectName, succeeded: true,
                stopwatch.ElapsedMilliseconds, error: null);
            return serialized;
        }
        catch (Exception exception)
        {
            await WriteAuditAsync(actionName, objectName, succeeded: false,
                stopwatch.ElapsedMilliseconds, exception.GetType().Name);
            if (IsRecoverableToolException(exception))
            {
                var serialized = SerializeBounded(new
                {
                    error = new
                    {
                        code = exception.GetType().Name,
                        message = exception.Message,
                        recoverable = true,
                    },
                });
                toolResultObserver?.Invoke(toolName, serialized);
                return serialized;
            }

            throw;
        }
    }

    private static bool IsRecoverableToolException(Exception exception)
        => exception is GridletValidationException or GridletQueryException;

    private ValueTask WriteAuditAsync(
        string actionName,
        string? objectName,
        bool succeeded,
        long durationMs,
        string? error)
        => auditSink.WriteAsync(
            new GridletAuditEvent(
                DateTimeOffset.UtcNow,
                userName,
                actionName,
                resolved.Context.ConnectionName,
                resolved.Context.Database,
                objectName,
                Sql: null,
                succeeded,
                durationMs,
                error),
            CancellationToken.None);

    private GridletConnectionContext CreateDataQueryContext(GridletConnectionContext context)
    {
        var dataConnectionString = context.Connection.AgentDataConnectionString;
        if (string.IsNullOrWhiteSpace(dataConnectionString))
        {
            return context;
        }

        var source = context.Connection;
        var copy = new GridletConnectionOptions
        {
            Name = source.Name,
            ConnectionString = dataConnectionString,
            ProviderName = source.ProviderName,
            DefaultDatabase = source.DefaultDatabase,
            AllowSqlExecution = source.AllowSqlExecution,
            AllowWrites = source.AllowWrites,
            AllowDdl = source.AllowDdl,
            AllowAgentSchemaAccess = source.AllowAgentSchemaAccess,
            AllowAgentDataAccess = source.AllowAgentDataAccess,
            AgentDataConnectionString = source.AgentDataConnectionString,
            AllowAgentDataWithPrimaryConnection = source.AllowAgentDataWithPrimaryConnection,
        };
        return new GridletConnectionContext(copy, context.Database);
    }

    private string SerializeBounded<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        if (json.Length <= settings.MaxToolResultCharacters)
        {
            return json;
        }

        var low = 0;
        var high = json.Length;
        var best = JsonSerializer.Serialize(new BoundedResult(true, string.Empty), JsonOptions);
        while (low <= high)
        {
            var length = low + ((high - low) / 2);
            var candidate = JsonSerializer.Serialize(
                new BoundedResult(true, json[..length]), JsonOptions);
            if (candidate.Length <= settings.MaxToolResultCharacters)
            {
                best = candidate;
                low = length + 1;
            }
            else
            {
                high = length - 1;
            }
        }
        return best;
    }

    private object? NormalizeCellValue(object? value)
        => value switch
        {
            null or DBNull => null,
            string text => LimitCellString(text),
            byte[] bytes => new
            {
                base64 = Convert.ToBase64String(bytes.AsSpan(0, Math.Min(bytes.Length, MaxCellCharacters))),
                truncated = bytes.Length > MaxCellCharacters,
            },
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or
                float or double or decimal or DateTime or DateTimeOffset or DateOnly or TimeOnly or
                TimeSpan or Guid => value,
            _ => LimitCellString(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };

    private string? LimitCellString(string? value)
        => value is not null && value.Length > MaxCellCharacters
            ? string.Concat(value.AsSpan(0, MaxCellCharacters), "… [truncated]")
            : value;

    private int MaxCellCharacters => Math.Max(64, Math.Min(4_096, settings.MaxToolResultCharacters / 4));

    private static void ValidateObjectName(string schema, string name)
    {
        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 512 ||
            string.IsNullOrWhiteSpace(name) || name.Length > 512)
        {
            throw new GridletValidationException(
                "Schema and object names must each contain between 1 and 512 characters.");
        }
    }

    private static string AuditObjectName(string schema, string name)
    {
        var value = $"{schema}.{name}";
        value = new string(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return value.Length <= 256 ? value : value[..256];
    }

    private sealed record BoundedResult(bool Truncated, string PartialJson);
}
