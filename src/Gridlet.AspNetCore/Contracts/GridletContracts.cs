using System.Text.Json;
using Gridlet.Models;

namespace Gridlet.AspNetCore.Contracts;

/// <summary>Wire-level DTOs. Kept separate from the core domain model so the HTTP contract can evolve independently.</summary>
public sealed record GridletMetaResponse(
    string Version,
    IReadOnlyList<GridletConnectionSummary> Connections,
    int MaxQueryResultRows);

public sealed record GridletConnectionSummary(
    string Name, string ProviderName, bool AllowSqlExecution, bool AllowWrites, bool AllowDdl);

public sealed record DbObjectDto(string Schema, string Name, string Type);

public sealed record TableStructureResponse(
    DbObjectDto Object,
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IndexInfo> Indexes,
    IReadOnlyList<ForeignKeyInfo> ForeignKeys);

public sealed record ObjectDefinitionResponse(string? Definition);

public sealed record QueryRequestBody(string? Sql, int? MaxRows = null);

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
