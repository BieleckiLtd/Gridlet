using System.Text.Json;
using Gridlet.Models;

namespace Gridlet.AspNetCore.Contracts;

/// <summary>Wire-level DTOs. Kept separate from the core domain model so the HTTP contract can evolve independently.</summary>
public sealed record GridletMetaResponse(
    string Version,
    IReadOnlyList<GridletConnectionSummary> Connections,
    int MaxQueryResultRows,
    GridletAgentInfo? Agent = null,
    string PublishedApiSegment = "pub",
    GridletVoiceInfo? Voice = null,
    IReadOnlyList<GridletUiModuleInfo>? Modules = null);

/// <summary>
/// An optional package that is installed and contributes browser assets. The shell loads these
/// after the base bundle, so a module the host did not install costs the browser nothing.
/// </summary>
public sealed record GridletUiModuleInfo(
    string Name,
    IReadOnlyList<string> Scripts,
    IReadOnlyList<string> Styles);

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
    bool IsInternal = false,
    string? Description = null)
{
    public DbObjectDto(string schema, string name, string type, string? subKind, bool isInternal)
        : this(schema, name, type, subKind, isInternal, null)
    {
    }

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

public sealed record SequenceDto(
    DbObjectDto Object,
    string DataType,
    string StartValue,
    string Increment,
    string MinimumValue,
    string MaximumValue,
    string? CurrentValue,
    bool IsCycling,
    bool IsCached,
    long? CacheSize);

public sealed record TableStructureResponse(
    DbObjectDto Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys,
    IReadOnlyList<CheckConstraintInfo> CheckConstraints,
    IReadOnlyList<UniqueConstraintInfo> UniqueConstraints,
    RowIdentityInfo? RowIdentity = null,
    IReadOnlyList<string>? TableOptions = null,
    IReadOnlyList<ForeignKeyDisplayDto>? ForeignKeyDisplays = null,
    TemporalTableInfo? Temporal = null,
    IReadOnlyList<DefaultConstraintInfo>? DefaultConstraints = null)
{
    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints,
        RowIdentityInfo? rowIdentity,
        IReadOnlyList<string>? tableOptions,
        IReadOnlyList<ForeignKeyDisplayDto>? foreignKeyDisplays,
        TemporalTableInfo? temporal)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints,
            rowIdentity, tableOptions, foreignKeyDisplays, temporal, null)
    {
    }

    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints,
        RowIdentityInfo? rowIdentity,
        IReadOnlyList<string>? tableOptions,
        IReadOnlyList<ForeignKeyDisplayDto>? foreignKeyDisplays)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints,
            rowIdentity, tableOptions, foreignKeyDisplays, null)
    {
    }

    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints,
        RowIdentityInfo? rowIdentity)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints,
            rowIdentity, null)
    {
    }

    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        IReadOnlyList<CheckConstraintInfo> checkConstraints,
        IReadOnlyList<UniqueConstraintInfo> uniqueConstraints)
        : this(@object, columns, indexes, foreignKeys, checkConstraints, uniqueConstraints, null, null)
    {
    }

    public TableStructureResponse(
        DbObjectDto @object,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<IndexInfo> indexes,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
        : this(@object, columns, indexes, foreignKeys, [], [], null, null)
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

public sealed record ForeignKeyDisplayDto(
    string ForeignKeyName,
    string LabelColumn,
    bool IsValid,
    string? ValidationMessage = null);

public sealed record ForeignKeyDisplaySaveRequest(string? LabelColumn);

public sealed record ForeignKeyLookupRequest(List<JsonElement>? Keys, string? Search = null);

public sealed record ForeignKeyLookupResponse(IReadOnlyList<ForeignKeyLookupItem> Items);

/// <summary>Body for scripting an object.</summary>
/// <param name="Include">Any of <c>drop</c>, <c>create</c> and <c>data</c>; defaults to create.</param>
/// <param name="MaxRows">How many rows to script, clamped to the server's query row limit.</param>
public sealed record ObjectScriptRequest(List<string?>? Include, int? MaxRows = null);

/// <summary>A generated script, ready for the query editor.</summary>
public sealed record ObjectScriptResponse(string Sql);

/// <summary>The parameters a stored procedure or function is called with.</summary>
public sealed record RoutineDefinitionResponse(
    DbObjectDto Object,
    IReadOnlyList<RoutineParameterInfo> Parameters);

/// <summary>Body for scripting a call: one entry per parameter the caller supplies a value for.</summary>
public sealed record RoutineScriptRequest(Dictionary<string, RoutineArgumentBody>? Arguments);

/// <param name="Value">The value as typed, quoted for the parameter's type unless <paramref name="IsRawSql"/>.</param>
/// <param name="IsNull">Pass NULL explicitly, which is not the same as omitting the argument.</param>
/// <param name="IsRawSql">Place the value in the script as written, for types a text box cannot express.</param>
public sealed record RoutineArgumentBody(string? Value, bool IsNull = false, bool IsRawSql = false);

/// <summary>A script that calls a routine, ready for the query editor.</summary>
public sealed record RoutineScriptResponse(string Sql);

public sealed record QueryRequestBody(string? Sql, int? MaxRows = null);

/// <summary>A replayable page of events from one server-side query job.</summary>
public sealed record QueryJobResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int NextEventIndex,
    int EventCount,
    IReadOnlyList<QueryStreamEvent> Events);

