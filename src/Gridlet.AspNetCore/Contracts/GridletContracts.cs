using System.Text.Json;
using Gridlet.Models;

namespace Gridlet.AspNetCore.Contracts;

/// <summary>Wire-level DTOs. Kept separate from the core domain model so the HTTP contract can evolve independently.</summary>
public sealed record GridletMetaResponse(
    string Version,
    IReadOnlyList<GridletConnectionSummary> Connections,
    int MaxQueryResultRows,
    GridletAgentInfo? Agent = null,
    string PublishedApiSegment = "pub");

public sealed record GridletConnectionSummary(
    string Name,
    string ProviderName,
    string? DefaultDatabase,
    bool AllowSqlExecution,
    bool AllowWrites,
    bool AllowDdl,
    GridletProviderCapabilities Capabilities,
    bool AllowAgentSchemaAccess = false,
    bool AllowAgentDataAccess = false,
    bool AllowAgentApiAccess = false);

public sealed record DbObjectDto(
    string Schema,
    string Name,
    string Type,
    string? SubKind = null,
    bool IsInternal = false)
{
    public DbObjectDto(string schema, string name, string type)
        : this(schema, name, type, null, false)
    {
    }

    public void Deconstruct(out string schema, out string name, out string type)
    {
        schema = Schema;
        name = Name;
        type = Type;
    }
}

public sealed record TableStructureResponse(
    DbObjectDto Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<UniqueConstraintInfo> UniqueConstraints)
{
    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
        : this(@object, columns, indexes, foreignKeys, [], [])
    {
    }

    public void Deconstruct(
        out DbObjectDto @object,
        out IReadOnlyList<ColumnInfo> columns,
        out IReadOnlyList<IndexInfo> indexes,
        out IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        @object = Object;
        columns = Columns;
        indexes = Indexes;
        foreignKeys = ForeignKeys;
    }
}

public sealed record ObjectDefinitionResponse(string? Definition);

public sealed record QueryRequestBody(string? Sql, int? MaxRows = null);

public sealed record AgentCredentialRequestBody(string? ApiKey);

public sealed record AgentCredentialResponse(string Handle, DateTimeOffset ExpiresAt);

public sealed record AgentCredentialRemoveRequestBody(string? Handle);

/// <param name="ShareSchema">
/// Whether the person has opted into sharing schema metadata for this turn. Defaults to
/// <see langword="false"/>: a client that says nothing shares nothing.
/// </param>
/// <param name="ShareData">
/// Whether the person has opted into sharing row data for this turn. Only the data chat route
/// accepts <see langword="true"/>, so the host's data authorization policy always applies.
/// </param>
/// <param name="ShareApi">
/// Whether the person has opted into sharing this Gridlet's published API endpoints for this turn,
/// which lets the agent read their definitions and invoke GET endpoints. This grants no direct
/// database-query access. If an endpoint is invoked, its response is shared and may contain data;
/// only the data chat route accepts <see langword="true"/> for that reason.
/// </param>
public sealed record AgentChatRequestBody(
    string? ProfileId,
    string? Message,
    List<GridletAgentMessage>? History = null,
    string? CredentialHandle = null,
    string? ConversationId = null,
    string? ReasoningEffort = null,
    bool ShareSchema = false,
    bool ShareData = false,
    bool ShareApi = false);

/// <summary>One browser answer to an agent's mid-turn request to share a scope.</summary>
public sealed record AgentPermissionDecisionBody(bool? Granted);

public sealed record GridletErrorResponse(string Error);

/// <summary>Body for row writes. <c>Key</c> identifies the row (primary-key columns); <c>Values</c> carries column values.</summary>
public sealed record RowWriteRequest(
    Dictionary<string, JsonElement>? Key,
    Dictionary<string, JsonElement>? Values);

public sealed record RowWriteResponse(int RowsAffected);

public sealed record SavedQuerySaveRequest(
    string? Id, string Name, string ConnectionName, string? Database, string Sql);

public sealed record PublishRequest(
    string? Id,
    string Name,
    string Method,
    string Route,
    string ConnectionName,
    string? Database,
    string Sql,
    List<PublishedParameter>? Parameters,
    string? AuthorizationPolicy,
    bool Enabled = true,
    int? MaxRows = null);
