using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    GridletAgentAccessGate gate,
    ISavedQueryStore? savedQueries,
    IPublishedEndpointStore? publishedEndpoints,
    IGridletPublishedEndpointInvoker? endpointInvoker,
    GridletAgentEnvironment? environment,
    int maxQueryResultRows,
    Action<string, string>? toolResultObserver = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Every tool is registered for every turn, including the ones the person has not shared
    /// anything for. Model providers fix their tool list when a turn begins, so a tool that only
    /// appeared once permission was granted could never be called in the turn that asked for it.
    /// Each tool therefore checks <see cref="GridletAgentAccessGate"/> at the moment it runs.
    /// </summary>
    public IList<AITool> Create() =>
    [
        Describe(ListSchemasAsync, "list_schemas"),
        Describe(ListObjectsAsync, "list_database_objects"),
        Describe(DescribeTableAsync, "describe_table"),
        Describe(GetObjectDefinitionAsync, "get_object_definition"),
        Describe(ExecuteReadOnlyQueryAsync, "execute_read_only_query"),
        Describe(GetSharedAccessAsync, "get_shared_database_access"),
        Describe(RequestAccessAsync, "request_database_access"),
        Describe(GetGridletGuideAsync, "get_gridlet_guide", ("topics", GuideTopicList)),
        Describe(DescribeDeploymentAsync, "describe_gridlet_deployment"),
        Describe(ListPublishedEndpointsAsync, "list_published_api_endpoints"),
        Describe(InvokePublishedEndpointAsync, "invoke_published_api_endpoint"),
        Describe(ListSavedQueriesAsync, "list_saved_queries"),
    ];

    /// <summary>
    /// Builds one tool from its method and the Markdown file that describes it. What a tool is for
    /// and what its parameters mean is the part a model actually reads, so it lives in
    /// <c>Prompts/Tools/{name}.md</c> rather than in attributes on the method.
    /// </summary>
    internal static AIFunction Describe(
        Delegate method,
        string name,
        params (string Token, string Value)[] values)
    {
        var document = GridletPrompts.Document($"Tools/{name}");
        var function = AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = Substitute(document.Text),
        });

        return document.SectionNames.Count == 0
            ? function
            : new DescribedParameters(
                function,
                document.SectionNames.ToDictionary(
                    section => section,
                    section => Substitute(document.Section(section)),
                    StringComparer.Ordinal));

        string Substitute(string text)
        {
            foreach (var (token, value) in values)
            {
                text = text.Replace($"{{{token}}}", value, StringComparison.Ordinal);
            }

            return text;
        }
    }

    private static string GuideTopicList
    {
        get
        {
            var topics = GridletPrompts.GuideTopics;
            return topics.Count < 2
                ? string.Join(string.Empty, topics)
                : $"{string.Join(", ", topics.Take(topics.Count - 1))}, or {topics[^1]}";
        }
    }

    /// <summary>
    /// Puts the parameter descriptions from a prompt file into the schema the model is shown.
    /// <see cref="AIFunctionFactory"/> reads those from <see cref="DescriptionAttribute"/>, which a
    /// file cannot supply, so the generated schema is amended instead of being replaced — the
    /// types, requiredness, and defaults it inferred from the method all stay authoritative.
    /// </summary>
    private sealed class DescribedParameters : DelegatingAIFunction
    {
        private readonly JsonElement schema;

        public DescribedParameters(AIFunction inner, IReadOnlyDictionary<string, string> descriptions)
            : base(inner)
        {
            var node = JsonNode.Parse(inner.JsonSchema.GetRawText())?.AsObject()
                ?? throw new InvalidOperationException(
                    $"Tool '{inner.Name}' produced a schema that is not an object.");
            var properties = node["properties"]?.AsObject();
            foreach (var (parameter, description) in descriptions)
            {
                // A section naming a parameter the method does not have would silently document
                // nothing, and is nearly always a rename that missed the file.
                var property = properties?[parameter]?.AsObject()
                    ?? throw new InvalidOperationException(
                        $"'Prompts/Tools/{inner.Name}.md' describes a parameter '{parameter}' that " +
                        $"tool '{inner.Name}' does not take.");
                property["description"] = description;
            }

            schema = JsonSerializer.SerializeToElement(node);
        }

        public override JsonElement JsonSchema => schema;
    }

    private async Task<string> ListSchemasAsync(CancellationToken cancellationToken)
        => await RequireSharedAsync(
               GridletAgentAccessScope.Schema, "list_schemas", cancellationToken)
           ?? await ExecuteAuditedAsync(
               "agent.tool.list_schemas",
               "list_schemas",
               objectName: null,
               token => resolved.Provider.Schema.GetSchemasAsync(resolved.Context, token),
               cancellationToken);

    private async Task<string> ListObjectsAsync(
        string? schema = null,
        string? nameContains = null,
        string? objectType = null,
        CancellationToken cancellationToken = default)
        => await RequireSharedAsync(
               GridletAgentAccessScope.Schema, "list_database_objects", cancellationToken)
           ?? await ExecuteAuditedAsync(
            "agent.tool.list_objects",
            "list_database_objects",
            objectName: string.IsNullOrWhiteSpace(schema) ? null : schema,
            async token =>
            {
                DbObjectType? requestedType = null;
                if (!string.IsNullOrWhiteSpace(objectType))
                {
                    var normalizedType = objectType.Trim();
                    if (!Enum.TryParse<DbObjectType>(normalizedType, ignoreCase: true, out var parsedType) ||
                        !Enum.IsDefined(parsedType) ||
                        !string.Equals(parsedType.ToString(), normalizedType, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new GridletValidationException(
                            $"Object type '{objectType}' is invalid. Use Table, View, StoredProcedure, " +
                            "ScalarFunction, TableValuedFunction, or Trigger.");
                    }

                    requestedType = parsedType;
                }

                var objects = await resolved.Provider.Schema.GetObjectsAsync(resolved.Context, token);
                IEnumerable<DbObjectInfo> filtered = objects;
                if (!string.IsNullOrWhiteSpace(schema))
                {
                    filtered = filtered.Where(item =>
                        string.Equals(item.Schema, schema, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(nameContains))
                {
                    filtered = filtered.Where(item =>
                        item.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
                }

                if (requestedType is not null)
                {
                    filtered = filtered.Where(item => item.Type == requestedType.Value);
                }

                return filtered.ToArray();
            },
            cancellationToken);

    private async Task<string> DescribeTableAsync(
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Schema, "describe_table", cancellationToken);
        if (denied is not null) return denied;

        ValidateObjectName(schema, name);
        return await ExecuteAuditedAsync(
            "agent.tool.describe_table",
            "describe_table",
            AuditObjectName(schema, name),
            token => resolved.Provider.Schema.GetTableDefinitionAsync(
                resolved.Context, schema, name, token),
            cancellationToken);
    }

    private async Task<string> GetObjectDefinitionAsync(
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Schema, "get_object_definition", cancellationToken);
        if (denied is not null) return denied;

        ValidateObjectName(schema, name);
        return await ExecuteAuditedAsync(
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

    private async Task<string> ExecuteReadOnlyQueryAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Data, "execute_read_only_query", cancellationToken);
        if (denied is not null) return denied;

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

    // ---- access ------------------------------------------------------------------

    private Task<string> GetSharedAccessAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = gate.Current;
        return Task.FromResult(Observe("get_shared_database_access", new
        {
            shared = new { schema = current.Schema, data = current.Data, api = current.Api },
            canBeRequested = new
            {
                schema = gate.HostAllows.Schema && !current.Schema,
                data = gate.HostAllows.Data && !current.Data,
                api = gate.HostAllows.Api && !current.Api,
            },
            note = GridletPrompts.Text("Notes/shared-access"),
        }));
    }

    private async Task<string> RequestAccessAsync(
        string scope,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!TryParseScope(scope, out var requested))
        {
            return Observe("request_database_access", new
            {
                error = new
                {
                    code = "invalid_scope",
                    message = "Scope must be 'schema', 'data', or 'api'.",
                    recoverable = true,
                },
            });
        }

        var outcome = await gate.RequestAsync(requested, reason, cancellationToken);
        var current = gate.Current;
        await WriteAuditAsync(
            "agent.access.request",
            objectName: requested.ToString().ToLowerInvariant(),
            succeeded: outcome is GridletAgentAccessRequestOutcome.Granted
                or GridletAgentAccessRequestOutcome.AlreadyShared,
            durationMs: 0,
            error: outcome.ToString());

        return Observe("request_database_access", new
        {
            granted = outcome is GridletAgentAccessRequestOutcome.Granted
                or GridletAgentAccessRequestOutcome.AlreadyShared,
            outcome = outcome switch
            {
                GridletAgentAccessRequestOutcome.AlreadyShared => "already_shared",
                GridletAgentAccessRequestOutcome.NotConfigured => "not_configured",
                GridletAgentAccessRequestOutcome.AlreadyWaiting => "already_waiting",
                GridletAgentAccessRequestOutcome.Granted => "granted",
                GridletAgentAccessRequestOutcome.Denied => "denied",
                _ => "timed_out",
            },
            shared = new { schema = current.Schema, data = current.Data, api = current.Api },
            guidance = outcome switch
            {
                GridletAgentAccessRequestOutcome.Granted or
                    GridletAgentAccessRequestOutcome.AlreadyShared =>
                    "Continue with the work you asked for.",
                GridletAgentAccessRequestOutcome.NotConfigured =>
                    "The host disabled this scope for this connection. Do not ask again; explain " +
                    "the limitation and answer as far as you can without it.",
                GridletAgentAccessRequestOutcome.AlreadyWaiting =>
                    "Another request is still on screen. Wait for that answer instead of asking again.",
                _ => "Respect the decision. Do not ask again in this turn, and answer without that " +
                     "context or say plainly what you cannot determine without it.",
            },
        });
    }

    /// <summary>
    /// The single check every database tool makes before it touches the connection. It returns a
    /// recoverable error the model can act on rather than throwing, so a turn that hits a closed
    /// scope can ask for it and carry on.
    /// </summary>
    private async Task<string?> RequireSharedAsync(
        GridletAgentAccessScope scope,
        string toolName,
        CancellationToken cancellationToken)
    {
        if (gate.IsShared(scope)) return null;

        var name = scope.ToString().ToLowerInvariant();
        var allowed = gate.HostAllows.Allows(scope);
        await WriteAuditAsync(
            $"agent.tool.{toolName}", objectName: null, succeeded: false, durationMs: 0,
            error: allowed ? "AccessNotShared" : "AccessNotConfigured");
        return Observe(toolName, new
        {
            error = new
            {
                code = allowed ? "access_not_shared" : "access_not_configured",
                message = GridletPrompts.Section(
                    "Notes/access-denied",
                    allowed ? "not-shared-message" : "not-configured-message",
                    ("scope", name)),
                recoverable = true,
                nextStep = GridletPrompts.Section(
                    "Notes/access-denied",
                    allowed ? "not-shared-next-step" : "not-configured-next-step",
                    ("scope", name)),
            },
        });
    }

    /// <summary>Where Gridlet itself answers, e.g. <c>https://localhost:5088/gridlet</c>.</summary>
    private string MountUrl() =>
        environment is null
            ? string.Empty
            : environment.BaseAddress.TrimEnd('/') + environment.MountPath;

    private static bool TryParseScope(string? value, out GridletAgentAccessScope scope)
    {
        scope = GridletAgentAccessScope.Schema;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalized = value.Trim();
        if (normalized.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            scope = GridletAgentAccessScope.Data;
            return true;
        }
        if (normalized.Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            scope = GridletAgentAccessScope.Api;
            return true;
        }
        return normalized.Equals("schema", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Gridlet product knowledge -----------------------------------------------

    private Task<string> GetGridletGuideAsync(
        string? topic,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(topic))
        {
            return Task.FromResult(Observe("get_gridlet_guide", new
            {
                topics = GridletPrompts.GuideTopics,
            }));
        }

        var text = GridletPrompts.Guide(topic);
        return Task.FromResult(Observe("get_gridlet_guide", text is null
            ? new
            {
                error = new
                {
                    code = "unknown_topic",
                    message = $"'{topic}' is not a guide topic.",
                    recoverable = true,
                },
                topics = GridletPrompts.GuideTopics,
            }
            : (object)new { topic, guide = text }));
    }

    private async Task<string> DescribeDeploymentAsync(CancellationToken cancellationToken)
    {
        var connection = resolved.Context.Connection;
        // Capability metadata is optional for third-party providers, so a provider without it
        // simply reports no capabilities rather than failing the tool.
        var capabilities = (resolved.Provider as IGridletProviderMetadata)?.Capabilities;
        var published = publishedEndpoints is null
            ? null
            : await publishedEndpoints.GetAllAsync(cancellationToken);
        var saved = savedQueries is null
            ? null
            : await savedQueries.GetAllAsync(cancellationToken);

        return Observe("describe_gridlet_deployment", new
        {
            connection = new
            {
                name = connection.Name,
                provider = resolved.Provider.ProviderName.ToString(),
                database = resolved.Context.Database,
                allowsSqlExecution = connection.AllowSqlExecution,
                allowsRowWrites = connection.AllowWrites,
                allowsDdl = connection.AllowDdl,
                allowsAgentSchemaAccess = connection.AllowAgentSchemaAccess,
                allowsAgentDataAccess = connection.AllowAgentDataAccess,
                allowsAgentApiAccess = connection.AllowAgentApiAccess,
                usesSeparateAgentDataIdentity =
                    !string.IsNullOrWhiteSpace(connection.AgentDataConnectionString),
            },
            providerCapabilities = capabilities is null ? null : new
            {
                capabilities.DefaultSchema,
                capabilities.SupportsSchemas,
                capabilities.SupportsViews,
                capabilities.SupportsStoredProcedures,
                capabilities.SupportsFunctions,
                capabilities.SupportsTriggers,
                capabilities.ObjectEditMode,
            },
            limits = new
            {
                maxQueryResultRows,
                agentMaxQueryRows = settings.MaxQueryRows,
                agentQueryTimeoutSeconds = settings.QueryTimeoutSeconds,
                agentMaxToolCallsPerTurn = settings.MaxToolIterations,
            },
            publishedEndpointCount = published?.Count,
            savedQueryCount = saved?.Count,
            // The real address, when the host supplied one. Without it the model reaches for the
            // placeholder URLs in the product guide and hands people links that resolve to nothing.
            installation = environment is null ? null : new
            {
                baseAddress = environment.BaseAddress,
                mountPath = environment.MountPath,
                gridletUrl = MountUrl(),
                publishedApiSegment = environment.PublishedApiSegment,
                publishedEndpointUrlPattern =
                    $"{MountUrl()}/{environment.PublishedApiSegment}/{{route}}",
            },
            note = GridletPrompts.Section(
                "Notes/deployment",
                environment is null ? "without-installation" : "with-installation"),
        });
    }

    private async Task<string> ListPublishedEndpointsAsync(CancellationToken cancellationToken)
    {
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Api, "list_published_api_endpoints", cancellationToken);
        if (denied is not null) return denied;
        if (publishedEndpoints is null)
        {
            return Observe("list_published_api_endpoints", new
            {
                error = new
                {
                    code = "store_unavailable",
                    message = "This host does not expose a published-endpoint store.",
                    recoverable = true,
                },
            });
        }

        return await ExecuteAuditedAsync(
            "agent.tool.list_published_endpoints",
            "list_published_api_endpoints",
            objectName: null,
            async token =>
            {
                var endpoints = await publishedEndpoints.GetAllAsync(token);
                return endpoints.Select(endpoint => new
                {
                    endpoint.Name,
                    endpoint.Method,
                    endpoint.Route,
                    endpoint.ConnectionName,
                    endpoint.Database,
                    sql = LimitCellString(endpoint.Sql),
                    parameters = endpoint.Parameters.Select(parameter => new
                    {
                        parameter.Name,
                        parameter.Required,
                        parameter.Type,
                    }).ToArray(),
                    endpoint.Enabled,
                    endpoint.MaxRows,
                    requiresPolicy = !string.IsNullOrWhiteSpace(endpoint.AuthorizationPolicy),
                    endpoint.AuthorizationPolicy,
                    url = environment is null
                        ? null
                        : $"{MountUrl()}/{environment.PublishedApiSegment}/{endpoint.Route.TrimStart('/')}",
                }).ToArray();
            },
            cancellationToken);
    }

    private async Task<string> InvokePublishedEndpointAsync(
        string name,
        string? parameters = null,
        CancellationToken cancellationToken = default)
    {
        // API access grants no direct query access. It separately permits this invocation, whose
        // response may contain row data; the endpoint's SQL and execution identity bound what the
        // response can disclose.
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Api, "invoke_published_api_endpoint", cancellationToken);
        if (denied is not null) return denied;
        if (endpointInvoker is null)
        {
            return Observe("invoke_published_api_endpoint", new
            {
                error = new
                {
                    code = "invoker_unavailable",
                    message = "This host does not let the agent call published endpoints.",
                    recoverable = true,
                    nextStep = "Describe the endpoint and offer to open it in an API request tab.",
                },
            });
        }

        if (!TryParseEndpointParameters(parameters, out var query, out var parseError))
        {
            return Observe("invoke_published_api_endpoint", new
            {
                error = new
                {
                    code = "invalid_parameters",
                    message = parseError,
                    recoverable = true,
                },
            });
        }

        var invocation = await endpointInvoker.InvokeAsync(
            name, query, new GridletAgentUserContext(null, userName, userName is not null),
            cancellationToken);
        await WriteAuditAsync(
            "agent.tool.invoke_published_endpoint",
            objectName: name,
            succeeded: invocation.Succeeded,
            durationMs: invocation.ElapsedMilliseconds,
            error: invocation.ErrorCode);

        if (!invocation.Succeeded)
        {
            return Observe("invoke_published_api_endpoint", new
            {
                error = new
                {
                    code = invocation.ErrorCode,
                    message = invocation.ErrorMessage,
                    recoverable = true,
                },
            });
        }

        return Observe("invoke_published_api_endpoint", new
        {
            request = new { method = invocation.Method, url = invocation.Url },
            response = new
            {
                status = invocation.StatusCode,
                contentType = invocation.ContentType,
                elapsedMs = invocation.ElapsedMilliseconds,
                body = invocation.Body,
                truncated = invocation.Truncated,
            },
            guidance = "Show the person the URL you called and the response body verbatim in a " +
                       "json code block. Do not tidy, reformat, or summarise away the actual " +
                       "values — seeing the real shape is the point.",
        });
    }

    /// <summary>
    /// Accepts the flat JSON object the tool documents and nothing else. Anything nested would have
    /// no meaning in a query string, and a silent coercion would send a value nobody chose.
    /// </summary>
    private static bool TryParseEndpointParameters(
        string? parameters,
        out Dictionary<string, string?> query,
        out string error)
    {
        query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(parameters)) return true;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(parameters);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            error = $"Parameters must be a JSON object. {exception.Message}";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Parameters must be a JSON object such as {\"id\":\"42\"}.";
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    query[property.Name] = property.Value.GetString();
                    break;
                case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                    query[property.Name] = property.Value.GetRawText();
                    break;
                case JsonValueKind.Null:
                    query[property.Name] = null;
                    break;
                default:
                    error = $"Parameter '{property.Name}' must be a string, number, boolean, or null.";
                    return false;
            }
        }

        return true;
    }

    private async Task<string> ListSavedQueriesAsync(CancellationToken cancellationToken)
    {
        var denied = await RequireSharedAsync(
            GridletAgentAccessScope.Schema, "list_saved_queries", cancellationToken);
        if (denied is not null) return denied;
        if (savedQueries is null)
        {
            return Observe("list_saved_queries", new
            {
                error = new
                {
                    code = "store_unavailable",
                    message = "This host does not expose a saved-query store.",
                    recoverable = true,
                },
            });
        }

        return await ExecuteAuditedAsync(
            "agent.tool.list_saved_queries",
            "list_saved_queries",
            objectName: null,
            async token =>
            {
                var queries = await savedQueries.GetAllAsync(token);
                return queries.Select(query => new
                {
                    query.Name,
                    query.ConnectionName,
                    query.Database,
                    sql = LimitCellString(query.Sql),
                    query.UpdatedAtUtc,
                }).ToArray();
            },
            cancellationToken);
    }

    /// <summary>Serializes a locally produced result and reports it as a tool result event.</summary>
    private string Observe(string toolName, object value)
    {
        var serialized = SerializeBounded(value);
        toolResultObserver?.Invoke(toolName, serialized);
        return serialized;
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
        => exception is GridletValidationException
            or GridletQueryException
            or GridletObjectNotFoundException;

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
            AllowAgentApiAccess = source.AllowAgentApiAccess,
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