/// <summary>The state acknowledged by a query-job cancellation request.</summary>
public sealed record QueryJobCancelResponse(
    string Id,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int EventCount);

/// <summary>
/// A bounded result set or two-sided comparison diff submitted for conversion to a richer
/// download format.
/// </summary>
public sealed record ResultExportRequest(
    ResultColumn[]? Columns,
    JsonElement[][]? Rows,
    string? ProviderName = null,
    bool?[][]? BinaryValues = null);

/// <summary>One condition in the <c>filter</c> query parameter of the table-data routes.</summary>
/// <param name="Column">The column to compare.</param>
/// <param name="Operator">
/// One of <c>equals</c>, <c>notEquals</c>, <c>lessThan</c>, <c>lessThanOrEqual</c>,
/// <c>greaterThan</c>, <c>greaterThanOrEqual</c>, <c>contains</c>, <c>notContains</c>,
/// <c>startsWith</c>, <c>endsWith</c>, <c>isNull</c> or <c>isNotNull</c>.
/// </param>
/// <param name="Value">The value to compare against; omitted for the null checks.</param>
public sealed record TableDataFilterBody(string? Column, string? Operator, string? Value = null);

/// <summary>Body for an execution-plan request.</summary>
/// <param name="Sql">The statement to explain.</param>
/// <param name="Mode">
/// <c>estimated</c> (the default) compiles the statement without running it; <c>actual</c> runs it
/// and reports the plan the engine used.
/// </param>
public sealed record QueryPlanRequestBody(string? Sql, string? Mode = null);

/// <summary>An execution plan. <paramref name="Mode"/> is <c>estimated</c> or <c>actual</c>.</summary>
public sealed record QueryPlanResponse(
    string Mode,
    string Format,
    IReadOnlyList<QueryPlanNode> Roots,
    string? RawText,
    IReadOnlyList<string> Messages);

/// <summary>Body for a transaction control request on a pinned session.</summary>
/// <param name="Command">One of <c>begin</c>, <c>commit</c> or <c>rollback</c>.</param>
public sealed record SessionTransactionRequest(string? Command);

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

public sealed record TableImportResponse(int RowsImported);

public sealed record SequenceRestartRequest(string? Value);

/// <summary>Body for a rename. The new name is always unqualified: a rename never moves an object.</summary>
public sealed record RenameRequest(string? NewName);

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
