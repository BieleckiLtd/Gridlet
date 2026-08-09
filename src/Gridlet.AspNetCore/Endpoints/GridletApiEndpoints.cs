using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gridlet.Abstractions;
using Gridlet.AspNetCore.Agents;
using Gridlet.AspNetCore.Contracts;
using Gridlet.Auditing;
using Gridlet.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Gridlet.AspNetCore.GridletEndpointHelpers;

namespace Gridlet.AspNetCore;

/// <summary>The JSON API consumed by the embedded UI (and usable directly).</summary>
internal static partial class GridletApiEndpoints
{
    private const string UnexpectedErrorMessage = "An unexpected server error occurred.";
    private static readonly string Version =
        typeof(GridletApiEndpoints).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "dev";

    private static readonly GridletProviderCapabilities LegacyProviderCapabilities = new(
        DefaultSchema: "dbo",
        SupportsSchemas: true,
        SupportsViews: true,
        SupportsStoredProcedures: true,
        SupportsFunctions: true,
        SupportsTriggers: true,
        SupportsClusteredPrimaryKeys: true,
        SuggestedDataTypes: ["int", "nvarchar(100)"],
        SelectExample: "SELECT TOP (100) * FROM {object};",
        CreateTriggerExample:
            "CREATE TRIGGER dbo.NewTrigger\nON dbo.SomeTable\nAFTER INSERT\nAS\nBEGIN\n    SELECT 1;\nEND;",
        ObjectEditMode: "Alter",
        SupportsCheckConstraints: false,
        SupportsUniqueConstraints: false,
        SupportsIndexes: false);

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9\-_/]*$")]
    private static partial Regex RoutePattern();

    public static void Map(RouteGroupBuilder api, GridletOptions options)
    {
        api.MapGet("/meta", GetMeta);
        api.MapGet("/connections/{connection}/databases", GetDatabases);
        api.MapGet("/connections/{connection}/databases/{database}/objects", GetObjects);
        api.MapGet("/connections/{connection}/databases/{database}/schemas", GetSchemas);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/data", GetObjectData);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/data/stream", StreamObjectData);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/structure", GetObjectStructure);
        api.MapGet("/connections/{connection}/databases/{database}/objects/{schema}/{name}/definition", GetObjectDefinition);
        api.MapPost("/connections/{connection}/databases/{database}/query", ExecuteQuery);
        MapSessions(api);

        // Optional Microsoft Agent Framework integration. The routes stay dormant when no
        // IGridletAgentService has been registered by the host.
        var storeAgentCredential = api.MapPost(
            "/agents/{profileId}/credentials", StoreAgentCredential);
        var removeAgentCredential = api.MapDelete(
            "/agents/credentials", RemoveAgentCredential);
        api.MapDelete("/agents/conversations/{conversationId}", CloseAgentConversation);
        var dataAgent = api.MapPost(
            "/connections/{connection}/databases/{database}/agents/data/chat", ChatWithDataAgent);
        var schemaAgent = api.MapPost(
            "/connections/{connection}/databases/{database}/agents/schema/chat", ChatWithSchemaAgent);
        // Answering an access prompt widens what a live turn can reach, so each answer is guarded
        // by the same policy as the chat route that grants the scope up front.
        var grantData = api.MapPost("/agents/permissions/{requestId}/data", GrantDataAccess);
        var grantApi = api.MapPost("/agents/permissions/{requestId}/api", GrantApiAccess);
        var grantSchema = api.MapPost("/agents/permissions/{requestId}/schema", GrantSchemaAccess);
        if (!string.IsNullOrWhiteSpace(options.Security.AgentDataAuthorizationPolicy))
        {
            dataAgent.RequireAuthorization(options.Security.AgentDataAuthorizationPolicy);
            grantData.RequireAuthorization(options.Security.AgentDataAuthorizationPolicy);
            grantApi.RequireAuthorization(options.Security.AgentDataAuthorizationPolicy);
        }
        if (!string.IsNullOrWhiteSpace(options.Security.AgentSchemaAuthorizationPolicy))
        {
            schemaAgent.RequireAuthorization(options.Security.AgentSchemaAuthorizationPolicy);
            grantSchema.RequireAuthorization(options.Security.AgentSchemaAuthorizationPolicy);
        }
        if (!string.IsNullOrWhiteSpace(options.Security.AgentCredentialAuthorizationPolicy))
        {
            storeAgentCredential.RequireAuthorization(options.Security.AgentCredentialAuthorizationPolicy);
            removeAgentCredential.RequireAuthorization(options.Security.AgentCredentialAuthorizationPolicy);
        }

        // Row editing (POST for update/delete so the JSON body binds on every server).
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows", InsertRow);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows/update", UpdateRow);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/rows/delete", DeleteRow);

        // Table designer.
        api.MapPost("/connections/{connection}/databases/{database}/schemas", CreateSchema);
        api.MapPut("/connections/{connection}/databases/{database}/schemas/{schema}", AlterSchema);
        api.MapDelete("/connections/{connection}/databases/{database}/schemas/{schema}", DropSchema);
        api.MapPost("/connections/{connection}/databases/{database}/tables", CreateTable);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns", AddColumn);
        api.MapPut("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns/{column}", AlterColumn);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}/columns/{column}", DropColumn);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/primary-key", AddPrimaryKey);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/check-constraints", AddCheckConstraint);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/check-constraints/drop", DropCheckConstraint);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/unique-constraints", AddUniqueConstraint);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/unique-constraints/drop", DropUniqueConstraint);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/indexes", CreateIndex);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}/indexes/{index}", DropIndex);
        api.MapPost("/connections/{connection}/databases/{database}/objects/{schema}/{name}/foreign-keys", AddForeignKey);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}/constraints/{constraint}", DropConstraint);
        api.MapDelete("/connections/{connection}/databases/{database}/objects/{schema}/{name}", DropObject);

        // Saved queries.
        api.MapGet("/queries", GetSavedQueries);
        api.MapPost("/queries", SaveQuery);
        api.MapDelete("/queries/{id}", DeleteSavedQuery);

        // Published endpoint administration (invocation lives in GridletPublishedEndpoints).
        api.MapGet("/published", GetPublishedEndpoints);
        api.MapPost("/published", SavePublishedEndpoint);
        api.MapDelete("/published/{id}", DeletePublishedEndpoint);
    }

    // ---- meta & schema ----

    private static IResult GetMeta(
        IOptionsMonitor<GridletOptions> options,
        IGridletProviderRegistry providers,
        IServiceProvider services)
    {
        var connections = options.CurrentValue.Connections
            .Select(c => new GridletConnectionSummary(
                c.Name,
                c.ProviderName.ToString(),
                c.DefaultDatabase,
                c.AllowSqlExecution,
                c.AllowWrites,
                c.AllowDdl,
                providers.Get(c.ProviderName) is IGridletProviderMetadata metadata
                    ? metadata.Capabilities
                    : LegacyProviderCapabilities,
                c.AllowAgentSchemaAccess,
                c.AllowAgentDataAccess,
                c.AllowAgentApiAccess))
            .ToArray();
        return Results.Ok(new GridletMetaResponse(
            Version,
            connections,
            options.CurrentValue.Limits.MaxQueryResultRows,
            services.GetService<IGridletAgentService>()?.Info,
            options.CurrentValue.PublishedApiSegment));
    }

    private static Task<IResult> GetDatabases(
        string connection,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection);
            var databases = await resolved.Provider.Schema.GetDatabasesAsync(resolved.Context, cancellationToken);
            return Results.Ok(databases);
        });

    private static Task<IResult> GetObjects(
        string connection,
        string database,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var objects = await resolved.Provider.Schema.GetObjectsAsync(resolved.Context, cancellationToken);
            return Results.Ok(objects.Select(ToDto).ToArray());
        });

    private static Task<IResult> GetSchemas(
        string connection,
        string database,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            return Results.Ok(await resolved.Provider.Schema.GetSchemasAsync(resolved.Context, cancellationToken));
        });

    private static Task<IResult> GetObjectData(
        string connection,
        string database,
        string schema,
        string name,
        int? page,
        int? pageSize,
        string? sort,
        string? dir,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var limits = options.CurrentValue.Limits;
            var request = new TableDataRequest(
                Page: Math.Max(1, page ?? 1),
                PageSize: Math.Clamp(pageSize ?? limits.DefaultPageSize, 1, limits.MaxPageSize),
                SortColumn: string.IsNullOrWhiteSpace(sort) ? null : sort,
                SortDirection: string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
                    ? SortDirection.Descending
                    : SortDirection.Ascending);

            var resolved = resolver.Resolve(connection, database);
            var dataPage = await resolved.Provider.Data.GetPageAsync(
                resolved.Context, schema, name, request, cancellationToken);
            return Results.Ok(dataPage);
        });

    private static async Task StreamObjectData(
        string connection, string database, string schema, string name,
        int? maxRows, string? sort, string? dir,
        IGridletConnectionResolver resolver, IOptionsMonitor<GridletOptions> options,
        ILoggerFactory loggerFactory, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var limits = options.CurrentValue.Limits;
        var cap = Math.Clamp(maxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
        var pageSize = Math.Min(500, limits.MaxPageSize);
        var direction = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending : SortDirection.Ascending;
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

        async Task WriteAsync(QueryStreamEvent value)
        {
            await JsonSerializer.SerializeAsync(httpContext.Response.Body, value, JsonSerializerOptions.Web, cancellationToken);
            await httpContext.Response.WriteAsync("\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            var resolved = resolver.Resolve(connection, database);
            var emitted = 0;
            var page = 1;
            long totalRows = 0;
            do
            {
                var data = await resolved.Provider.Data.GetPageAsync(resolved.Context, schema, name,
                    new TableDataRequest(page, Math.Min(pageSize, cap - emitted), sort, direction), cancellationToken);
                totalRows = data.TotalRows;
                if (page == 1)
                {
                    await WriteAsync(new QueryStreamEvent(
                        "resultSet", 0, data.Columns, RowIdentity: data.RowIdentity));
                }
                if (data.Rows.Count == 0) break;
                await WriteAsync(new QueryStreamEvent("rows", 0, Rows: data.Rows, RowKeys: data.RowKeys));
                emitted += data.Rows.Count;
                page++;
            }
            while (emitted < cap && emitted < totalRows);

            await WriteAsync(new QueryStreamEvent("resultSetCompleted", 0, Truncated: emitted < totalRows));
            await WriteAsync(new QueryStreamEvent("completed", RecordsAffected: emitted));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            var statusCode = ex switch
            {
                GridletUnknownConnectionException or GridletObjectNotFoundException
                    => StatusCodes.Status404NotFound,
                GridletValidationException or GridletQueryException
                    => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError,
            };
            var clientMessage = ex.Message;
            if (statusCode == StatusCodes.Status500InternalServerError)
            {
                LogUnexpectedStreamError(loggerFactory, ex, "table data");
                clientMessage = UnexpectedErrorMessage;
            }

            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = statusCode;
                await httpContext.Response.WriteAsJsonAsync(
                    new GridletErrorResponse(clientMessage), cancellationToken);
                return;
            }
            await TryWriteStreamEventAsync(
                httpContext, new QueryStreamEvent("error", Message: clientMessage));
        }
    }

    private static Task<IResult> GetObjectStructure(
        string connection,
        string database,
        string schema,
        string name,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var definition = await resolved.Provider.Schema.GetTableDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            return Results.Ok(new TableStructureResponse(
                ToDto(definition.Object), definition.Columns, definition.Indexes, definition.ForeignKeys,
                definition.CheckConstraints, definition.UniqueConstraints, definition.RowIdentity));
        });

    private static Task<IResult> GetObjectDefinition(
        string connection,
        string database,
        string schema,
        string name,
        IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            var definition = await resolved.Provider.Schema.GetObjectDefinitionAsync(
                resolved.Context, schema, name, cancellationToken);
            return Results.Ok(new ObjectDefinitionResponse(definition));
        });

    // ---- database agents ----

    private static Task<IResult> StoreAgentCredential(
        string profileId,
        AgentCredentialRequestBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => ExecuteAgentCredentialAsync(async () =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var agent = services.GetService<IGridletAgentService>();
            if (agent is null)
            {
                return Results.NotFound(new GridletErrorResponse(
                    "Database agents are not configured for this application."));
            }

            var user = AgentUser(httpContext);
            var options = httpContext.RequestServices
                .GetRequiredService<IOptionsMonitor<GridletOptions>>().CurrentValue;
            if (!user.IsAuthenticated && !options.Security.AllowAnonymousAgentCredentials)
            {
                return Results.Unauthorized();
            }

            var profile = agent.Info.Profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, profileId, StringComparison.OrdinalIgnoreCase));
            if (profile is null || !profile.AllowsUserApiKey)
            {
                throw new GridletAgentException(
                    "The selected agent profile does not accept user-supplied API keys.");
            }

            if (string.IsNullOrWhiteSpace(body.ApiKey) || body.ApiKey.Length > 8_192)
            {
                throw new GridletAgentException("A valid API key is required.");
            }

            var credential = await agent.StoreCredentialAsync(
                profile.Id, body.ApiKey, user, cancellationToken);
            await AuditCredentialAsync(audit, user.DisplayName, "agent.credential.store", profile.Id, true, null);
            return Results.Ok(new AgentCredentialResponse(credential.Handle, credential.ExpiresAt));
        }, audit, httpContext, "agent.credential.store", profileId);

    private static Task<IResult> RemoveAgentCredential(
        [Microsoft.AspNetCore.Mvc.FromBody] AgentCredentialRemoveRequestBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => ExecuteAgentCredentialAsync(async () =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var agent = services.GetService<IGridletAgentService>();
            if (agent is null)
            {
                return Results.NotFound(new GridletErrorResponse(
                    "Database agents are not configured for this application."));
            }

            var user = AgentUser(httpContext);
            var options = httpContext.RequestServices
                .GetRequiredService<IOptionsMonitor<GridletOptions>>().CurrentValue;
            if (!user.IsAuthenticated && !options.Security.AllowAnonymousAgentCredentials)
            {
                return Results.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.Handle) || body.Handle.Length > 256)
            {
                throw new GridletAgentException("The credential handle is invalid or expired.");
            }

            await agent.RemoveCredentialAsync(body.Handle, user, cancellationToken);
            await AuditCredentialAsync(audit, user.DisplayName, "agent.credential.remove", null, true, null);
            return Results.NoContent();
        }, audit, httpContext, "agent.credential.remove", profileId: null);

    private static Task ChatWithDataAgent(
        string connection,
        string database,
        AgentChatRequestBody body,
        IGridletConnectionResolver resolver,
        IGridletAuditSink audit,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => ChatWithAgent(
            connection, database, GridletAgentMode.Data, body, resolver, audit, services,
            httpContext, cancellationToken);

    private static Task<IResult> GrantDataAccess(
        string requestId,
        AgentPermissionDecisionBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext)
        => ResolveAgentPermission(
            requestId, GridletAgentAccessScope.Data, body, services, audit, httpContext);

    private static Task<IResult> GrantSchemaAccess(
        string requestId,
        AgentPermissionDecisionBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext)
        => ResolveAgentPermission(
            requestId, GridletAgentAccessScope.Schema, body, services, audit, httpContext);

    private static Task<IResult> GrantApiAccess(
        string requestId,
        AgentPermissionDecisionBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext)
        => ResolveAgentPermission(
            requestId, GridletAgentAccessScope.Api, body, services, audit, httpContext);

    /// <summary>
    /// Delivers one Allow or Deny answer to the turn waiting for it. Unknown, expired, and
    /// somebody else's requests all return the same 404 so this route cannot be used to discover
    /// which request ids are live.
    /// </summary>
    private static async Task<IResult> ResolveAgentPermission(
        string requestId,
        GridletAgentAccessScope scope,
        AgentPermissionDecisionBody body,
        IServiceProvider services,
        IGridletAuditSink audit,
        HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        if (body?.Granted is not { } granted)
        {
            return Results.BadRequest(new GridletErrorResponse(
                "The access decision must state whether the request was granted."));
        }
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 100)
        {
            return Results.NotFound(new GridletErrorResponse(
                "The access request is no longer waiting for an answer."));
        }

        var broker = services.GetService<IGridletAgentPermissionBroker>();
        if (broker is null)
        {
            return Results.NotFound(new GridletErrorResponse(
                "Database agents are not configured for this application."));
        }

        var user = AgentUser(httpContext);
        var resolved = broker.TryResolve(requestId, scope, granted, user);
        await AuditAsync(
            audit, httpContext, $"agent.access.{scope.ToString().ToLowerInvariant()}",
            connectionName: string.Empty, database: null, objectName: null, sql: null,
            succeeded: resolved && granted, durationMs: 0,
            error: resolved ? (granted ? null : "Denied by the user.") : "No matching request.");
        return resolved
            ? Results.NoContent()
            : Results.NotFound(new GridletErrorResponse(
                "The access request is no longer waiting for an answer."));
    }

    private static async Task<IResult> CloseAgentConversation(
        string conversationId,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IsValidConversationId(conversationId))
        {
            return Results.BadRequest(new GridletErrorResponse("The agent conversation id is invalid."));
        }

        var agent = services.GetService<IGridletAgentService>();
        if (agent is null)
        {
            return Results.NotFound(new GridletErrorResponse(
                "Database agents are not configured for this application."));
        }

        await agent.CloseConversationAsync(
            conversationId, AgentUser(httpContext), cancellationToken);
        return Results.NoContent();
    }

    private static Task ChatWithSchemaAgent(
        string connection,
        string database,
        AgentChatRequestBody body,
        IGridletConnectionResolver resolver,
        IGridletAuditSink audit,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken cancellationToken)
        => ChatWithAgent(
            connection, database, GridletAgentMode.Schema, body, resolver, audit, services,
            httpContext, cancellationToken);

    private static async Task ChatWithAgent(
        string connection,
        string database,
        GridletAgentMode mode,
        AgentChatRequestBody body,
        IGridletConnectionResolver resolver,
        IGridletAuditSink audit,
        IServiceProvider services,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        ResolvedConnection resolved;
        try
        {
            resolved = resolver.Resolve(connection, database);
        }
        catch (GridletUnknownConnectionException ex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse(ex.Message), cancellationToken);
            return;
        }
        var allowed = mode == GridletAgentMode.Data
            ? resolved.Context.Connection.AllowAgentDataAccess
            : resolved.Context.Connection.AllowAgentSchemaAccess;
        if (!allowed)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse(
                    $"{(mode == GridletAgentMode.Data ? "Data" : "Schema")} agent access is disabled for connection '{connection}'."),
                cancellationToken);
            return;
        }

        // The API scope has its own per-connection switch, and the route's mode does not imply it,
        // so it is checked here rather than only inside the agent service. A connection that never
        // opted in gets the same 403 as any other disabled scope.
        if (body.ShareApi && !resolved.Context.Connection.AllowAgentApiAccess)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse(
                    $"Published API agent access is disabled for connection '{connection}'."),
                cancellationToken);
            return;
        }

        // The route carries the host authorization policy, so the schema route may never open data
        // access — neither up front nor by an access prompt answered later in the turn. API access
        // grants no direct query access, but an invoked endpoint may return rows, so it uses the
        // route carrying the same host authorization policy.
        if (mode == GridletAgentMode.Schema && (body.ShareData || body.ShareApi))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse(
                    body.ShareData
                        ? "Sharing database data requires the data agent route."
                        : "Published API access requires the data agent route because an invoked " +
                          "endpoint may return row data; it does not enable direct data queries."),
                cancellationToken);
            return;
        }

        var agent = services.GetService<IGridletAgentService>();
        if (agent is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse("Database agents are not configured for this application."),
                cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(body.ProfileId))
        {
            await WriteAgentRequestErrorAsync(httpContext, "An agent profile is required.", cancellationToken);
            return;
        }
        var profile = agent.Info.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, body.ProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            await WriteAgentRequestErrorAsync(
                httpContext, "The selected agent profile is not configured.", cancellationToken);
            return;
        }
        if (body.CredentialHandle is { Length: > 256 })
        {
            await WriteAgentRequestErrorAsync(
                httpContext, "The credential handle is invalid or expired.", cancellationToken);
            return;
        }
        var reasoningEffort = body.ReasoningEffort?.Trim().ToLowerInvariant();
        if (reasoningEffort is { Length: > 0 } &&
            !(profile.ReasoningEfforts?.Contains(reasoningEffort, StringComparer.Ordinal) ?? false))
        {
            await WriteAgentRequestErrorAsync(
                httpContext,
                "The selected reasoning effort is not allowed for this agent profile.",
                cancellationToken);
            return;
        }
        if (body.ConversationId is not null && !IsValidConversationId(body.ConversationId))
        {
            await WriteAgentRequestErrorAsync(
                httpContext, "The agent conversation id is invalid.", cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(body.Message) || body.Message.Length > 20_000)
        {
            await WriteAgentRequestErrorAsync(
                httpContext, "A message between 1 and 20,000 characters is required.", cancellationToken);
            return;
        }

        var history = body.History ?? [];
        if (history.Count > 50 || history.Any(message =>
                message is null ||
                string.IsNullOrWhiteSpace(message.Content) ||
                message.Content.Length > 20_000 ||
                message.Role is not ("user" or "assistant")) ||
            history.Sum(message => (long)(message?.Content?.Length ?? 0)) > 200_000)
        {
            await WriteAgentRequestErrorAsync(httpContext, "The conversation history is invalid or too long.", cancellationToken);
            return;
        }

        var request = new GridletAgentRequest(
            connection,
            database,
            new GridletAgentAccess(body.ShareSchema, body.ShareData, body.ShareApi),
            profile.Id,
            body.Message,
            history,
            body.CredentialHandle,
            AgentUser(httpContext),
            body.ConversationId,
            reasoningEffort is { Length: > 0 } ? reasoningEffort : null,
            AgentEnvironment(httpContext));
        var stopwatch = Stopwatch.StartNew();
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";

        var completed = false;
        var serviceReportedError = false;
        try
        {
            await foreach (var agentEvent in agent.ChatAsync(request, cancellationToken))
            {
                if (string.Equals(agentEvent.Type, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    completed = true;
                }
                else if (string.Equals(agentEvent.Type, "error", StringComparison.OrdinalIgnoreCase))
                {
                    serviceReportedError = true;
                }
                await JsonSerializer.SerializeAsync(
                    httpContext.Response.Body, agentEvent, JsonSerializerOptions.Web, cancellationToken);
                await httpContext.Response.WriteAsync("\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            if (!completed && !serviceReportedError)
            {
                throw new GridletAgentException("The agent response ended before completion.");
            }

            await AuditAsync(
                audit, httpContext, $"agent.{mode.ToString().ToLowerInvariant()}.chat",
                connection, database, profile.Id, sql: null, succeeded: !serviceReportedError,
                stopwatch.ElapsedMilliseconds,
                error: serviceReportedError ? "The agent service reported an error." : null);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            await AuditAsync(
                audit, httpContext, $"agent.{mode.ToString().ToLowerInvariant()}.chat",
                connection, database, profile.Id, sql: null, succeeded: false,
                stopwatch.ElapsedMilliseconds, "Cancelled by the client.");
        }
        catch (OperationCanceledException)
        {
            const string timeoutMessage = "The agent request timed out.";
            await AuditAsync(
                audit, httpContext, $"agent.{mode.ToString().ToLowerInvariant()}.chat",
                connection, database, profile.Id, sql: null, succeeded: false,
                stopwatch.ElapsedMilliseconds, timeoutMessage);
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await httpContext.Response.WriteAsJsonAsync(
                    new GridletErrorResponse(timeoutMessage), CancellationToken.None);
            }
            else
            {
                await TryWriteAgentStreamEventAsync(
                    httpContext, new GridletAgentStreamEvent("error", timeoutMessage));
            }
        }
        catch (Exception ex)
        {
            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(GridletApiEndpoints))
                .LogError(ex, "Agent chat failed for profile {ProfileId}.", profile.Id);
            var safeMessage = SafeAgentError(ex);
            await AuditAsync(
                audit, httpContext, $"agent.{mode.ToString().ToLowerInvariant()}.chat",
                connection, database, profile.Id, sql: null, succeeded: false,
                stopwatch.ElapsedMilliseconds, safeMessage);

            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = ex is GridletAgentException
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status502BadGateway;
                await httpContext.Response.WriteAsJsonAsync(
                    new GridletErrorResponse(safeMessage), cancellationToken);
                return;
            }

            await TryWriteAgentStreamEventAsync(
                httpContext, new GridletAgentStreamEvent("error", safeMessage));
        }
    }

    private static async Task WriteAgentRequestErrorAsync(
        HttpContext httpContext,
        string message,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(message), cancellationToken);
    }

    private static string SafeAgentError(Exception exception)
        => exception switch
        {
            GridletAgentException => exception.Message,
            _ => "The agent provider could not complete the request. Check its endpoint, model, and credential.",
        };

    private static async Task TryWriteAgentStreamEventAsync(
        HttpContext httpContext,
        GridletAgentStreamEvent value)
    {
        if (httpContext.RequestAborted.IsCancellationRequested) return;
        try
        {
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body, value, JsonSerializerOptions.Web, CancellationToken.None);
            await httpContext.Response.WriteAsync("\n", CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client disconnected while the final error event was being written.
        }
    }

    private static async Task<IResult> ExecuteAgentCredentialAsync(
        Func<Task<IResult>> action,
        IGridletAuditSink audit,
        HttpContext httpContext,
        string actionName,
        string? profileId)
    {
        try
        {
            return await action();
        }
        catch (GridletAgentException exception)
        {
            await AuditCredentialAsync(
                audit, UserName(httpContext), actionName, profileId, false, exception.Message);
            return Results.BadRequest(new GridletErrorResponse(exception.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            const string message = "The agent credential operation could not be completed.";
            await AuditCredentialAsync(
                audit, UserName(httpContext), actionName, profileId, false, message);
            return Results.Json(
                new GridletErrorResponse(message), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ValueTask AuditCredentialAsync(
        IGridletAuditSink audit,
        string? userName,
        string action,
        string? profileId,
        bool succeeded,
        string? error)
        => audit.WriteAsync(new GridletAuditEvent(
            DateTimeOffset.UtcNow,
            userName,
            action,
            ConnectionName: "-",
            Database: null,
            ObjectName: profileId,
            Sql: null,
            succeeded,
            DurationMs: 0,
            error), CancellationToken.None);

    /// <summary>
    /// The address this Gridlet is actually answering on, taken from the request the browser just
    /// made. An agent given only the product's documented defaults invents plausible hostnames
    /// instead of naming the one the person is looking at.
    /// </summary>
    private static GridletAgentEnvironment AgentEnvironment(HttpContext httpContext)
    {
        var request = httpContext.Request;
        var baseAddress = string.Concat(
            request.Scheme, "://", request.Host.ToUriComponent(),
            request.PathBase.HasValue ? request.PathBase.ToUriComponent() : string.Empty, "/");
        var mountPath = httpContext.RequestServices.GetService<GridletMountPath>()?.Value ?? "/gridlet";
        var published = httpContext.RequestServices
            .GetService<IOptionsMonitor<GridletOptions>>()?.CurrentValue.PublishedApiSegment ?? "pub";
        return new GridletAgentEnvironment(baseAddress, mountPath, published);
    }

    private static GridletAgentUserContext AgentUser(HttpContext httpContext)
    {
        var identity = httpContext.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return new GridletAgentUserContext(null, null, IsAuthenticated: false);
        }

        var issuer = httpContext.User.FindFirst("iss")?.Value
            ?? identity.AuthenticationType
            ?? "authenticated";
        var subject = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? identity.Name
            ?? throw new GridletAgentException(
                "The authenticated user has no stable identifier for agent credentials.");
        var ownerHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer}\u001f{subject}")));
        return new GridletAgentUserContext(ownerHash, identity.Name, IsAuthenticated: true);
    }

    private static bool IsValidConversationId(string value)
        => value.Length is >= 1 and <= 100 &&
           value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    // ---- ad-hoc queries ----

    private static async Task ExecuteQuery(
        string connection,
        string database,
        QueryRequestBody body,
        IGridletConnectionResolver resolver,
        IOptionsMonitor<GridletOptions> options,
        IGridletAuditSink audit,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ResolvedConnection resolved;
        try
        {
            resolved = resolver.Resolve(connection, database);
        }
        catch (GridletUnknownConnectionException ex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(ex.Message), cancellationToken);
            return;
        }
        catch (GridletValidationException ex)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new GridletErrorResponse(ex.Message), cancellationToken);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpectedStreamError(loggerFactory, ex, "query preamble");
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse(UnexpectedErrorMessage), cancellationToken);
            return;
        }

        if (!resolved.Context.Connection.AllowSqlExecution)
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await httpContext.Response.WriteAsJsonAsync(
                new GridletErrorResponse($"SQL execution is disabled for connection '{resolved.Context.ConnectionName}'."),
                cancellationToken);
            return;
        }

        var limits = options.CurrentValue.Limits;
        var maxRows = Math.Clamp(body.MaxRows ?? limits.MaxQueryResultRows, 1, limits.MaxQueryResultRows);
        var sql = body.Sql ?? "";
        await WriteQueryStreamAsync(
            httpContext, audit, loggerFactory, connection, database, sql,
            token => resolved.Provider.Query.StreamAsync(
                resolved.Context, sql,
                new QueryRequestOptions(maxRows, limits.CommandTimeoutSeconds),
                parameters: null, token),
            cancellationToken);
    }

    /// <summary>
    /// Writes a query's events to the response as NDJSON, audits the execution, and reports failures
    /// either as a status code (before the response starts) or as a final <c>error</c> event.
    /// </summary>
    private static async Task WriteQueryStreamAsync(
        HttpContext httpContext,
        IGridletAuditSink audit,
        ILoggerFactory loggerFactory,
        string connection,
        string? database,
        string sql,
        Func<CancellationToken, IAsyncEnumerable<QueryStreamEvent>> stream,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        try
        {
            await foreach (var queryEvent in stream(cancellationToken))
            {
                await JsonSerializer.SerializeAsync(httpContext.Response.Body, queryEvent,
                    JsonSerializerOptions.Web, cancellationToken);
                await httpContext.Response.WriteAsync("\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }

            await AuditAsync(audit, httpContext, "query.execute", connection, database, null, sql,
                succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
        }
        catch (OperationCanceledException)
        {
            // The browser's AbortController closes the request and cancellation reaches the provider.
        }
        catch (Exception ex)
        {
            await AuditAsync(audit, httpContext, "query.execute", connection, database, null, sql,
                succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
            if (httpContext.RequestAborted.IsCancellationRequested)
            {
                return;
            }
            if (!httpContext.Response.HasStarted)
            {
                var statusCode = ex switch
                {
                    GridletUnknownConnectionException or GridletObjectNotFoundException
                        or GridletSessionNotFoundException => StatusCodes.Status404NotFound,
                    GridletSessionBusyException => StatusCodes.Status409Conflict,
                    GridletValidationException or GridletQueryException => StatusCodes.Status400BadRequest,
                    _ => StatusCodes.Status500InternalServerError,
                };
                var clientMessage = statusCode == StatusCodes.Status500InternalServerError
                    ? UnexpectedErrorMessage
                    : ex.Message;
                if (statusCode == StatusCodes.Status500InternalServerError)
                {
                    LogUnexpectedStreamError(loggerFactory, ex, "query execution");
                }

                httpContext.Response.StatusCode = statusCode;
                await httpContext.Response.WriteAsJsonAsync(
                    new GridletErrorResponse(clientMessage), cancellationToken);
                return;
            }
            var isExpected = ex is GridletValidationException or GridletQueryException or
                GridletUnknownConnectionException or GridletObjectNotFoundException or
                GridletSessionNotFoundException or GridletSessionBusyException;
            var streamedMessage = isExpected ? ex.Message : UnexpectedErrorMessage;
            if (!isExpected)
            {
                LogUnexpectedStreamError(loggerFactory, ex, "query execution");
            }

            var error = new QueryStreamEvent(
                "error", Message: streamedMessage, DurationMs: stopwatch.ElapsedMilliseconds);
            await TryWriteStreamEventAsync(httpContext, error);
        }
    }

    private static void LogUnexpectedStreamError(
        ILoggerFactory loggerFactory,
        Exception exception,
        string operation)
        => loggerFactory.CreateLogger("Gridlet.AspNetCore.Streaming")
            .LogError(exception, "Unexpected Gridlet {Operation} streaming failure.", operation);

    private static async Task TryWriteStreamEventAsync(HttpContext httpContext, QueryStreamEvent value)
    {
        if (httpContext.RequestAborted.IsCancellationRequested) return;
        try
        {
            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body, value, JsonSerializerOptions.Web, CancellationToken.None);
            await httpContext.Response.WriteAsync("\n", CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The client disconnected between the cancellation check and the response write.
        }
    }

    // ---- row editing ----

    private static Task<IResult> InsertRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.insert", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.InsertRowAsync(
                resolved.Context, schema, name, RequireMap(body.Values, "values"), ct),
            cancellationToken);

    private static Task<IResult> UpdateRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.update", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.UpdateRowAsync(
                resolved.Context, schema, name, RequireMap(body.Key, "key"), RequireMap(body.Values, "values"), ct),
            cancellationToken);

    private static Task<IResult> DeleteRow(
        string connection, string database, string schema, string name,
        RowWriteRequest body, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => WriteRow(connection, database, schema, name, "row.delete", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Writes.DeleteRowAsync(
                resolved.Context, schema, name, RequireMap(body.Key, "key"), ct),
            cancellationToken);

    private static Task<IResult> WriteRow(
        string connection, string database, string schema, string name, string action,
        IGridletConnectionResolver resolver, IGridletAuditSink audit, HttpContext httpContext,
        Func<ResolvedConnection, CancellationToken, Task<int>> write,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowWrites)
            {
                return Forbidden($"Row editing is disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var rows = await write(resolved, cancellationToken);
                await AuditAsync(audit, httpContext, action, connection, database, $"{schema}.{name}", null,
                    succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
                return Results.Ok(new RowWriteResponse(rows));
            }
            catch (Exception ex)
            {
                await AuditAsync(audit, httpContext, action, connection, database, $"{schema}.{name}", null,
                    succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    private static Dictionary<string, object?> RequireMap(
        Dictionary<string, JsonElement>? map, string what)
        => map is { Count: > 0 }
            ? ToClrMap(map)
            : throw new GridletValidationException($"The request must include non-empty '{what}'.");

    // ---- table designer ----

    private static Task<IResult> CreateSchema(
        string connection, string database, SchemaDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, body.Name, "ddl.createSchema", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.CreateSchemaAsync(resolved.Context, body, ct), cancellationToken);

    private static Task<IResult> AlterSchema(
        string connection, string database, string schema, SchemaDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => string.IsNullOrWhiteSpace(body.Owner)
            ? Task.FromResult<IResult>(Results.BadRequest(new GridletErrorResponse("An owner is required.")))
            : Ddl(connection, database, schema, "ddl.alterSchemaOwner", resolver, audit, httpContext,
                (resolved, ct) => resolved.Provider.Ddl.AlterSchemaOwnerAsync(resolved.Context, schema, body.Owner!, ct),
                cancellationToken);

    private static Task<IResult> DropSchema(
        string connection, string database, string schema,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, schema, "ddl.dropSchema", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropSchemaAsync(resolved.Context, schema, ct), cancellationToken);

    private static Task<IResult> CreateTable(
        string connection, string database, TableDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{body.Schema}.{body.Name}", "ddl.createTable", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.CreateTableAsync(resolved.Context, body, ct),
            cancellationToken);

    private static Task<IResult> AddColumn(
        string connection, string database, string schema, string name, ColumnDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}", "ddl.addColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddColumnAsync(resolved.Context, schema, name, body, ct),
            cancellationToken);

    private static Task<IResult> AlterColumn(
        string connection, string database, string schema, string name, string column, ColumnDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{column}", "ddl.alterColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AlterColumnAsync(resolved.Context, schema, name, column, body, ct),
            cancellationToken);

    private static Task<IResult> DropColumn(
        string connection, string database, string schema, string name, string column,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{column}", "ddl.dropColumn", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropColumnAsync(resolved.Context, schema, name, column, ct),
            cancellationToken);

    private static Task<IResult> AddPrimaryKey(
        string connection, string database, string schema, string name, PrimaryKeyDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{body.Name}", "ddl.addPrimaryKey", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddPrimaryKeyAsync(resolved.Context, schema, name, body, ct),
            cancellationToken);

    private static Task<IResult> AddCheckConstraint(
        string connection, string database, string schema, string name, CheckConstraintDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{body.Name ?? "(unnamed)"}",
            "ddl.addCheckConstraint", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddCheckConstraintAsync(
                resolved.Context, schema, name, body, ct), cancellationToken);

    private static Task<IResult> DropCheckConstraint(
        string connection, string database, string schema, string name, ConstraintReference body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => DropConstraintReference(connection, database, schema, name, body,
            "ddl.dropCheckConstraint", resolver, audit, httpContext,
            (resolved, reference, ct) => resolved.Provider.Ddl.DropCheckConstraintAsync(
                resolved.Context, schema, name, reference, ct), cancellationToken);

    private static Task<IResult> AddUniqueConstraint(
        string connection, string database, string schema, string name, UniqueConstraintDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{body.Name ?? "(unnamed)"}",
            "ddl.addUniqueConstraint", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddUniqueConstraintAsync(
                resolved.Context, schema, name, body, ct), cancellationToken);

    private static Task<IResult> DropUniqueConstraint(
        string connection, string database, string schema, string name, ConstraintReference body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => DropConstraintReference(connection, database, schema, name, body,
            "ddl.dropUniqueConstraint", resolver, audit, httpContext,
            (resolved, reference, ct) => resolved.Provider.Ddl.DropUniqueConstraintAsync(
                resolved.Context, schema, name, reference, ct), cancellationToken);

    private static Task<IResult> DropConstraintReference(
        string connection, string database, string schema, string name, ConstraintReference body,
        string action, IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext,
        Func<ResolvedConnection, ConstraintReference, CancellationToken, Task> drop,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Name) && body.Ordinal is null)
        {
            return Task.FromResult<IResult>(Results.BadRequest(
                new GridletErrorResponse("A constraint name or ordinal is required.")));
        }

        var reference = body with
        {
            Name = string.IsNullOrWhiteSpace(body.Name) ? null : body.Name.Trim(),
        };
        var label = reference.Name ?? $"#{reference.Ordinal}";
        return Ddl(connection, database, $"{schema}.{name}.{label}", action, resolver, audit, httpContext,
            (resolved, ct) => drop(resolved, reference, ct), cancellationToken);
    }

    private static Task<IResult> CreateIndex(
        string connection, string database, string schema, string name, IndexDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{body.Name}", "ddl.createIndex",
            resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.CreateIndexAsync(
                resolved.Context, schema, name, body, ct), cancellationToken);

    private static Task<IResult> DropIndex(
        string connection, string database, string schema, string name, string index,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{index}", "ddl.dropIndex",
            resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropIndexAsync(
                resolved.Context, schema, name, index, ct), cancellationToken);

    private static Task<IResult> AddForeignKey(
        string connection, string database, string schema, string name, ForeignKeyDesign body,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{body.Name}", "ddl.addForeignKey", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.AddForeignKeyAsync(resolved.Context, schema, name, body, ct),
            cancellationToken);

    private static Task<IResult> DropConstraint(
        string connection, string database, string schema, string name, string constraint,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}.{constraint}", "ddl.dropConstraint", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropConstraintAsync(resolved.Context, schema, name, constraint, ct),
            cancellationToken);

    private static Task<IResult> DropObject(
        string connection, string database, string schema, string name, DbObjectType? type,
        IGridletConnectionResolver resolver, IGridletAuditSink audit,
        HttpContext httpContext, CancellationToken cancellationToken)
        => Ddl(connection, database, $"{schema}.{name}", "ddl.dropObject", resolver, audit, httpContext,
            (resolved, ct) => resolved.Provider.Ddl.DropObjectAsync(
                resolved.Context, schema, name, type ?? DbObjectType.Table, ct),
            cancellationToken);

    private static Task<IResult> Ddl(
        string connection, string database, string objectName, string action,
        IGridletConnectionResolver resolver, IGridletAuditSink audit, HttpContext httpContext,
        Func<ResolvedConnection, CancellationToken, Task> execute,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var resolved = resolver.Resolve(connection, database);
            if (!resolved.Context.Connection.AllowDdl)
            {
                return Forbidden($"Schema changes are disabled for connection '{resolved.Context.ConnectionName}'.");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                await execute(resolved, cancellationToken);
                await AuditAsync(audit, httpContext, action, connection, database, objectName, null,
                    succeeded: true, stopwatch.ElapsedMilliseconds, error: null);
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                await AuditAsync(audit, httpContext, action, connection, database, objectName, null,
                    succeeded: false, stopwatch.ElapsedMilliseconds, ex.Message);
                throw;
            }
        });

    // ---- saved queries ----

    private static Task<IResult> GetSavedQueries(ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () => Results.Ok(await store.GetAllAsync(cancellationToken)));

    private static Task<IResult> SaveQuery(
        SavedQuerySaveRequest body, ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () =>
        {
            if (string.IsNullOrWhiteSpace(body.Name) ||
                string.IsNullOrWhiteSpace(body.Sql) ||
                string.IsNullOrWhiteSpace(body.ConnectionName))
            {
                throw new GridletValidationException("A saved query needs a name, a connection, and SQL text.");
            }

            var saved = await store.SaveAsync(
                new SavedQuery(
                    string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("n") : body.Id,
                    body.Name.Trim(), body.ConnectionName, body.Database, body.Sql, DateTimeOffset.UtcNow),
                cancellationToken);
            return Results.Ok(saved);
        });

    private static Task<IResult> DeleteSavedQuery(string id, ISavedQueryStore store, CancellationToken cancellationToken)
        => Execute(async () => await store.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No saved query with id '{id}'.")));

    // ---- published endpoints (admin) ----

    private static Task<IResult> GetPublishedEndpoints(IPublishedEndpointStore store, CancellationToken cancellationToken)
        => Execute(async () => Results.Ok(await store.GetAllAsync(cancellationToken)));

    private static Task<IResult> SavePublishedEndpoint(
        PublishRequest body, IPublishedEndpointStore store, IGridletConnectionResolver resolver,
        CancellationToken cancellationToken)
        => Execute(async () =>
        {
            var method = body.Method?.ToUpperInvariant();
            if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE"))
            {
                throw new GridletValidationException("Method must be GET, POST, PUT, PATCH, or DELETE.");
            }

            var route = (body.Route ?? "").Trim('/', ' ');
            if (route.Length == 0 || !RoutePattern().IsMatch(route))
            {
                throw new GridletValidationException(
                    "Route must contain only letters, digits, '-', '_' and '/' segments (e.g. sales/top-customers).");
            }

            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Sql))
            {
                throw new GridletValidationException("A published endpoint needs a name and SQL text.");
            }

            if (body.MaxRows is < 0)
            {
                throw new GridletValidationException(
                    "MaxRows must be null (use the server default), 0 (uncapped), or a positive number.");
            }

            resolver.Resolve(body.ConnectionName, body.Database); // throws for unknown connections

            var parameters = body.Parameters ?? [];
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameters)
            {
                if (!ParameterNamePattern().IsMatch(parameter.Name) || !names.Add(parameter.Name))
                {
                    throw new GridletValidationException($"Invalid or duplicate parameter name '{parameter.Name}'.");
                }

                if (parameter.Type.ToLowerInvariant() is not ("auto" or "string" or "integer" or "number" or "boolean"))
                {
                    throw new GridletValidationException(
                        $"Parameter '{parameter.Name}' has an unsupported type '{parameter.Type}'.");
                }
            }

            var saved = await store.SaveAsync(
                new PublishedEndpoint(
                    string.IsNullOrWhiteSpace(body.Id) ? Guid.NewGuid().ToString("n") : body.Id,
                    body.Name.Trim(), method, route, body.ConnectionName, body.Database, body.Sql,
                    parameters,
                    string.IsNullOrWhiteSpace(body.AuthorizationPolicy) ? null : body.AuthorizationPolicy.Trim(),
                    body.Enabled, DateTimeOffset.UtcNow, body.MaxRows),
                cancellationToken);
            return Results.Ok(saved);
        });

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial System.Text.RegularExpressions.Regex ParameterNamePattern();

    private static Task<IResult> DeletePublishedEndpoint(
        string id, IPublishedEndpointStore store, CancellationToken cancellationToken)
        => Execute(async () => await store.DeleteAsync(id, cancellationToken)
            ? Results.Ok(new { deleted = true })
            : Results.NotFound(new GridletErrorResponse($"No published endpoint with id '{id}'.")));

    private static DbObjectDto ToDto(DbObjectInfo info)
        => new(info.Schema, info.Name, info.Type.ToString(), info.SubKind, info.IsInternal);
}
